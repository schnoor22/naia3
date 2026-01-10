namespace Naia.Domain.Entities;

/// <summary>
/// Represents a single point/tag configuration in the system.
/// Points are the logical representation of time-series data streams.
/// 
/// DESIGN DECISIONS:
/// - Id (GUID): Used for relational integrity in PostgreSQL
/// - PointSequenceId (long): Used for efficient storage in QuestDB (much smaller than GUID)
/// - This dual-ID approach optimizes both relational queries and time-series storage
/// </summary>
public sealed class Point
{
    /// <summary>Primary key for relational database (PostgreSQL)</summary>
    public Guid Id { get; private set; }
    
    /// <summary>
    /// Sequential ID for time-series database (QuestDB).
    /// BIGINT is far more efficient than GUID for time-series storage.
    /// This is assigned by the database on insert.
    /// </summary>
    public long PointSequenceId { get; private set; }
    
    /// <summary>Human-readable unique name (e.g., "WTG001.ActivePower")</summary>
    public string Name { get; private set; } = string.Empty;
    
    /// <summary>Optional description for documentation</summary>
    public string? Description { get; private set; }
    
    /// <summary>Engineering units (MW, °C, m/s, PSI, etc.)</summary>
    public string? EngineeringUnits { get; private set; }
    
    /// <summary>Data type of the point value</summary>
    public PointValueType ValueType { get; private set; }
    
    /// <summary>Kind of point (input from source, calculated, manual entry)</summary>
    public PointKind Kind { get; private set; }
    
    /// <summary>Source system address/tag (e.g., OPC node ID, PI tag)</summary>
    public string? SourceAddress { get; private set; }
    
    // Data Source relationship
    public Guid? DataSourceId { get; private set; }
    public DataSource? DataSource { get; private set; }
    
    // Compression settings (SDT - Swinging Door Trending)
    public bool CompressionEnabled { get; private set; }
    public double CompressionDeviation { get; private set; }
    public int CompressionMinIntervalSeconds { get; private set; }
    public int CompressionMaxIntervalSeconds { get; private set; }
    
    // Exception deviation (filter noise before compression)
    public bool ExceptionEnabled { get; private set; }
    public double ExceptionDeviation { get; private set; }
    
    // Scaling (Zero/Span for raw value conversion)
    public double Zero { get; private set; }
    public double Span { get; private set; }
    
    // Metadata
    public bool IsEnabled { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastValueAt { get; private set; }
    
    private Point() { } // EF Core
    
    public static Point Create(
        string name,
        PointValueType valueType = PointValueType.Float64,
        PointKind kind = PointKind.Input,
        string? description = null,
        string? engineeringUnits = null,
        Guid? dataSourceId = null,
        string? sourceAddress = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Point name is required", nameof(name));
            
        return new Point
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description,
            EngineeringUnits = engineeringUnits,
            ValueType = valueType,
            Kind = kind,
            DataSourceId = dataSourceId,
            SourceAddress = sourceAddress,
            CompressionEnabled = true,
            CompressionDeviation = 0.5, // 0.5% default
            CompressionMinIntervalSeconds = 0,
            CompressionMaxIntervalSeconds = 600, // 10 minutes max
            ExceptionEnabled = true,
            ExceptionDeviation = 0.1, // 0.1% default
            Zero = 0,
            Span = 100,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Assign the sequence ID after database insert.
    /// This is called by the repository after PostgreSQL assigns the IDENTITY value.
    /// </summary>
    public void SetSequenceId(long sequenceId)
    {
        if (PointSequenceId != 0)
            throw new InvalidOperationException("Sequence ID already assigned");
        PointSequenceId = sequenceId;
    }
    
    public void UpdateLastValueTimestamp(DateTime timestamp)
    {
        LastValueAt = timestamp;
    }
    
    public void ConfigureCompression(
        bool enabled,
        double deviation,
        int minIntervalSeconds,
        int maxIntervalSeconds)
    {
        CompressionEnabled = enabled;
        CompressionDeviation = Math.Max(0, deviation);
        CompressionMinIntervalSeconds = Math.Max(0, minIntervalSeconds);
        CompressionMaxIntervalSeconds = Math.Max(minIntervalSeconds, maxIntervalSeconds);
    }
    
    public void ConfigureException(bool enabled, double deviation)
    {
        ExceptionEnabled = enabled;
        ExceptionDeviation = Math.Max(0, deviation);
    }
    
    public void ConfigureScaling(double zero, double span)
    {
        Zero = zero;
        Span = span;
    }
    
    public void Disable() => IsEnabled = false;
    public void Enable() => IsEnabled = true;
}

/// <summary>
/// Data type of the point value.
/// </summary>
public enum PointValueType
{
    Float64 = 1,    // Most common for industrial data
    Float32 = 2,
    Int64 = 3,
    Int32 = 4,
    Boolean = 5,
    String = 6,
    DateTime = 7
}

/// <summary>
/// Kind of point - determines how values are sourced.
/// </summary>
public enum PointKind
{
    /// <summary>Data comes from an external source via DataSource</summary>
    Input = 1,
    
    /// <summary>Calculated from other points via formula</summary>
    Calculated = 2,
    
    /// <summary>Manually entered by users</summary>
    Manual = 3,
    
    /// <summary>Aggregated from other points (rollups)</summary>
    Aggregate = 4
}
