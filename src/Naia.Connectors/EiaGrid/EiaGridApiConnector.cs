using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Naia.Connectors.Abstractions;

namespace Naia.Connectors.EiaGrid;

/// <summary>
/// EIA Grid Data API Connector for US electricity grid real-time data.
/// Provides demand, generation, and interchange data from regional transmission organizations.
/// </summary>
public sealed class EiaGridApiConnector : ICurrentValueConnector, IHistoricalDataConnector
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EiaGridApiConnector> _logger;
    private readonly SemaphoreSlim _throttle;
    private readonly ConcurrentDictionary<string, SeriesMetadata> _seriesCache = new();
    
    private ConnectorConfiguration? _config;
    private string _baseUrl = string.Empty;
    private string _apiKey = string.Empty;
    private List<SeriesDefinition> _series = new();
    
    public string ConnectorType => "EiaGridApi";
    public string DisplayName => "EIA Grid Data API";
    public bool IsAvailable { get; private set; }
    
    public EiaGridApiConnector(HttpClient httpClient, ILogger<EiaGridApiConnector> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _throttle = new SemaphoreSlim(5, 5); // Max 5 concurrent requests (API rate limit: 5000/hour)
    }
    
    public async Task InitializeAsync(ConnectorConfiguration config, CancellationToken ct = default)
    {
        _config = config;
        _baseUrl = config.ConnectionString.TrimEnd('/');
        
        // Get API key from credentials
        if (!config.Credentials.TryGetValue("ApiKey", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("EIA API key not provided in configuration");
            IsAvailable = false;
            return;
        }
        _apiKey = apiKey;
        
        // Parse series from config
        if (config.Credentials.TryGetValue("Series", out var seriesJson))
        {
            _series = JsonSerializer.Deserialize<List<SeriesDefinition>>(seriesJson) ?? new List<SeriesDefinition>();
        }
        
        // Configure HttpClient
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NAIA/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
        
        // Test connection
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(config.Timeout);
        
        try
        {
            var health = await CheckHealthAsync(cts.Token);
            IsAvailable = health.IsHealthy;
            
            if (IsAvailable)
            {
                _logger.LogInformation("EIA Grid API connector initialized: {SeriesCount} series configured",
                    _series.Count);
            }
            else
            {
                _logger.LogWarning("EIA Grid API connector initialization failed: {Message}", health.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EIA Grid API connector initialization error");
            IsAvailable = false;
        }
    }
    
    public async Task<ConnectorHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Simple health check - test API key validity with a minimal request
            var testUrl = $"{_baseUrl}/electricity/rto/region-data/data/?api_key={_apiKey}&frequency=hourly&data[0]=value&length=1";
            var response = await _httpClient.GetAsync(testUrl, ct);
            
            var responseTime = DateTime.UtcNow - startTime;
            
            if (response.IsSuccessStatusCode)
            {
                return new ConnectorHealthStatus
                {
                    IsHealthy = true,
                    Message = "EIA Grid API is accessible",
                    ResponseTime = responseTime,
                    Details = new Dictionary<string, object>
                    {
                        ["StatusCode"] = (int)response.StatusCode,
                        ["BaseUrl"] = _baseUrl
                    }
                };
            }
            
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            return new ConnectorHealthStatus
            {
                IsHealthy = false,
                Message = $"EIA Grid API returned {response.StatusCode}: {errorContent}",
                ResponseTime = responseTime
            };
        }
        catch (Exception ex)
        {
            return new ConnectorHealthStatus
            {
                IsHealthy = false,
                Message = $"EIA Grid API health check failed: {ex.Message}",
                ResponseTime = DateTime.UtcNow - startTime
            };
        }
    }
    
    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        var health = await CheckHealthAsync(ct);
        return health.IsHealthy;
    }
    
    #region Current Value Operations
    
    public async Task<DataValue> ReadCurrentValueAsync(string sourceAddress, CancellationToken ct = default)
    {
        var result = await ReadCurrentValuesAsync(new[] { sourceAddress }, ct);
        return result.TryGetValue(sourceAddress, out var value)
            ? value
            : throw new InvalidOperationException($"No data available for {sourceAddress}");
    }
    
    public async Task<IReadOnlyDictionary<string, DataValue>> ReadCurrentValuesAsync(
        IEnumerable<string> sourceAddresses,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, DataValue>();
        var addressList = sourceAddresses.ToList();
        
        foreach (var address in addressList)
        {
            await _throttle.WaitAsync(ct);
            try
            {
                var seriesInfo = ParseSourceAddress(address);
                if (seriesInfo == null)
                {
                    _logger.LogWarning("Invalid source address: {Address}", address);
                    continue;
                }
                
                var data = await FetchLatestDataAsync(seriesInfo.Route, seriesInfo.SeriesId, ct);
                if (data != null)
                {
                    results[address] = data;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch data for {Address}", address);
            }
            finally
            {
                _throttle.Release();
            }
        }
        
        return results;
    }
    
    #endregion
    
    #region Historical Data Operations
    
    public async Task<TimeSeriesData> ReadHistoricalDataAsync(
        string sourceAddress,
        DateTime startTime,
        DateTime endTime,
        CancellationToken ct = default)
    {
        var seriesInfo = ParseSourceAddress(sourceAddress);
        if (seriesInfo == null)
            throw new ArgumentException($"Invalid source address: {sourceAddress}");
        
        await _throttle.WaitAsync(ct);
        try
        {
            var startStr = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH");
            var endStr = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH");
            
            // Build EIA API query URL
            var url = $"{_baseUrl}/{seriesInfo.Route}/data/?" +
                      $"api_key={_apiKey}&" +
                      $"frequency=hourly&" +
                      $"data[0]=value&" +
                      $"facets[respondent][]={ExtractRegion(seriesInfo.SeriesId)}&" +
                      $"facets[type][]={ExtractDataType(seriesInfo.SeriesId)}&" +
                      $"start={startStr}&" +
                      $"end={endStr}&" +
                      $"sort[0][column]=period&" +
                      $"sort[0][direction]=asc&" +
                      $"length=5000";
            
            var response = await _httpClient.GetStringAsync(url, ct);
            var apiResponse = JsonSerializer.Deserialize<EiaApiResponse>(response);
            
            var values = new List<DataValue>();
            if (apiResponse?.Response?.Data != null)
            {
                foreach (var point in apiResponse.Response.Data)
                {
                    if (point.Value.HasValue && DateTime.TryParse(point.Period, out var timestamp))
                    {
                        values.Add(new DataValue
                        {
                            Value = point.Value.Value,
                            Timestamp = timestamp.ToUniversalTime(),
                            Quality = DataQuality.Good,
                            Units = GetUnits(seriesInfo.SeriesId)
                        });
                    }
                }
            }
            
            return new TimeSeriesData
            {
                SourceAddress = sourceAddress,
                StartTime = startTime,
                EndTime = endTime,
                Values = values,
                Units = GetUnits(seriesInfo.SeriesId)
            };
        }
        finally
        {
            _throttle.Release();
        }
    }
    
    public async Task<IReadOnlyList<TimeSeriesData>> ReadHistoricalDataBatchAsync(
        IEnumerable<string> sourceAddresses,
        DateTime startTime,
        DateTime endTime,
        CancellationToken ct = default)
    {
        var results = new List<TimeSeriesData>();
        
        foreach (var address in sourceAddresses)
        {
            try
            {
                var data = await ReadHistoricalDataAsync(address, startTime, endTime, ct);
                results.Add(data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read historical data for {Address}", address);
            }
        }
        
        return results;
    }
    
    #endregion
    
    #region Helper Methods
    
    private async Task<DataValue?> FetchLatestDataAsync(string route, string seriesId, CancellationToken ct)
    {
        try
        {
            var region = ExtractRegion(seriesId);
            var dataType = ExtractDataType(seriesId);
            
            // Query for the most recent hourly value
            var url = $"{_baseUrl}/{route}/data/?" +
                      $"api_key={_apiKey}&" +
                      $"frequency=hourly&" +
                      $"data[0]=value&" +
                      $"facets[respondent][]={region}&" +
                      $"facets[type][]={dataType}&" +
                      $"sort[0][column]=period&" +
                      $"sort[0][direction]=desc&" +
                      $"length=1";
            
            var response = await _httpClient.GetStringAsync(url, ct);
            var apiResponse = JsonSerializer.Deserialize<EiaApiResponse>(response);
            
            if (apiResponse?.Response?.Data?.Count > 0)
            {
                var point = apiResponse.Response.Data[0];
                if (point.Value.HasValue && DateTime.TryParse(point.Period, out var timestamp))
                {
                    return new DataValue
                    {
                        Value = point.Value.Value,
                        Timestamp = timestamp.ToUniversalTime(),
                        Quality = DataQuality.Good,
                        Units = GetUnits(seriesId)
                    };
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch latest data for series {SeriesId}", seriesId);
            return null;
        }
    }
    
    private static SeriesMetadata? ParseSourceAddress(string sourceAddress)
    {
        // Format: "eia/{route}/{seriesId}"
        // Example: "eia/electricity/rto/region-data/EBA.CISO-ALL.D.H"
        var parts = sourceAddress.Split('/');
        if (parts.Length < 3 || parts[0] != "eia")
            return null;
        
        var seriesId = parts[^1]; // Last part is series ID
        var route = string.Join("/", parts.Skip(1).Take(parts.Length - 2)); // Middle parts are route
        
        return new SeriesMetadata { Route = route, SeriesId = seriesId };
    }
    
    private static string CreateSourceAddress(string route, string seriesId)
    {
        return $"eia/{route}/{seriesId}";
    }
    
    private static string CreatePointName(string seriesId, string friendlyName)
    {
        if (!string.IsNullOrWhiteSpace(friendlyName))
        {
            return $"GRID_{friendlyName.ToUpperInvariant().Replace(" ", "_").Replace("-", "_")}";
        }
        
        // Fallback: use series ID
        return $"GRID_{seriesId.Replace(".", "_").Replace("-", "_")}";
    }
    
    private static string ExtractRegion(string seriesId)
    {
        // EBA.CISO-ALL.D.H -> CISO-ALL
        var parts = seriesId.Split('.');
        return parts.Length > 1 ? parts[1] : "";
    }
    
    private static string ExtractDataType(string seriesId)
    {
        // EBA.CISO-ALL.D.H -> D (Demand)
        var parts = seriesId.Split('.');
        return parts.Length > 2 ? parts[2] : "";
    }
    
    private static string GetUnits(string seriesId)
    {
        var dataType = ExtractDataType(seriesId);
        return dataType switch
        {
            "D" => "MW",      // Demand
            "NG" => "MW",     // Net Generation
            "TI" => "MW",     // Total Interchange
            "DF" => "MW",     // Demand Forecast
            "ID" => "MW",     // Interchange (Deliveries)
            "IR" => "MW",     // Interchange (Receipts)
            _ => "MW"
        };
    }
    
    private static string GetDescription(string seriesId)
    {
        var region = ExtractRegion(seriesId);
        var dataType = ExtractDataType(seriesId);
        
        var typeDesc = dataType switch
        {
            "D" => "Demand",
            "NG" => "Net Generation",
            "TI" => "Total Interchange",
            "DF" => "Demand Forecast",
            "ID" => "Interchange Deliveries",
            "IR" => "Interchange Receipts",
            _ => dataType
        };
        
        return $"{region} {typeDesc}";
    }
    
    public List<DiscoveredPoint> GetConfiguredPoints()
    {
        var points = new List<DiscoveredPoint>();
        
        foreach (var series in _series)
        {
            var sourceAddress = CreateSourceAddress(series.Route, series.SeriesId);
            var pointName = CreatePointName(series.SeriesId, series.FriendlyName);
            
            points.Add(new DiscoveredPoint
            {
                SourceAddress = sourceAddress,
                Name = pointName,
                Description = !string.IsNullOrWhiteSpace(series.FriendlyName) 
                    ? series.FriendlyName 
                    : GetDescription(series.SeriesId),
                EngineeringUnits = GetUnits(series.SeriesId),
                PointType = "Float64",
                Attributes = new Dictionary<string, object>
                {
                    ["Route"] = series.Route,
                    ["SeriesId"] = series.SeriesId,
                    ["Region"] = ExtractRegion(series.SeriesId),
                    ["DataType"] = ExtractDataType(series.SeriesId),
                    ["Provider"] = "EIA"
                }
            });
        }
        
        return points;
    }
    
    #endregion
    
    public Task DisposeAsync()
    {
        _throttle.Dispose();
        return Task.CompletedTask;
    }
}

#region Response Models

internal sealed class EiaApiResponse
{
    [JsonPropertyName("response")]
    public EiaResponse? Response { get; set; }
}

internal sealed class EiaResponse
{
    [JsonPropertyName("data")]
    public List<EiaDataPoint> Data { get; set; } = new();
    
    [JsonPropertyName("total")]
    public int Total { get; set; }
}

internal sealed class EiaDataPoint
{
    [JsonPropertyName("period")]
    public string Period { get; set; } = string.Empty;
    
    [JsonPropertyName("respondent")]
    public string? Respondent { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("value")]
    public double? Value { get; set; }
    
    [JsonPropertyName("value-units")]
    public string? ValueUnits { get; set; }
}

internal sealed class SeriesMetadata
{
    public string Route { get; set; } = string.Empty;
    public string SeriesId { get; set; } = string.Empty;
}

#endregion
