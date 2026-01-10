using Microsoft.Extensions.Options;

namespace Naia.Connectors.PI;

/// <summary>
/// Configuration for PI Web API connection.
/// </summary>
public sealed class PIWebApiOptions
{
    public const string SectionName = "PIWebApi";
    
    /// <summary>
    /// PI Web API base URL (e.g., https://SDHQPIVWEB01.enxco.com/piwebapi)
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// PI Data Archive server name (e.g., sdhqpisrvr01)
    /// </summary>
    public string DataArchive { get; set; } = string.Empty;
    
    /// <summary>
    /// AF Server name (e.g., occafsrvr01)
    /// </summary>
    public string? AfServer { get; set; }
    
    /// <summary>
    /// Use Windows integrated authentication (default: true)
    /// </summary>
    public bool UseWindowsAuth { get; set; } = true;
    
    /// <summary>
    /// Username for Basic authentication (if not using Windows auth)
    /// </summary>
    public string? Username { get; set; }
    
    /// <summary>
    /// Password for Basic authentication (if not using Windows auth)
    /// </summary>
    public string? Password { get; set; }
    
    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Maximum concurrent API requests
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 10;
    
    /// <summary>
    /// Polling interval in milliseconds for real-time data
    /// </summary>
    public int PollingIntervalMs { get; set; } = 1000;
    
    /// <summary>
    /// Number of points per batch when reading values
    /// </summary>
    public int BatchSize { get; set; } = 100;
    
    /// <summary>
    /// Point name filters for discovery (comma-separated wildcards)
    /// Example: "WIND.*,SOLAR.*,BESS.*"
    /// </summary>
    public string? PointFilters { get; set; }
    
    /// <summary>
    /// Maximum points to discover (-1 for unlimited)
    /// </summary>
    public int MaxDiscoveredPoints { get; set; } = 10000;
}

/// <summary>
/// Options validator for PI Web API configuration.
/// </summary>
public sealed class PIWebApiOptionsValidator : IValidateOptions<PIWebApiOptions>
{
    public ValidateOptionsResult Validate(string? name, PIWebApiOptions options)
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            errors.Add("PIWebApi:BaseUrl is required");
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || 
                 (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            errors.Add("PIWebApi:BaseUrl must be a valid HTTP(S) URL");
        }
        
        if (string.IsNullOrWhiteSpace(options.DataArchive))
        {
            errors.Add("PIWebApi:DataArchive is required");
        }
        
        if (!options.UseWindowsAuth)
        {
            if (string.IsNullOrWhiteSpace(options.Username))
                errors.Add("PIWebApi:Username is required when UseWindowsAuth is false");
            if (string.IsNullOrWhiteSpace(options.Password))
                errors.Add("PIWebApi:Password is required when UseWindowsAuth is false");
        }
        
        if (options.TimeoutSeconds < 1 || options.TimeoutSeconds > 300)
        {
            errors.Add("PIWebApi:TimeoutSeconds must be between 1 and 300");
        }
        
        if (options.PollingIntervalMs < 100)
        {
            errors.Add("PIWebApi:PollingIntervalMs must be at least 100ms");
        }
        
        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
