using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Connectors.Abstractions;

namespace Naia.Connectors.PI;

/// <summary>
/// Background worker that polls PI Web API for data and publishes to Kafka.
/// 
/// FLOW:
///   1. On startup: Discover PI Points matching filters
///   2. Register discovered points with NAIA (via database or API)
///   3. Poll PI Web API for current values at configured interval
///   4. Publish values to Kafka topic 'naia.datapoints'
///   5. Track metrics (points/sec, errors, latency)
/// 
/// The worker supports:
///   - Configurable polling intervals
///   - Batch processing for efficiency
///   - Automatic reconnection on failures
///   - Point filtering (wildcard patterns)
///   - Graceful shutdown
/// </summary>
public sealed class PIIngestionWorker : BackgroundService
{
    private readonly PIWebApiConnector _connector;
    private readonly IProducer<string, string> _producer;
    private readonly PIWebApiOptions _options;
    private readonly ILogger<PIIngestionWorker> _logger;
    
    private readonly List<DiscoveredPoint> _monitoredPoints = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    private long _messagesPublished;
    private long _errorCount;
    private DateTime _lastPollTime;
    
    public PIIngestionWorker(
        PIWebApiConnector connector,
        IProducer<string, string> producer,
        IOptions<PIWebApiOptions> options,
        ILogger<PIIngestionWorker> logger)
    {
        _connector = connector;
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PI Ingestion Worker starting...");
        
        // Initialize the connector
        try
        {
            await InitializeConnectorAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize PI Web API connector");
            return;
        }
        
        // Discover points
        try
        {
            await DiscoverPointsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover PI Points");
            return;
        }
        
        if (_monitoredPoints.Count == 0)
        {
            _logger.LogWarning("No PI Points discovered. Worker will exit.");
            return;
        }
        
        _logger.LogInformation(
            "PI Ingestion Worker started. Monitoring {PointCount} points at {Interval}ms interval",
            _monitoredPoints.Count, _options.PollingIntervalMs);
        
        // Main polling loop
        var pollInterval = TimeSpan.FromMilliseconds(_options.PollingIntervalMs);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                await PollAndPublishAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errorCount);
                _logger.LogError(ex, "Error during PI polling cycle");
            }
            
            sw.Stop();
            
            // Wait for next poll interval
            var elapsed = sw.Elapsed;
            var delay = pollInterval - elapsed;
            
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }
            else
            {
                _logger.LogWarning(
                    "Polling took {Elapsed}ms which exceeds interval of {Interval}ms",
                    elapsed.TotalMilliseconds, _options.PollingIntervalMs);
            }
        }
        
        _logger.LogInformation(
            "PI Ingestion Worker stopped. Published {Messages} messages, {Errors} errors",
            _messagesPublished, _errorCount);
    }
    
    private async Task InitializeConnectorAsync(CancellationToken ct)
    {
        var config = new ConnectorConfiguration
        {
            ConnectionString = _options.BaseUrl,
            PiDataArchive = _options.DataArchive,
            AfServerName = _options.AfServer,
            UseWindowsAuth = _options.UseWindowsAuth,
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
            MaxConcurrentRequests = _options.MaxConcurrentRequests,
            BatchSize = _options.BatchSize,
            Credentials = new Dictionary<string, string>()
        };
        
        if (!_options.UseWindowsAuth && !string.IsNullOrEmpty(_options.Username))
        {
            config.Credentials["Username"] = _options.Username;
            config.Credentials["Password"] = _options.Password ?? "";
        }
        
        await _connector.InitializeAsync(config, ct);
        
        if (!_connector.IsAvailable)
        {
            throw new InvalidOperationException("PI Web API connector is not available after initialization");
        }
    }
    
    private async Task DiscoverPointsAsync(CancellationToken ct)
    {
        var filters = string.IsNullOrEmpty(_options.PointFilters)
            ? new[] { "*" }
            : _options.PointFilters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        var maxPoints = _options.MaxDiscoveredPoints < 0 ? int.MaxValue : _options.MaxDiscoveredPoints;
        
        foreach (var filter in filters)
        {
            if (_monitoredPoints.Count >= maxPoints)
                break;
            
            _logger.LogInformation("Discovering PI Points matching '{Filter}'...", filter);
            
            var discovered = await _connector.DiscoverPointsAsync(
                filter, 
                maxPoints - _monitoredPoints.Count, 
                ct);
            
            foreach (var point in discovered)
            {
                if (!_monitoredPoints.Any(p => p.SourceAddress == point.SourceAddress))
                {
                    _monitoredPoints.Add(point);
                }
            }
            
            _logger.LogInformation("Found {Count} points for filter '{Filter}'", discovered.Count, filter);
        }
        
        _logger.LogInformation("Total points to monitor: {Count}", _monitoredPoints.Count);
    }
    
    private async Task PollAndPublishAsync(CancellationToken ct)
    {
        _lastPollTime = DateTime.UtcNow;
        
        // Get addresses in batches
        var addresses = _monitoredPoints.Select(p => p.SourceAddress).ToList();
        
        // Read current values
        var values = await _connector.ReadCurrentValuesAsync(addresses, ct);
        
        // Publish to Kafka
        var tasks = new List<Task>();
        
        foreach (var (address, value) in values)
        {
            var point = _monitoredPoints.FirstOrDefault(p => p.SourceAddress == address);
            if (point == null) continue;
            
            var message = new PIDataPointMessage
            {
                SourceAddress = address,
                PointName = point.Name,
                Value = value.Value,
                Timestamp = value.Timestamp,
                Quality = value.Quality.ToString(),
                Units = value.Units ?? point.EngineeringUnits,
                DataSourceType = "PIWebApi",
                DataArchive = _options.DataArchive,
                IngestTimestamp = DateTime.UtcNow
            };
            
            var json = JsonSerializer.Serialize(message, _jsonOptions);
            
            tasks.Add(_producer.ProduceAsync(
                "naia.datapoints",
                new Message<string, string>
                {
                    Key = address,
                    Value = json
                },
                ct));
        }
        
        await Task.WhenAll(tasks);
        Interlocked.Add(ref _messagesPublished, tasks.Count);
        
        if (_messagesPublished % 10000 == 0)
        {
            _logger.LogInformation(
                "PI Ingestion: {Published} messages published, {Rate} points/poll",
                _messagesPublished, values.Count);
        }
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PI Ingestion Worker stopping...");
        await base.StopAsync(cancellationToken);
        _producer.Flush(cancellationToken);
    }
}

/// <summary>
/// Message format for PI data points published to Kafka.
/// </summary>
public sealed class PIDataPointMessage
{
    public required string SourceAddress { get; init; }
    public required string PointName { get; init; }
    public object? Value { get; init; }
    public DateTime Timestamp { get; init; }
    public required string Quality { get; init; }
    public string? Units { get; init; }
    public required string DataSourceType { get; init; }
    public required string DataArchive { get; init; }
    public DateTime IngestTimestamp { get; init; }
}
