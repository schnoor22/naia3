using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Naia.PatternEngine.Events;
using Naia.PatternEngine.Services;
using Npgsql;
using StackExchange.Redis;

namespace Naia.PatternEngine.Workers;

/// <summary>
/// Consumes PointBehaviorUpdated events and calculates pairwise correlations
/// between points that have similar behavioral fingerprints.
/// 
/// Uses QuestDB to efficiently compute correlations over sliding windows,
/// and Redis to cache correlation pairs and detect significant changes.
/// </summary>
public sealed class CorrelationProcessor : BaseKafkaConsumer<PointBehaviorUpdated>
{
    private readonly IPatternEventPublisher _eventPublisher;
    private readonly IConnectionMultiplexer _redis;
    private readonly CorrelationProcessorOptions _options;
    private readonly PatternKafkaOptions _kafkaOptions;
    private readonly string _questDbConnectionString;
    
    // Buffer behavior updates for batch correlation processing
    private readonly ConcurrentQueue<PointBehaviorUpdated> _behaviorQueue = new();
    private readonly Timer _correlationTimer;
    private readonly SemaphoreSlim _processLock = new(1, 1);

    public CorrelationProcessor(
        ILogger<CorrelationProcessor> logger,
        IOptions<PatternFlywheelOptions> options,
        IPatternEventPublisher eventPublisher,
        IConnectionMultiplexer redis,
        string questDbConnectionString)
        : base(
            logger,
            options.Value.Kafka.BootstrapServers,
            options.Value.Kafka.CorrelationProcessorGroupId,
            options.Value.Kafka.PointsBehaviorTopic)
    {
        _eventPublisher = eventPublisher;
        _redis = redis;
        _options = options.Value.CorrelationProcessor;
        _kafkaOptions = options.Value.Kafka;
        _questDbConnectionString = questDbConnectionString;
        
        // Process correlations every 30 seconds
        _correlationTimer = new Timer(
            CorrelationTimerCallback,
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    protected override Task ProcessMessageAsync(
        PointBehaviorUpdated message, 
        string key, 
        CancellationToken cancellationToken)
    {
        // Queue for batch processing
        _behaviorQueue.Enqueue(message);
        return Task.CompletedTask;
    }

    private async void CorrelationTimerCallback(object? state)
    {
        if (!await _processLock.WaitAsync(0))
            return;

        try
        {
            await ProcessCorrelationBatchAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in correlation timer callback");
        }
        finally
        {
            _processLock.Release();
        }
    }

    private async Task ProcessCorrelationBatchAsync(CancellationToken cancellationToken)
    {
        // Drain the queue
        var behaviors = new List<PointBehaviorUpdated>();
        while (_behaviorQueue.TryDequeue(out var behavior))
        {
            behaviors.Add(behavior);
        }

        if (behaviors.Count == 0) return;

        Logger.LogDebug("Processing correlations for {Count} behavior updates", behaviors.Count);

        // Group by similar behavioral fingerprints to reduce correlation pairs
        var groups = GroupByBehavioralSimilarity(behaviors);
        
        var db = _redis.GetDatabase();
        var publishBatch = new List<(string Key, CorrelationsUpdated Event)>();

        foreach (var group in groups)
        {
            if (group.Count < 2) continue;

            // Calculate pairwise correlations within the group
            var correlations = await CalculateCorrelationsAsync(group, cancellationToken);

            foreach (var corr in correlations)
            {
                // Check if this is a significant change from cached value
                var cacheKey = GetCorrelationCacheKey(corr.PointId1, corr.PointId2);
                var cachedValue = await db.StringGetAsync(cacheKey);

                if (cachedValue.HasValue)
                {
                    var cachedCorrelation = (double)cachedValue;
                    var change = Math.Abs(cachedCorrelation - corr.Correlation);
                    
                    if (change < _options.ChangeThresholdForPublish)
                        continue; // No significant change
                }

                // Only publish correlations above threshold
                if (Math.Abs(corr.Correlation) < _options.MinCorrelationThreshold)
                {
                    // Cache the low correlation to avoid recalculating
                    await db.StringSetAsync(cacheKey, corr.Correlation, 
                        TimeSpan.FromHours(_options.RedisTtlHours));
                    continue;
                }

                // Create event
                var evt = new CorrelationsUpdated
                {
                    CorrelationBatchId = Guid.NewGuid().ToString(),
                    PointIds = new List<Guid> { corr.PointId1, corr.PointId2 },
                    SignificantCorrelationCount = 1,
                    AverageCorrelation = Math.Abs(corr.Correlation),
                    WindowStart = corr.WindowStart.UtcDateTime,
                    WindowEnd = corr.WindowEnd.UtcDateTime,
                    CreatedAt = DateTime.UtcNow
                };

                publishBatch.Add(($"{corr.PointId1}:{corr.PointId2}", evt));

                // Update cache
                await db.StringSetAsync(cacheKey, corr.Correlation, 
                    TimeSpan.FromHours(_options.RedisTtlHours));
            }
        }

        if (publishBatch.Count > 0)
        {
            await _eventPublisher.PublishBatchAsync(
                _kafkaOptions.CorrelationsUpdatedTopic,
                publishBatch,
                cancellationToken);

            Logger.LogInformation(
                "Published {Count} CorrelationsUpdated events from {Behaviors} behaviors",
                publishBatch.Count, behaviors.Count);
        }
    }

    /// <summary>
    /// Group points by behavioral similarity to reduce correlation calculation pairs.
    /// Points with very different behaviors are unlikely to correlate.
    /// </summary>
    private List<List<PointBehaviorUpdated>> GroupByBehavioralSimilarity(
        List<PointBehaviorUpdated> behaviors)
    {
        // Simple grouping by update rate buckets
        // Points updating at very different rates are unlikely to correlate
        var groups = new Dictionary<string, List<PointBehaviorUpdated>>();

        foreach (var b in behaviors)
        {
            var rateBucket = GetRateBucket(b.UpdateRateHz);
            var rangeBucket = GetRangeBucket(b.MinValue, b.MaxValue);
            var key = $"{rateBucket}:{rangeBucket}";

            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<PointBehaviorUpdated>();
                groups[key] = group;
            }
            group.Add(b);
        }

        return groups.Values.ToList();
    }

    private string GetRateBucket(double updateRateHz)
    {
        return updateRateHz switch
        {
            > 10 => "fast",      // > 10 Hz
            > 1 => "medium",     // 1-10 Hz
            > 0.1 => "slow",     // 0.1-1 Hz
            _ => "veryslow"      // < 0.1 Hz
        };
    }

    private string GetRangeBucket(double min, double max)
    {
        var range = max - min;
        return range switch
        {
            < 1 => "tiny",
            < 100 => "small",
            < 10000 => "medium",
            _ => "large"
        };
    }

    private async Task<List<CorrelationResult>> CalculateCorrelationsAsync(
        List<PointBehaviorUpdated> group,
        CancellationToken cancellationToken)
    {
        var results = new List<CorrelationResult>();
        
        // Limit pairs to prevent explosion
        var pointIds = group.Select(g => g.PointId).Distinct().Take(50).ToList();
        
        if (pointIds.Count < 2) return results;

        var windowStart = DateTimeOffset.UtcNow.AddHours(-_options.CorrelationWindowHours);
        
        await using var conn = new NpgsqlConnection(_questDbConnectionString);
        await conn.OpenAsync(cancellationToken);

        // Calculate correlations for each pair
        for (var i = 0; i < pointIds.Count - 1; i++)
        {
            for (var j = i + 1; j < pointIds.Count; j++)
            {
                var corr = await CalculatePairCorrelationAsync(
                    conn, pointIds[i], pointIds[j], windowStart, cancellationToken);

                if (corr != null)
                {
                    results.Add(corr);
                }
            }
        }

        return results;
    }

    private async Task<CorrelationResult?> CalculatePairCorrelationAsync(
        NpgsqlConnection conn,
        Guid pointId1,
        Guid pointId2,
        DateTimeOffset windowStart,
        CancellationToken cancellationToken)
    {
        try
        {
            // QuestDB correlation query using time-aligned samples
            // Uses ASOF JOIN to align timestamps between two point series
            var sql = @"
                WITH aligned AS (
                    SELECT 
                        a.timestamp,
                        a.value as value1,
                        b.value as value2
                    FROM point_data a
                    ASOF JOIN point_data b ON (b.point_id = $2)
                    WHERE a.point_id = $1
                    AND a.timestamp >= $3
                    AND a.timestamp <= now()
                )
                SELECT 
                    COUNT(*) as sample_count,
                    corr(value1, value2) as correlation,
                    MIN(timestamp) as window_start,
                    MAX(timestamp) as window_end
                FROM aligned
                WHERE value2 IS NOT NULL
            ";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue(pointId1);
            cmd.Parameters.AddWithValue(pointId2);
            cmd.Parameters.AddWithValue(windowStart.UtcDateTime);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            
            if (await reader.ReadAsync(cancellationToken))
            {
                var sampleCount = reader.GetInt64(0);
                
                if (sampleCount < _options.MinSamplesForCorrelation)
                    return null;

                var correlation = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
                var windowStartResult = reader.IsDBNull(2) ? windowStart : new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero);
                var windowEnd = reader.IsDBNull(3) ? DateTimeOffset.UtcNow : new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero);

                // Calculate approximate p-value using Fisher transformation
                var pValue = CalculatePValue(correlation, (int)sampleCount);

                return new CorrelationResult
                {
                    PointId1 = pointId1,
                    PointId2 = pointId2,
                    Correlation = correlation,
                    PValue = pValue,
                    SampleCount = (int)sampleCount,
                    WindowStart = windowStartResult,
                    WindowEnd = windowEnd,
                    LagMs = 0, // TODO: Calculate lag with cross-correlation
                    IsLeading = false
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, 
                "Error calculating correlation between {PointId1} and {PointId2}",
                pointId1, pointId2);
            return null;
        }
    }

