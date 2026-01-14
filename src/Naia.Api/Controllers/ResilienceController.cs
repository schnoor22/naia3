using Microsoft.AspNetCore.Mvc;
using Naia.Application.Abstractions;

namespace Naia.Api.Controllers;

/// <summary>
/// API controller for Data Resilience services (TIC + Shadow Historian).
/// Provides visibility into data integrity chain status, shadow buffer health,
/// and manual recovery controls.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ResilienceController : ControllerBase
{
    private readonly ILogger<ResilienceController> _logger;
    private readonly IShadowBuffer _shadowBuffer;
    private readonly IIntegrityChainService _chainService;
    private readonly IGapRecoveryService _recoveryService;
    
    public ResilienceController(
        IShadowBuffer shadowBuffer,
        IIntegrityChainService chainService,
        IGapRecoveryService recoveryService,
        ILogger<ResilienceController> logger)
    {
        _shadowBuffer = shadowBuffer;
        _chainService = chainService;
        _recoveryService = recoveryService;
        _logger = logger;
    }
    
    /// <summary>
    /// Get overall resilience status including shadow buffer and chain health.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var shadowStats = await _shadowBuffer.GetStatsAsync(cancellationToken);
        
        var status = new
        {
            shadowBuffer = new
            {
                totalEntries = shadowStats.TotalEntries,
                confirmedEntries = shadowStats.ConfirmedEntries,
                unconfirmedEntries = shadowStats.UnconfirmedEntries,
                totalPoints = shadowStats.TotalPoints,
                databaseSizeBytes = shadowStats.DatabaseSizeBytes,
                databaseSizeMB = shadowStats.DatabaseSizeBytes / (1024.0 * 1024.0),
                oldestEntry = shadowStats.OldestEntry,
                newestEntry = shadowStats.NewestEntry,
                entriesPerDataSource = shadowStats.EntriesPerDataSource
            },
            health = new
            {
                isHealthy = shadowStats.UnconfirmedEntries < 100,
                message = shadowStats.UnconfirmedEntries < 100 
                    ? "System healthy" 
                    : $"{shadowStats.UnconfirmedEntries} unconfirmed entries - may need recovery"
            }
        };
        
        return Ok(status);
    }
    
    /// <summary>
    /// Get shadow buffer statistics.
    /// </summary>
    [HttpGet("shadow/stats")]
    public async Task<IActionResult> GetShadowStats(CancellationToken cancellationToken)
    {
        var stats = await _shadowBuffer.GetStatsAsync(cancellationToken);
        return Ok(stats);
    }
    
    /// <summary>
    /// Get unconfirmed entries in shadow buffer for a data source.
    /// </summary>
    [HttpGet("shadow/unconfirmed/{dataSourceId}")]
    public async Task<IActionResult> GetUnconfirmed(
        string dataSourceId,
        [FromQuery] int hoursBack = 24,
        CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddHours(-hoursBack);
        var entries = await _shadowBuffer.GetUnconfirmedAsync(dataSourceId, since, cancellationToken);
        
        return Ok(new
        {
            dataSourceId,
            since,
            count = entries.Count,
            entries = entries.Select(e => new
            {
                e.ShadowId,
                e.BatchId,
                e.PointCount,
                e.BufferedAt,
                e.MinTimestamp,
                e.MaxTimestamp
            })
        });
    }
    
    /// <summary>
    /// Manually purge expired shadow buffer entries.
    /// </summary>
    [HttpPost("shadow/purge")]
    public async Task<IActionResult> PurgeShadow(
        [FromQuery] int? retentionDays = null,
        CancellationToken cancellationToken = default)
    {
        var before = await _shadowBuffer.GetStatsAsync(cancellationToken);
        
        var retention = retentionDays.HasValue 
            ? TimeSpan.FromDays(retentionDays.Value) 
            : (TimeSpan?)null;
        
        await _shadowBuffer.PurgeExpiredAsync(retention, cancellationToken);
        
        var after = await _shadowBuffer.GetStatsAsync(cancellationToken);
        
        return Ok(new
        {
            purged = before.TotalEntries - after.TotalEntries,
            before = before.TotalEntries,
            after = after.TotalEntries,
            retentionDays = retentionDays ?? 7
        });
    }
    
    /// <summary>
    /// Get TIC chain status for a data source.
    /// </summary>
    [HttpGet("chain/{dataSourceId}")]
    public async Task<IActionResult> GetChainStatus(
        string dataSourceId,
        CancellationToken cancellationToken)
    {
        var lastEntry = await _chainService.GetLastEntryAsync(dataSourceId, cancellationToken);
        
        if (lastEntry == null)
        {
            return Ok(new
            {
                dataSourceId,
                hasChain = false,
                message = "No chain entries for this data source"
            });
        }
        
        // Check for recent gaps
        var gaps = await _chainService.DetectGapsAsync(
            dataSourceId,
            DateTime.UtcNow.AddHours(-24),
            DateTime.UtcNow,
            cancellationToken);
        
        return Ok(new
        {
            dataSourceId,
            hasChain = true,
            lastEntry = new
            {
                lastEntry.EntryId,
                lastEntry.SequenceNumber,
                lastEntry.PointCount,
                lastEntry.MinTimestamp,
                lastEntry.MaxTimestamp,
                lastEntry.CreatedAt,
                chainHash = lastEntry.ChainHash[..16] + "..."
            },
            gaps = new
            {
                count = gaps.Count,
                items = gaps.Select(g => new
                {
                    g.GapId,
                    g.MissingCount,
                    g.GapStartTime,
                    g.GapEndTime,
                    g.Status
                })
            }
        });
    }
    
    /// <summary>
    /// Create a checkpoint in the TIC chain.
    /// </summary>
    [HttpPost("chain/{dataSourceId}/checkpoint")]
    public async Task<IActionResult> CreateCheckpoint(
        string dataSourceId,
        [FromQuery] string reason = "Manual checkpoint",
        CancellationToken cancellationToken = default)
    {
        await _chainService.CreateCheckpointAsync(dataSourceId, reason, cancellationToken);
        
        return Ok(new
        {
            dataSourceId,
            reason,
            createdAt = DateTime.UtcNow
        });
    }
    
    /// <summary>
    /// Get recovery status for a data source.
    /// </summary>
    [HttpGet("recovery/{dataSourceId}")]
    public async Task<IActionResult> GetRecoveryStatus(
        string dataSourceId,
        CancellationToken cancellationToken)
    {
        var status = await _recoveryService.GetStatusAsync(dataSourceId, cancellationToken);
        return Ok(status);
    }
    
    /// <summary>
    /// Trigger gap recovery scan for a specific data source or all sources.
    /// </summary>
    [HttpPost("recovery/scan")]
    public async Task<IActionResult> TriggerRecovery(
        [FromQuery] string? dataSourceId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Manual gap recovery triggered for {DataSource}",
            dataSourceId ?? "all sources");
        
        var result = await _recoveryService.ScanAndRecoverAsync(dataSourceId, cancellationToken);
        
        return Ok(new
        {
            dataSourceId = dataSourceId ?? "all",
            gapsDetected = result.GapsDetected,
            gapsRecovered = result.GapsRecovered,
            gapsFailed = result.GapsFailed,
            pointsRecovered = result.PointsRecovered,
            durationMs = result.Duration.TotalMilliseconds,
            activeGaps = result.ActiveGaps.Select(g => new
            {
                g.GapId,
                g.DataSourceId,
                g.MissingCount,
                g.Status,
                g.GapStartTime,
                g.GapEndTime
            }),
            error = result.ErrorMessage
        });
    }
    
    /// <summary>
    /// Get combined health report for all resilience systems.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealthReport(CancellationToken cancellationToken)
    {
        var shadowStats = await _shadowBuffer.GetStatsAsync(cancellationToken);
        
        var isHealthy = shadowStats.UnconfirmedEntries < 1000;
        var issues = new List<string>();
        
        if (shadowStats.UnconfirmedEntries >= 100)
            issues.Add($"High unconfirmed count: {shadowStats.UnconfirmedEntries}");
        
        if (shadowStats.DatabaseSizeBytes > 500 * 1024 * 1024)
            issues.Add($"Large shadow buffer: {shadowStats.DatabaseSizeBytes / 1024 / 1024}MB");
        
        return Ok(new
        {
            status = isHealthy ? "healthy" : "degraded",
            isHealthy,
            timestamp = DateTime.UtcNow,
            components = new
            {
                shadowBuffer = new
                {
                    healthy = shadowStats.UnconfirmedEntries < 100,
                    unconfirmedCount = shadowStats.UnconfirmedEntries,
                    totalPoints = shadowStats.TotalPoints
                },
                integrityChain = new
                {
                    healthy = true, // Would need per-source check
                    dataSources = shadowStats.EntriesPerDataSource.Count
                }
            },
            issues
        });
    }
}
