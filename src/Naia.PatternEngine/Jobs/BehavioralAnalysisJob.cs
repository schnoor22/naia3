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
/// Calculates behavioral statistics for all active points from QuestDB time-series data.
/// This is the first stage of the Pattern Flywheel - transforming raw time-series into
/// behavioral fingerprints that enable correlation analysis and pattern matching.
/// 
/// Runs every 5 minutes via Hangfire scheduler.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 240)]
public sealed class BehavioralAnalysisJob : IBehavioralAnalysisJob
{
    private readonly ILogger<BehavioralAnalysisJob> _logger;
    private readonly PatternFlywheelOptions _options;
    private readonly IConnectionMultiplexer _redis;
    private readonly string _postgresConnectionString;
    private readonly string _questDbConnectionString;

    public BehavioralAnalysisJob(
        ILogger<BehavioralAnalysisJob> logger,
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
        context?.WriteLine("Starting behavioral analysis job...");
        _logger.LogInformation("Starting behavioral analysis job");
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var processedCount = 0;
        var errorCount = 0;

        try
        {
            // Get all active points from PostgreSQL
            var points = await GetActivePointsAsync(cancellationToken);
            context?.WriteLine($"Found {points.Count} active points to analyze");
            
            var progressBar = context?.WriteProgressBar();
            var processed = 0;

            // Process points in batches for efficiency
            var batchSize = _options.BehavioralAggregator.BatchSize;
            var batches = points.Chunk(batchSize);

            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var behaviors = await CalculateBehaviorsBatchAsync(batch, cancellationToken);
                    
                    // Store behaviors in Redis cache and PostgreSQL
                    await StoreBehaviorsAsync(behaviors, cancellationToken);
                    
                    processedCount += behaviors.Count;
                    processed += batch.Length;
                    
                    progressBar?.SetValue(100.0 * processed / points.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing batch of {Count} points", batch.Length);
                    errorCount += batch.Length;
                }
            }

            stopwatch.Stop();
            context?.WriteLine($"Completed: {processedCount} points analyzed, {errorCount} errors, {stopwatch.ElapsedMilliseconds}ms");
            
            _logger.LogInformation(
                "Behavioral analysis complete: {Processed} points, {Errors} errors, {Duration}ms",
                processedCount, errorCount, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Behavioral analysis job failed");
            context?.WriteLine($"ERROR: {ex.Message}");
            throw;
        }
    }

    private async Task<List<PointInfo>> GetActivePointsAsync(CancellationToken cancellationToken)
    {
        var points = new List<PointInfo>();

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        // Get points with recent data (active in last 24h)
        var sql = @"
            SELECT p.id, p.point_sequence_id, p.name, p.data_source_id
            FROM points p
            WHERE p.is_enabled = true
              AND p.point_sequence_id IS NOT NULL
            ORDER BY p.point_sequence_id
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            points.Add(new PointInfo
            {
                Id = reader.GetGuid(0),
                IdSeq = reader.GetInt64(1),  // Fixed: Use Int64 for BIGINT column
                Name = reader.GetString(2),
                DataSourceId = reader.GetGuid(3)
            });
        }

