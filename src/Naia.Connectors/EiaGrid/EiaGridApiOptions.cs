namespace Naia.Connectors.EiaGrid;

/// <summary>
/// Configuration options for the EIA Grid Data API connector.
/// Provides real-time US electricity grid data from the Energy Information Administration.
/// Free API key available at: https://www.eia.gov/opendata/
/// </summary>
public sealed class EiaGridApiOptions
{
    public const string SectionName = "EiaGrid";
    
    /// <summary>
    /// Enable/disable the EIA Grid API connector
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// EIA Open Data API key (register at https://www.eia.gov/opendata/)
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Base URL for EIA Open Data API v2
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.eia.gov/v2";
    
    /// <summary>
    /// Polling interval in milliseconds (default: 15 minutes)
    /// EIA updates hourly, but we poll more frequently to catch updates quickly
    /// </summary>
    public int PollingIntervalMs { get; set; } = 900000; // 15 minutes
    
    /// <summary>
    /// HTTP request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Maximum number of data points to request per API call
    /// </summary>
    public int MaxPointsPerRequest { get; set; } = 5000;
    
    /// <summary>
    /// List of EIA series IDs to monitor.
    /// Format: {route}/{series_id}
    /// 
    /// Common series for grid data:
    /// - electricity/rto/region-data: Regional transmission organization data
    ///   Examples: EBA.{REGION}-ALL.D.H (demand), EBA.{REGION}-ALL.NG.H (net generation)
    ///   Regions: CISO (California), ERCO (Texas/ERCOT), MISO (Midwest), NYIS (NY), PJM, etc.
    /// 
    /// Available data types (suffix):
    /// - D.H = Demand (MW)
    /// - NG.H = Net Generation (MW)
    /// - TI.H = Total Interchange (MW)
    /// - DF.H = Demand Forecast (MW)
    /// </summary>
    public List<SeriesDefinition> Series { get; set; } = new()
    {
        // Sample series for major US grid operators
        new() { Route = "electricity/rto/region-data", SeriesId = "EBA.CISO-ALL.D.H", FriendlyName = "CAISO Demand" },
        new() { Route = "electricity/rto/region-data", SeriesId = "EBA.ERCO-ALL.D.H", FriendlyName = "ERCOT Demand" },
        new() { Route = "electricity/rto/region-data", SeriesId = "EBA.MISO-ALL.D.H", FriendlyName = "MISO Demand" },
        new() { Route = "electricity/rto/region-data", SeriesId = "EBA.NYIS-ALL.D.H", FriendlyName = "NYISO Demand" },
        new() { Route = "electricity/rto/region-data", SeriesId = "EBA.PJM-ALL.D.H", FriendlyName = "PJM Demand" }
    };
}

/// <summary>
/// Defines an EIA data series to monitor
/// </summary>
public sealed class SeriesDefinition
{
    /// <summary>
    /// API route path (e.g., "electricity/rto/region-data")
    /// </summary>
    public string Route { get; set; } = string.Empty;
    
    /// <summary>
    /// Series ID to query (e.g., "EBA.CISO-ALL.D.H")
    /// </summary>
    public string SeriesId { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable name for the point (e.g., "CAISO Demand")
    /// </summary>
    public string FriendlyName { get; set; } = string.Empty;
}
