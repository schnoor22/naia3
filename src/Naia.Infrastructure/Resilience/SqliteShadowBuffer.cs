using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;

namespace Naia.Infrastructure.Resilience;

/// <summary>
/// Configuration for the Shadow Buffer.
/// </summary>
public sealed class ShadowBufferOptions
{
    public const string SectionName = "ShadowBuffer";
    
    /// <summary>Path to the SQLite database file</summary>
    public string DatabasePath { get; set; } = "shadow_buffer.db";
    
    /// <summary>How long to retain confirmed entries (default 7 days)</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
    
    /// <summary>How often to run purge (default every hour)</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);
    
    /// <summary>Enable compression for stored batches</summary>
    public bool EnableCompression { get; set; } = true;
    
    /// <summary>Compression level (default Fastest for low latency)</summary>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Fastest;
    
    /// <summary>Maximum database size before warning (default 1GB)</summary>
    public long MaxDatabaseSizeBytes { get; set; } = 1024 * 1024 * 1024;
}

/// <summary>
/// SQLite-based Shadow Buffer implementation.
/// Provides a local rolling buffer of ALL data before it enters Kafka,
/// enabling recovery from any downstream failure.
/// 
/// DESIGN NOTES:
/// - Uses WAL mode for concurrent reads/writes
/// - Stores both JSON and optionally compressed data
/// - Indexed for fast lookups by data source and confirmation status
/// - Automatic purge of old confirmed entries
/// </summary>
public sealed class SqliteShadowBuffer : IShadowBuffer, IAsyncDisposable
{
    private readonly ILogger<SqliteShadowBuffer> _logger;
    private readonly ShadowBufferOptions _options;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    
    public SqliteShadowBuffer(
        IOptions<ShadowBufferOptions> options,
        ILogger<SqliteShadowBuffer> logger)
    {
        _options = options.Value;
        _logger = logger;
        
        // Ensure directory exists
        var directory = Path.GetDirectoryName(_options.DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        _connectionString = $"Data Source={_options.DatabasePath}";
        
        InitializeDatabase();
        
        _logger.LogInformation(
            "Shadow Buffer initialized at {Path} with {Retention} retention",
            _options.DatabasePath, _options.RetentionPeriod);
    }
    
    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        
        // Enable WAL mode for better concurrency
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }
        
