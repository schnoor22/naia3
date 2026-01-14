using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;
using StackExchange.Redis;

namespace Naia.Infrastructure.Resilience;

/// <summary>
/// Temporal Integrity Chain (TIC) implementation using Redis for distributed chain state.
/// 
/// The TIC creates a blockchain-inspired chain of hashes that links each batch to the previous one.
/// This allows instant detection of any gaps in the data stream - if a batch is missing,
/// the chain validation will fail immediately.
/// 
/// CHAIN STRUCTURE:
/// Entry N: ChainHash = SHA256(PreviousHash + BatchId + PointCount + Timestamps + DataHash)
///    ↓
/// Entry N+1: Uses Entry N's ChainHash as its PreviousHash
///    ↓
/// Entry N+2: Uses Entry N+1's ChainHash as its PreviousHash
/// 
/// If Entry N+1 is lost, Entry N+2's validation will fail because the expected
/// PreviousHash won't match the stored Entry N's ChainHash.
/// </summary>
public sealed class RedisIntegrityChainService : IIntegrityChainService
{
    private readonly ILogger<RedisIntegrityChainService> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    
    private const string ChainKeyPrefix = "tic:chain:";
    private const string LastEntryKeyPrefix = "tic:last:";
    private const string GapsKeyPrefix = "tic:gaps:";
    private const string CheckpointsKeyPrefix = "tic:checkpoints:";
    private const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    
    public RedisIntegrityChainService(
        IConnectionMultiplexer redis,
        ILogger<RedisIntegrityChainService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
        
        _logger.LogInformation("Temporal Integrity Chain service initialized");
    }
    
    public async Task<ChainEntry> CreateChainEntryAsync(
        DataPointBatch batch,
        string dataSourceId,
        CancellationToken cancellationToken = default)
    {
        // Get the last entry for this data source
        var lastEntry = await GetLastEntryAsync(dataSourceId, cancellationToken);
        var previousHash = lastEntry?.ChainHash ?? GenesisHash;
        var nextSequence = (lastEntry?.SequenceNumber ?? 0) + 1;
        
        // Calculate timestamps
        DateTime minTs = DateTime.MaxValue;
        DateTime maxTs = DateTime.MinValue;
        foreach (var point in batch.Points)
        {
            if (point.Timestamp < minTs) minTs = point.Timestamp;
            if (point.Timestamp > maxTs) maxTs = point.Timestamp;
        }
        
        if (batch.Points.Count == 0)
        {
            minTs = maxTs = batch.CreatedAt;
        }
        
        // Calculate data hash (hash of the serialized points)
        var dataJson = JsonSerializer.Serialize(batch.Points, JsonOptions);
        var dataHash = ComputeHash(dataJson);
        
        // Calculate chain hash = SHA256(previousHash + batchId + pointCount + timestamps + dataHash)
        var chainInput = $"{previousHash}|{batch.BatchId}|{batch.Count}|{minTs:O}|{maxTs:O}|{dataHash}";
        var chainHash = ComputeHash(chainInput);
        
        var entry = new ChainEntry
        {
            EntryId = $"tic_{Guid.NewGuid():N}",
            DataSourceId = dataSourceId,
            SequenceNumber = nextSequence,
            BatchId = batch.BatchId,
            PointCount = batch.Count,
            MinTimestamp = minTs,
            MaxTimestamp = maxTs,
            CreatedAt = DateTime.UtcNow,
            PreviousHash = previousHash,
            DataHash = dataHash,
            ChainHash = chainHash
        };
        
        // Store in Redis
        await StoreEntryAsync(entry, cancellationToken);
        
        _logger.LogDebug(
            "Created chain entry {EntryId} for {DataSource}: seq={Seq}, hash={Hash}",
            entry.EntryId, dataSourceId, nextSequence, chainHash[..16]);
        
        return entry;
    }
    
    public async Task<ChainValidationResult> ValidateChainAsync(
        ChainEntry entry,
        CancellationToken cancellationToken = default)
    {
        // Get the last known entry for this data source
        var lastEntry = await GetLastEntryAsync(entry.DataSourceId, cancellationToken);
        
        if (lastEntry == null)
        {
            // First entry for this source - must have genesis hash
            if (entry.PreviousHash != GenesisHash)
            {
                return ChainValidationResult.Invalid(
                    entry.EntryId,
                    1,
                    entry.SequenceNumber,
                    CreateGap(entry.DataSourceId, 0, entry.SequenceNumber, DateTime.MinValue, entry.MinTimestamp),
                    $"First entry must have genesis hash, got {entry.PreviousHash[..16]}");
            }
            
            return ChainValidationResult.Valid(entry.EntryId, entry.SequenceNumber);
        }
        
        // Check sequence number
        var expectedSequence = lastEntry.SequenceNumber + 1;
        if (entry.SequenceNumber != expectedSequence)
        {
            var gap = CreateGap(
                entry.DataSourceId,
                lastEntry.SequenceNumber,
                entry.SequenceNumber,
                lastEntry.MaxTimestamp,
                entry.MinTimestamp);
            
            // Store the gap for recovery
            await StoreGapAsync(gap, cancellationToken);
            
            _logger.LogWarning(
                "Chain gap detected for {DataSource}: expected seq {Expected}, got {Actual}",
                entry.DataSourceId, expectedSequence, entry.SequenceNumber);
            
            return ChainValidationResult.Invalid(
                entry.EntryId,
                expectedSequence,
                entry.SequenceNumber,
                gap,
                $"Sequence gap: expected {expectedSequence}, got {entry.SequenceNumber}");
        }
        
        // Check previous hash
        if (entry.PreviousHash != lastEntry.ChainHash)
        {
            var gap = CreateGap(
                entry.DataSourceId,
                lastEntry.SequenceNumber,
                entry.SequenceNumber,
                lastEntry.MaxTimestamp,
                entry.MinTimestamp);
            
            await StoreGapAsync(gap, cancellationToken);
            
            _logger.LogWarning(
                "Chain hash mismatch for {DataSource}: expected {Expected}, got {Actual}",
                entry.DataSourceId, lastEntry.ChainHash[..16], entry.PreviousHash[..16]);
            
            return ChainValidationResult.Invalid(
                entry.EntryId,
                expectedSequence,
                entry.SequenceNumber,
                gap,
                $"Hash mismatch: expected {lastEntry.ChainHash[..16]}, got {entry.PreviousHash[..16]}");
        }
        
        return ChainValidationResult.Valid(entry.EntryId, entry.SequenceNumber);
    }
    
