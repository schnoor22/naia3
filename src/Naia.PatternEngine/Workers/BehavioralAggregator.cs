using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Naia.PatternEngine.Events;
using Naia.PatternEngine.Services;
using StackExchange.Redis;

namespace Naia.PatternEngine.Workers;

/// <summary>
/// Consumes raw datapoints from naia.datapoints, maintains sliding-window behavioral statistics,
/// and publishes PointBehaviorUpdated events when sufficient samples accumulate.
/// 
/// This is the entry point of the Pattern Flywheel - it transforms raw time-series data
/// into behavioral fingerprints that downstream workers use for correlation and clustering.
/// </summary>
public sealed class BehavioralAggregator : BaseKafkaConsumer<DataPointMessage>
{
    private readonly IPatternEventPublisher _eventPublisher;
    private readonly IConnectionMultiplexer _redis;
    private readonly BehavioralAggregatorOptions _options;
    private readonly PatternKafkaOptions _kafkaOptions;
    
    // In-memory buffer for accumulating stats before Redis write
    private readonly ConcurrentDictionary<Guid, PointBehaviorBuffer> _buffers = new();
    private readonly Timer _publishTimer;
    private readonly object _publishLock = new();
    private volatile bool _isPublishing;

    public BehavioralAggregator(
        ILogger<BehavioralAggregator> logger,
        IOptions<PatternFlywheelOptions> options,
        IPatternEventPublisher eventPublisher,
        IConnectionMultiplexer redis)
        : base(
            logger,
            options.Value.Kafka.BootstrapServers,
            options.Value.Kafka.BehavioralAggregatorGroupId,
            options.Value.Kafka.DataPointsTopic)
    {
        _eventPublisher = eventPublisher;
        _redis = redis;
        _options = options.Value.BehavioralAggregator;
        _kafkaOptions = options.Value.Kafka;
        
        // Timer to periodically publish accumulated behavior events
        _publishTimer = new Timer(
            PublishTimerCallback,
            null,
            TimeSpan.FromSeconds(_options.PublishIntervalSeconds),
            TimeSpan.FromSeconds(_options.PublishIntervalSeconds));
    }

    protected override async Task ProcessMessageAsync(
        DataPointMessage message, 
        string key, 
        CancellationToken cancellationToken)
    {
        var pointId = message.PointId;
        
        // Get or create buffer for this point
        var buffer = _buffers.GetOrAdd(pointId, _ => new PointBehaviorBuffer(pointId));
        
        // Update running statistics
        buffer.AddSample(message.Value, message.Timestamp, message.Quality);
        
        // If buffer exceeds memory limit, evict oldest entries
        if (_buffers.Count > _options.MaxPointsInMemory)
        {
            EvictOldestBuffers();
        }
    }

    private void PublishTimerCallback(object? state)
    {
        if (_isPublishing) return;
        
        lock (_publishLock)
        {
            if (_isPublishing) return;
            _isPublishing = true;
        }

        try
        {
            _ = PublishReadyBehaviorsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in publish timer callback");
        }
        finally
        {
            _isPublishing = false;
        }
    }

