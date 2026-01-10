using Naia.Domain.ValueObjects;

namespace Naia.Application.Abstractions;

/// <summary>
/// The main ingestion pipeline that processes data points from Kafka to storage.
/// 
/// PIPELINE FLOW:
/// 
///   Kafka Consumer
///        ↓
///   ┌─────────────────────────────────────────┐
///   │         INGESTION PIPELINE              │
///   ├─────────────────────────────────────────┤
///   │  1. Idempotency Check (Redis)           │
///   │     → Skip if duplicate                 │
///   │                                         │
///   │  2. Update Current Value Cache (Redis)  │
///   │     → Real-time dashboards              │
///   │                                         │
///   │  3. Write to Time-Series DB (QuestDB)   │
///   │     → Historical storage                │
///   │                                         │
///   │  4. Mark Processed (Redis)              │
///   │     → Idempotency tracking              │
///   └─────────────────────────────────────────┘
///        ↓
///   Commit Offset (Kafka)
/// 
/// GUARANTEES:
/// - Exactly-once processing via idempotency
/// - At-least-once delivery via manual offset commits
/// - Zero data loss via Kafka durability
/// </summary>
public interface IIngestionPipeline
{
    /// <summary>Current pipeline state</summary>
    PipelineState State { get; }
    
    /// <summary>Start the pipeline processing loop</summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>Stop the pipeline gracefully</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>Get pipeline health status</summary>
    Task<PipelineHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    
    /// <summary>Get pipeline metrics</summary>
    Task<PipelineMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Pipeline state
/// </summary>
public enum PipelineState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

/// <summary>
/// Result of processing a batch through the pipeline.
/// </summary>
public sealed class PipelineResult
{
    public required bool Success { get; init; }
    public required int ProcessedCount { get; init; }
    public required long DurationMs { get; init; }
    public bool IsRetryable { get; init; }
    public bool WasSkipped { get; init; }
    public string? ErrorMessage { get; init; }
    
    public static PipelineResult SuccessResult(int count, long durationMs, bool skipped = false)
    {
        return new PipelineResult
        {
            Success = true,
            ProcessedCount = count,
            DurationMs = durationMs,
            WasSkipped = skipped
        };
    }
    
    public static PipelineResult RetryableError(string error, long durationMs)
    {
        return new PipelineResult
        {
            Success = false,
            ProcessedCount = 0,
            DurationMs = durationMs,
            IsRetryable = true,
            ErrorMessage = error
        };
    }
    
    public static PipelineResult NonRetryableError(string error, long durationMs)
    {
        return new PipelineResult
        {
            Success = false,
            ProcessedCount = 0,
            DurationMs = durationMs,
            IsRetryable = false,
            ErrorMessage = error
        };
    }
}

/// <summary>
/// Health status of the pipeline components.
/// </summary>
public sealed class PipelineHealth
{
    public PipelineState State { get; set; }
    public bool IsHealthy { get; set; }
    public PipelineMetrics? Metrics { get; set; }
    public DateTime? LastProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Runtime metrics for the pipeline.
/// </summary>
public sealed class PipelineMetrics
{
    public long TotalBatches { get; set; }
    public long TotalPoints { get; set; }
    public long DuplicateBatches { get; set; }
    public long RetryableErrors { get; set; }
    public long NonRetryableErrors { get; set; }
    public long DlqMessages { get; set; }
    public double AverageDurationMs { get; set; }
    public DateTime? LastProcessedAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public bool HasRecentErrors { get; set; }
}
