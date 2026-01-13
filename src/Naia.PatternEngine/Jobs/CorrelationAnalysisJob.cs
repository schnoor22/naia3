using System.Text.Json;
using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Npgsql;
using StackExchange.Redis;

namespace Naia.PatternEngine.Jobs;

/// <summary>
/// Calculates pairwise Pearson correlations between points using QuestDB ASOF JOIN.
/// This is the second stage of the Pattern Flywheel - identifying which points behave together.
/// 
/// Optimization: Groups points by behavioral similarity to reduce O(n²) comparison complexity.
/// Uses QuestDB's ASOF JOIN for efficient time-aligned correlation computation.
/// 
/// Runs every 15 minutes via Hangfire scheduler.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 840)]
public sealed class CorrelationAnalysisJob : ICorrelationAnalysisJob
{
    private readonly ILogger<CorrelationAnalysisJob> _logger;
    private readonly PatternFlywheelOptions _options;
    private readonly IConnectionMultiplexer _redis;
    private readonly string _postgresConnectionString;
    private readonly string _questDbConnectionString;

    public CorrelationAnalysisJob(
        ILogger<CorrelationAnalysisJob> logger,
        IOptions<PatternFlywheelOptions> options,
        IConnectionMultiplexer redis,
        string postgresConnectionString,
        string questDbConnectionString)
    {
        _logger = logger;
        _options = options.Value;
        _redis = redis;
        _postgresConnectionString = postgresConnectionString;
        _questDbConnectionString = questDbConnectionString;
    }