    private double CalculatePValue(double r, int n)
    {
        if (n <= 2) return 1.0;
        
        // Fisher z-transformation
        var z = 0.5 * Math.Log((1 + r) / (1 - r));
        var se = 1.0 / Math.Sqrt(n - 3);
        var zScore = Math.Abs(z / se);
        
        // Approximate p-value from z-score (two-tailed)
        return 2 * (1 - NormalCdf(zScore));
    }

    private double NormalCdf(double z)
    {
        // Approximation of normal CDF
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var sign = z < 0 ? -1 : 1;
        z = Math.Abs(z) / Math.Sqrt(2);
        var t = 1.0 / (1.0 + p * z);
        var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-z * z);
        
        return 0.5 * (1.0 + sign * y);
    }

    private string GetCorrelationCacheKey(Guid id1, Guid id2)
    {
        // Ensure consistent key ordering
        var (first, second) = id1.CompareTo(id2) < 0 ? (id1, id2) : (id2, id1);
        return $"{_options.RedisKeyPrefix}{first}:{second}";
    }

    protected override Task OnStoppingAsync(CancellationToken cancellationToken)
    {
        _correlationTimer.Dispose();
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _correlationTimer.Dispose();
        _processLock.Dispose();
        base.Dispose();
    }
}

internal sealed class CorrelationResult
{
    public Guid PointId1 { get; set; }
    public Guid PointId2 { get; set; }
    public double Correlation { get; set; }
    public double PValue { get; set; }
    public int SampleCount { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset WindowEnd { get; set; }
    public double LagMs { get; set; }
    public bool IsLeading { get; set; }
}
