namespace Naia.Domain.Entities;

/// <summary>
/// Represents a data source connection (OPC UA server, PI System, Modbus device, etc.)
/// This is the origin of time-series data entering NAIA.
/// </summary>
public sealed class DataSource
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DataSourceType SourceType { get; private set; }
    public string? ConnectionString { get; private set; }
    public string? Description { get; private set; }
    
    /// <summary>
    /// JSON configuration specific to the source type.
    /// OPC UA: { "serverUrl": "opc.tcp://...", "securityMode": "None" }
    /// PI: { "afServerName": "...", "piDataArchive": "..." }
    /// </summary>
    public string? ConfigurationJson { get; private set; }
    
    public bool IsEnabled { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastConnectedAt { get; private set; }
    public ConnectionStatus Status { get; private set; }
    
    // Navigation
    private readonly List<Point> _points = new();
    public IReadOnlyCollection<Point> Points => _points.AsReadOnly();
    
    private DataSource() { } // EF Core
    
    public static DataSource Create(
        string name,
        DataSourceType sourceType,
        string? connectionString = null,
        string? description = null,
        string? configurationJson = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Data source name is required", nameof(name));
            
        return new DataSource
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            SourceType = sourceType,
            ConnectionString = connectionString,
            Description = description,
            ConfigurationJson = configurationJson,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            Status = ConnectionStatus.Disconnected
        };
    }
    
    public void UpdateConnectionStatus(ConnectionStatus status)
    {
        Status = status;
        if (status == ConnectionStatus.Connected)
            LastConnectedAt = DateTime.UtcNow;
    }
    
    public void Disable() => IsEnabled = false;
    public void Enable() => IsEnabled = true;
    
    public void UpdateConfiguration(string? connectionString, string? configurationJson)
    {
        ConnectionString = connectionString;
        ConfigurationJson = configurationJson;
    }
}

/// <summary>
/// Type of data source - determines which adapter/connector to use.
/// </summary>
public enum DataSourceType
{
    /// <summary>OPC UA server connection</summary>
    OpcUa = 1,
    
    /// <summary>OSIsoft PI Web API</summary>
    PiWebApi = 2,
    
    /// <summary>OSIsoft PI AF SDK (direct connection)</summary>
    PiAfSdk = 10,
    
    /// <summary>Modbus TCP/RTU</summary>
    Modbus = 3,
    
    /// <summary>DNP3 protocol</summary>
    Dnp3 = 4,
    
    /// <summary>MQTT broker subscription</summary>
    Mqtt = 5,
    
    /// <summary>Generic REST/HTTP API polling</summary>
    RestApi = 6,
    
    /// <summary>SQL database polling</summary>
    SqlPoll = 7,
    
    /// <summary>CSV file import</summary>
    CsvImport = 8,
    
    /// <summary>Kafka topic subscription</summary>
    Kafka = 9
}

/// <summary>
/// Current connection status of a data source.
/// </summary>
public enum ConnectionStatus
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Error = 3,
    Disabled = 4
}
