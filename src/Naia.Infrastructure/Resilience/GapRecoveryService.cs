using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;

namespace Naia.Infrastructure.Resilience;

/// <summary>
/// Gap Detection & Recovery Service.
/// Coordinates between the Temporal Integrity Chain (gap detection) and 
/// Shadow Buffer (data recovery) to automatically heal data gaps.
/// 
/// RECOVERY STRATEGY:
/// 1. Scan TIC for detected gaps
/// 2. For each gap, query Shadow Buffer for unconfirmed entries in the time range
/// 3. Replay shadow entries through the ingestion pipeline
/// 4. Mark gap as recovered after successful replay
/// 
/// This creates a self-healing data pipeline that can recover from:
/// - Kafka failures (messages lost before consumer)
/// - Ingestion service crashes (messages consumed but not stored)
/// - QuestDB write failures (temporary unavailability)
/// - Network partitions (connector can't reach Kafka)
/// </summary>
public sealed class GapRecoveryService : IGapRecoveryService
{
    private readonly ILogger<GapRecoveryService> _logger;
    private readonly IIntegrityChainService _chainService;
    private readonly IShadowBuffer _shadowBuffer;
    private readonly IDataPointProducer _producer;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public GapRecoveryService(
        IIntegrityChainService chainService,
        IShadowBuffer shadowBuffer,
        IDataPointProducer producer,
        ILogger<GapRecoveryService> logger)
    {
        _chainService = chainService;
        _shadowBuffer = shadowBuffer;
        _producer = producer;
        _logger = logger;
        
        _logger.LogInformation("Gap Recovery Service initialized");
    }
    
    public async Task<GapRecoveryResult> ScanAndRecoverAsync(
        string? dataSourceId = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var activeGaps = new List<ChainGap>();
        int gapsDetected = 0, gapsRecovered = 0, gapsFailed = 0;
        long pointsRecovered = 0;
        
        try
        {
            // Get all data sources to scan (from shadow buffer stats)
            var stats = await _shadowBuffer.GetStatsAsync(cancellationToken);
            var dataSources = dataSourceId != null 
                ? new[] { dataSourceId } 
                : stats.EntriesPerDataSource.Keys.ToArray();
            
            foreach (var source in dataSources)
            {
                // Check for unconfirmed entries in shadow buffer
                var unconfirmed = await _shadowBuffer.GetUnconfirmedAsync(
                    source,
                    DateTime.UtcNow.AddHours(-24), // Look back 24 hours
                    cancellationToken);
                
                if (unconfirmed.Count == 0)
                    continue;
                
                _logger.LogInformation(
                    "Found {Count} unconfirmed entries for {DataSource}",
                    unconfirmed.Count, source);
                
                // Check for gaps in the chain
                var gaps = await _chainService.DetectGapsAsync(
                    source,
                    DateTime.UtcNow.AddHours(-24),
                    DateTime.UtcNow,
                    cancellationToken);
                
                gapsDetected += gaps.Count;
                
                foreach (var gap in gaps)
                {
                    if (gap.Status == GapStatus.Recovered)
                        continue;
                    
                    _logger.LogInformation(
                        "Attempting recovery for gap {GapId}: {Missing} missing entries",
                        gap.GapId, gap.MissingCount);
                    
                    try
                    {
                        // Get shadow entries for this gap's time range
                        var shadowEntries = await _shadowBuffer.GetForRecoveryAsync(
                            source,
                            gap.GapStartTime,
                            gap.GapEndTime,
                            cancellationToken);
                        
                        if (shadowEntries.Count == 0)
                        {
                            _logger.LogWarning(
                                "No shadow entries found for gap {GapId}",
                                gap.GapId);
                            
                            activeGaps.Add(gap with { Status = GapStatus.RecoveryFailed });
                            gapsFailed++;
                            continue;
                        }
                        
                        // Replay the shadow entries
                        long recoveredPoints = 0;
                        foreach (var entry in shadowEntries)
                        {
                            var batch = JsonSerializer.Deserialize<DataPointBatch>(
                                entry.BatchJson, JsonOptions);
                            
                            if (batch == null)
                                continue;
                            
                            // Republish to Kafka with recovery flag
                            var result = await _producer.PublishAsync(batch, cancellationToken);
                            
                            if (result.Success)
                            {
                                recoveredPoints += batch.Count;
                                // Mark as confirmed since we're reprocessing
                                await _shadowBuffer.ConfirmAsync(entry.ShadowId, cancellationToken);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Failed to replay shadow entry {ShadowId}: {Error}",
                                    entry.ShadowId, result.ErrorMessage);
                            }
                        }
                        
                        pointsRecovered += recoveredPoints;
                        gapsRecovered++;
                        
                        _logger.LogInformation(
                            "Recovered gap {GapId}: {Points} points from {Entries} shadow entries",
                            gap.GapId, recoveredPoints, shadowEntries.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, 
                            "Failed to recover gap {GapId}",
                            gap.GapId);
                        
                        activeGaps.Add(gap with 
                        { 
                            Status = GapStatus.RecoveryFailed,
                            RecoveryAttempts = gap.RecoveryAttempts + 1,
                            LastRecoveryError = ex.Message
                        });
                        gapsFailed++;
                    }
                }
            }
            
            sw.Stop();
            
            return new GapRecoveryResult
            {
                GapsDetected = gapsDetected,
                GapsRecovered = gapsRecovered,
                GapsFailed = gapsFailed,
                PointsRecovered = pointsRecovered,
                Duration = sw.Elapsed,
                ActiveGaps = activeGaps
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            
            _logger.LogError(ex, "Gap recovery scan failed");
            
            return new GapRecoveryResult
            {
                GapsDetected = gapsDetected,
                GapsRecovered = gapsRecovered,
                GapsFailed = gapsFailed,
                PointsRecovered = pointsRecovered,
                Duration = sw.Elapsed,
                ActiveGaps = activeGaps,
                ErrorMessage = ex.Message
            };
        }
    }
    
