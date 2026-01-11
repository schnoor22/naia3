namespace Naia.Connectors.OpcSimulator;

/// <summary>
/// Configuration options for the OPC UA Simulator connector.
/// Connects to the NAIA OPC UA Simulator for testing with simulated
/// wind, solar, and battery storage assets.
/// </summary>
public sealed class OpcSimulatorOptions
{
    public const string SectionName = "OpcSimulator";
    
    /// <summary>
    /// Enable/disable the OPC Simulator connector.
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Start connector automatically when system starts.
    /// </summary>
    public bool AutoStart { get; set; } = false;
    
    /// <summary>
    /// OPC UA server endpoint URL.
    /// </summary>
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840/NAIA";
    
    /// <summary>
    /// Polling interval in milliseconds.
    /// </summary>
    public int PollingIntervalMs { get; set; } = 1000;
    
    /// <summary>
    /// Subscription sampling interval for monitored items.
    /// </summary>
    public int SamplingIntervalMs { get; set; } = 500;
    
    /// <summary>
    /// Node patterns to subscribe to (supports wildcards).
    /// </summary>
    public List<string> NodePatterns { get; set; } = new()
    {
        "ThorntonWind.*",    // Wind turbines
        "DesertStarSolar.*", // Solar inverters
        "GatewayBESS.*"      // Battery storage
    };
    
    /// <summary>
    /// Maximum number of nodes to monitor.
    /// </summary>
    public int MaxNodes { get; set; } = 5000;
    
    /// <summary>
    /// Kafka topic to publish data to.
    /// </summary>
    public string KafkaTopic { get; set; } = "naia.datapoints";
    
    /// <summary>
    /// Security mode (None, Sign, SignAndEncrypt).
    /// </summary>
    public string SecurityMode { get; set; } = "None";
    
    /// <summary>
    /// Security policy (None, Basic128Rsa15, Basic256, etc.).
    /// </summary>
    public string SecurityPolicy { get; set; } = "None";
}
