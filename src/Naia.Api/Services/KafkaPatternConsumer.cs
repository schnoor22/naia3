using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR;
using Naia.Api.Hubs;

namespace Naia.Api.Services;

/// <summary>
/// Background service that consumes pattern notifications from Kafka
/// and forwards them to SignalR clients.
/// </summary>
public class KafkaPatternConsumer : BackgroundService
{
    private readonly ILogger<KafkaPatternConsumer> _logger;
    private readonly IHubContext<PatternHub> _hubContext;
    private readonly string _bootstrapServers;
    private IConsumer<string, string>? _consumer;

    private const string TopicSuggestions = "naia.patterns.suggestions";
    private const string TopicUpdated = "naia.patterns.updated";
    private const string TopicClusters = "naia.patterns.clusters";

    public KafkaPatternConsumer(
        IConfiguration configuration,
        ILogger<KafkaPatternConsumer> logger,
        IHubContext<PatternHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
        _bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = "naia-api-signalr",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false
        };

        _consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Reason}", e.Reason))
            .Build();

        _consumer.Subscribe(new[] { TopicSuggestions, TopicUpdated, TopicClusters });
        _logger.LogInformation("KafkaPatternConsumer started, subscribed to pattern topics");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(TimeSpan.FromSeconds(1));
                    if (consumeResult == null)
                        continue;

                    await ProcessMessage(consumeResult, stoppingToken);
                    _consumer.StoreOffset(consumeResult);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming Kafka message");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing pattern notification");
                }
            }
        }
        finally
        {
            _consumer.Close();
            _consumer.Dispose();
            _logger.LogInformation("KafkaPatternConsumer stopped");
        }
    }

    private async Task ProcessMessage(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        var topic = result.Topic;
        var message = result.Message.Value;

        try
        {
            switch (topic)
            {
                case TopicSuggestions:
                    await HandleSuggestionMessage(message, cancellationToken);
                    break;
                case TopicUpdated:
                    await HandlePatternUpdateMessage(message, cancellationToken);
                    break;
                case TopicClusters:
                    await HandleClusterMessage(message, cancellationToken);
                    break;
                default:
                    _logger.LogWarning("Unknown topic: {Topic}", topic);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize message from {Topic}", topic);
        }
    }

    private async Task HandleSuggestionMessage(string message, CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Deserialize<JsonElement>(message);
        var type = data.GetProperty("Type").GetString();

        if (type == "Created")
        {
            var suggestion = data.GetProperty("Suggestion");
            await _hubContext.Clients.All.SendAsync("SuggestionCreated", suggestion, cancellationToken);
            _logger.LogDebug("Forwarded SuggestionCreated to SignalR");
        }
        else if (type == "Approved")
        {
            var suggestionId = Guid.Parse(data.GetProperty("SuggestionId").GetString()!);
            var patternName = data.GetProperty("PatternName").GetString();
            await _hubContext.Clients.All.SendAsync("SuggestionApproved", suggestionId, patternName, cancellationToken);
            _logger.LogDebug("Forwarded SuggestionApproved to SignalR");
        }
    }

    private async Task HandlePatternUpdateMessage(string message, CancellationToken cancellationToken)
    {
        var pattern = JsonSerializer.Deserialize<JsonElement>(message);
        await _hubContext.Clients.All.SendAsync("PatternUpdated", pattern, cancellationToken);
        _logger.LogDebug("Forwarded PatternUpdated to SignalR");
    }

    private async Task HandleClusterMessage(string message, CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Deserialize<JsonElement>(message);
        var type = data.GetProperty("Type").GetString();

        if (type == "ClusterDetected")
        {
            var patternId = Guid.Parse(data.GetProperty("PatternId").GetString()!);
            var pointCount = data.GetProperty("PointCount").GetInt32();
            var description = data.TryGetProperty("Description", out var desc) ? desc.GetString() : null;
            await _hubContext.Clients.All.SendAsync("ClusterDetected", patternId, pointCount, description, cancellationToken);
            _logger.LogDebug("Forwarded ClusterDetected to SignalR");
        }
        else if (type == "PendingCountChanged")
        {
            var pendingCount = data.GetProperty("PendingCount").GetInt32();
            await _hubContext.Clients.All.SendAsync("PendingCountChanged", pendingCount, cancellationToken);
            _logger.LogDebug("Forwarded PendingCountChanged to SignalR");
        }
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}
