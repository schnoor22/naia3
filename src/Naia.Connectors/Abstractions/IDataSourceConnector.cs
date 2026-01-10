namespace Naia.Connectors.Abstractions;

/// <summary>
/// Interface for data source connectors that can read data from external systems.
/// </summary>
public interface IDataSourceConnector
{
    /// <summary>Unique type identifier for this connector</summary>
    string ConnectorType { get; }
    
    /// <summary>Display name for this connector</summary>
    string DisplayName { get; }
    
    /// <summary>Current availability status</summary>
    bool IsAvailable { get; }
    
    /// <summary>Initialize the connector with configuration</summary>
    Task InitializeAsync(ConnectorConfiguration config, CancellationToken ct = default);
    
    /// <summary>Check connector health</summary>
    Task<ConnectorHealthStatus> CheckHealthAsync(CancellationToken ct = default);
    
    /// <summary>Test the connection</summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
    
    /// <summary>Dispose of resources</summary>
    Task DisposeAsync();
}

/// <summary>
/// Connector that can read current values from data points.
/// </summary>
public interface ICurrentValueConnector : IDataSourceConnector
{
    /// <summary>Read current value for a single point</summary>
    Task<DataValue> ReadCurrentValueAsync(string sourceAddress, CancellationToken ct = default);
    
    /// <summary>Read current values for multiple points</summary>
    Task<IReadOnlyDictionary<string, DataValue>> ReadCurrentValuesAsync(
        IEnumerable<string> sourceAddresses, 
        CancellationToken ct = default);
}

/// <summary>
/// Connector that can read historical time series data.
/// </summary>
public interface IHistoricalDataConnector : IDataSourceConnector
{
    /// <summary>Read historical data for a single point</summary>
    Task<TimeSeriesData> ReadHistoricalDataAsync(
        string sourceAddress,
        DateTime startTime,
        DateTime endTime,
        CancellationToken ct = default);
    
    /// <summary>Read historical data for multiple points</summary>
    Task<IReadOnlyList<TimeSeriesData>> ReadHistoricalDataBatchAsync(
        IEnumerable<string> sourceAddresses,
        DateTime startTime,
        DateTime endTime,
        CancellationToken ct = default);
}

/// <summary>
/// Connector that can discover available data points.
/// </summary>
public interface IDiscoverableConnector : IDataSourceConnector
{
    /// <summary>Search for available points matching the filter</summary>
    Task<IReadOnlyList<DiscoveredPoint>> DiscoverPointsAsync(
        string? nameFilter = null,
        int maxResults = 1000,
        CancellationToken ct = default);
    
    /// <summary>Get point metadata</summary>
    Task<PointMetadata?> GetPointMetadataAsync(string sourceAddress, CancellationToken ct = default);
}

/// <summary>
/// A discovered point from the data source.
/// </summary>
public sealed class DiscoveredPoint
{
    public required string SourceAddress { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? EngineeringUnits { get; init; }
    public string? PointType { get; init; }
    public string? WebId { get; init; }
    public Dictionary<string, object> Attributes { get; init; } = new();
}

/// <summary>
/// Detailed metadata for a data point.
/// </summary>
public sealed class PointMetadata
{
    public required string SourceAddress { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? EngineeringUnits { get; init; }
    public string? PointType { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public double? Span { get; init; }
    public double? Zero { get; init; }
    public double? CompressionDeviation { get; init; }
    public double? ExceptionDeviation { get; init; }
    public TimeSpan? CompressionTimeout { get; init; }
    public TimeSpan? ExceptionTimeout { get; init; }
    public DateTime? CreationDate { get; init; }
    public string? CreatedBy { get; init; }
    public Dictionary<string, object> ExtendedAttributes { get; init; } = new();
}

/// <summary>
/// Connector that supports real-time data subscriptions.
/// </summary>
public interface IStreamingConnector : IDataSourceConnector
{
    /// <summary>Subscribe to real-time updates for points</summary>
    Task<IDisposable> SubscribeAsync(
        IEnumerable<string> sourceAddresses,
        Action<string, DataValue> onValueReceived,
        CancellationToken ct = default);
    
    /// <summary>Poll for updates on subscribed points</summary>
    Task<IReadOnlyDictionary<string, DataValue>> PollUpdatesAsync(
        TimeSpan timeout,
        CancellationToken ct = default);
}
