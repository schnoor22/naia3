using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using System.Text.Json;

namespace Naia.Infrastructure.Messaging;

/// <summary>
/// Kafka-based implementation of pattern notifications.
/// Publishes pattern events to Kafka topics for consumption by the API layer (SignalR forwarding).
/// This enables separation of the PatternEngine into an independent worker service.
/// </summary>
public class KafkaPatternNotifier : IPatternNotifier, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaPatternNotifier> _logger;
    private bool _disposed;
    
    private const string TopicSuggestions = "naia.patterns.suggestions";
    private const string TopicUpdated = "naia.patterns.updated";
    private const string TopicClusters = "naia.patterns.clusters";
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public KafkaPatternNotifier(
        IOptions<KafkaOptions> options,
        ILogger<KafkaPatternNotifier> logger)
    {
        _logger = logger;
        
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            ClientId = "naia-pattern-notifier",
            Acks = Acks.Leader,
            EnableIdempotence = true,
            MaxInFlight = 5,
            LingerMs = 10,
            CompressionType = CompressionType.Snappy
        };
        
        _producer = new ProducerBuilder<string, string>(producerConfig)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka producer error: {Error}", e.Reason))
            .Build();
        
        _logger.LogInformation("Kafka pattern notifier initialized: {Servers}", options.Value.BootstrapServers);
    }

    public async Task NotifySuggestionCreatedAsync(object suggestion)
    {
        if (_disposed) return;
        
        try
        {
            var json = JsonSerializer.Serialize(suggestion, JsonOptions);
            var message = new Message<string, string>
            {
                Key = Guid.NewGuid().ToString(), // Suggestion ID if available
                Value = json,
                Timestamp = Timestamp.Default
            };
            
            var result = await _producer.ProduceAsync(TopicSuggestions, message);
            
            _logger.LogDebug("Published suggestion to Kafka: {Topic} partition {Partition} offset {Offset}",
                result.Topic, result.Partition.Value, result.Offset.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish suggestion to Kafka");
        }
    }

    public async Task NotifySuggestionApprovedAsync(Guid suggestionId, string patternName)
    {
        if (_disposed) return;
        
        try
        {
            var payload = new { SuggestionId = suggestionId, PatternName = patternName, Approved = true };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var message = new Message<string, string>
            {
                Key = suggestionId.ToString(),
                Value = json,
                Timestamp = Timestamp.Default
            };
            
            await _producer.ProduceAsync(TopicSuggestions, message);
            
            _logger.LogDebug("Published suggestion approval to Kafka: {SuggestionId}", suggestionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish suggestion approval to Kafka");
        }
    }

    public async Task NotifyPatternUpdatedAsync(object pattern)
    {
        if (_disposed) return;
        
        try
        {
            var json = JsonSerializer.Serialize(pattern, JsonOptions);
            var message = new Message<string, string>
            {
                Key = Guid.NewGuid().ToString(), // Pattern ID if available
                Value = json,
                Timestamp = Timestamp.Default
            };
            
            await _producer.ProduceAsync(TopicUpdated, message);
            
            _logger.LogDebug("Published pattern update to Kafka");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish pattern update to Kafka");
        }
    }

    public async Task NotifyClusterDetectedAsync(Guid clusterId, int pointCount, string? commonPrefix)
    {
        if (_disposed) return;
        
        try
        {
            var payload = new { ClusterId = clusterId, PointCount = pointCount, CommonPrefix = commonPrefix };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var message = new Message<string, string>
            {
                Key = clusterId.ToString(),
                Value = json,
                Timestamp = Timestamp.Default
            };
            
            await _producer.ProduceAsync(TopicClusters, message);
            
            _logger.LogDebug("Published cluster detection to Kafka: {ClusterId}", clusterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish cluster detection to Kafka");
        }
    }

    public async Task NotifyPendingCountChangedAsync(int newCount)
    {
        if (_disposed) return;
        
        try
        {
            var payload = new { PendingCount = newCount, Timestamp = DateTime.UtcNow };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var message = new Message<string, string>
            {
                Key = "pending-count",
                Value = json,
                Timestamp = Timestamp.Default
            };
            
            await _producer.ProduceAsync(TopicSuggestions, message);
            
            _logger.LogDebug("Published pending count to Kafka: {Count}", newCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish pending count to Kafka");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        
        _producer.Flush(TimeSpan.FromSeconds(10));
        _producer.Dispose();
        
        _logger.LogInformation("Kafka pattern notifier disposed");
        await Task.CompletedTask;
    }
}
