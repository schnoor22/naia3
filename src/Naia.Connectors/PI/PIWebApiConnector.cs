using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Naia.Connectors.Abstractions;
using Naia.Connectors.PI.Models;

namespace Naia.Connectors.PI;

/// <summary>
/// PI Web API Connector - Connects NAIA to OSIsoft PI System via PI Web API.
/// 
/// Supports:
/// - PI Point discovery and enumeration
/// - Current value retrieval (single and batch)
/// - Historical data retrieval (recorded values)
/// - Point metadata and attributes
/// - AF Server element browsing
/// 
/// Configuration:
/// {
///   "ConnectionString": "https://pi-web-api-server/piwebapi",
///   "PiDataArchive": "sdhqpisrvr01",
///   "UseWindowsAuth": true
/// }
/// </summary>
public sealed class PIWebApiConnector : ICurrentValueConnector, IHistoricalDataConnector, IDiscoverableConnector
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PIWebApiConnector> _logger;
    private readonly SemaphoreSlim _throttle;
    private readonly ConcurrentDictionary<string, string> _webIdCache = new();
    
    private ConnectorConfiguration? _config;
    private string _baseUrl = string.Empty;
    private string? _dataServerWebId;
    private string? _dataServerName;
    
    public string ConnectorType => "PIWebApi";
    public string DisplayName => "PI Web API";
    public bool IsAvailable { get; private set; }
    
    public PIWebApiConnector(HttpClient httpClient, ILogger<PIWebApiConnector> logger)
    {
        // Create a new HttpClient with SSL bypass for self-signed certificates
        // This ensures the callback is applied at socket level before TLS handshake
        var handler = new HttpClientHandler
        {
            UseDefaultCredentials = true,
            PreAuthenticate = true,
            // Accept all certificates (development environment only)
            ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) =>
            {
                // Log certificate issues but accept anyway
                if (errors != System.Net.Security.SslPolicyErrors.None)
                {
                    logger.LogWarning("SSL certificate validation error: {Errors}. Certificate subject: {Subject}", 
                        errors, certificate?.Subject);
                }
                return true; // Accept the certificate
            }
        };
        
        _httpClient = new HttpClient(handler);
        _logger = logger;
        _throttle = new SemaphoreSlim(10, 10); // Default max concurrent requests
    }
    
    public async Task InitializeAsync(ConnectorConfiguration config, CancellationToken ct = default)
    {
        _config = config;
        _baseUrl = config.ConnectionString.TrimEnd('/');
        _dataServerName = config.PiDataArchive;
        
        // Skip initialization if pointing to localhost or explicitly disabled
        if (_baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) || 
            _baseUrl.Contains("127.0.0.1"))
        {
            IsAvailable = false;
            _logger.LogDebug("PI Web API connector disabled (localhost URL)");
            return;
        }
        
        // Configure authentication (only once per singleton lifetime)
        if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
        {
            ConfigureAuthentication(config);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }
        
        // Test connection with timeout via CancellationToken instead
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(config.Timeout);
        
        try
        {
            var health = await CheckHealthAsync(cts.Token);
            IsAvailable = health.IsHealthy;
            
            if (IsAvailable)
            {
                _logger.LogInformation("PI Web API connector initialized: {BaseUrl}", _baseUrl);
                
                // Cache the data server WebId for faster lookups
                await CacheDataServerWebIdAsync(ct);
            }
            else
            {
                _logger.LogWarning("PI Web API connector failed to initialize: {Message}", health.Message);
            }
        }
        catch (OperationCanceledException)
        {
            IsAvailable = false;
            _logger.LogError("PI Web API connection timeout");
        }
    }
    
    private void ConfigureAuthentication(ConnectorConfiguration config)
    {
        if (!config.UseWindowsAuth)
        {
            // Use Basic auth if credentials provided
            var hasUsername = config.Credentials.TryGetValue("Username", out var username) && !string.IsNullOrWhiteSpace(username);
            var hasPassword = config.Credentials.TryGetValue("Password", out var password) && !string.IsNullOrWhiteSpace(password);
            
            if (hasUsername && hasPassword)
            {
                var authBytes = Encoding.ASCII.GetBytes($"{username}:{password}");
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                _logger.LogDebug("Configured Basic authentication for PI Web API");
            }
        }
        // Windows auth is handled by HttpClientHandler.UseDefaultCredentials
    }
    
    public async Task<ConnectorHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/system", ct);
            sw.Stop();
            
            if (response.IsSuccessStatusCode)
            {
                var systemInfo = await response.Content.ReadFromJsonAsync<PIWebApiSystemInfo>(ct);
                return new ConnectorHealthStatus
                {
                    IsHealthy = true,
                    Message = $"Connected to {systemInfo?.ProductTitle ?? "PI Web API"} v{systemInfo?.ProductVersion}",
                    ResponseTime = sw.Elapsed,
                    Details = new Dictionary<string, object>
                    {
                        ["productVersion"] = systemInfo?.ProductVersion ?? "unknown",
                        ["baseUrl"] = _baseUrl
                    }
                };
            }
            
            return new ConnectorHealthStatus
            {
                IsHealthy = false,
                Message = $"PI Web API returned {response.StatusCode}: {response.ReasonPhrase}",
                ResponseTime = sw.Elapsed
            };
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "HTTP error connecting to PI Web API at {BaseUrl}", _baseUrl);
            return new ConnectorHealthStatus
            {
                IsHealthy = false,
                Message = $"Connection failed: {ex.Message}",
                ResponseTime = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error checking PI Web API health");
            return new ConnectorHealthStatus
            {
                IsHealthy = false,
                Message = $"Error: {ex.Message}",
                ResponseTime = sw.Elapsed
            };
        }
    }
    
    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        var health = await CheckHealthAsync(ct);
        return health.IsHealthy;
    }
    
    private async Task CacheDataServerWebIdAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{_baseUrl}/dataservers";
            _logger.LogInformation("Fetching data servers from: {Url}", url);
            
            var response = await _httpClient.GetFromJsonAsync<PIWebApiItemsResponse<PIDataServer>>(url, ct);
            
            if (response?.Items != null)
            {
                _logger.LogInformation("Found {Count} data servers in PI", response.Items.Count);
                
                foreach (var server in response.Items)
                {
                    _logger.LogInformation("  - Server: {Name} (WebId: {WebId})", server.Name, server.WebId);
                }
                
                // Find the target data server or use the first one
                var targetServer = !string.IsNullOrEmpty(_dataServerName)
                    ? response.Items.FirstOrDefault(s => 
                        s.Name?.Equals(_dataServerName, StringComparison.OrdinalIgnoreCase) == true)
                    : response.Items.FirstOrDefault();
                
                if (targetServer != null)
                {
                    _dataServerWebId = targetServer.WebId;
                    _dataServerName = targetServer.Name;
                    _logger.LogInformation(
                        "Selected PI Data Archive: {ServerName} (WebId: {WebId})", 
                        _dataServerName, _dataServerWebId);
                }
                else
                {
                    _logger.LogWarning("Could not find data server '{ServerName}' in PI system", _dataServerName);
                }
            }
            else
            {
                _logger.LogWarning("No data servers returned from PI Web API");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache PI Data Server WebId");
        }
    }
    
    #region Current Value Operations
    
    public async Task<DataValue> ReadCurrentValueAsync(string sourceAddress, CancellationToken ct = default)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var webId = await GetPointWebIdAsync(sourceAddress, ct);
            
            var response = await _httpClient.GetFromJsonAsync<PIStreamValue>(
                $"{_baseUrl}/streams/{webId}/value", ct);
            
            if (response?.Value == null)
            {
                throw new InvalidOperationException($"No data returned for {sourceAddress}");
            }
            
            return MapToDataValue(response.Value);
        }
        finally
        {
            _throttle.Release();
        }
    }
    
    public async Task<IReadOnlyDictionary<string, DataValue>> ReadCurrentValuesAsync(
        IEnumerable<string> sourceAddresses, 
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, DataValue>();
        var addressList = sourceAddresses.ToList();
        
        if (addressList.Count == 0)
            return results;
        
        // Get WebIds for all points (uses cache where available)
        var webIdMap = new Dictionary<string, string>(); // WebId -> SourceAddress
        var webIds = new List<(string webId, string address)>();
        
        _logger.LogDebug("Reading current values for {Count} points", addressList.Count);
        
        foreach (var address in addressList)
        {
            try
            {
                var webId = await GetPointWebIdAsync(address, ct);
                webIds.Add((webId, address));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not resolve WebId for {Address} (point may not exist in PI)", address);
            }
        }
        
        _logger.LogDebug("Successfully resolved {Count} WebIds out of {Total} addresses", webIds.Count, addressList.Count);
        
        if (webIds.Count == 0)
            return results;
        
        // Use individual /streams/{webId}/value calls to avoid URL length limits
        int successCount = 0;
        int failureCount = 0;
        foreach (var (webId, address) in webIds)
        {
            await _throttle.WaitAsync(ct);
            try
            {
                var url = $"{_baseUrl}/streams/{webId}/value";
                
                // First get raw response to see what PI is actually returning
                var rawResponse = await _httpClient.GetAsync(url, ct);
                var content = await rawResponse.Content.ReadAsStringAsync(ct);
                
                if (failureCount == 0 && successCount == 0)
                {
                    // Log first response to understand format
                    _logger.LogWarning("Sample PI /streams response for {Address}: {Content}", address, content.Substring(0, Math.Min(300, content.Length)));
                }
                
                // PI /streams/{webId}/value returns PITimedValue directly, not wrapped in PIStreamValue
                var timedValue = System.Text.Json.JsonSerializer.Deserialize<PITimedValue>(content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (timedValue != null)
                {
                    results[address] = MapToDataValue(timedValue);
                    successCount++;
                }
                else
                {
                    _logger.LogDebug("No value returned from PI for {Address} at URL {Url}", address, url);
                    failureCount++;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "HTTP error reading {Address}: {StatusCode}", address, ex.StatusCode);
                failureCount++;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "JSON parse error for {Address} - response format may be incompatible", address);
                failureCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error reading current value for {Address}", address);
                failureCount++;
            }
            finally
            {
                _throttle.Release();
            }
        }
        
        _logger.LogDebug("Read values complete: {Success} succeeded, {Failure} failed", successCount, failureCount);
        
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
        await _throttle.WaitAsync(ct);
        try
        {
            var webId = await GetPointWebIdAsync(sourceAddress, ct);
            
            var startStr = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endStr = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            
            var response = await _httpClient.GetFromJsonAsync<PIWebApiItemsResponse<PITimedValue>>(
                $"{_baseUrl}/streams/{webId}/recorded?startTime={startStr}&endTime={endStr}", ct);
            
            var values = response?.Items?.Select(MapToDataValue).ToList() ?? new List<DataValue>();
            
            return new TimeSeriesData
            {
                SourceAddress = sourceAddress,
                StartTime = startTime,
                EndTime = endTime,
                Values = values,
                Units = response?.Items?.FirstOrDefault()?.UnitsAbbreviation
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
        var addressList = sourceAddresses.ToList();
        
        // Get WebIds and build mapping
        var webIdMap = new Dictionary<string, string>();
        var webIds = new List<string>();
        
        foreach (var address in addressList)
        {
            try
            {
                var webId = await GetPointWebIdAsync(address, ct);
                webIds.Add(webId);
                webIdMap[webId] = address;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve WebId for {Address}", address);
            }
        }
        
        if (webIds.Count == 0)
            return results;
        
        var startStr = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var endStr = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        
        // Batch request
        const int batchSize = 50; // Lower batch size for historical data
        foreach (var batch in webIds.Chunk(batchSize))
        {
            await _throttle.WaitAsync(ct);
            try
            {
                var webIdParam = string.Join("&webId=", batch);
                var response = await _httpClient.GetFromJsonAsync<PIWebApiItemsResponse<PIStreamSetValue>>(
                    $"{_baseUrl}/streamsets/recorded?startTime={startStr}&endTime={endStr}&webId={webIdParam}", ct);
                
                if (response?.Items != null)
                {
                    foreach (var item in response.Items)
                    {
                        if (item.WebId != null && webIdMap.TryGetValue(item.WebId, out var address))
                        {
                            results.Add(new TimeSeriesData
                            {
                                SourceAddress = address,
                                StartTime = startTime,
                                EndTime = endTime,
                                Values = item.Items?.Select(MapToDataValue).ToList() ?? new List<DataValue>(),
                                Units = item.UnitsAbbreviation
                            });
                        }
                    }
                }
            }
            finally
            {
                _throttle.Release();
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
        var results = new List<DiscoveredPoint>();
        
        await _throttle.WaitAsync(ct);
        try
        {
            var pageSize = Math.Min(maxResults, 1000);
            
            // Build the search/query endpoint URL with pagination support
            var url = $"{_baseUrl}/search/query";
            
            // Build the query parameter - just use name filter
            string query;
            if (!string.IsNullOrEmpty(nameFilter) && nameFilter != "*")
            {
                // PI Web API search syntax is simple - no escaping needed
                query = $"name:{nameFilter}";
            }
            else
            {
                query = "*";  // Match all
            }
            
            // Build full URL with count parameter to request more results
            var fullUrl = $"{url}?q={System.Net.WebUtility.UrlEncode(query)}&count={pageSize}";
            
            _logger.LogInformation("Discovering PI points - URL: {Url}", fullUrl);
            
            try
            {
                var response = await _httpClient.GetFromJsonAsync<PIWebApiSearchResponse>(fullUrl, ct);
                
                if (response?.Items != null && response.Items.Count > 0)
                {
                    _logger.LogInformation("Found {TotalHits} points matching filter '{Filter}'", response.TotalHits ?? response.Items.Count, nameFilter);
                    
                    // Log point names for debugging
                    var allPoints = string.Join("\n  ", response.Items.Take(50).Select(p => p.Name ?? "unknown"));
                    _logger.LogInformation("Sample {Count} point names from {Server}:\n  {Points}", 
                        Math.Min(50, response.Items.Count), _dataServerName, allPoints);
                    
                    foreach (var point in response.Items.Take(maxResults))
                    {
                        results.Add(new DiscoveredPoint
                        {
                            SourceAddress = point.Name ?? "",
                            Name = point.Name ?? "",
                            Description = point.Description,
                            EngineeringUnits = point.UoM,
                            PointType = point.DataType,
                            WebId = point.WebId,
                            Attributes = new Dictionary<string, object>
                            {
                                ["itemType"] = point.ItemType ?? "",
                                ["uniqueId"] = point.UniqueID ?? ""
                            }
                        });
                        
                        if (!string.IsNullOrEmpty(point.Name) && !string.IsNullOrEmpty(point.WebId))
                        {
                            _webIdCache[point.Name.ToUpperInvariant()] = point.WebId;
                        }
                    }
                    
                    _logger.LogInformation("Successfully discovered {Count} points matching filter '{Filter}'", results.Count, nameFilter);
                }
                else
                {
                    _logger.LogWarning("No points returned from PI search endpoint");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying PI search endpoint: {Url}", fullUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DiscoverPointsAsync");
        }
        finally
        {
            _throttle.Release();
        }
        
        return results;
    }
    
    public async Task<PointMetadata?> GetPointMetadataAsync(string sourceAddress, CancellationToken ct = default)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var webId = await GetPointWebIdAsync(sourceAddress, ct);
            
            var point = await _httpClient.GetFromJsonAsync<PIPoint>(
                $"{_baseUrl}/points/{webId}", ct);
            
            if (point == null)
                return null;
            
            return new PointMetadata
            {
                SourceAddress = sourceAddress,
                Name = point.Name ?? sourceAddress,
                Description = point.Descriptor,
                EngineeringUnits = point.EngineeringUnits,
                PointType = point.PointType,
                Span = point.Span,
                Zero = point.Zero,
                Minimum = point.Zero,
                Maximum = (point.Zero ?? 0) + (point.Span ?? 100),
                ExtendedAttributes = new Dictionary<string, object>
                {
                    ["pointClass"] = point.PointClass ?? "",
                    ["step"] = point.Step,
                    ["future"] = point.Future,
                    ["displayDigits"] = point.DisplayDigits ?? 0
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get metadata for {SourceAddress}", sourceAddress);
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }
    
    #endregion
    
    #region WebId Resolution
    
    private async Task<string> GetPointWebIdAsync(string sourceAddress, CancellationToken ct)
    {
        // Normalize the address
        var normalizedAddress = sourceAddress.ToUpperInvariant().Trim();
        
        // Check cache first
        if (_webIdCache.TryGetValue(normalizedAddress, out var cachedWebId))
        {
            return cachedWebId;
        }
        
        // Query PI Web API
        if (string.IsNullOrEmpty(_dataServerWebId))
        {
            await CacheDataServerWebIdAsync(ct);
        }
        
        // Try to get point by name
        var url = $"{_baseUrl}/points?path=\\\\{Uri.EscapeDataString(_dataServerName ?? "")}\\{Uri.EscapeDataString(sourceAddress)}";
        
        try
        {
            var response = await _httpClient.GetFromJsonAsync<PIPoint>(url, ct);
            
            if (response?.WebId != null)
            {
                _webIdCache[normalizedAddress] = response.WebId;
                return response.WebId;
            }
        }
        catch (HttpRequestException)
        {
            // Try searching instead
        }
        
        // Fall back to search - use wildcard search for better matching with spaces
        if (!string.IsNullOrEmpty(_dataServerWebId))
        {
            var searchUrl = $"{_baseUrl}/dataservers/{_dataServerWebId}/points?nameFilter={Uri.EscapeDataString(sourceAddress)}&maxCount=10";
            var searchResponse = await _httpClient.GetFromJsonAsync<PIWebApiItemsResponse<PIPoint>>(searchUrl, ct);
            
            _logger.LogDebug("Search for '{Address}' returned {Count} results", sourceAddress, searchResponse?.Items?.Count ?? 0);
            
            // Try exact match first (case-insensitive)
            var point = searchResponse?.Items?.FirstOrDefault(p => 
                p.Name?.Equals(sourceAddress, StringComparison.OrdinalIgnoreCase) == true);
            
            // If no exact match, try partial match (substring anywhere in name)
            if (point == null && searchResponse?.Items?.Count > 0)
            {
                point = searchResponse.Items.FirstOrDefault(p =>
                    p.Name?.IndexOf(sourceAddress, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            
            if (point?.WebId != null)
            {
                _logger.LogDebug("Found point '{PointName}' for search '{Address}'", point.Name, sourceAddress);
                _webIdCache[normalizedAddress] = point.WebId;
                return point.WebId;
            }
            
            // Log what we found for debugging
            if (searchResponse?.Items?.Count > 0)
            {
                _logger.LogDebug(
                    "Search found {Count} points for '{Address}' but none matched: {Names}",
                    searchResponse.Items.Count,
                    sourceAddress,
                    string.Join(", ", searchResponse.Items.Select(p => $"'{p.Name}'")));
            }
            else
            {
                _logger.LogDebug("Search for '{Address}' returned no results from nameFilter", sourceAddress);
            }
        }
        
        throw new InvalidOperationException($"PI Point not found: {sourceAddress}");
    }
    
    #endregion
    
    #region Helpers
    
    private static DataValue MapToDataValue(PITimedValue piValue)
    {
        return new DataValue
        {
            Value = piValue.Value,
            Timestamp = piValue.Timestamp,
            Quality = MapQuality(piValue.Good, piValue.Questionable, piValue.Substituted),
            Units = piValue.UnitsAbbreviation
        };
    }
    
    private static DataQuality MapQuality(bool good, bool questionable, bool substituted)
    {
        if (substituted) return DataQuality.Substituted;
        if (questionable) return DataQuality.Uncertain;
        return good ? DataQuality.Good : DataQuality.Bad;
    }
    
    #endregion
    
    public Task DisposeAsync()
    {
        _throttle.Dispose();
        return Task.CompletedTask;
    }
}