    private async Task PublishReadyBehaviorsAsync(CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow;
        var publishBatch = new List<(string Key, PointBehaviorUpdated Event)>();
        var pointsProcessed = 0;

        foreach (var kvp in _buffers)
        {
            var pointId = kvp.Key;
            var buffer = kvp.Value;

            // Only publish if we have enough samples
            if (buffer.SampleCount < _options.MinSamplesForBehavior)
                continue;

            // Check if we already published recently (via Redis)
            var redisKey = $"{_options.RedisKeyPrefix}{pointId}";
            var lastPublish = await db.StringGetAsync($"{redisKey}:lastPublish");
            
            if (lastPublish.HasValue)
            {
                var lastPublishTime = DateTimeOffset.FromUnixTimeSeconds((long)lastPublish);
                if ((now - lastPublishTime).TotalSeconds < _options.PublishIntervalSeconds)
                    continue;
            }

            // Calculate behavior metrics from buffer
            var behavior = buffer.CalculateBehavior();
            
            // Check if behavior changed significantly from last cached value
            var cachedBehavior = await GetCachedBehaviorAsync(db, redisKey);
            if (cachedBehavior != null && !HasSignificantChange(cachedBehavior, behavior))
                continue;

            // Create event
            var evt = new PointBehaviorUpdated
            {
                PointId = pointId,
                PointIdSeq = 0, // TODO: Look up from PostgreSQL
                PointName = pointId.ToString(), // TODO: Look up from PostgreSQL
                SampleCount = buffer.SampleCount,
                WindowStart = buffer.WindowStart.UtcDateTime,
                WindowEnd = buffer.WindowEnd.UtcDateTime,
                MeanValue = behavior.Mean,
                StdDeviation = behavior.StdDev,
                MinValue = behavior.Min,
                MaxValue = behavior.Max,
                UpdateRateHz = behavior.MedianUpdateRateMs > 0 ? 1000.0 / behavior.MedianUpdateRateMs : 0,
                CreatedAt = now.UtcDateTime
            };

            publishBatch.Add((pointId.ToString(), evt));
            pointsProcessed++;

            // Update Redis cache
            await CacheBehaviorAsync(db, redisKey, behavior, now);
        }

        // Publish batch to Kafka
        if (publishBatch.Count > 0)
        {
            await _eventPublisher.PublishBatchAsync(
                _kafkaOptions.PointsBehaviorTopic,
                publishBatch,
                cancellationToken);

            Logger.LogInformation(
                "Published {Count} PointBehaviorUpdated events",
                publishBatch.Count);
        }
    }

    private async Task<BehaviorMetrics?> GetCachedBehaviorAsync(IDatabase db, string redisKey)
    {
        var hash = await db.HashGetAllAsync($"{redisKey}:metrics");
        if (hash.Length == 0) return null;

        var dict = hash.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());
        