        // Create main table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS shadow_entries (
                    shadow_id TEXT PRIMARY KEY,
                    data_source_id TEXT NOT NULL,
                    batch_id TEXT NOT NULL,
                    chain_entry_id TEXT,
                    point_count INTEGER NOT NULL,
                    batch_json TEXT NOT NULL,
                    compressed_data BLOB,
                    buffered_at TEXT NOT NULL,
                    confirmed_at TEXT,
                    min_timestamp TEXT NOT NULL,
                    max_timestamp TEXT NOT NULL
                );
                
                CREATE INDEX IF NOT EXISTS idx_shadow_datasource 
                    ON shadow_entries(data_source_id);
                    
                CREATE INDEX IF NOT EXISTS idx_shadow_confirmed 
                    ON shadow_entries(confirmed_at);
                    
                CREATE INDEX IF NOT EXISTS idx_shadow_buffered 
                    ON shadow_entries(buffered_at);
                    
                CREATE INDEX IF NOT EXISTS idx_shadow_unconfirmed 
                    ON shadow_entries(data_source_id, confirmed_at) 
                    WHERE confirmed_at IS NULL;
            ";
            cmd.ExecuteNonQuery();
        }
        
        // Create stats table for fast metrics
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS shadow_stats (
                    stat_key TEXT PRIMARY KEY,
                    stat_value TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
            ";
            cmd.ExecuteNonQuery();
        }
    }
    
    public async Task<string> BufferAsync(
        DataPointBatch batch,
        string dataSourceId,
        CancellationToken cancellationToken = default)
    {
        var shadowId = $"shd_{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(batch, JsonOptions);
        
        byte[]? compressed = null;
        if (_options.EnableCompression)
        {
            compressed = Compress(json);
        }
        
        // Find min/max timestamps
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
        
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO shadow_entries 
                (shadow_id, data_source_id, batch_id, point_count, batch_json, 
                 compressed_data, buffered_at, min_timestamp, max_timestamp)
                VALUES 
                (@shadowId, @dataSourceId, @batchId, @pointCount, @batchJson,
                 @compressed, @bufferedAt, @minTs, @maxTs);
            ";
            
            cmd.Parameters.AddWithValue("@shadowId", shadowId);
            cmd.Parameters.AddWithValue("@dataSourceId", dataSourceId);
            cmd.Parameters.AddWithValue("@batchId", batch.BatchId);
            cmd.Parameters.AddWithValue("@pointCount", batch.Count);
            cmd.Parameters.AddWithValue("@batchJson", json);
            cmd.Parameters.AddWithValue("@compressed", compressed ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@bufferedAt", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@minTs", minTs.ToString("O"));
            cmd.Parameters.AddWithValue("@maxTs", maxTs.ToString("O"));
            
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            
            _logger.LogDebug(
                "Buffered batch {BatchId} ({Count} points) as {ShadowId}",
                batch.BatchId, batch.Count, shadowId);
            
            return shadowId;
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    public async Task ConfirmAsync(
        string shadowId,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE shadow_entries 
                SET confirmed_at = @confirmedAt
                WHERE shadow_id = @shadowId;
            ";
            
            cmd.Parameters.AddWithValue("@shadowId", shadowId);
            cmd.Parameters.AddWithValue("@confirmedAt", DateTime.UtcNow.ToString("O"));
            
            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            
            if (rows > 0)
            {
                _logger.LogDebug("Confirmed shadow entry {ShadowId}", shadowId);
            }
            else
            {
                _logger.LogWarning("Shadow entry {ShadowId} not found for confirmation", shadowId);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    public async Task<IReadOnlyList<ShadowEntry>> GetUnconfirmedAsync(
        string dataSourceId,
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT shadow_id, data_source_id, batch_id, chain_entry_id, point_count,
                   batch_json, compressed_data, buffered_at, confirmed_at, min_timestamp, max_timestamp
            FROM shadow_entries
            WHERE data_source_id = @dataSourceId 
              AND confirmed_at IS NULL
              AND buffered_at >= @since
            ORDER BY buffered_at ASC;
        ";
        
        cmd.Parameters.AddWithValue("@dataSourceId", dataSourceId);
        cmd.Parameters.AddWithValue("@since", since.ToString("O"));
        
        var entries = new List<ShadowEntry>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(ReadEntry(reader));
        }
        
        return entries;
    }
    
    public async Task<IReadOnlyList<ShadowEntry>> GetForRecoveryAsync(
        string dataSourceId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT shadow_id, data_source_id, batch_id, chain_entry_id, point_count,
                   batch_json, compressed_data, buffered_at, confirmed_at, min_timestamp, max_timestamp
            FROM shadow_entries
            WHERE data_source_id = @dataSourceId 
              AND min_timestamp >= @from
              AND max_timestamp <= @to
            ORDER BY min_timestamp ASC;
        ";
        
        cmd.Parameters.AddWithValue("@dataSourceId", dataSourceId);
        cmd.Parameters.AddWithValue("@from", from.ToString("O"));
        cmd.Parameters.AddWithValue("@to", to.ToString("O"));
        
        var entries = new List<ShadowEntry>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(ReadEntry(reader));
        }
        
        _logger.LogInformation(
            "Retrieved {Count} entries for recovery: {DataSource} from {From} to {To}",
            entries.Count, dataSourceId, from, to);
        
        return entries;
    }
    
    public async Task PurgeExpiredAsync(
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default)
    {
        var retentionPeriod = retention ?? _options.RetentionPeriod;
        var cutoff = DateTime.UtcNow - retentionPeriod;
        
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM shadow_entries
                WHERE confirmed_at IS NOT NULL
                  AND confirmed_at < @cutoff;
            ";
            
            cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
            
            var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken);
            
            if (deleted > 0)
            {
                _logger.LogInformation(
                    "Purged {Count} expired shadow entries (older than {Cutoff})",
                    deleted, cutoff);
                
                // Vacuum to reclaim space
                using var vacuumCmd = connection.CreateCommand();
                vacuumCmd.CommandText = "VACUUM;";
                await vacuumCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    public async Task<ShadowBufferStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        // Get counts
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = @"
            SELECT 
                COUNT(*) as total,
                SUM(CASE WHEN confirmed_at IS NOT NULL THEN 1 ELSE 0 END) as confirmed,
                SUM(CASE WHEN confirmed_at IS NULL THEN 1 ELSE 0 END) as unconfirmed,
                COALESCE(SUM(point_count), 0) as total_points,
                MIN(buffered_at) as oldest,
                MAX(buffered_at) as newest
            FROM shadow_entries;
        ";
        
        long total = 0, confirmed = 0, unconfirmed = 0, totalPoints = 0;
        DateTime oldest = DateTime.UtcNow, newest = DateTime.UtcNow;
        
        using (var reader = await countCmd.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                total = reader.GetInt64(0);
                confirmed = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                unconfirmed = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                totalPoints = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                oldest = reader.IsDBNull(4) ? DateTime.UtcNow : DateTime.Parse(reader.GetString(4));
                newest = reader.IsDBNull(5) ? DateTime.UtcNow : DateTime.Parse(reader.GetString(5));
            }
        }
        
        // Get per-datasource counts
        var entriesPerSource = new Dictionary<string, long>();
        using var sourceCmd = connection.CreateCommand();
        sourceCmd.CommandText = @"
            SELECT data_source_id, COUNT(*) as cnt
            FROM shadow_entries
            GROUP BY data_source_id;
        ";
        
        using (var reader = await sourceCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                entriesPerSource[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        
        // Get database size
        long dbSize = 0;
        if (File.Exists(_options.DatabasePath))
        {
            dbSize = new FileInfo(_options.DatabasePath).Length;
        }
        
        return new ShadowBufferStats
        {
            TotalEntries = total,
            ConfirmedEntries = confirmed,
            UnconfirmedEntries = unconfirmed,
            TotalPoints = totalPoints,
            DatabaseSizeBytes = dbSize,
            OldestEntry = oldest,
            NewestEntry = newest,
            EntriesPerDataSource = entriesPerSource
        };
    }
    
    private ShadowEntry ReadEntry(SqliteDataReader reader)
    {
        return new ShadowEntry
        {
            ShadowId = reader.GetString(0),
            DataSourceId = reader.GetString(1),
            BatchId = reader.GetString(2),
            ChainEntryId = reader.IsDBNull(3) ? null : reader.GetString(3),
            PointCount = reader.GetInt32(4),
            BatchJson = reader.GetString(5),
            CompressedData = reader.IsDBNull(6) ? null : (byte[])reader.GetValue(6),
            BufferedAt = DateTime.Parse(reader.GetString(7)),
            ConfirmedAt = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
            MinTimestamp = DateTime.Parse(reader.GetString(9)),
            MaxTimestamp = DateTime.Parse(reader.GetString(10))
        };
    }
    
    private byte[] Compress(string json)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, _options.CompressionLevel, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }
    
    private string Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }
    
    public ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
