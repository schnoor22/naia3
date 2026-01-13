using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Connectors.Abstractions;
using Naia.Domain.ValueObjects;

namespace Naia.Connectors.Weather;

/// <summary>
/// Background service that polls Weather API and publishes data to Kafka.
/// Runs continuously when enabled in configuration.
/// </summary>
public sealed class WeatherIngestionWorker : BackgroundService
{
    private readonly WeatherApiConnector _connector;
    private readonly IProducer<string, string> _producer;
    private readonly WeatherApiOptions _options;
    private readonly ILogger<WeatherIngestionWorker> _logger;
    
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    
    private readonly List<DiscoveredPoint> _monitoredPoints = new();
    private long _messagesPublished;
    private long _errorCount;
    
    public WeatherIngestionWorker(
        WeatherApiConnector connector,
        IProducer<string, string> producer,
        IOptions<WeatherApiOptions> options,
        ILogger<WeatherIngestionWorker> logger)
    {
        _connector = connector;
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Weather API connector is disabled in configuration");
            return;
        }
        
        _logger.LogInformation("Weather Ingestion Worker starting...");
        
        try
        {
            // Initialize connector
            await InitializeConnectorAsync(stoppingToken);
            
            if (!_connector.IsAvailable)
            {
                _logger.LogWarning("Weather API connector not available - worker will not start");
                return;
            }
            
            // Discover points (registration happens via API/UI)
            if (_options.EnableAutoDiscovery)
            {
                await DiscoverPointsAsync(stoppingToken);
            }
            
            // Main polling loop
            var pollInterval = TimeSpan.FromMilliseconds(_options.PollingIntervalMs);
            _logger.LogInformation("Starting weather polling loop (interval: {Interval})", pollInterval);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollAndPublishAsync(stoppingToken);
                    
                    if (_messagesPublished % 100 == 0 && _messagesPublished > 0)
                    {
                        _logger.LogInformation("Weather ingestion stats - Published: {Published}, Errors: {Errors}",
                            _messagesPublished, _errorCount);
                    }
                }
                catch (Exception ex)
                {
                    _errorCount++;
                    _logger.LogError(ex, "Error during weather polling cycle");
                }
                
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Weather Ingestion Worker stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Weather Ingestion Worker");
            throw;
        }
        
        _logger.LogInformation("Weather Ingestion Worker stopped. Total published: {Published}, Errors: {Errors}",
            _messagesPublished, _errorCount);
    }
    
    private async Task InitializeConnectorAsync(CancellationToken ct)
    {
        var config = new ConnectorConfiguration
        {
            ConnectionString = _options.BaseUrl,
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
            MaxConcurrentRequests = 10,
            Credentials = new Dictionary<string, string>
            {
                ["Locations"] = JsonSerializer.Serialize(_options.Locations),
                ["Variables"] = JsonSerializer.Serialize(_options.Variables)
            }
        };
        
        await _connector.InitializeAsync(config, ct);
        
        if (!_connector.IsAvailable)
        {
            throw new InvalidOperationException("Weather API connector is not available after initialization");
        }
        
        _logger.LogInformation("Weather API connector initialized successfully");
    }
    
    private async Task DiscoverPointsAsync(CancellationToken ct)
    {
        _logger.LogInformation("Discovering weather points...");
        
        var discovered = await _connector.DiscoverPointsAsync(null, _options.MaxDiscoveredPoints, ct);
        _monitoredPoints.AddRange(discovered);
        
        _logger.LogInformation("Discovered {Count} weather points for monitoring", discovered.Count);
        _logger.LogInformation("Note: Points must be registered via API/UI before data will persist");
    }
    
    private async Task PollAndPublishAsync(CancellationToken ct)
    {
        if (_monitoredPoints.Count == 0)
        {
            _logger.LogWarning("No monitored points configured for weather ingestion");
            return;
        }
        
        var addresses = _monitoredPoints.Select(p => p.SourceAddress).ToList();
        
        try
        {
            var values = await _connector.ReadCurrentValuesAsync(addresses, ct);
            
            if (values.Count == 0)
            {
                _logger.LogWarning("No weather data received from API");
                return;
            }
            
            // Create data point batch
            var dataPoints = new List<DataPoint>();
            foreach (var kvp in values)
            {
                var point = _monitoredPoints.FirstOrDefault(p => p.SourceAddress == kvp.Key);
                if (point == null) continue;
                
                var value = kvp.Value.Value;
                var doubleValue = value switch
                {
                    double d => d,
                    float f => (double)f,
                    int i => (double)i,
                    long l => (double)l,
                    _ => Convert.ToDouble(value)
                };
                
                dataPoints.Add(new DataPoint
                {
                    PointSequenceId = 0,
                    PointName = point.Name,
                    Timestamp = kvp.Value.Timestamp,
                    Value = doubleValue,
                    Quality = MapQuality(kvp.Value.Quality),
                    DataSourceId = "WeatherApi",
                    SourceTag = kvp.Key,
                    ReceivedAt = DateTime.UtcNow
                });
            }
            
            if (dataPoints.Count == 0)
                return;
            
            var batch = new DataPointBatch
            {
                Points = dataPoints,
                BatchId = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
                DataSourceId = "WeatherApi"
            };
            
            // Publish to Kafka
            var json = JsonSerializer.Serialize(batch, _jsonOptions);
            var message = new Message<string, string>
            {
                Key = batch.BatchId,
                Value = json,
                Timestamp = new Timestamp(DateTime.UtcNow)
            };
            
            var result = await _producer.ProduceAsync("naia.datapoints", message, ct);
            _messagesPublished++;
            
            _logger.LogDebug("Published weather batch: {PointCount} points to Kafka partition {Partition} at offset {Offset}",
                dataPoints.Count, result.Partition.Value, result.Offset.Value);
        }
        catch (Exception ex)
        {
            _errorCount++;
            _logger.LogError(ex, "Failed to poll and publish weather data");
        }
    }
    
    private static Domain.ValueObjects.DataQuality MapQuality(Abstractions.DataQuality quality)
    {
        return quality switch
        {
            Abstractions.DataQuality.Good => Domain.ValueObjects.DataQuality.Good,
            Abstractions.DataQuality.Bad => Domain.ValueObjects.DataQuality.Bad,
            Abstractions.DataQuality.Uncertain => Domain.ValueObjects.DataQuality.Uncertain,
            Abstractions.DataQuality.Substituted => Domain.ValueObjects.DataQuality.Substituted,
            _ => Domain.ValueObjects.DataQuality.Good
        };
    }
}
