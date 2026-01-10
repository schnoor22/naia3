namespace Naia.Connectors.Abstractions;

/// <summary>
/// Configuration for a data source connector.
/// </summary>
public sealed class ConnectorConfiguration
{
    /// <summary>The base URL or connection string for the data source</summary>
    public required string ConnectionString { get; init; }
    
    /// <summary>Optional PI Data Archive server name</summary>
    public string? PiDataArchive { get; init; }
    
    /// <summary>Optional AF Server name</summary>
    public string? AfServerName { get; init; }
    
    /// <summary>Connection timeout</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>Credentials (Username, Password, etc.)</summary>
    public Dictionary<string, string> Credentials { get; init; } = new();
    
    /// <summary>Use Windows integrated authentication</summary>
    public bool UseWindowsAuth { get; init; } = true;
    
    /// <summary>Maximum concurrent requests</summary>
    public int MaxConcurrentRequests { get; init; } = 10;
    
    /// <summary>Batch size for bulk operations</summary>
    public int BatchSize { get; init; } = 1000;
}

/// <summary>
/// Result of a connector health check.
/// </summary>
public sealed class ConnectorHealthStatus
{
    public bool IsHealthy { get; init; }
    public string Message { get; init; } = string.Empty;
    public TimeSpan ResponseTime { get; init; }
    public Dictionary<string, object> Details { get; init; } = new();
}

/// <summary>
/// Single data value from a source point.
/// </summary>
public sealed class DataValue
{
    public object? Value { get; init; }
    public DateTime Timestamp { get; init; }
    public DataQuality Quality { get; init; }
    public string? Units { get; init; }
}

/// <summary>
/// Data quality indicator.
/// </summary>
public enum DataQuality
{
    Good = 0,
    Bad = 1,
    Uncertain = 2,
    Substituted = 3
}

/// <summary>
/// Time series data from a data source.
/// </summary>
public sealed class TimeSeriesData
{
    public required string SourceAddress { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public List<DataValue> Values { get; init; } = new();
    public string? Units { get; init; }
}
