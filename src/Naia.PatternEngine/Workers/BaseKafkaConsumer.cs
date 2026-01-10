using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Naia.PatternEngine.Workers;

/// <summary>
/// Base class for all Kafka consumer workers in the pattern flywheel.
/// Provides common functionality: connection, error handling, offset management, graceful shutdown.
/// </summary>
/// <typeparam name="TMessage">The message type to consume</typeparam>
public abstract class BaseKafkaConsumer<TMessage> : BackgroundService where TMessage : class
{
    protected readonly ILogger Logger;
    protected readonly string BootstrapServers;
    protected readonly string GroupId;
    protected readonly string Topic;
    protected readonly JsonSerializerOptions JsonOptions;
    
    private IConsumer<string, string>? _consumer;
    private readonly CancellationTokenSource _internalCts = new();

    protected BaseKafkaConsumer(
        ILogger logger,
        string bootstrapServers,
        string groupId,
        string topic)
    {
        Logger = logger;
        BootstrapServers = bootstrapServers;
        GroupId = groupId;
        Topic = topic;
        JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Process a single message. Implementations should be idempotent.
    /// </summary>
    protected abstract Task ProcessMessageAsync(TMessage message, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Called when consumer is starting (after Kafka connection established)
    /// </summary>
    protected virtual Task OnStartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called when consumer is stopping (before Kafka disconnect)
    /// </summary>
    protected virtual Task OnStoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation(
            "Starting {ConsumerName} consumer for topic {Topic} with group {GroupId}",
            GetType().Name, Topic, GroupId);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _internalCts.Token);
        var linkedToken = linkedCts.Token;

        try
        {
            await InitializeConsumerAsync(linkedToken);
            await OnStartingAsync(linkedToken);
            await ConsumeLoopAsync(linkedToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Logger.LogInformation("{ConsumerName} shutdown requested", GetType().Name);
        }
        catch (Exception ex)
        {
            Logger.LogCritical(ex, "{ConsumerName} crashed unexpectedly", GetType().Name);
            throw;
        }
        finally
        {
            await OnStoppingAsync(CancellationToken.None);
            CloseConsumer();
        }
    }

    private Task InitializeConsumerAsync(CancellationToken cancellationToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // Manual offset management for reliability
            EnablePartitionEof = false,
            MaxPollIntervalMs = 300000, // 5 minutes
            SessionTimeoutMs = 30000,
            HeartbeatIntervalMs = 10000,
            // Reduce fetch size for lower latency on event topics
            FetchMinBytes = 1,
            FetchWaitMaxMs = 100
        };

        _consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
            {
                Logger.LogError("Kafka consumer error: {Error}", error.Reason);
                if (error.IsFatal)
                {
                    Logger.LogCritical("Fatal Kafka error, triggering shutdown");
                    _internalCts.Cancel();
                }
            })
            .SetPartitionsAssignedHandler((consumer, partitions) =>
            {
                Logger.LogInformation(
                    "{ConsumerName} assigned partitions: {Partitions}",
                    GetType().Name,
                    string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition}]")));
            })
            .SetPartitionsRevokedHandler((consumer, partitions) =>
            {
                Logger.LogInformation(
                    "{ConsumerName} revoked partitions: {Partitions}",
                    GetType().Name,
                    string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition}]")));
            })
            .Build();

        _consumer.Subscribe(Topic);
        Logger.LogInformation("{ConsumerName} subscribed to {Topic}", GetType().Name, Topic);

        return Task.CompletedTask;
    }

    private async Task ConsumeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Poll with short timeout for responsive shutdown
                var result = _consumer!.Consume(TimeSpan.FromMilliseconds(500));
                
                if (result == null)
                    continue;

                if (result.Message?.Value == null)
                {
                    Logger.LogWarning("Received null message on {Topic}, skipping", Topic);
                    _consumer.Commit(result);
                    continue;
                }

                var messageKey = result.Message.Key ?? string.Empty;
                
                try
                {
                    var message = JsonSerializer.Deserialize<TMessage>(result.Message.Value, JsonOptions);
                    
                    if (message == null)
                    {
                        Logger.LogWarning("Failed to deserialize message on {Topic}: {Value}", Topic, result.Message.Value);
                        _consumer.Commit(result);
                        continue;
                    }

                    await ProcessMessageAsync(message, messageKey, cancellationToken);
                    
                    // Commit AFTER successful processing (at-least-once semantics)
                    _consumer.Commit(result);
                }
                catch (JsonException ex)
                {
                    Logger.LogError(ex, "JSON deserialization error on {Topic}: {Value}", Topic, result.Message.Value);
                    // Commit to avoid infinite loop on bad messages
                    _consumer.Commit(result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.LogError(ex, "Error processing message on {Topic}, will retry on next consume", Topic);
                    // Don't commit - message will be redelivered on next poll
                    await Task.Delay(1000, cancellationToken); // Brief backoff
                }
            }
            catch (ConsumeException ex)
            {
                Logger.LogError(ex, "Kafka consume exception on {Topic}", Topic);
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private void CloseConsumer()
    {
        try
        {
            _consumer?.Close();
            _consumer?.Dispose();
            Logger.LogInformation("{ConsumerName} consumer closed", GetType().Name);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error closing {ConsumerName} consumer", GetType().Name);
        }
    }

    public override void Dispose()
    {
        _internalCts.Cancel();
        _internalCts.Dispose();
        base.Dispose();
    }
}
