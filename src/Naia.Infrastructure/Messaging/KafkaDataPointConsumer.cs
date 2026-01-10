using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;

namespace Naia.Infrastructure.Messaging;

/// <summary>
/// Kafka consumer for data point batches with manual offset commits.
/// 
/// DESIGN PRINCIPLES:
/// 1. MANUAL OFFSET COMMITS ONLY - Never auto-commit
/// 2. At-least-once delivery - Use idempotency store for deduplication
/// 3. Backpressure - Pause/resume partitions based on downstream health
/// 4. Graceful shutdown - Commit before closing
/// 
/// CONFIGURATION RATIONALE:
/// - enable.auto.commit=false: We control when offsets commit
/// - auto.offset.reset=earliest: Start from beginning on new consumer group
/// - max.poll.interval.ms=5min: Allow long processing batches
/// </summary>
public sealed class KafkaDataPointConsumer : IDataPointConsumer, IDisposable
{
    private readonly ILogger<KafkaDataPointConsumer> _logger;
    private readonly KafkaOptions _options;
    private readonly IConsumer<string, string> _consumer;
    private readonly CancellationTokenSource _cts;
    
    private ConsumeResult<string, string>? _lastResult;
    private bool _disposed;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public KafkaDataPointConsumer(
        IOptions<KafkaOptions> options,
        ILogger<KafkaDataPointConsumer> logger,
        string? consumerInstanceId = null)
    {
        _options = options.Value;
        _logger = logger;
        _cts = new CancellationTokenSource();
        
        var instanceId = consumerInstanceId ?? Environment.MachineName;
        
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            ClientId = $"{_options.ConsumerClientIdPrefix}-{instanceId}",
            
            // CRITICAL: Manual offset management
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            
            // STABILITY
            SessionTimeoutMs = _options.SessionTimeoutMs,
            HeartbeatIntervalMs = _options.HeartbeatIntervalMs,
            MaxPollIntervalMs = _options.MaxPollIntervalMs,
            
            // PERFORMANCE
            FetchMaxBytes = 52428800, // 50MB
            MaxPartitionFetchBytes = 1048576, // 1MB per partition
            
            // ISOLATION
            IsolationLevel = IsolationLevel.ReadCommitted // See only committed transactions
        };
        
        _consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((consumer, error) =>
            {
                if (error.IsFatal)
                    _logger.LogCritical("Kafka consumer FATAL error: {Code} - {Reason}", error.Code, error.Reason);
                else
                    _logger.LogWarning("Kafka consumer error: {Code} - {Reason}", error.Code, error.Reason);
            })
            .SetPartitionsAssignedHandler((consumer, partitions) =>
            {
                _logger.LogInformation(
                    "Partitions assigned: {Partitions}",
                    string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition.Value}]")));
            })
            .SetPartitionsRevokedHandler((consumer, partitions) =>
            {
                _logger.LogInformation(
                    "Partitions revoked: {Partitions}",
                    string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition.Value}]")));
            })
            .SetPartitionsLostHandler((consumer, partitions) =>
            {
                _logger.LogWarning(
                    "Partitions LOST (no commit opportunity): {Partitions}",
                    string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition.Value}]")));
            })
            .Build();
        
        _consumer.Subscribe(_options.DataPointsTopic);
        _logger.LogInformation(
            "Kafka consumer initialized: {Group} on {Topic}",
            _options.ConsumerGroupId, _options.DataPointsTopic);
    }
    
    public async Task<ConsumeContext?> ConsumeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            
            // Consume runs synchronously in librdkafka
            var result = await Task.Run(
                () => _consumer.Consume(timeout),
                linkedCts.Token);
            
            if (result == null)
                return null;
            
            if (result.IsPartitionEOF)
            {
                _logger.LogDebug(
                    "Reached end of partition {Topic}[{Partition}]@{Offset}",
                    result.Topic, result.Partition.Value, result.Offset.Value);
                return null;
            }
            
            _lastResult = result;
            
            // Deserialize batch
            DataPointBatch batch;
            try
            {
                batch = JsonSerializer.Deserialize<DataPointBatch>(result.Message.Value, JsonOptions)
                    ?? throw new JsonException("Deserialized to null");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "Failed to deserialize message at {Topic}[{Partition}]@{Offset}",
                    result.Topic, result.Partition.Value, result.Offset.Value);
                
                // Return as failed context - caller should send to DLQ
                return ConsumeContext.Failed(
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    result.Message.Key,
                    ex.Message);
            }
            
            // Extract batch ID from headers if present
            string? batchId = null;
            if (result.Message.Headers?.TryGetLastBytes("batch_id", out var batchIdBytes) == true)
            {
                batchId = System.Text.Encoding.UTF8.GetString(batchIdBytes);
            }
            
            return ConsumeContext.Successful(
                batch,
                result.Topic,
                result.Partition.Value,
                result.Offset.Value,
                result.Message.Key,
                batchId ?? batch.BatchId);
        }
        catch (ConsumeException ex)
        {
            _logger.LogError(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
    
    public Task CommitAsync(ConsumeContext context, CancellationToken cancellationToken = default)
    {
        if (_lastResult == null)
        {
            _logger.LogWarning("CommitAsync called but no message has been consumed");
            return Task.CompletedTask;
        }
        
        try
        {
            // Commit this specific offset + 1 (next message to consume)
            var offsets = new[]
            {
                new TopicPartitionOffset(
                    context.Topic,
                    new Partition(context.Partition),
                    new Offset(context.Offset + 1))
            };
            
            _consumer.Commit(offsets);
            
            _logger.LogDebug(
                "Committed offset {Topic}[{Partition}]@{Offset}",
                context.Topic, context.Partition, context.Offset + 1);
        }
        catch (KafkaException ex)
        {
            _logger.LogError(ex, "Failed to commit offset: {Reason}", ex.Error.Reason);
            throw;
        }
        
        return Task.CompletedTask;
    }
    
    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        var assignment = _consumer.Assignment;
        if (assignment.Count > 0)
        {
            _consumer.Pause(assignment);
            _logger.LogWarning("Consumer PAUSED - backpressure applied");
        }
        return Task.CompletedTask;
    }
    
    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        var assignment = _consumer.Assignment;
        if (assignment.Count > 0)
        {
            _consumer.Resume(assignment);
            _logger.LogInformation("Consumer RESUMED");
        }
        return Task.CompletedTask;
    }
    
    public Task SeekAsync(string topic, int partition, long offset, CancellationToken cancellationToken = default)
    {
        _consumer.Seek(new TopicPartitionOffset(topic, new Partition(partition), new Offset(offset)));
        _logger.LogInformation("Seeked to {Topic}[{Partition}]@{Offset}", topic, partition, offset);
        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            _cts.Cancel();
            _consumer.Close(); // Triggers final offset commit
            _consumer.Dispose();
            _cts.Dispose();
            _logger.LogInformation("Kafka consumer disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing Kafka consumer");
        }
    }
}
