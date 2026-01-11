using Naia.Domain.ValueObjects;

namespace Naia.Application.Abstractions;

/// <summary>
/// Kafka producer abstraction for publishing data point batches.
/// </summary>
public interface IDataPointProducer
{
    /// <summary>
    /// Publish a batch of data points to Kafka.
    /// Returns when the message is acknowledged by the broker (acks=all).
    /// </summary>
    Task<ProduceResult> PublishAsync(
        DataPointBatch batch,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Publish a single data point.
    /// For efficiency, prefer batching with PublishAsync(DataPointBatch).
    /// </summary>
    Task<ProduceResult> PublishAsync(
        DataPoint point,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Publish a raw JSON message to a specific topic.
    /// Used for backfill batches and other custom messages.
    /// </summary>
    Task<ProduceResult> PublishAsync(
        string topic,
        string key,
        string jsonPayload,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Flush any pending messages.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a Kafka produce operation.
/// </summary>
public sealed record ProduceResult
{
    public required bool Success { get; init; }
    public required string Topic { get; init; }
    public required int Partition { get; init; }
    public required long Offset { get; init; }
    public required DateTime Timestamp { get; init; }
    public string? ErrorMessage { get; init; }
    
    public static ProduceResult Successful(string topic, int partition, long offset)
    {
        return new ProduceResult
        {
            Success = true,
            Topic = topic,
            Partition = partition,
            Offset = offset,
            Timestamp = DateTime.UtcNow
        };
    }
    
    public static ProduceResult Failed(string topic, string errorMessage)
    {
        return new ProduceResult
        {
            Success = false,
            Topic = topic,
            Partition = -1,
            Offset = -1,
            Timestamp = DateTime.UtcNow,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Kafka consumer abstraction for processing data point batches.
/// Uses manual offset commits for exactly-once semantics.
/// </summary>
public interface IDataPointConsumer : IDisposable
{
    /// <summary>
    /// Consume the next message from Kafka.
    /// Returns null if no message available within timeout.
    /// </summary>
    Task<ConsumeContext?> ConsumeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Commit the offset for a successfully processed message.
    /// CRITICAL: Only call this AFTER successful processing.
    /// </summary>
    Task CommitAsync(ConsumeContext context, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Pause consumption (backpressure).
    /// </summary>
    Task PauseAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resume consumption after pause.
    /// </summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Seek to a specific offset.
    /// </summary>
    Task SeekAsync(string topic, int partition, long offset, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for a consumed Kafka message.
/// </summary>
public sealed record ConsumeContext
{
    public required bool IsSuccess { get; init; }
    public DataPointBatch? Batch { get; init; }
    public required string Topic { get; init; }
    public required int Partition { get; init; }
    public required long Offset { get; init; }
    public string? PartitionKey { get; init; }
    public string? BatchId { get; init; }
    public string? ErrorMessage { get; init; }
    
    public static ConsumeContext Successful(
        DataPointBatch batch,
        string topic,
        int partition,
        long offset,
        string? key,
        string batchId)
    {
        return new ConsumeContext
        {
            IsSuccess = true,
            Batch = batch,
            Topic = topic,
            Partition = partition,
            Offset = offset,
            PartitionKey = key,
            BatchId = batchId
        };
    }
    
    public static ConsumeContext Failed(
        string topic,
        int partition,
        long offset,
        string? key,
        string error)
    {
        return new ConsumeContext
        {
            IsSuccess = false,
            Topic = topic,
            Partition = partition,
            Offset = offset,
            PartitionKey = key,
            ErrorMessage = error
        };
    }
}