    public async Task RequestBackfillAsync(
        ChainGap gap,
        CancellationToken cancellationToken = default)
    {
        // Get shadow entries for backfill
        var shadowEntries = await _shadowBuffer.GetForRecoveryAsync(
            gap.DataSourceId,
            gap.GapStartTime,
            gap.GapEndTime,
            cancellationToken);
        
        if (shadowEntries.Count == 0)
        {
            _logger.LogWarning(
                "No shadow entries available for backfill of gap {GapId}",
                gap.GapId);
            return;
        }
        
        _logger.LogInformation(
            "Initiating backfill for gap {GapId}: {Count} shadow entries available",
            gap.GapId, shadowEntries.Count);
        
        foreach (var entry in shadowEntries)
        {
            try
            {
                var batch = JsonSerializer.Deserialize<DataPointBatch>(
                    entry.BatchJson, JsonOptions);
                
                if (batch == null)
                    continue;
                
                // Create a backfill batch (mark it as recovery)
                var backfillBatch = DataPointBatch.Create(
                    batch.Points,
                    batch.DataSourceId);
                
                await _producer.PublishAsync(backfillBatch, cancellationToken);
                
                _logger.LogDebug(
                    "Replayed shadow entry {ShadowId} ({Points} points)",
                    entry.ShadowId, entry.PointCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to replay shadow entry {ShadowId}",
                    entry.ShadowId);
            }
        }
    }
    
    public async Task<RecoveryStatus> GetStatusAsync(
        string dataSourceId,
        CancellationToken cancellationToken = default)
    {
        // Get gaps for this source
        var gaps = await _chainService.DetectGapsAsync(
            dataSourceId,
            DateTime.UtcNow.AddDays(-7),
            DateTime.UtcNow,
            cancellationToken);
        
        var pendingGaps = gaps.Count(g => g.Status == GapStatus.Detected);
        var recoveringGaps = gaps.Count(g => g.Status == GapStatus.RecoveryInProgress);
        
        // Get unconfirmed in shadow buffer
        var unconfirmed = await _shadowBuffer.GetUnconfirmedAsync(
            dataSourceId,
            DateTime.UtcNow.AddDays(-7),
            cancellationToken);
        
        // Get last chain entry for validation time
        var lastEntry = await _chainService.GetLastEntryAsync(dataSourceId, cancellationToken);
        
        var isHealthy = pendingGaps == 0 && recoveringGaps == 0 && unconfirmed.Count < 100;
        
        string healthMessage;
        if (isHealthy)
        {
            healthMessage = "Data pipeline is healthy";
        }
        else if (pendingGaps > 0)
        {
            healthMessage = $"{pendingGaps} gaps detected, recovery pending";
        }
        else if (recoveringGaps > 0)
        {
            healthMessage = $"{recoveringGaps} gaps being recovered";
        }
        else
        {
            healthMessage = $"{unconfirmed.Count} unconfirmed entries in shadow buffer";
        }
        
        return new RecoveryStatus
        {
            DataSourceId = dataSourceId,
            PendingGaps = pendingGaps,
            RecoveringGaps = recoveringGaps,
            UnconfirmedInShadow = unconfirmed.Count,
            LastChainValidation = lastEntry?.CreatedAt ?? DateTime.MinValue,
            IsHealthy = isHealthy,
            HealthMessage = healthMessage
        };
    }
}
