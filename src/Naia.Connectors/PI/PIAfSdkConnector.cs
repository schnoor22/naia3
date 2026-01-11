using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Naia.Connectors.Abstractions;
using OSIsoft.AF;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;
using OSIsoft.AF.Time;

namespace Naia.Connectors.PI;

/// <summary>
/// PI AF SDK Connector - Direct connection to PI Data Archive using OSIsoft AF SDK.
/// 
/// Advantages over PI Web API:
/// - Local library (faster than REST API)
/// - AFDataPipe for real-time push-based subscriptions
/// - Direct PI Data Archive access (lower latency)
/// - Better performance for large point counts (1M+)
/// - Native SDT (compression/exception deviation) support
/// 
/// Supports:
/// - PI Point discovery via PIPoint.FindPIPoints()
/// - Current/historical value reads
/// - Real-time subscriptions via AFDataPipe
/// - Connection to both PI Data Archive and AF Server
/// </summary>
public sealed class PIAfSdkConnector : ICurrentValueConnector, IHistoricalDataConnector, IDiscoverableConnector, IStreamingConnector
{
    private readonly ILogger<PIAfSdkConnector> _logger;
    private readonly ConcurrentDictionary<string, PIPoint> _pointCache = new();
    
    private PIServer? _piServer;
    private PISystem? _afSystem;
    private ConnectorConfiguration? _config;
    
    public string ConnectorType => "PIAfSdk";
    public string DisplayName => "PI AF SDK";
    public bool IsAvailable { get; private set; }
    
    public PIAfSdkConnector(ILogger<PIAfSdkConnector> logger)
    {
        _logger = logger;
    }
    
