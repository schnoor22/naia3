using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Npgsql;
using StackExchange.Redis;

namespace Naia.PatternEngine.Jobs;

/// <summary>
/// Maintenance job for cleanup and housekeeping tasks.
/// Runs daily to maintain system health and performance.
/// 
/// Tasks:
/// - Cleanup expired suggestions
/// - Purge old correlation cache entries
/// - Remove orphaned clusters
/// - Apply retention policies
/// - Compact Redis caches
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 1800)]
public sealed class MaintenanceJob : IMaintenanceJob
{
    private readonly ILogger<MaintenanceJob> _logger;
    private readonly PatternFlywheelOptions _options;
    private readonly IConnectionMultiplexer _redis;
    private readonly string _postgresConnectionString;

    public MaintenanceJob(
        ILogger<MaintenanceJob> logger,
        IOptions<PatternFlywheelOptions> options,
        IConnectionMultiplexer redis,
        string postgresConnectionString)
    {
        _logger = logger;
        _options = options.Value;
        _redis = redis;
        _postgresConnectionString = postgresConnectionString;
    }

    public async Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken)
    {
        context?.WriteLine("Starting maintenance job...");
        _logger.LogInformation("Starting maintenance job");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Cleanup PostgreSQL tables
            var (suggestions, correlations, clusters, feedbackLogs, behaviors) = 
                await CleanupPostgreSqlAsync(context, cancellationToken);

            // Cleanup Redis caches
            var redisKeys = await CleanupRedisAsync(context, cancellationToken);

            // Vacuum analyze tables for better performance
            await VacuumTablesAsync(context, cancellationToken);

            stopwatch.Stop();
            
            context?.WriteLine($"Maintenance complete:");
            context?.WriteLine($"  - Expired suggestions: {suggestions}");
            context?.WriteLine($"  - Old correlations: {correlations}");
            context?.WriteLine($"  - Stale clusters: {clusters}");
            context?.WriteLine($"  - Old feedback logs: {feedbackLogs}");
            context?.WriteLine($"  - Old behavioral stats: {behaviors}");
            context?.WriteLine($"  - Redis keys expired: {redisKeys}");
            context?.WriteLine($"  - Duration: {stopwatch.ElapsedMilliseconds}ms");

            _logger.LogInformation(
                "Maintenance complete: {Suggestions} suggestions, {Correlations} correlations, {Clusters} clusters cleaned, {Duration}ms",
                suggestions, correlations, clusters, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Maintenance job failed");
            context?.WriteLine($"ERROR: {ex.Message}");
            throw;
        }
    }

    private async Task<(int suggestions, int correlations, int clusters, int feedbackLogs, int behaviors)> 
        CleanupPostgreSqlAsync(PerformContext? context, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var retentionDays = _options.Maintenance?.RetentionDays ?? 90;

        // 1. Delete old expired suggestions (keep pending/approved/rejected for history)
        var suggestionsSql = @"
            DELETE FROM pattern_suggestions
            WHERE status = 'expired'
              AND created_at < NOW() - INTERVAL '7 days'
        ";
        await using var suggestionsCmd = new NpgsqlCommand(suggestionsSql, conn);
        var suggestions = await suggestionsCmd.ExecuteNonQueryAsync(cancellationToken);
        context?.WriteLine($"  Cleaned {suggestions} expired suggestions");

        // 2. Delete old correlation cache entries
        var correlationsSql = $@"
            DELETE FROM correlation_cache
            WHERE calculated_at < NOW() - INTERVAL '{retentionDays} days'
        ";
        await using var correlationsCmd = new NpgsqlCommand(correlationsSql, conn);
        var correlations = await correlationsCmd.ExecuteNonQueryAsync(cancellationToken);
        context?.WriteLine($"  Cleaned {correlations} old correlations");

        // 3. Mark old inactive clusters for deletion
        var clustersSql = @"
            DELETE FROM behavioral_clusters
            WHERE is_active = false
              AND detected_at < NOW() - INTERVAL '7 days'
              AND NOT EXISTS (
                  SELECT 1 FROM pattern_suggestions ps
                  WHERE ps.cluster_id = behavioral_clusters.id
                    AND ps.status IN ('pending', 'approved')
              )
        ";
        await using var clustersCmd = new NpgsqlCommand(clustersSql, conn);
        var clusters = await clustersCmd.ExecuteNonQueryAsync(cancellationToken);
        context?.WriteLine($"  Cleaned {clusters} stale clusters");

        // 4. Archive old feedback logs (keep last N days)
        var feedbackSql = $@"
            DELETE FROM pattern_feedback_log
            WHERE created_at < NOW() - INTERVAL '{retentionDays} days'
        ";
        await using var feedbackCmd = new NpgsqlCommand(feedbackSql, conn);
        var feedbackLogs = await feedbackCmd.ExecuteNonQueryAsync(cancellationToken);
        context?.WriteLine($"  Cleaned {feedbackLogs} old feedback logs");

        // 5. Delete old behavioral stats
        var behaviorsSql = @"
            DELETE FROM behavioral_stats
            WHERE calculated_at < NOW() - INTERVAL '7 days'
        ";
        await using var behaviorsCmd = new NpgsqlCommand(behaviorsSql, conn);
        var behaviors = await behaviorsCmd.ExecuteNonQueryAsync(cancellationToken);
        context?.WriteLine($"  Cleaned {behaviors} old behavioral stats");

        return (suggestions, correlations, clusters, feedbackLogs, behaviors);
    }

    private async Task<int> CleanupRedisAsync(PerformContext? context, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var expiredCount = 0;

        // Scan for naia:* keys and check for orphaned entries
        // (Most Redis keys have TTL set, so this is mainly for diagnostics)
        
        var patterns = new[] { "naia:behavior:*", "naia:corr:*", "naia:cluster:*" };
        
        foreach (var pattern in patterns)
        {
            var keys = server.Keys(pattern: pattern, pageSize: 1000).Take(10000).ToList();
            context?.WriteLine($"  Found {keys.Count} Redis keys matching {pattern}");
            
            // Check for keys without TTL (shouldn't happen, but cleanup if they exist)
            foreach (var key in keys)
            {
                var ttl = await db.KeyTimeToLiveAsync(key);
                if (ttl == null)
                {
                    // Key has no TTL - set a reasonable one
                    await db.KeyExpireAsync(key, TimeSpan.FromHours(48));
                    expiredCount++;
                }
            }
        }

        if (expiredCount > 0)
        {
            context?.WriteLine($"  Set TTL on {expiredCount} Redis keys without expiration");
        }

        return expiredCount;
    }

    private async Task VacuumTablesAsync(PerformContext? context, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var tables = new[]
        {
            "pattern_suggestions",
            "correlation_cache",
            "behavioral_clusters",
            "behavioral_stats",
            "pattern_feedback_log"
        };

        foreach (var table in tables)
        {
            try
            {
                // ANALYZE updates statistics for query planner
                await using var cmd = new NpgsqlCommand($"ANALYZE {table}", conn);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                context?.WriteLine($"  Analyzed table: {table}");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to analyze table {Table}", table);
            }
        }
    }
}