    public async Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken)
    {
        context?.WriteLine("Starting correlation analysis job...");
        _logger.LogInformation("Starting correlation analysis job");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var correlationsCalculated = 0;
        var significantCorrelations = 0;

        try
        {
            // Get behavioral stats from Redis/PostgreSQL
            var behaviors = await GetBehavioralStatsAsync(cancellationToken);
            context?.WriteLine($"Found {behaviors.Count} points with behavioral data");

            if (behaviors.Count < 2)
            {
                context?.WriteLine("Not enough points for correlation analysis");
                return;
            }

            // Group points by behavioral similarity to reduce comparisons
            var groups = GroupPointsByBehavior(behaviors);
            context?.WriteLine($"Grouped into {groups.Count} behavioral groups");

            var progressBar = context?.WriteProgressBar();
            var processedGroups = 0;

            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (group.Points.Count < 2)
                    continue;

                // Calculate correlations within the group using QuestDB ASOF JOIN
                var correlations = await CalculateGroupCorrelationsAsync(
                    group.Points, cancellationToken);

                // Store significant correlations
                var significant = correlations
                    .Where(c => Math.Abs(c.Correlation) >= _options.CorrelationProcessor.MinCorrelation)
                    .ToList();

                await StoreCorrelationsAsync(significant, cancellationToken);

                correlationsCalculated += correlations.Count;
                significantCorrelations += significant.Count;

                processedGroups++;
                progressBar?.SetValue(100.0 * processedGroups / groups.Count);
            }

            stopwatch.Stop();
            context?.WriteLine($"Completed: {correlationsCalculated} correlations calculated, {significantCorrelations} significant, {stopwatch.ElapsedMilliseconds}ms");

            _logger.LogInformation(
                "Correlation analysis complete: {Total} pairs, {Significant} significant (>= {Threshold}), {Duration}ms",
                correlationsCalculated, significantCorrelations,
                _options.CorrelationProcessor.MinCorrelation,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Correlation analysis job failed");
            context?.WriteLine($"ERROR: {ex.Message}");
            throw;
        }
    }

    private async Task<List<BehaviorStat>> GetBehavioralStatsAsync(CancellationToken cancellationToken)
    {
        var stats = new List<BehaviorStat>();

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            SELECT point_id, point_id_seq, point_name, mean_value, std_deviation,
                   min_value, max_value, update_rate_hz, sample_count
            FROM behavioral_stats
            WHERE calculated_at > NOW() - INTERVAL '24 hours'
              AND sample_count >= @MinSamples
            ORDER BY point_id_seq
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MinSamples", _options.BehavioralAggregator.MinSamplesForBehavior);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            stats.Add(new BehaviorStat
            {
                PointId = reader.GetGuid(0),
                PointIdSeq = reader.GetInt64(1),  // Fixed: Use Int64 for BIGINT column
                PointName = reader.GetString(2),
                MeanValue = reader.GetDouble(3),
                StdDeviation = reader.GetDouble(4),
                MinValue = reader.GetDouble(5),
                MaxValue = reader.GetDouble(6),
                UpdateRateHz = reader.GetDouble(7),
                SampleCount = reader.GetInt32(8)
            });
        }

        return stats;
    }

    private List<BehaviorGroup> GroupPointsByBehavior(List<BehaviorStat> behaviors)
    {
        // Group points by similar update rates (within 2x) and overlapping value ranges
        // This optimization reduces O(n²) complexity by only comparing points
        // that could plausibly be related
        
        var groups = new List<BehaviorGroup>();
        var assigned = new HashSet<Guid>();

        foreach (var behavior in behaviors.OrderBy(b => b.UpdateRateHz))
        {
            if (assigned.Contains(behavior.PointId))
                continue;

            var group = new BehaviorGroup
            {
                UpdateRateRange = (
                    behavior.UpdateRateHz / 2.0,
                    behavior.UpdateRateHz * 2.0
                ),
                Points = new List<BehaviorStat> { behavior }
            };

            // Find other points with similar characteristics
            foreach (var other in behaviors)
            {
                if (assigned.Contains(other.PointId) || other.PointId == behavior.PointId)
                    continue;

                // Check update rate similarity (within 2x)
                if (other.UpdateRateHz >= group.UpdateRateRange.Min &&
                    other.UpdateRateHz <= group.UpdateRateRange.Max)
                {
                    group.Points.Add(other);
                    assigned.Add(other.PointId);
                }
            }

            assigned.Add(behavior.PointId);
            
            if (group.Points.Count >= 2)
                groups.Add(group);
        }

        return groups;
    }

    private async Task<List<CorrelationResult>> CalculateGroupCorrelationsAsync(
        List<BehaviorStat> points,
        CancellationToken cancellationToken)
    {
        var results = new List<CorrelationResult>();
        var windowHours = _options.CorrelationProcessor.WindowHours;
        var minSamples = _options.CorrelationProcessor.MinSamples;

        await using var questConn = new NpgsqlConnection(_questDbConnectionString);
        await questConn.OpenAsync(cancellationToken);

        // Compare each pair within the group
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var p1 = points[i];
                var p2 = points[j];

                try
                {
                    // Use QuestDB ASOF JOIN to align time-series and calculate correlation
                    var correlation = await CalculatePairCorrelationAsync(
                        questConn, p1.PointIdSeq, p2.PointIdSeq, windowHours, minSamples, cancellationToken);

                    if (correlation.HasValue)
                    {
                        results.Add(new CorrelationResult
                        {
                            PointId1 = p1.PointId < p2.PointId ? p1.PointId : p2.PointId,
                            PointId2 = p1.PointId < p2.PointId ? p2.PointId : p1.PointId,
                            PointIdSeq1 = p1.PointIdSeq < p2.PointIdSeq ? p1.PointIdSeq : p2.PointIdSeq,
                            PointIdSeq2 = p1.PointIdSeq < p2.PointIdSeq ? p2.PointIdSeq : p1.PointIdSeq,
                            Correlation = correlation.Value,
                            SampleCount = minSamples, // Approximation
                            CalculatedAt = DateTime.UtcNow
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, 
                        "Failed to calculate correlation between {P1} and {P2}",
                        p1.PointName, p2.PointName);
                }
            }
        }

        return results;
    }

    private async Task<double?> CalculatePairCorrelationAsync(
        NpgsqlConnection conn,
        long pointIdSeq1,  // Fixed: Use long for BIGINT column
        long pointIdSeq2,  // Fixed: Use long for BIGINT column
        int windowHours,
        int minSamples,
        CancellationToken cancellationToken)
    {
        // QuestDB-compatible query using ASOF JOIN for time alignment
        // and dateadd() for interval arithmetic
        var windowMicroseconds = (long)windowHours * 3600 * 1000000L;
        
        var sql = $@"
            SELECT 
                corr(a.value, b.value) as correlation,
                count(*) as sample_count
            FROM 
                (SELECT timestamp, value FROM point_data 
                 WHERE point_id = {pointIdSeq1} 
                   AND timestamp > dateadd('h', -{windowHours}, now())) a
            ASOF JOIN 
                (SELECT timestamp, value FROM point_data 
                 WHERE point_id = {pointIdSeq2}
                   AND timestamp > dateadd('h', -{windowHours}, now())) b
            WHERE a.value IS NOT NULL AND b.value IS NOT NULL
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                var sampleCount = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                if (sampleCount >= minSamples && !reader.IsDBNull(0))
                {
                    return reader.GetDouble(0);
                }
            }
        }
        catch (Npgsql.NpgsqlException ex) when (ex.Message.Contains("does not exist"))
        {
            // Table not created yet, will retry later
        }

        return null;
    }

    private async Task StoreCorrelationsAsync(
        List<CorrelationResult> correlations,
        CancellationToken cancellationToken)
    {
        if (correlations.Count == 0)
            return;

        // Store in Redis for fast lookups during clustering
        var db = _redis.GetDatabase();
        var ttl = TimeSpan.FromHours(_options.CorrelationProcessor.CacheTtlHours);
        var batch = db.CreateBatch();

        foreach (var corr in correlations)
        {
            var key = $"naia:corr:{corr.PointIdSeq1}:{corr.PointIdSeq2}";
            batch.StringSetAsync(key, corr.Correlation.ToString("F6"), ttl);
        }

        batch.Execute();

        // Upsert to PostgreSQL correlation_cache
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            INSERT INTO correlation_cache (
                point_id_1, point_id_2, correlation, sample_count,
                window_start, window_end, calculated_at
            ) VALUES (
                @PointId1, @PointId2, @Correlation, @SampleCount,
                @WindowStart, @WindowEnd, @CalculatedAt
            )
            ON CONFLICT (point_id_1, point_id_2) DO UPDATE SET
                correlation = EXCLUDED.correlation,
                sample_count = EXCLUDED.sample_count,
                window_start = EXCLUDED.window_start,
                window_end = EXCLUDED.window_end,
                calculated_at = EXCLUDED.calculated_at
        ";

        var windowStart = DateTime.UtcNow.AddHours(-_options.CorrelationProcessor.WindowHours);
        var windowEnd = DateTime.UtcNow;

        foreach (var corr in correlations)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@PointId1", corr.PointId1);
            cmd.Parameters.AddWithValue("@PointId2", corr.PointId2);
            cmd.Parameters.AddWithValue("@Correlation", corr.Correlation);
            cmd.Parameters.AddWithValue("@SampleCount", corr.SampleCount);
            cmd.Parameters.AddWithValue("@WindowStart", windowStart);
            cmd.Parameters.AddWithValue("@WindowEnd", windowEnd);
            cmd.Parameters.AddWithValue("@CalculatedAt", corr.CalculatedAt);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

internal sealed record BehaviorStat
{
    public Guid PointId { get; init; }
    public long PointIdSeq { get; init; }  // Fixed: BIGINT/LONG not INT
    public required string PointName { get; init; }
    public double MeanValue { get; init; }
    public double StdDeviation { get; init; }
    public double MinValue { get; init; }
    public double MaxValue { get; init; }
    public double UpdateRateHz { get; init; }
    public int SampleCount { get; init; }
}

internal sealed class BehaviorGroup
{
    public (double Min, double Max) UpdateRateRange { get; set; }
    public List<BehaviorStat> Points { get; set; } = new();
}

internal sealed record CorrelationResult
{
    public Guid PointId1 { get; init; }
    public Guid PointId2 { get; init; }
    public long PointIdSeq1 { get; init; }  // Fixed: BIGINT/LONG not INT
    public long PointIdSeq2 { get; init; }  // Fixed: BIGINT/LONG not INT
    public double Correlation { get; init; }
    public int SampleCount { get; init; }
    public DateTime CalculatedAt { get; init; }
}