    public async Task InitializeAsync(ConnectorConfiguration config, CancellationToken ct = default)
    {
        _config = config;
        
        try
        {
            // Connect to PI Data Archive
            _piServer = await Task.Run(() =>
            {
                var servers = new PIServers();
                var server = servers[config.PiDataArchive ?? ""];
                
                if (server == null)
                {
                    throw new InvalidOperationException($"PI Data Archive '{config.PiDataArchive}' not found");
                }
                
                server.Connect();
                return server;
            }, ct);
            
            // Connect to AF Server (optional)
            if (!string.IsNullOrEmpty(config.AfServerName))
            {
                _afSystem = await Task.Run(() =>
                {
                    var systems = new PISystems();
                    var system = systems[config.AfServerName];
                    
                    if (system != null)
                    {
                        system.Connect();
                    }
                    
                    return system;
                }, ct);
            }
            
            IsAvailable = _piServer?.ConnectionInfo?.IsConnected == true;
            
            if (IsAvailable)
            {
                _logger.LogInformation(
                    "PI AF SDK connector initialized: PI Server={Server}, AF Server={AFServer}",
                    _piServer.Name, _afSystem?.Name ?? "None");
            }
            else
            {
                _logger.LogWarning("PI AF SDK connector failed to connect to PI Data Archive");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize PI AF SDK connector");
            IsAvailable = false;
            throw;
        }
    }
    
    public async Task<ConnectorHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            if (_piServer == null)
            {
                return new ConnectorHealthStatus
                {
                    IsHealthy = false,
                    Message = "PI Server not initialized",
                    ResponseTime = sw.Elapsed
                };
            }
            
            var isConnected = await Task.Run(() => _piServer.ConnectionInfo.IsConnected, ct);
            sw.Stop();
            
            if (isConnected)
            {
                return new ConnectorHealthStatus
                {
                    IsHealthy = true,
                    Message = $"Connected to PI Server '{_piServer.Name}'",
                    ResponseTime = sw.Elapsed,
                    Details = new Dictionary<string, object>
                    {
                        ["serverName"] = _piServer.Name,
                        ["serverVersion"] = _piServer.ServerVersion.ToString(),
                        ["serverTime"] = _piServer.ServerTime.LocalTime
                    }
                };
            }
            
            return new ConnectorHealthStatus
            {
                IsHealthy = false,
                Message = "PI Server not connected",
                ResponseTime = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error checking PI Server health");
            return new ConnectorHealthStatus
            {
                IsHealthy = false,
                Message = $"Error: {ex.Message}",
                ResponseTime = sw.Elapsed
            };
        }
    }
    
    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_piServer?.ConnectionInfo?.IsConnected == true);
    }
    
    #region Current Value Operations
    
    public async Task<DataValue> ReadCurrentValueAsync(string sourceAddress, CancellationToken ct = default)
    {
        var point = await GetPIPointAsync(sourceAddress, ct);
        
        var afValue = await Task.Run(() => point.CurrentValue(), ct);
        
        return MapToDataValue(afValue);
    }
    
    public async Task<IReadOnlyDictionary<string, DataValue>> ReadCurrentValuesAsync(
        IEnumerable<string> sourceAddresses, 
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, DataValue>();
        var addressList = sourceAddresses.ToList();
        
        if (addressList.Count == 0)
            return results;
        
        // Get PIPoint objects
        var points = new List<PIPoint>();
        var pointMap = new Dictionary<PIPoint, string>();
        
        foreach (var address in addressList)
        {
            try
            {
                var point = await GetPIPointAsync(address, ct);
                points.Add(point);
                pointMap[point] = address;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve PI Point: {Address}", address);
            }
        }
        
        if (points.Count == 0)
            return results;
        
        // Bulk read snapshots
        var values = await Task.Run(() =>
        {
            var dict = new AFValues();
            
            foreach (var point in points)
            {
                try
                {
                    var value = point.CurrentValue();
                    dict.Add(value);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read current value for {Point}", point.Name);
                }
            }
            
            return dict;
        }, ct);
        
        foreach (var afValue in values)
        {
            if (afValue.PIPoint != null && pointMap.TryGetValue(afValue.PIPoint, out var address))
            {
                results[address] = MapToDataValue(afValue);
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
        var point = await GetPIPointAsync(sourceAddress, ct);
        
        var timeRange = new AFTimeRange(startTime, endTime);
        
        var afValues = await Task.Run(() => 
            point.RecordedValues(timeRange, AFBoundaryType.Inside, null, false), ct);
        
        var values = new List<DataValue>();
        foreach (var afValue in afValues)
        {
            values.Add(MapToDataValue(afValue));
        }
        
        return new TimeSeriesData
        {
            SourceAddress = sourceAddress,
            StartTime = startTime,
            EndTime = endTime,
            Values = values,
            Units = point.GetAttribute(PICommonPointAttributes.EngineeringUnits)?.ToString() ?? string.Empty
        };
    }
    
    public async Task<IReadOnlyList<TimeSeriesData>> ReadHistoricalDataBatchAsync(
        IEnumerable<string> sourceAddresses, 
        DateTime startTime, 
        DateTime endTime, 
        CancellationToken ct = default)
    {
        var results = new List<TimeSeriesData>();
        var addressList = sourceAddresses.ToList();
        
        var timeRange = new AFTimeRange(startTime, endTime);
        
        foreach (var address in addressList)
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
        if (_piServer == null)
        {
            throw new InvalidOperationException("PI Server not initialized");
        }
        
        var results = new List<DiscoveredPoint>();
        
        var query = string.IsNullOrEmpty(nameFilter) ? "*" : nameFilter;
        
        var points = await Task.Run(() => 
            PIPoint.FindPIPoints(_piServer, query, null, null).Take(maxResults).ToList(), ct);
        
        foreach (var point in points)
        {
            try
            {
                // Load point attributes
                point.LoadAttributes(new[] { 
                    PICommonPointAttributes.Descriptor,
                    PICommonPointAttributes.EngineeringUnits,
                    PICommonPointAttributes.PointType,
                    "pointclass",
                    PICommonPointAttributes.Span,
                    PICommonPointAttributes.Zero,
                    PICommonPointAttributes.Step,
                    PICommonPointAttributes.Compressing,
                    PICommonPointAttributes.CompressionDeviation,
                    PICommonPointAttributes.CompressionPercentage,
                    "compressingtimeout",
                    PICommonPointAttributes.ExceptionDeviation,
                    PICommonPointAttributes.ExceptionPercentage,
                    "exceptiontimeout"
                });
                
                results.Add(new DiscoveredPoint
                {
                    SourceAddress = point.Name,
                    Name = point.Name,
                    Description = point.GetAttribute(PICommonPointAttributes.Descriptor)?.ToString() ?? string.Empty,
                    EngineeringUnits = point.GetAttribute(PICommonPointAttributes.EngineeringUnits)?.ToString() ?? string.Empty,
                    PointType = point.GetAttribute(PICommonPointAttributes.PointType)?.ToString() ?? string.Empty,
                    Attributes = new Dictionary<string, object>
                    {
                        ["pointClass"] = point.GetAttribute("pointclass")?.ToString() ?? string.Empty,
                        ["span"] = point.GetAttribute(PICommonPointAttributes.Span) ?? 0.0,
                        ["zero"] = point.GetAttribute(PICommonPointAttributes.Zero) ?? 0.0,
                        ["step"] = point.GetAttribute(PICommonPointAttributes.Step) ?? false,
                        ["compressing"] = point.GetAttribute(PICommonPointAttributes.Compressing) ?? false,
                        ["compressionDeviation"] = point.GetAttribute(PICommonPointAttributes.CompressionDeviation) ?? 0.0,
                        ["compressionPercentage"] = point.GetAttribute(PICommonPointAttributes.CompressionPercentage) ?? 0.0,
                        ["compressionTimeout"] = point.GetAttribute("compressingtimeout") ?? 0,
                        ["exceptionDeviation"] = point.GetAttribute(PICommonPointAttributes.ExceptionDeviation) ?? 0.0,
                        ["exceptionPercentage"] = point.GetAttribute(PICommonPointAttributes.ExceptionPercentage) ?? 0.0,
                        ["exceptionTimeout"] = point.GetAttribute("exceptiontimeout") ?? 0
                    }
                });
                
                // Cache the point
                _pointCache[point.Name.ToUpperInvariant()] = point;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load attributes for point {Point}", point.Name);
            }
        }
        
        _logger.LogInformation("Discovered {Count} PI Points matching '{Filter}'", results.Count, query);
        
        return results;
    }
    
    public async Task<PointMetadata?> GetPointMetadataAsync(string sourceAddress, CancellationToken ct = default)
    {
        try
        {
            var point = await GetPIPointAsync(sourceAddress, ct);
            
            await Task.Run(() => point.LoadAttributes(new[] { 
                PICommonPointAttributes.Descriptor,
                PICommonPointAttributes.EngineeringUnits,
                PICommonPointAttributes.PointType,
                "pointclass",
                PICommonPointAttributes.Span,
                PICommonPointAttributes.Zero,
                PICommonPointAttributes.Step,
                PICommonPointAttributes.Compressing,
                PICommonPointAttributes.CompressionDeviation,
                PICommonPointAttributes.CompressionPercentage,
                "compressingtimeout",
                PICommonPointAttributes.ExceptionDeviation,
                PICommonPointAttributes.ExceptionPercentage,
                "exceptiontimeout",
                PICommonPointAttributes.CreationDate,
                PICommonPointAttributes.Creator,
                PICommonPointAttributes.PointSource
            }), ct);
            
            var zero = (double)(point.GetAttribute(PICommonPointAttributes.Zero) ?? 0.0);
            var span = (double)(point.GetAttribute(PICommonPointAttributes.Span) ?? 0.0);
            
            return new PointMetadata
            {
                SourceAddress = sourceAddress,
                Name = point.Name,
                Description = point.GetAttribute(PICommonPointAttributes.Descriptor)?.ToString() ?? string.Empty,
                EngineeringUnits = point.GetAttribute(PICommonPointAttributes.EngineeringUnits)?.ToString() ?? string.Empty,
                PointType = point.GetAttribute(PICommonPointAttributes.PointType)?.ToString() ?? string.Empty,
                Span = span,
                Zero = zero,
                Minimum = zero,
                Maximum = zero + span,
                CompressionDeviation = (double)(point.GetAttribute(PICommonPointAttributes.CompressionDeviation) ?? 0.0),
                ExceptionDeviation = (double)(point.GetAttribute(PICommonPointAttributes.ExceptionDeviation) ?? 0.0),
                CompressionTimeout = TimeSpan.FromSeconds((int)(point.GetAttribute("compressingtimeout") ?? 0)),
                ExceptionTimeout = TimeSpan.FromSeconds((int)(point.GetAttribute("exceptiontimeout") ?? 0)),
                CreationDate = point.GetAttribute(PICommonPointAttributes.CreationDate) is DateTime dt ? dt.ToLocalTime() : DateTime.MinValue,
                CreatedBy = point.GetAttribute(PICommonPointAttributes.Creator)?.ToString() ?? string.Empty,
                ExtendedAttributes = new Dictionary<string, object>
                {
                    ["pointClass"] = point.GetAttribute("pointclass")?.ToString() ?? string.Empty,
                    ["step"] = point.GetAttribute(PICommonPointAttributes.Step) ?? false,
                    ["compressing"] = point.GetAttribute(PICommonPointAttributes.Compressing) ?? false,
                    ["compressionPercentage"] = point.GetAttribute(PICommonPointAttributes.CompressionPercentage) ?? 0.0,
                    ["exceptionPercentage"] = point.GetAttribute(PICommonPointAttributes.ExceptionPercentage) ?? 0.0,
                    ["pointId"] = point.ID,
                    ["pointSource"] = point.GetAttribute(PICommonPointAttributes.PointSource)?.ToString() ?? string.Empty
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get metadata for {SourceAddress}", sourceAddress);
            return null;
        }
    }
    
    #endregion
    
    #region Streaming Operations (AFDataPipe)
    
    public Task<IDisposable> SubscribeAsync(
        IEnumerable<string> sourceAddresses, 
        Action<string, DataValue> onValueReceived, 
        CancellationToken ct = default)
    {
        // This will be implemented by PIDataPipeManager
        throw new NotImplementedException("Use PIDataPipeManager for subscriptions");
    }
    
    public Task<IReadOnlyDictionary<string, DataValue>> PollUpdatesAsync(
        TimeSpan timeout, 
        CancellationToken ct = default)
    {
        // Not needed - AFDataPipe is push-based
        throw new NotImplementedException("AFDataPipe is push-based, not polling");
    }
    
    #endregion
    
    #region Helper Methods
    
    private async Task<PIPoint> GetPIPointAsync(string sourceAddress, CancellationToken ct)
    {
        var normalizedAddress = sourceAddress.ToUpperInvariant().Trim();
        
        if (_pointCache.TryGetValue(normalizedAddress, out var cachedPoint))
        {
            return cachedPoint;
        }
        
        if (_piServer == null)
        {
            throw new InvalidOperationException("PI Server not initialized");
        }
        
        var point = await Task.Run(() => PIPoint.FindPIPoint(_piServer, sourceAddress), ct);
        
        if (point == null)
        {
            throw new InvalidOperationException($"PI Point not found: {sourceAddress}");
        }
        
        _pointCache[normalizedAddress] = point;
        return point;
    }
    
    public PIPoint? GetCachedPoint(string sourceAddress)
    {
        _pointCache.TryGetValue(sourceAddress.ToUpperInvariant(), out var point);
        return point;
    }
    
    public PIServer? GetPIServer() => _piServer;
    
    private static DataValue MapToDataValue(AFValue afValue)
    {
        return new DataValue
        {
            Value = afValue.Value,
            Timestamp = afValue.Timestamp.UtcTime,
            Quality = MapQuality(afValue.IsGood),
            Units = afValue.UOM?.Abbreviation
        };
    }
    
    private static DataQuality MapQuality(bool isGood)
    {
        return isGood ? DataQuality.Good : DataQuality.Bad;
    }
    
    #endregion
    
    public Task DisposeAsync()
    {
        try
        {
            _piServer?.Disconnect();
            _afSystem?.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disconnecting from PI servers");
        }
        
        return Task.CompletedTask;
    }
}
