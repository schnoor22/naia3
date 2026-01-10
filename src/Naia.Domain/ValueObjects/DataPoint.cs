namespace Naia.Domain.ValueObjects;

/// <summary>
/// Represents a single time-series data point value.
/// This is the fundamental unit of data flowing through the NAIA ingestion pipeline.
/// 
/// DESIGN PHILOSOPHY:
/// - Immutable (record type): Thread-safe, cacheable, no side effects
/// - Contains all metadata needed for routing and storage decisions
/// - Optimized for high-throughput pipelines (minimal allocations)
/// - Uses PointSequenceId (long) for QuestDB efficiency, not GUID
/// </summary>
public sealed record DataPoint
{
    /// <summary>
    /// Point sequence ID (maps to QuestDB point_id column).
    /// This is the efficient numeric identifier for time-series storage.
    /// </summary>
    public required long PointSequenceId { get; init; }
    
    /// <summary>Human-readable point name (for logging/debugging)</summary>
    public required string PointName { get; init; }
    
    /// <summary>UTC timestamp when value was recorded at source</summary>
    public required DateTime Timestamp { get; init; }
    
    /// <summary>The actual numeric value</summary>
    public required double Value { get; init; }
    
    /// <summary>Data quality indicator</summary>
    public DataQuality Quality { get; init; } = DataQuality.Good;
    
    /// <summary>Source system identifier (for tracing)</summary>
    public string? DataSourceId { get; init; }
    
    /// <summary>Original tag/address from source system</summary>
    public string? SourceTag { get; init; }
    
    /// <summary>When this point was received by NAIA (for latency tracking)</summary>
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>Unique key for idempotency detection</summary>
    public string IdempotencyKey => $"{PointSequenceId}:{Timestamp:O}";
    
    /// <summary>Calculate latency from source to NAIA</summary>
    public TimeSpan Latency => ReceivedAt - Timestamp;
    
    /// <summary>
    /// Create from OPC UA subscription data.
    /// </summary>
    public static DataPoint FromOpc(
        long pointSequenceId,
        string pointName,
        DateTime timestamp,
        double value,
        uint opcStatusCode,
        string? dataSourceId = null,
        string? sourceTag = null)
    {
        return new DataPoint
        {
            PointSequenceId = pointSequenceId,
            PointName = pointName,
            Timestamp = timestamp,
            Value = value,
            Quality = MapOpcQuality(opcStatusCode),
            DataSourceId = dataSourceId,
            SourceTag = sourceTag,
            ReceivedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Create from PI Web API data.
    /// </summary>
    public static DataPoint FromPi(
        long pointSequenceId,
        string pointName,
        DateTime timestamp,
        double value,
        bool isGood,
        string? dataSourceId = null,
        string? sourceTag = null)
    {
        return new DataPoint
        {
            PointSequenceId = pointSequenceId,
            PointName = pointName,
            Timestamp = timestamp,
            Value = value,
            Quality = isGood ? DataQuality.Good : DataQuality.Bad,
            DataSourceId = dataSourceId,
            SourceTag = sourceTag,
            ReceivedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Create from CSV import row.
    /// </summary>
    public static DataPoint FromCsv(
        long pointSequenceId,
        string pointName,
        DateTime timestamp,
        double value,
        string? dataSourceId = null)
    {
        return new DataPoint
        {
            PointSequenceId = pointSequenceId,
            PointName = pointName,
            Timestamp = timestamp,
            Value = value,
            Quality = DataQuality.Good,
            DataSourceId = dataSourceId,
            ReceivedAt = DateTime.UtcNow
        };
    }
    
    private static DataQuality MapOpcQuality(uint statusCode)
    {
        // OPC UA status codes: Good = 0, Uncertain = 0x40000000, Bad = 0x80000000
        return statusCode switch
        {
            0 => DataQuality.Good,
            var s when s >= 0x80000000 => DataQuality.Bad,
            var s when s >= 0x40000000 => DataQuality.Uncertain,
            _ => DataQuality.Good
        };
    }
}

/// <summary>
/// Quality indicator for data points.
/// Based on OPC UA quality codes but simplified for NAIA.
/// </summary>
public enum DataQuality : byte
{
    /// <summary>Value is good and reliable</summary>
    Good = 0,
    
    /// <summary>Value is uncertain (sensor degraded, interpolated, etc.)</summary>
    Uncertain = 1,
    
    /// <summary>Value is bad (communication failure, out of range, etc.)</summary>
    Bad = 2,
    
    /// <summary>No value available (point not configured, deleted, etc.)</summary>
    NotAvailable = 3,
    
    /// <summary>Value was substituted manually</summary>
    Substituted = 4
}

/// <summary>
/// Batch of data points for efficient pipeline processing.
/// </summary>
public sealed record DataPointBatch
{
    public required IReadOnlyList<DataPoint> Points { get; init; }
    public required string BatchId { get; init; }
    public required DateTime CreatedAt { get; init; }
    public string? DataSourceId { get; init; }
    
    public int Count => Points.Count;
    public bool IsEmpty => Points.Count == 0;
    
    /// <summary>Idempotency key for the entire batch</summary>
    public string IdempotencyKey => $"batch:{BatchId}";
    
    public static DataPointBatch Create(IEnumerable<DataPoint> points, string? dataSourceId = null)
    {
        return new DataPointBatch
        {
            Points = points.ToList(),
            BatchId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            DataSourceId = dataSourceId
        };
    }
    
    public static DataPointBatch Empty() => new()
    {
        Points = Array.Empty<DataPoint>(),
        BatchId = "empty",
        CreatedAt = DateTime.UtcNow
    };
}

/// <summary>
/// Current value snapshot for a point (fast reads).
/// This is what powers real-time dashboards via Redis.
/// </summary>
public sealed record CurrentValue
{
    public required long PointSequenceId { get; init; }
    public required string PointName { get; init; }
    public required DateTime Timestamp { get; init; }
    public required double Value { get; init; }
    public DataQuality Quality { get; init; }
    public string? EngineeringUnits { get; init; }
    
    /// <summary>When this snapshot was last updated in cache</summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
    
    public static CurrentValue FromDataPoint(DataPoint point, string? engineeringUnits = null)
    {
        return new CurrentValue
        {
            PointSequenceId = point.PointSequenceId,
            PointName = point.PointName,
            Timestamp = point.Timestamp,
            Value = point.Value,
            Quality = point.Quality,
            EngineeringUnits = engineeringUnits,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
