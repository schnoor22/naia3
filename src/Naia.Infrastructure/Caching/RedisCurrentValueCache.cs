using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;
using StackExchange.Redis;

namespace Naia.Infrastructure.Caching;

/// <summary>
/// Redis-backed current value cache for real-time dashboard access.
/// 
/// DESIGN:
/// - Key pattern: naia:cv:{pointSequenceId}
/// - Value: JSON serialized CurrentValue
/// - TTL: Configurable (default 1 hour)
/// 
/// PERFORMANCE:
/// - Sub-millisecond reads for dashboard rendering
/// - Pipeline operations for batch updates
/// - Connection multiplexing for concurrent access
/// </summary>
public sealed class RedisCurrentValueCache : ICurrentValueCache, IAsyncDisposable
{
    private readonly ILogger<RedisCurrentValueCache> _logger;
    private readonly RedisOptions _options;
    private IConnectionMultiplexer? _connection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;
    
    private const string KeyPrefix = "naia:cv:";
    
    public RedisCurrentValueCache(
        IOptions<RedisOptions> options,
        ILogger<RedisCurrentValueCache> logger)
    {
        _options = options.Value;
        _logger = logger;
    }
    
    private async Task<IDatabase> GetDatabaseAsync()
    {
        if (_connection != null && _connection.IsConnected)
            return _connection.GetDatabase();
            
        await _connectionLock.WaitAsync();
        try
        {
            if (_connection != null && _connection.IsConnected)
                return _connection.GetDatabase();
                
            var configOptions = ConfigurationOptions.Parse(_options.ConnectionString);
            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectRetry = 3;
            configOptions.ConnectTimeout = 5000;
            
            _connection = await ConnectionMultiplexer.ConnectAsync(configOptions);
            _logger.LogInformation("Redis connection established: {Endpoint}", _options.ConnectionString);
            
            return _connection.GetDatabase();
        }
        finally
        {
            _connectionLock.Release();
        }
    }
    
    public async Task<CurrentValue?> GetAsync(long pointSequenceId, CancellationToken cancellationToken = default)
    {
        var db = await GetDatabaseAsync();
        var key = $"{KeyPrefix}{pointSequenceId}";
        
        var value = await db.StringGetAsync(key);
        if (value.IsNullOrEmpty)
            return null;
            
        return JsonSerializer.Deserialize<CurrentValue>(value!);
    }
    
    public async Task<IReadOnlyDictionary<long, CurrentValue>> GetManyAsync(
        IEnumerable<long> pointSequenceIds,
        CancellationToken cancellationToken = default)
    {
        var db = await GetDatabaseAsync();
        var ids = pointSequenceIds.ToList();
        var keys = ids.Select(id => (RedisKey)$"{KeyPrefix}{id}").ToArray();
        
        var values = await db.StringGetAsync(keys);
        var results = new Dictionary<long, CurrentValue>();
        
        for (var i = 0; i < values.Length; i++)
        {
            if (!values[i].IsNullOrEmpty)
            {
                var cv = JsonSerializer.Deserialize<CurrentValue>(values[i]!);
                if (cv != null)
                    results[ids[i]] = cv;
            }
        }
        
        return results;
    }
    
    public async Task SetAsync(CurrentValue value, CancellationToken cancellationToken = default)
    {
        var db = await GetDatabaseAsync();
        var key = $"{KeyPrefix}{value.PointSequenceId}";
        var json = JsonSerializer.Serialize(value);
        
        await db.StringSetAsync(key, json, TimeSpan.FromSeconds(_options.CurrentValueTtlSeconds));
    }
    
    public async Task SetManyAsync(IEnumerable<CurrentValue> values, CancellationToken cancellationToken = default)
    {
        var db = await GetDatabaseAsync();
        var batch = db.CreateBatch();
        var tasks = new List<Task>();
        var ttl = TimeSpan.FromSeconds(_options.CurrentValueTtlSeconds);
        
        foreach (var value in values)
        {
            var key = $"{KeyPrefix}{value.PointSequenceId}";
            var json = JsonSerializer.Serialize(value);
            tasks.Add(batch.StringSetAsync(key, json, ttl));
        }
        
        batch.Execute();
        await Task.WhenAll(tasks);
        
        _logger.LogDebug("Updated {Count} current values in Redis", tasks.Count);
    }
    
    public async Task RemoveAsync(long pointSequenceId, CancellationToken cancellationToken = default)
    {
        var db = await GetDatabaseAsync();
        var key = $"{KeyPrefix}{pointSequenceId}";
        await db.KeyDeleteAsync(key);
    }
    
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = await GetDatabaseAsync();
            await db.PingAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis health check failed");
            return false;
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        if (_connection != null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
            _connection = null;
        }
        
        _connectionLock.Dispose();
        _logger.LogInformation("Redis connection disposed");
    }
}

/// <summary>
/// Redis configuration options.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";
    
    /// <summary>Redis connection string</summary>
    public string ConnectionString { get; set; } = "localhost:6379";
    
    /// <summary>TTL for current value cache entries (seconds)</summary>
    public int CurrentValueTtlSeconds { get; set; } = 3600; // 1 hour
    
    /// <summary>TTL for idempotency keys (seconds)</summary>
    public int IdempotencyTtlSeconds { get; set; } = 86400; // 24 hours
}
