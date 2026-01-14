using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;
using Naia.Infrastructure.Telemetry;
using Npgsql;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Naia.Infrastructure.TimeSeries;

/// <summary>
/// QuestDB time-series writer using HTTP ILP (InfluxDB Line Protocol).
/// 
/// RESILIENCE:
/// - Polly retry policy with exponential backoff (3 attempts)
/// - Circuit breaker to prevent cascading failures
/// - Dead letter queue for failed writes (PostgreSQL)
/// 
/// PERFORMANCE NOTES:
/// - Uses InfluxDB Line Protocol (ILP) for maximum write throughput
/// - HTTP transport for simplicity and reliability
/// - Batching for efficiency
/// 
/// DURABILITY:
/// - HTTP transport with retries
/// - WAL (Write-Ahead Log) enabled on QuestDB server
/// - Dead letter queue for recovery of failed writes
/// 
/// ILP FORMAT:
/// table_name,tag1=value1 field1=value1,field2=value2 timestamp_nanos
/// </summary>
public sealed class QuestDbTimeSeriesWriter : ITimeSeriesWriter, IAsyncDisposable
{
    private readonly ILogger<QuestDbTimeSeriesWriter> _logger;
    private readonly QuestDbOptions _options;
    private readonly HttpClient _httpClient;
    private readonly StringBuilder _lineBuffer = new();
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly string? _deadLetterConnectionString;
    private bool _disposed;
    
