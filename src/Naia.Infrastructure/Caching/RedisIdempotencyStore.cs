using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using StackExchange.Redis;

namespace Naia.Infrastructure.Caching;

/// <summary>
/// Redis-backed idempotency store for duplicate message detection.
/// 
/// PATTERN:
/// - Key: naia:idempotency:{idempotencyKey}
/// - Value: Timestamp when message was processed
/// - TTL: 24 hours (covers normal retries + manual replay)
/// 
/// USAGE:
/// 1. Before processing: Check if key exists → if yes, skip (duplicate)
/// 2. After successful processing: Set key with TTL
/// 3. Kafka offset committed AFTER idempotency key set
/// 
/// This ensures exactly-once processing semantics:
/// - If processing fails before idempotency set → message reprocessed
/// - If processing succeeds but offset commit fails → idempotency prevents re-execution
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore, IAsyncDisposable
{
    private readonly ILogger<RedisIdempotencyStore> _logger;
    private readonly RedisOptions _options;
    private IConnectionMultiplexer? _connection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;
    
    private const string KeyPrefix = "naia:idempotency:";
    
    public TimeSpan KeyTtl => TimeSpan.FromSeconds(_options.IdempotencyTtlSeconds);
    
    public RedisIdempotencyStore(
        IOptions<RedisOptions> options,
        ILogger<RedisIdempotencyStore> logger)
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
            _logger.LogInformation("Redis idempotency store connected: {Endpoint}", _options.ConnectionString);
            
            return _connection.GetDatabase();
        }
        finally
        {
            _connectionLock.Release();
        }
    }
    
    public async Task<(bool IsDuplicate, DateTime? ProcessedAt)> CheckAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var db = await GetDatabaseAsync();
        var key = $"{KeyPrefix}{idempotencyKey}";
        
        var value = await db.StringGetAsync(key);
        
        if (value.IsNullOrEmpty)
        {
            return (false, null);
        }
        
        if (DateTime.TryParse(value, out var processedAt))
        {
            _logger.LogDebug("Duplicate detected: {Key} (processed at {ProcessedAt})", idempotencyKey, processedAt);
            return (true, processedAt);
        }
        
        // Key exists but value is not parseable - treat as duplicate anyway
        return (true, null);
    }
    
    public async Task MarkProcessedAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var db = await GetDatabaseAsync();
        var key = $"{KeyPrefix}{idempotencyKey}";
        var value = DateTime.UtcNow.ToString("O");
        
        await db.StringSetAsync(key, value, KeyTtl);
        
        _logger.LogDebug("Marked as processed: {Key}", idempotencyKey);
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
        _logger.LogInformation("Redis idempotency store disposed");
    }
}
