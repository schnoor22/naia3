using Naia.Domain.ValueObjects;

namespace Naia.Application.Abstractions;

/// <summary>
/// Abstraction for time-series data storage (QuestDB).
/// This is the write path for historical data.
/// </summary>
public interface ITimeSeriesWriter
{
    /// <summary>
    /// Write a batch of data points to the time-series database.
    /// </summary>
    Task WriteAsync(DataPointBatch batch, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Flush any buffered data to ensure durability.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Health check - can we write to the database?
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstraction for time-series data reads (QuestDB).
/// </summary>
public interface ITimeSeriesReader
{
    /// <summary>
    /// Read data points for a point within a time range.
    /// </summary>
    Task<IReadOnlyList<DataPoint>> ReadRangeAsync(
        long pointSequenceId,
        DateTime startTime,
        DateTime endTime,
        int? limit = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the most recent value for a point.
    /// </summary>
    Task<DataPoint?> GetLastValueAsync(
        long pointSequenceId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get aggregated data (min, max, avg) for a time period.
    /// </summary>
    Task<AggregatedData?> GetAggregatedAsync(
        long pointSequenceId,
        DateTime startTime,
        DateTime endTime,
        AggregationPeriod period,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregated time-series data.
/// </summary>
public sealed record AggregatedData
{
    public required long PointSequenceId { get; init; }
    public required DateTime PeriodStart { get; init; }
    public required DateTime PeriodEnd { get; init; }
    public required double MinValue { get; init; }
    public required double MaxValue { get; init; }
    public required double AvgValue { get; init; }
    public required double StdDev { get; init; }
    public required long SampleCount { get; init; }
}

/// <summary>
/// Aggregation period for rollups.
/// </summary>
public enum AggregationPeriod
{
    Minute = 1,
    FiveMinutes = 5,
    FifteenMinutes = 15,
    Hour = 60,
    Day = 1440,
    Week = 10080,
    Month = 43200
}
