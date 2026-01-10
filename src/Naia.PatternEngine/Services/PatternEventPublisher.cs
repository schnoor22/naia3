using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Naia.PatternEngine.Configuration;

namespace Naia.PatternEngine.Services;

/// <summary>
/// Shared Kafka producer for publishing pattern flywheel events.
/// Thread-safe singleton instance with delivery guarantees.
/// </summary>
public interface IPatternEventPublisher : IAsyncDisposable
{
    Task PublishAsync<T>(string topic, string key, T message, CancellationToken cancellationToken = default) where T : class;
    Task PublishBatchAsync<T>(string topic, IEnumerable<(string Key, T Message)> messages, CancellationToken cancellationToken = default) where T : class;
}

public sealed class PatternEventPublisher : IPatternEventPublisher
{
    private readonly ILogger<PatternEventPublisher> _logger;
    private readonly IProducer<string, string> _producer;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public PatternEventPublisher(
        ILogger<PatternEventPublisher> logger,
        PatternKafkaOptions options)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            Acks = Acks.All, // Wait for all replicas
            EnableIdempotence = true, // Exactly-once delivery
            MessageSendMaxRetries = 5,
            RetryBackoffMs = 100,
            LingerMs = 5, // Small batching for lower latency on event topics
            CompressionType = CompressionType.Lz4,
            // Transactional ID for exactly-once semantics (if needed later)
            // TransactionalId = "pattern-flywheel-producer"
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka producer error: {Error}", error.Reason);
            })
            .Build();

        _logger.LogInformation("PatternEventPublisher initialized with bootstrap servers: {Servers}", 
            options.BootstrapServers);
    }

    public async Task PublishAsync<T>(string topic, string key, T message, CancellationToken cancellationToken = default) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(PatternEventPublisher));

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        
        try
        {
            var result = await _producer.ProduceAsync(
                topic,
                new Message<string, string>
                {
                    Key = key,
                    Value = json,
                    Timestamp = new Timestamp(DateTimeOffset.UtcNow)
                },
                cancellationToken);

            _logger.LogDebug(
                "Published message to {Topic}[{Partition}] at offset {Offset}",
                result.Topic, result.Partition.Value, result.Offset.Value);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to publish message to {Topic} with key {Key}", topic, key);
            throw;
        }
    }

    public async Task PublishBatchAsync<T>(
        string topic, 
        IEnumerable<(string Key, T Message)> messages, 
        CancellationToken cancellationToken = default) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(PatternEventPublisher));

        var tasks = new List<Task>();
        var count = 0;

        foreach (var (key, message) in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var json = JsonSerializer.Serialize(message, _jsonOptions);
            
            // Use produce (non-async) for batching efficiency, track with delivery handler
            _producer.Produce(
                topic,
                new Message<string, string>
                {
                    Key = key,
                    Value = json,
                    Timestamp = new Timestamp(DateTimeOffset.UtcNow)
                },
                deliveryReport =>
                {
                    if (deliveryReport.Error.IsError)
                    {
                        _logger.LogError(
                            "Batch delivery failed for {Topic} key {Key}: {Error}",
                            topic, key, deliveryReport.Error.Reason);
                    }
                });
            
            count++;
        }

        // Flush all pending messages
        _producer.Flush(cancellationToken);
        
        _logger.LogDebug("Published batch of {Count} messages to {Topic}", count, topic);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            // Flush any remaining messages
            _producer.Flush(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error flushing producer during dispose");
        }

        _producer.Dispose();
        _logger.LogInformation("PatternEventPublisher disposed");
    }
}
