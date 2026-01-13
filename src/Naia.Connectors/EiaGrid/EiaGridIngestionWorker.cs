using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Connectors.Abstractions;
using Naia.Domain.ValueObjects;

namespace Naia.Connectors.EiaGrid;

/// <summary>
/// Background service that polls EIA Grid Data API and publishes data to Kafka.
/// Runs continuously when enabled in configuration.
/// </summary>
public sealed class EiaGridIngestionWorker : BackgroundService
{
    private readonly EiaGridApiConnector _connector;
    private readonly IProducer<string, string> _producer;
    private readonly EiaGridApiOptions _options;
    private readonly ILogger<EiaGridIngestionWorker> _logger;
    
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    
    private readonly List<DiscoveredPoint> _monitoredPoints = new();
    private long _messagesPublished;
    private long _errorCount;
    
    public EiaGridIngestionWorker(
        EiaGridApiConnector connector,
        IProducer<string, string> producer,
        IOptions<EiaGridApiOptions> options,
        ILogger<EiaGridIngestionWorker> logger)
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
            _logger.LogInformation("EIA Grid API connector is disabled in configuration");
            return;
        }
        
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("EIA Grid API key is not configured. Set EiaGrid:ApiKey in appsettings.json");
            return;
        }
        
        _logger.LogInformation("EIA Grid Ingestion Worker starting...");
        
        try
        {
            // Initialize connector
            await InitializeConnectorAsync(stoppingToken);
            
            if (!_connector.IsAvailable)
            {
                _logger.LogWarning("EIA Grid API connector not available - worker will not start");
                return;
            }
            
            // Get configured points
            await GetConfiguredPointsAsync(stoppingToken);
            
            // Main polling loop
            var pollInterval = TimeSpan.FromMilliseconds(_options.PollingIntervalMs);
            _logger.LogInformation("Starting EIA grid polling loop (interval: {Interval})", pollInterval);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollAndPublishAsync(stoppingToken);
                    
                    if (_messagesPublished % 100 == 0 && _messagesPublished > 0)
                    {
                        _logger.LogInformation("EIA grid ingestion stats - Published: {Published}, Errors: {Errors}",
                            _messagesPublished, _errorCount);
                    }
                }
                catch (Exception ex)
                {
                    _errorCount++;
                    _logger.LogError(ex, "Error during EIA grid polling cycle");
                }
                
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("EIA Grid Ingestion Worker stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in EIA Grid Ingestion Worker");
            throw;
        }
        
        _logger.LogInformation("EIA Grid Ingestion Worker stopped. Total published: {Published}, Errors: {Errors}",
            _messagesPublished, _errorCount);
    }
    
    private async Task InitializeConnectorAsync(CancellationToken ct)
    {
        var config = new ConnectorConfiguration
        {
            ConnectionString = _options.BaseUrl,
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
            MaxConcurrentRequests = 5,
            Credentials = new Dictionary<string, string>
            {
                ["ApiKey"] = _options.ApiKey,
                ["Series"] = JsonSerializer.Serialize(_options.Series)
            }
        };
        
        await _connector.InitializeAsync(config, ct);
        
        if (!_connector.IsAvailable)
        {
            throw new InvalidOperationException("EIA Grid API connector is not available after initialization");
        }
        
        _logger.LogInformation("EIA Grid API connector initialized successfully");
    }
    
    private async Task GetConfiguredPointsAsync(CancellationToken ct)
    {
        await Task.Yield(); // Make async
        
        _logger.LogInformation("Loading configured EIA grid points...");
        
        var configuredPoints = _connector.GetConfiguredPoints();
        _monitoredPoints.AddRange(configuredPoints);
        
        _logger.LogInformation("Found {Count} configured EIA grid series", configuredPoints.Count);
        _logger.LogInformation("Note: Points must be registered via API/UI before data will persist");
    }
    
    private async Task PollAndPublishAsync(CancellationToken ct)
    {
        if (_monitoredPoints.Count == 0)
        {
            _logger.LogWarning("No monitored points configured for EIA grid ingestion");
            return;
        }
        
        var addresses = _monitoredPoints.Select(p => p.SourceAddress).ToList();
        
        try
        {
            var values = await _connector.ReadCurrentValuesAsync(addresses, ct);
            
            if (values.Count == 0)
            {
                _logger.LogWarning("No EIA grid data received from API");
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
                    DataSourceId = "EiaGrid",
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
                DataSourceId = "EiaGrid"
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
            
            _logger.LogDebug("Published EIA grid batch: {PointCount} points to Kafka partition {Partition} at offset {Offset}",
                dataPoints.Count, result.Partition.Value, result.Offset.Value);
        }
        catch (Exception ex)
        {
            _errorCount++;
            _logger.LogError(ex, "Failed to poll and publish EIA grid data");
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
