using Naia.Domain.ValueObjects;

namespace Naia.Application.Abstractions;

/// <summary>
/// Abstraction for current value cache (Redis).
/// Provides sub-millisecond access to latest point values for real-time dashboards.
/// </summary>
public interface ICurrentValueCache
{
    /// <summary>
    /// Get the current value for a single point.
    /// </summary>
    Task<CurrentValue?> GetAsync(long pointSequenceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get current values for multiple points.
    /// </summary>
    Task<IReadOnlyDictionary<long, CurrentValue>> GetManyAsync(
        IEnumerable<long> pointSequenceIds,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update the current value for a point.
    /// </summary>
    Task SetAsync(CurrentValue value, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update current values for multiple points.
    /// </summary>
    Task SetManyAsync(IEnumerable<CurrentValue> values, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Remove a point from the cache.
    /// </summary>
    Task RemoveAsync(long pointSequenceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Health check - is Redis accessible?
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstraction for idempotency tracking.
/// Prevents duplicate message processing in the Kafka pipeline.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Check if a message was already processed.
    /// Returns (isDuplicate, originalProcessedTimestamp).
    /// </summary>
    Task<(bool IsDuplicate, DateTime? ProcessedAt)> CheckAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Mark a message as processed.
    /// Must be called AFTER successful processing, BEFORE offset commit.
    /// </summary>
    Task MarkProcessedAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the TTL for idempotency keys (how long we remember processed messages).
    /// </summary>
    TimeSpan KeyTtl { get; }
}