        return points;
    }

    private async Task<List<BehaviorResult>> CalculateBehaviorsBatchAsync(
        PointInfo[] points,
        CancellationToken cancellationToken)
    {
        var results = new List<BehaviorResult>();
        var pointIdSeqs = points.Select(p => p.IdSeq).ToArray();
        
        // Query QuestDB for time-series data over the analysis window
        var windowHours = _options.BehavioralAggregator.WindowHours;
        var minSamples = _options.BehavioralAggregator.MinSamplesForBehavior;
        
        // Get summary statistics for all points in batch (use dedicated connection)
        var summaryByPointSeq = await GetPointSummariesAsync(pointIdSeqs, windowHours, minSamples, cancellationToken);

        // For points with sufficient data, calculate detailed stats
        foreach (var point in points)
        {
            if (!summaryByPointSeq.TryGetValue(point.IdSeq, out var summary))
                continue;

            try
            {
                // Get detailed values for stddev calculation (each gets its own connection)
                var values = await GetPointValuesAsync(point.IdSeq, windowHours, cancellationToken);
                if (values.Count < minSamples)
                    continue;

                // Calculate statistics using MathNet.Numerics
                var stats = new DescriptiveStatistics(values);
                
                // Calculate update rate from timestamps (separate connection)
                var timestamps = await GetPointTimestampsAsync(point.IdSeq, windowHours, cancellationToken);
                var updateRateHz = CalculateUpdateRate(timestamps);

                results.Add(new BehaviorResult
                {
                    PointId = point.Id,
                    PointIdSeq = point.IdSeq,
                    PointName = point.Name,
                    SampleCount = (int)summary.SampleCount,
                    WindowStart = summary.WindowStart,
                    WindowEnd = summary.WindowEnd,
                    MeanValue = stats.Mean,
                    StdDeviation = stats.StandardDeviation,
                    MinValue = summary.MinValue,
                    MaxValue = summary.MaxValue,
                    UpdateRateHz = updateRateHz,
                    CalculatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error processing point {PointName} ({PointId})", point.Name, point.IdSeq);
            }
        }

        return results;
    }

    private async Task<Dictionary<long, QuestDbSummary>> GetPointSummariesAsync(
        long[] pointIdSeqs,  // Fixed: Changed to long[]
        int windowHours,
        int minSamples,
        CancellationToken cancellationToken)
    {
        var summaryByPointSeq = new Dictionary<long, QuestDbSummary>();
        
        try
        {
            await using var conn = new NpgsqlConnection(_questDbConnectionString);
            await conn.OpenAsync(cancellationToken);

            // Build point filter
            var pointFilter = string.Join(",", pointIdSeqs);
            
            var sql = $@"
                SELECT * FROM (
                    SELECT 
                        point_id,
                        count(*) as sample_count,
                        min(CAST(value AS DOUBLE)) as min_val,
                        max(CAST(value AS DOUBLE)) as max_val,
                        avg(CAST(value AS DOUBLE)) as avg_val,
                        min(timestamp) as window_start,
                        max(timestamp) as window_end
                    FROM point_data
                    WHERE point_id IN ({pointFilter})
                      AND timestamp > dateadd('h', -{windowHours}, now())
                    GROUP BY point_id
                )
                WHERE sample_count >= {minSamples}
            ";

            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var pointIdSeq = reader.GetInt64(0);  // Fixed: Use Int64 for BIGINT/LONG column
                summaryByPointSeq[pointIdSeq] = new QuestDbSummary
                {
                    SampleCount = reader.GetInt64(1),
                    MinValue = reader.GetDouble(2),
                    MaxValue = reader.GetDouble(3),
                    AvgValue = reader.GetDouble(4),
                    WindowStart = reader.GetDateTime(5),
                    WindowEnd = reader.GetDateTime(6)
                };
            }
        }
        catch (Npgsql.NpgsqlException ex) when (ex.Message.Contains("does not exist"))
        {
            // QuestDB table doesn't exist yet - waiting for first ingested data
            _logger.LogDebug("QuestDB table 'point_data' does not exist yet");
        }

        return summaryByPointSeq;
    }

    private async Task<List<double>> GetPointValuesAsync(
        long pointIdSeq,  // Fixed: Use long for BIGINT column
        int windowHours,
        CancellationToken cancellationToken)
    {
        var values = new List<double>();
        
        try
        {
            // Use dedicated connection for this query
            await using var conn = new NpgsqlConnection(_questDbConnectionString);
            await conn.OpenAsync(cancellationToken);

            // Sample up to 10000 values for stddev calculation
            var sql = $@"
                SELECT value
                FROM point_data
                WHERE point_id = {pointIdSeq}
                  AND timestamp > dateadd('h', -{windowHours}, now())
                ORDER BY timestamp DESC
                LIMIT 10000
            ";

            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                    values.Add(reader.GetDouble(0));
            }
        }
        catch (Npgsql.NpgsqlException ex) when (ex.Message.Contains("does not exist"))
        {
            // Table doesn't exist yet, return empty list
        }

        return values;
    }

    private async Task<List<DateTime>> GetPointTimestampsAsync(
        long pointIdSeq,  // Fixed: Use long for BIGINT column
        int windowHours,
        CancellationToken cancellationToken)
    {
        var timestamps = new List<DateTime>();
        
        try
        {
            // Use dedicated connection for this query
            await using var conn = new NpgsqlConnection(_questDbConnectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = $@"
                SELECT timestamp
                FROM point_data
                WHERE point_id = {pointIdSeq}
                  AND timestamp > dateadd('h', -{windowHours}, now())
                ORDER BY timestamp DESC
                LIMIT 1000
            ";

            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                timestamps.Add(reader.GetDateTime(0));
            }
        }
        catch (Npgsql.NpgsqlException ex) when (ex.Message.Contains("does not exist"))
        {
            // Table doesn't exist yet, return empty list
        }

        return timestamps;
    }

    private double CalculateUpdateRate(List<DateTime> timestamps)
    {
        if (timestamps.Count < 2)
            return 0;

        var intervals = new List<double>();
        for (int i = 1; i < timestamps.Count; i++)
        {
            var interval = (timestamps[i - 1] - timestamps[i]).TotalMilliseconds;
            if (interval > 0)
                intervals.Add(interval);
        }

        if (intervals.Count == 0)
            return 0;

        var medianMs = intervals.OrderBy(x => x).ElementAt(intervals.Count / 2);
        return medianMs > 0 ? 1000.0 / medianMs : 0;
    }

    private async Task StoreBehaviorsAsync(
        List<BehaviorResult> behaviors,
        CancellationToken cancellationToken)
    {
        if (behaviors.Count == 0)
            return;

        var db = _redis.GetDatabase();
        var batch = db.CreateBatch();
        var ttl = TimeSpan.FromHours(_options.BehavioralAggregator.CacheTtlHours);

        // Store in Redis for fast lookups during correlation
        foreach (var behavior in behaviors)
        {
            var key = $"naia:behavior:{behavior.PointIdSeq}";
            var json = JsonSerializer.Serialize(behavior);
            batch.StringSetAsync(key, json, ttl);
        }

        batch.Execute();

        // Also upsert to PostgreSQL for persistence
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            INSERT INTO behavioral_stats (
                point_id, point_id_seq, point_name, sample_count,
                window_start, window_end, mean_value, std_deviation,
                min_value, max_value, update_rate_hz, calculated_at
            ) VALUES (
                @PointId, @PointIdSeq, @PointName, @SampleCount,
                @WindowStart, @WindowEnd, @MeanValue, @StdDeviation,
                @MinValue, @MaxValue, @UpdateRateHz, @CalculatedAt
            )
            ON CONFLICT (point_id) DO UPDATE SET
                sample_count = EXCLUDED.sample_count,
                window_start = EXCLUDED.window_start,
                window_end = EXCLUDED.window_end,
                mean_value = EXCLUDED.mean_value,
                std_deviation = EXCLUDED.std_deviation,
                min_value = EXCLUDED.min_value,
                max_value = EXCLUDED.max_value,
                update_rate_hz = EXCLUDED.update_rate_hz,
                calculated_at = EXCLUDED.calculated_at
        ";

        foreach (var behavior in behaviors)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@PointId", behavior.PointId);
            cmd.Parameters.AddWithValue("@PointIdSeq", behavior.PointIdSeq);
            cmd.Parameters.AddWithValue("@PointName", behavior.PointName);
            cmd.Parameters.AddWithValue("@SampleCount", behavior.SampleCount);
            cmd.Parameters.AddWithValue("@WindowStart", behavior.WindowStart);
            cmd.Parameters.AddWithValue("@WindowEnd", behavior.WindowEnd);
            cmd.Parameters.AddWithValue("@MeanValue", behavior.MeanValue);
            cmd.Parameters.AddWithValue("@StdDeviation", behavior.StdDeviation);
            cmd.Parameters.AddWithValue("@MinValue", behavior.MinValue);
            cmd.Parameters.AddWithValue("@MaxValue", behavior.MaxValue);
            cmd.Parameters.AddWithValue("@UpdateRateHz", behavior.UpdateRateHz);
            cmd.Parameters.AddWithValue("@CalculatedAt", behavior.CalculatedAt);
            
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

internal sealed record PointInfo
{
    public Guid Id { get; init; }
    public long IdSeq { get; init; }  // Fixed: Use long to match BIGINT column
    public required string Name { get; init; }
    public Guid DataSourceId { get; init; }
}

internal sealed record QuestDbSummary
{
    public long SampleCount { get; init; }
    public double MinValue { get; init; }
    public double MaxValue { get; init; }
    public double AvgValue { get; init; }
    public DateTime WindowStart { get; init; }
    public DateTime WindowEnd { get; init; }
}

internal sealed record BehaviorResult
{
    public Guid PointId { get; init; }
    public long PointIdSeq { get; init; }  // Fixed: Use long for BIGINT column
    public required string PointName { get; init; }
    public int SampleCount { get; init; }
    public DateTime WindowStart { get; init; }
    public DateTime WindowEnd { get; init; }
    public double MeanValue { get; init; }
    public double StdDeviation { get; init; }
    public double MinValue { get; init; }
    public double MaxValue { get; init; }
    public double UpdateRateHz { get; init; }
    public DateTime CalculatedAt { get; init; }
}
