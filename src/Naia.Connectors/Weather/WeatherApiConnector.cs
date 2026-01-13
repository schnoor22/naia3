using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Naia.Connectors.Abstractions;

namespace Naia.Connectors.Weather;

/// <summary>
/// Weather API Connector using Open-Meteo (free, no API key required).
/// Provides current weather observations and historical data for specified locations.
/// </summary>
public sealed class WeatherApiConnector : ICurrentValueConnector, IHistoricalDataConnector, IDiscoverableConnector
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WeatherApiConnector> _logger;
    private readonly SemaphoreSlim _throttle;
    private readonly ConcurrentDictionary<string, WeatherLocation> _locationCache = new();
    
    private ConnectorConfiguration? _config;
    private string _baseUrl = string.Empty;
    private List<WeatherLocation> _locations = new();
    private List<string> _variables = new();
    
    public string ConnectorType => "WeatherApi";
    public string DisplayName => "Weather API (Open-Meteo)";
    public bool IsAvailable { get; private set; }
    
    public WeatherApiConnector(HttpClient httpClient, ILogger<WeatherApiConnector> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _throttle = new SemaphoreSlim(10, 10); // Max 10 concurrent requests
    }
    
    public async Task InitializeAsync(ConnectorConfiguration config, CancellationToken ct = default)
    {
        _config = config;
        _baseUrl = config.ConnectionString.TrimEnd('/');
        
        // Configure HttpClient
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NAIA/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
        
        // Parse locations from config
        if (config.Credentials.TryGetValue("Locations", out var locationsJson))
        {
            _locations = JsonSerializer.Deserialize<List<string>>(locationsJson)
                ?.Select(ParseLocation)
                .Where(loc => loc != null)
                .Select(loc => loc!)
                .ToList() ?? new List<WeatherLocation>();
        }
        
        // Parse variables from config
        if (config.Credentials.TryGetValue("Variables", out var variablesJson))
        {
            _variables = JsonSerializer.Deserialize<List<string>>(variablesJson) ?? new List<string>();
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
                _logger.LogInformation("Weather API connector initialized: {LocationCount} locations, {VariableCount} variables",
                    _locations.Count, _variables.Count);
            }
            else
            {
                _logger.LogWarning("Weather API connector initialization failed: {Message}", health.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Weather API connector initialization error");
            IsAvailable = false;
        }
    }
    
    public async Task<ConnectorHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Simple health check - fetch current weather for a test location
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/forecast?latitude=52.5&longitude=-0.5&current=temperature_2m",
                ct);
            
            var responseTime = DateTime.UtcNow - startTime;
            
            if (response.IsSuccessStatusCode)
            {
                return new ConnectorHealthStatus
                {
                    IsHealthy = true,
                    Message = "Weather API is accessible",
                    ResponseTime = responseTime,
                    Details = new Dictionary<string, object>
                    {
                        ["StatusCode"] = (int)response.StatusCode,
                        ["BaseUrl"] = _baseUrl
                    }
                };
            }
            
            return new ConnectorHealthStatus
            {
                IsHealthy = false,
                Message = $"Weather API returned {response.StatusCode}",
                ResponseTime = responseTime
            };
        }
        catch (Exception ex)
        {
            return new ConnectorHealthStatus
            {
                IsHealthy = false,
                Message = $"Weather API health check failed: {ex.Message}",
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
        
        // Group addresses by location
        var locationGroups = addressList
            .Select(addr => (Address: addr, Location: ParseSourceAddress(addr)))
            .Where(x => x.Location != null)
            .GroupBy(x => $"{x.Location!.Latitude},{x.Location.Longitude}");
        
        foreach (var group in locationGroups)
        {
            await _throttle.WaitAsync(ct);
            try
            {
                var location = group.First().Location!;
                var variables = group.Select(x => x.Location!.Variable).Distinct().ToList();
                
                var weatherData = await FetchCurrentWeatherAsync(location.Latitude, location.Longitude, variables, ct);
                
                foreach (var item in group)
                {
                    var variable = item.Location!.Variable;
                    if (weatherData.TryGetValue(variable, out var value))
                    {
                        results[item.Address] = new DataValue
                        {
                            Value = value.Value,
                            Timestamp = value.Timestamp,
                            Quality = DataQuality.Good,
                            Units = GetUnits(variable)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch weather data for location group");
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
        var location = ParseSourceAddress(sourceAddress);
        if (location == null)
            throw new ArgumentException($"Invalid source address: {sourceAddress}");
        
        await _throttle.WaitAsync(ct);
        try
        {
            var startStr = startTime.ToString("yyyy-MM-dd");
            var endStr = endTime.ToString("yyyy-MM-dd");
            
            var url = $"{_baseUrl}/forecast?" +
                      $"latitude={location.Latitude}&" +
                      $"longitude={location.Longitude}&" +
                      $"start_date={startStr}&" +
                      $"end_date={endStr}&" +
                      $"hourly={location.Variable}&" +
                      $"timezone=UTC";
            
            var response = await _httpClient.GetStringAsync(url, ct);
            var data = JsonSerializer.Deserialize<OpenMeteoResponse>(response);
            
            var values = new List<DataValue>();
            if (data?.Hourly?.Time != null && data.Hourly.Values.ContainsKey(location.Variable))
            {
                var times = data.Hourly.Time;
                var vals = data.Hourly.Values[location.Variable];
                
                for (int i = 0; i < times.Count && i < vals.Count; i++)
                {
                    if (vals[i].HasValue)
                    {
                        values.Add(new DataValue
                        {
                            Value = vals[i].Value,
                            Timestamp = DateTime.Parse(times[i]).ToUniversalTime(),
                            Quality = DataQuality.Good,
                            Units = GetUnits(location.Variable)
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
                Units = GetUnits(location.Variable)
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
    
    #region Discovery Operations
    
    public async Task<IReadOnlyList<DiscoveredPoint>> DiscoverPointsAsync(
        string? nameFilter = null, 
        int maxResults = 1000, 
        CancellationToken ct = default)
    {
        var points = new List<DiscoveredPoint>();
        
        foreach (var location in _locations)
        {
            foreach (var variable in _variables)
            {
                var pointName = CreatePointName(location.Latitude, location.Longitude, variable);
                var sourceAddress = CreateSourceAddress(location.Latitude, location.Longitude, variable);
                
                // Apply name filter if provided
                if (!string.IsNullOrEmpty(nameFilter) && 
                    !pointName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                
                points.Add(new DiscoveredPoint
                {
                    SourceAddress = sourceAddress,
                    Name = pointName,
                    Description = $"{GetVariableDescription(variable)} at {location.Latitude:F2}, {location.Longitude:F2}",
                    EngineeringUnits = GetUnits(variable),
                    PointType = "Float64",
                    Attributes = new Dictionary<string, object>
                    {
                        ["Latitude"] = location.Latitude,
                        ["Longitude"] = location.Longitude,
                        ["Variable"] = variable,
                        ["Provider"] = "Open-Meteo"
                    }
                });
                
                if (points.Count >= maxResults)
                    break;
            }
            
            if (points.Count >= maxResults)
                break;
        }
        
        _logger.LogInformation("Discovered {Count} weather points across {LocationCount} locations", 
            points.Count, _locations.Count);
        
        return points;
    }
    
    public Task<PointMetadata?> GetPointMetadataAsync(string sourceAddress, CancellationToken ct = default)
    {
        var location = ParseSourceAddress(sourceAddress);
        if (location == null)
            return Task.FromResult<PointMetadata?>(null);
        
        return Task.FromResult<PointMetadata?>(new PointMetadata
        {
            SourceAddress = sourceAddress,
            Name = CreatePointName(location.Latitude, location.Longitude, location.Variable),
            Description = GetVariableDescription(location.Variable),
            EngineeringUnits = GetUnits(location.Variable),
            PointType = "Float64",
            ExtendedAttributes = new Dictionary<string, object>
            {
                ["Latitude"] = location.Latitude,
                ["Longitude"] = location.Longitude,
                ["Variable"] = location.Variable
            }
        });
    }
    
    #endregion
    
    #region Helper Methods
    
    private async Task<Dictionary<string, (double Value, DateTime Timestamp)>> FetchCurrentWeatherAsync(
        double latitude, 
        double longitude, 
        List<string> variables, 
        CancellationToken ct)
    {
        var variableStr = string.Join(",", variables);
        var url = $"{_baseUrl}/forecast?" +
                  $"latitude={latitude}&" +
                  $"longitude={longitude}&" +
                  $"current={variableStr}&" +
                  $"timezone=UTC";
        
        var response = await _httpClient.GetStringAsync(url, ct);
        var data = JsonSerializer.Deserialize<OpenMeteoResponse>(response);
        
        var results = new Dictionary<string, (double, DateTime)>();
        if (data?.Current != null)
        {
            var timestamp = DateTime.Parse(data.Current.Time).ToUniversalTime();
            
            foreach (var variable in variables)
            {
                if (data.Current.Values.TryGetValue(variable, out var value) && value.HasValue)
                {
                    results[variable] = (value.Value, timestamp);
                }
            }
        }
        
        return results;
    }
    
    private static WeatherLocation? ParseLocation(string location)
    {
        var parts = location.Split(',');
        if (parts.Length != 2)
            return null;
        
        if (double.TryParse(parts[0].Trim(), out var lat) && 
            double.TryParse(parts[1].Trim(), out var lon))
        {
            return new WeatherLocation { Latitude = lat, Longitude = lon };
        }
        
        return null;
    }
    
    private static WeatherSourceAddress? ParseSourceAddress(string sourceAddress)
    {
        // Format: "weather/{lat},{lon}/{variable}"
        var parts = sourceAddress.Split('/');
        if (parts.Length != 3 || parts[0] != "weather")
            return null;
        
        var location = ParseLocation(parts[1]);
        if (location == null)
            return null;
        
        return new WeatherSourceAddress
        {
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Variable = parts[2]
        };
    }
    
    private static string CreateSourceAddress(double latitude, double longitude, string variable)
    {
        return $"weather/{latitude:F2},{longitude:F2}/{variable}";
    }
    
    private static string CreatePointName(double latitude, double longitude, string variable)
    {
        var latStr = latitude >= 0 ? $"N{Math.Abs(latitude):F2}" : $"S{Math.Abs(latitude):F2}";
        var lonStr = longitude >= 0 ? $"E{Math.Abs(longitude):F2}" : $"W{Math.Abs(longitude):F2}";
        var varName = variable.ToUpperInvariant().Replace("_", "");
        return $"WEATHER_{latStr}_{lonStr}_{varName}";
    }
    
    private static string GetUnits(string variable)
    {
        return variable switch
        {
            "temperature_2m" => "°C",
            "apparent_temperature" => "°C",
            "dew_point_2m" => "°C",
            "relative_humidity_2m" => "%",
            "precipitation" => "mm",
            "rain" => "mm",
            "snowfall" => "cm",
            "pressure_msl" => "hPa",
            "surface_pressure" => "hPa",
            "cloud_cover" => "%",
            "wind_speed_10m" => "m/s",
            "wind_direction_10m" => "°",
            "wind_gusts_10m" => "m/s",
            _ => ""
        };
    }
    
    private static string GetVariableDescription(string variable)
    {
        return variable switch
        {
            "temperature_2m" => "Air Temperature (2m)",
            "apparent_temperature" => "Apparent Temperature",
            "dew_point_2m" => "Dew Point (2m)",
            "relative_humidity_2m" => "Relative Humidity (2m)",
            "precipitation" => "Total Precipitation",
            "rain" => "Rain",
            "snowfall" => "Snowfall",
            "pressure_msl" => "Sea Level Pressure",
            "surface_pressure" => "Surface Pressure",
            "cloud_cover" => "Cloud Cover",
            "wind_speed_10m" => "Wind Speed (10m)",
            "wind_direction_10m" => "Wind Direction (10m)",
            "wind_gusts_10m" => "Wind Gusts (10m)",
            _ => variable
        };
    }
    
    #endregion
    
    public Task DisposeAsync()
    {
        _throttle.Dispose();
        return Task.CompletedTask;
    }
}

#region Response Models

internal sealed class OpenMeteoResponse
{
    public CurrentWeather? Current { get; set; }
    public HourlyWeather? Hourly { get; set; }
}

internal sealed class CurrentWeather
{
    public string Time { get; set; } = string.Empty;
    public Dictionary<string, double?> Values { get; set; } = new();
}

internal sealed class HourlyWeather
{
    public List<string> Time { get; set; } = new();
    public Dictionary<string, List<double?>> Values { get; set; } = new();
}

internal sealed class WeatherLocation
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

internal sealed class WeatherSourceAddress
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Variable { get; set; } = string.Empty;
}

#endregion