    public async Task<ChainEntry?> GetLastEntryAsync(
        string dataSourceId,
        CancellationToken cancellationToken = default)
    {
        var key = $"{LastEntryKeyPrefix}{dataSourceId}";
        var json = await _db.StringGetAsync(key);
        
        if (json.IsNullOrEmpty)
            return null;
        
        return JsonSerializer.Deserialize<ChainEntry>(json!, JsonOptions);
    }
    
    public async Task CreateCheckpointAsync(
        string dataSourceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var lastEntry = await GetLastEntryAsync(dataSourceId, cancellationToken);
        
        if (lastEntry == null)
        {
            _logger.LogWarning(
                "Cannot create checkpoint for {DataSource}: no entries exist",
                dataSourceId);
            return;
        }
        
        // Create a checkpoint entry that marks this as a known-good state
        var checkpoint = lastEntry with
        {
            EntryId = $"chkpt_{Guid.NewGuid():N}",
            IsCheckpoint = true,
            CheckpointReason = reason
        };
        
        // Store checkpoint
        var key = $"{CheckpointsKeyPrefix}{dataSourceId}";
        var json = JsonSerializer.Serialize(checkpoint, JsonOptions);
        await _db.ListRightPushAsync(key, json);
        
        _logger.LogInformation(
            "Created checkpoint for {DataSource} at seq {Seq}: {Reason}",
            dataSourceId, checkpoint.SequenceNumber, reason);
    }
    
    public async Task<IReadOnlyList<ChainGap>> DetectGapsAsync(
        string dataSourceId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var key = $"{GapsKeyPrefix}{dataSourceId}";
        var values = await _db.ListRangeAsync(key);
        
        var gaps = new List<ChainGap>();
        foreach (var value in values)
        {
            var gap = JsonSerializer.Deserialize<ChainGap>(value!, JsonOptions);
            if (gap != null && 
                gap.GapStartTime >= from && 
                gap.GapEndTime <= to &&
                gap.Status != GapStatus.Recovered)
            {
                gaps.Add(gap);
            }
        }
        
        return gaps.OrderBy(g => g.GapStartTime).ToList();
    }
    
    private async Task StoreEntryAsync(ChainEntry entry, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        
        // Store as last entry (overwrites previous)
        var lastKey = $"{LastEntryKeyPrefix}{entry.DataSourceId}";
        await _db.StringSetAsync(lastKey, json);
        
        // Also store in chain history (for auditing)
        var chainKey = $"{ChainKeyPrefix}{entry.DataSourceId}";
        await _db.ListRightPushAsync(chainKey, json);
        
        // Trim chain history to last 10000 entries to prevent unbounded growth
        await _db.ListTrimAsync(chainKey, -10000, -1);
    }
    
    private async Task StoreGapAsync(ChainGap gap, CancellationToken cancellationToken)
    {
        var key = $"{GapsKeyPrefix}{gap.DataSourceId}";
        var json = JsonSerializer.Serialize(gap, JsonOptions);
        await _db.ListRightPushAsync(key, json);
        
        _logger.LogWarning(
            "Stored gap {GapId} for {DataSource}: {Missing} missing entries from {Start} to {End}",
            gap.GapId, gap.DataSourceId, gap.MissingCount, gap.GapStartTime, gap.GapEndTime);
    }
    
    private ChainGap CreateGap(
        string dataSourceId,
        long lastGoodSequence,
        long firstBadSequence,
        DateTime gapStart,
        DateTime gapEnd)
    {
        return new ChainGap
        {
            GapId = $"gap_{Guid.NewGuid():N}",
            DataSourceId = dataSourceId,
            LastGoodSequence = lastGoodSequence,
            FirstBadSequence = firstBadSequence,
            MissingCount = (int)(firstBadSequence - lastGoodSequence - 1),
            GapStartTime = gapStart,
            GapEndTime = gapEnd,
            DetectedAt = DateTime.UtcNow
        };
    }
    
    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