    public QuestDbTimeSeriesWriter(
        IOptions<QuestDbOptions> options,
        ILogger<QuestDbTimeSeriesWriter> logger,
        IConfiguration? configuration = null)
    {
        _options = options.Value;
        _logger = logger;
        _deadLetterConnectionString = configuration?.GetConnectionString("DefaultConnection");
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.HttpEndpoint),
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        // Build resilience pipeline with retry and circuit breaker
        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning("QuestDB write retry {Attempt} after {Delay}ms", 
                        args.AttemptNumber, args.RetryDelay.TotalMilliseconds);
                    return default;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromMinutes(1),
                OnOpened = args =>
                {
                    _logger.LogError("QuestDB circuit breaker OPENED - writes will fail fast for 1 minute");
                    return default;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("QuestDB circuit breaker CLOSED - normal operation resumed");
                    return default;
                }
            })
            .Build();
        
        _logger.LogInformation("QuestDB writer initialized with resilience: {Endpoint}", _options.HttpEndpoint);
    }
    
    public async Task WriteAsync(DataPointBatch batch, CancellationToken cancellationToken = default)
    {
        if (batch.IsEmpty)
            return;
        
        // Build ILP lines with \n line endings (InfluxDB standard)
        var linesList = new List<string>();
        var pointsToWrite = new List<(long PointId, DateTime Timestamp, double Value, int Quality)>();
        long microsecondOffset = 0;  // Ensure unique timestamps by adding microsecond offsets
        
        foreach (var point in batch.Points)
        {
            // Validate value - must be finite
            if (!double.IsFinite(point.Value))
            {
                _logger.LogWarning("Skipping point {PointId} with invalid value: {Value}", 
                    point.PointSequenceId, point.Value);
                continue;
            }
            
            // Format: table field1=value1,field2=value2 timestamp
            // Type suffixes: i=long, d=double (InfluxDB standard)
            // Convert to nanoseconds, adding microsecond offset to ensure uniqueness
            var baseTimestampNanos = ((DateTimeOffset)point.Timestamp).ToUnixTimeMilliseconds() * 1_000_000;
            var timestampNanos = baseTimestampNanos + microsecondOffset;
            microsecondOffset += 1000;  // Add 1 microsecond (1000 nanoseconds) per point for uniqueness
            
            // Quality: 1 for Good, 0 for Bad (LONG column)
            var qualityInt = point.Quality == DataQuality.Good ? 1 : 0;
            
            // Use type suffixes: i=long (point_id and quality), d=double (value per ILP spec)
            var line = $"{_options.TableName} point_id={point.PointSequenceId}i,value={point.Value}d,quality={qualityInt}i {timestampNanos}";
            linesList.Add(line);
            pointsToWrite.Add((point.PointSequenceId, point.Timestamp, point.Value, qualityInt));
        }
        
        if (linesList.Count == 0)
            return;
        
        var ilpContent = string.Join("\n", linesList) + "\n";  // Trailing newline for proper ILP format
        
        _logger.LogDebug("Writing {Lines} lines to QuestDB (batch {BatchId})", 
            batch.Points.Count, batch.BatchId);

        try
        {
            // Use resilience pipeline for write operation with metrics
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var span = NaiaMetrics.StartSpan("questdb.write");
            span?.SetTag("batch.id", batch.BatchId ?? "unknown");
            span?.SetTag("batch.size", linesList.Count);
            
            await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var content = new StringContent(ilpContent, Encoding.UTF8, "text/plain");
                var response = await _httpClient.PostAsync("/write", content, ct);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError("QuestDB write failed. Batch: {BatchId}, Status: {StatusCode}, Error: {Error}", 
                        batch.BatchId, response.StatusCode, error);
                    NaiaMetrics.DataPointsWritten.WithLabels("questdb", "error").Inc(linesList.Count);
                    throw new InvalidOperationException($"QuestDB write failed: {response.StatusCode} - {error}");
                }
                
                _logger.LogDebug("Wrote {Count} points to QuestDB", batch.Count);
                NaiaMetrics.DataPointsWritten.WithLabels("questdb", "success").Inc(linesList.Count);
                stopwatch.Stop();
                NaiaMetrics.DataPointWriteLatency.WithLabels("questdb").Observe(stopwatch.Elapsed.TotalSeconds);
            }, cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogError("QuestDB circuit breaker is open - sending batch {BatchId} to dead letter queue", batch.BatchId);
            NaiaMetrics.DataPointsWritten.WithLabels("questdb", "circuit_breaker").Inc(pointsToWrite.Count);
            await WriteToDeadLetterQueueAsync(pointsToWrite, batch.BatchId, "Circuit breaker open", cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write batch {BatchId} to QuestDB after all retries - sending to dead letter queue", batch.BatchId);
            NaiaMetrics.DataPointsWritten.WithLabels("questdb", "dlq").Inc(pointsToWrite.Count);
            await WriteToDeadLetterQueueAsync(pointsToWrite, batch.BatchId, ex.Message, cancellationToken);
            throw;
        }
    }
    
    private async Task WriteToDeadLetterQueueAsync(
        List<(long PointId, DateTime Timestamp, double Value, int Quality)> points,
        string? batchId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_deadLetterConnectionString))
        {
            _logger.LogWarning("Dead letter queue not configured - {Count} points lost", points.Count);
            return;
        }
        
        try
        {
            await using var conn = new NpgsqlConnection(_deadLetterConnectionString);
            await conn.OpenAsync(cancellationToken);
            
            const string sql = @"
                INSERT INTO questdb_dead_letters (point_id, timestamp, value, quality, batch_id, error_message)
                VALUES (@pointId, @timestamp, @value, @quality, @batchId, @error)";
            
            foreach (var point in points)
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@pointId", point.PointId);
                cmd.Parameters.AddWithValue("@timestamp", point.Timestamp);
                cmd.Parameters.AddWithValue("@value", point.Value);
                cmd.Parameters.AddWithValue("@quality", point.Quality);
                cmd.Parameters.AddWithValue("@batchId", (object?)batchId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@error", errorMessage);
                
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            
            _logger.LogWarning("Wrote {Count} points to dead letter queue", points.Count);
            NaiaMetrics.DeadLetterQueueSize.Inc(points.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write to dead letter queue - {Count} points lost!", points.Count);
        }
    }
    
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // HTTP writes are synchronous, nothing to flush
        await Task.CompletedTask;
    }
    
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QuestDB health check failed");
            return false;
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        _httpClient.Dispose();
        _logger.LogInformation("QuestDB writer disposed");
        await Task.CompletedTask;
    }
}
