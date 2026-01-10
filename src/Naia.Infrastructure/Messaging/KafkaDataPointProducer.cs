using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;

namespace Naia.Infrastructure.Messaging;

/// <summary>
/// Kafka producer for data point batches.
/// 
/// CONFIGURATION RATIONALE:
/// - acks=all: Wait for all in-sync replicas (durability guarantee)
/// - enable.idempotence=true: Producer-side deduplication (5-minute window)
/// - compression.type=snappy: Good balance of speed and compression
/// - linger.ms=50: Small batching delay for throughput
/// 
/// PARTITION STRATEGY:
/// - Key = DataSourceId (all points from same source → same partition)
/// - This ensures ordering per data source while allowing parallelism across sources
/// </summary>
public sealed class KafkaDataPointProducer : IDataPointProducer, IAsyncDisposable
{
    private readonly ILogger<KafkaDataPointProducer> _logger;
    private readonly KafkaOptions _options;
    private readonly IProducer<string, string> _producer;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    
    public KafkaDataPointProducer(
        IOptions<KafkaOptions> options,
        ILogger<KafkaDataPointProducer> logger)
    {
        _options = options.Value;
        _logger = logger;
        
        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = _options.ProducerClientId,
            
            // EXACTLY-ONCE SEMANTICS
            Acks = Acks.All,  // Wait for all in-sync replicas
            EnableIdempotence = true,  // Producer-side deduplication
            
            // PERFORMANCE
            CompressionType = CompressionType.Snappy,
            LingerMs = 50,  // Small batching delay
            BatchSize = 16384,  // 16KB batches
            
            // DURABILITY
            MessageTimeoutMs = 30000,  // 30 second timeout
            RequestTimeoutMs = 30000,
            
            // RELIABILITY
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 100
        };
        
        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((producer, error) =>
            {
                _logger.LogError("Kafka producer error: {Code} - {Reason}", error.Code, error.Reason);
            })
            .Build();
        
        _logger.LogInformation("Kafka producer initialized: {Servers}", _options.BootstrapServers);
    }
    
    public async Task<ProduceResult> PublishAsync(DataPointBatch batch, CancellationToken cancellationToken = default)
    {
        if (batch.IsEmpty)
            return ProduceResult.Successful(_options.DataPointsTopic, 0, 0);
            
        try
        {
            var json = JsonSerializer.Serialize(batch, JsonOptions);
            
            // Partition key = DataSourceId (ordering per source)
            var key = batch.DataSourceId ?? batch.BatchId;
            
            var message = new Message<string, string>
            {
                Key = key,
                Value = json,
                Headers = new Headers
                {
                    { "batch_id", System.Text.Encoding.UTF8.GetBytes(batch.BatchId) },
                    { "point_count", System.Text.Encoding.UTF8.GetBytes(batch.Count.ToString()) },
                    { "sent_at", System.Text.Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")) }
                }
            };
            
            var result = await _producer.ProduceAsync(_options.DataPointsTopic, message, cancellationToken);
            
            _logger.LogDebug(
                "Published batch {BatchId} ({Count} points) to {Topic}[{Partition}]@{Offset}",
                batch.BatchId, batch.Count, result.Topic, result.Partition.Value, result.Offset.Value);
            
            return ProduceResult.Successful(
                result.Topic,
                result.Partition.Value,
                result.Offset.Value);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to publish batch {BatchId}: {Reason}", batch.BatchId, ex.Error.Reason);
            return ProduceResult.Failed(_options.DataPointsTopic, ex.Error.Reason);
        }
    }
    
    public async Task<ProduceResult> PublishAsync(DataPoint point, CancellationToken cancellationToken = default)
    {
        var batch = DataPointBatch.Create(new[] { point }, point.DataSourceId);
        return await PublishAsync(batch, cancellationToken);
    }
    
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _producer.Flush(TimeSpan.FromSeconds(10));
        return Task.CompletedTask;
    }
    
    public async ValueTask DisposeAsync()
    {
        _producer.Flush(TimeSpan.FromSeconds(10));
        _producer.Dispose();
        _logger.LogInformation("Kafka producer disposed");
        await Task.CompletedTask;
    }
}

/// <summary>
/// Kafka configuration options.
/// </summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";
    
    /// <summary>Kafka bootstrap servers (comma-separated)</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";
    
    /// <summary>Topic for data point batches</summary>
    public string DataPointsTopic { get; set; } = "naia.datapoints";
    
    /// <summary>Dead letter queue topic for failed messages</summary>
    public string DlqTopic { get; set; } = "naia.datapoints.dlq";
    
    /// <summary>Consumer group ID for historian workers</summary>
    public string ConsumerGroupId { get; set; } = "naia-historians";
    
    /// <summary>Producer client ID</summary>
    public string ProducerClientId { get; set; } = "naia-producer";
    
    /// <summary>Consumer client ID prefix</summary>
    public string ConsumerClientIdPrefix { get; set; } = "naia-consumer";
    
    /// <summary>Session timeout for consumer (ms)</summary>
    public int SessionTimeoutMs { get; set; } = 30000;
    
    /// <summary>Heartbeat interval for consumer (ms)</summary>
    public int HeartbeatIntervalMs { get; set; } = 10000;
    
    /// <summary>Max poll interval before consumer kicked from group (ms)</summary>
    public int MaxPollIntervalMs { get; set; } = 300000; // 5 minutes
    
    /// <summary>Number of partitions for data points topic</summary>
    public int DataPointsPartitions { get; set; } = 12;
    
    /// <summary>Replication factor for topics</summary>
    public short ReplicationFactor { get; set; } = 3;
}
