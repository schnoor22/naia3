namespace Naia.Connectors.Weather;

/// <summary>
/// Configuration options for the Weather API connector.
/// Uses Open-Meteo API (free, no API key required).
/// </summary>
public sealed class WeatherApiOptions
{
    public const string SectionName = "WeatherApi";
    
    /// <summary>
    /// Enable/disable the Weather API connector
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Enable automatic point discovery on startup
    /// </summary>
    public bool EnableAutoDiscovery { get; set; } = true;
    
    /// <summary>
    /// Base URL for Open-Meteo API
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.open-meteo.com/v1";
    
    /// <summary>
    /// Polling interval in milliseconds (default: 5 minutes)
    /// Weather data updates typically every 1-15 minutes depending on source
    /// </summary>
    public int PollingIntervalMs { get; set; } = 300000; // 5 minutes
    
    /// <summary>
    /// HTTP request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// List of locations to monitor (format: "latitude,longitude" or "lat,lon")
    /// Example: "52.5,-0.5" for Kelmarsh Wind Farm location
    /// </summary>
    public List<string> Locations { get; set; } = new()
    {
        "52.5,-0.5"  // Default: Kelmarsh, UK
    };
    
    /// <summary>
    /// Maximum number of points to discover per location
    /// </summary>
    public int MaxDiscoveredPoints { get; set; } = 1000;
    
    /// <summary>
    /// Weather variables to collect from API.
    /// Available: temperature_2m, relative_humidity_2m, dew_point_2m, apparent_temperature,
    /// pressure_msl, surface_pressure, precipitation, rain, snowfall, cloud_cover,
    /// wind_speed_10m, wind_direction_10m, wind_gusts_10m
    /// </summary>
    public List<string> Variables { get; set; } = new()
    {
        "temperature_2m",
        "relative_humidity_2m",
        "dew_point_2m",
        "pressure_msl",
        "wind_speed_10m",
        "wind_direction_10m",
        "wind_gusts_10m",
        "precipitation",
        "cloud_cover"
    };
}