        return new BehaviorMetrics
        {
            Mean = double.Parse(dict.GetValueOrDefault("mean", "0")),
            StdDev = double.Parse(dict.GetValueOrDefault("stddev", "0")),
            Min = double.Parse(dict.GetValueOrDefault("min", "0")),
            Max = double.Parse(dict.GetValueOrDefault("max", "0")),
            MedianUpdateRateMs = double.Parse(dict.GetValueOrDefault("medianRate", "0"))
        };
    }

    private async Task CacheBehaviorAsync(IDatabase db, string redisKey, BehaviorMetrics behavior, DateTimeOffset now)
    {
        var ttl = TimeSpan.FromHours(_options.RedisTtlHours);
        
        var hashEntries = new HashEntry[]
        {
            new("mean", behavior.Mean.ToString("F6")),
            new("stddev", behavior.StdDev.ToString("F6")),
            new("min", behavior.Min.ToString("F6")),
            new("max", behavior.Max.ToString("F6")),
            new("medianRate", behavior.MedianUpdateRateMs.ToString("F2"))
        };

        await db.HashSetAsync($"{redisKey}:metrics", hashEntries);
        await db.KeyExpireAsync($"{redisKey}:metrics", ttl);
        await db.StringSetAsync($"{redisKey}:lastPublish", now.ToUnixTimeSeconds(), ttl);
    }

    private bool HasSignificantChange(BehaviorMetrics cached, BehaviorMetrics current)
    {
        // Consider significant if mean or stddev changed by more than 10%
        var meanChange = Math.Abs(cached.Mean - current.Mean) / Math.Max(Math.Abs(cached.Mean), 0.001);
        var stdDevChange = Math.Abs(cached.StdDev - current.StdDev) / Math.Max(cached.StdDev, 0.001);
        var rateChange = Math.Abs(cached.MedianUpdateRateMs - current.MedianUpdateRateMs) / 
                         Math.Max(cached.MedianUpdateRateMs, 1.0);

        return meanChange > 0.10 || stdDevChange > 0.20 || rateChange > 0.30;
    }

    private void EvictOldestBuffers()
    {
        // Evict buffers with oldest last update
        var toEvict = _buffers
            .OrderBy(x => x.Value.LastUpdate)
            .Take(_options.MaxPointsInMemory / 10)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in toEvict)
        {
            _buffers.TryRemove(key, out _);
        }

        Logger.LogDebug("Evicted {Count} behavioral buffers due to memory pressure", toEvict.Count);
    }

    protected override Task OnStoppingAsync(CancellationToken cancellationToken)
    {
        _publishTimer.Dispose();
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _publishTimer.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Message format for datapoints from naia.datapoints topic
/// </summary>
public sealed class DataPointMessage
{
    public Guid PointId { get; set; }
    public Guid DataSourceId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public double Value { get; set; }
    public int Quality { get; set; }
}

/// <summary>
/// In-memory buffer for accumulating behavioral statistics for a single point
/// </summary>
internal sealed class PointBehaviorBuffer
{
    private readonly Guid _pointId;
    private readonly object _lock = new();
    
    // Welford's online algorithm for running statistics
    private long _count;
    private double _mean;
    private double _m2; // Sum of squared differences
    private double _min = double.MaxValue;
    private double _max = double.MinValue;
    
    // Update rate tracking
    private readonly List<double> _updateIntervals = new();
    private DateTimeOffset? _lastTimestamp;
    
    // Quality tracking
    private int _goodQualityCount;
    private int _zeroCount;
    private double _lastValue;
    private int _changeCount;
    
    public DateTimeOffset WindowStart { get; private set; } = DateTimeOffset.MaxValue;
    public DateTimeOffset WindowEnd { get; private set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastUpdate { get; private set; }
    public int SampleCount => (int)_count;

    public PointBehaviorBuffer(Guid pointId)
    {
        _pointId = pointId;
    }

    public void AddSample(double value, DateTimeOffset timestamp, int quality)
    {
        lock (_lock)
        {
            _count++;
            LastUpdate = DateTimeOffset.UtcNow;

            // Update window bounds
            if (timestamp < WindowStart) WindowStart = timestamp;
            if (timestamp > WindowEnd) WindowEnd = timestamp;

            // Welford's algorithm for running mean/variance
            var delta = value - _mean;
            _mean += delta / _count;
            var delta2 = value - _mean;
            _m2 += delta * delta2;

            // Track min/max
            if (value < _min) _min = value;
            if (value > _max) _max = value;

            // Track update rate
            if (_lastTimestamp.HasValue)
            {
                var interval = (timestamp - _lastTimestamp.Value).TotalMilliseconds;
                if (interval > 0 && _updateIntervals.Count < 10000)
                {
                    _updateIntervals.Add(interval);
                }
            }
            _lastTimestamp = timestamp;

            // Track quality
            if (quality >= 192) _goodQualityCount++; // OPC UA Good quality threshold

            // Track zeros and changes
            if (Math.Abs(value) < double.Epsilon) _zeroCount++;
            if (_count > 1 && Math.Abs(value - _lastValue) > double.Epsilon) _changeCount++;
            _lastValue = value;
        }
    }

    public BehaviorMetrics CalculateBehavior()
    {
        lock (_lock)
        {
            var variance = _count > 1 ? _m2 / (_count - 1) : 0;
            var stdDev = Math.Sqrt(variance);

            // Calculate median and P95 update rates
            double medianRate = 0, p95Rate = 0;
            if (_updateIntervals.Count > 0)
            {
                var sorted = _updateIntervals.OrderBy(x => x).ToList();
                medianRate = sorted[sorted.Count / 2];
                p95Rate = sorted[(int)(sorted.Count * 0.95)];
            }

            return new BehaviorMetrics
            {
                Mean = _mean,
                StdDev = stdDev,
                Min = _min == double.MaxValue ? 0 : _min,
                Max = _max == double.MinValue ? 0 : _max,
                MedianUpdateRateMs = medianRate,
                P95UpdateRateMs = p95Rate,
                ZeroCount = _zeroCount,
                GoodQualityRatio = _count > 0 ? (double)_goodQualityCount / _count : 0,
                ChangeFrequency = _count > 1 ? (double)_changeCount / (_count - 1) : 0
            };
        }
    }
}

/// <summary>
/// Calculated behavioral metrics for a point
/// </summary>
internal sealed class BehaviorMetrics
{
    public double Mean { get; set; }
    public double StdDev { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double MedianUpdateRateMs { get; set; }
    public double P95UpdateRateMs { get; set; }
    public int ZeroCount { get; set; }
    public double GoodQualityRatio { get; set; }
    public double ChangeFrequency { get; set; }
}
