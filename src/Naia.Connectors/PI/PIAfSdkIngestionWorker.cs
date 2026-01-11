using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Naia.Connectors.PI;

/// <summary>
/// Background worker that streams PI data using AF SDK AFDataPipe to Kafka.
/// 
/// Event-Driven Flow:
///   1. On startup: Discover PI Points → Insert to PostgreSQL
///   2. Subscribe via AFDataPipe for real-time push
///   3. AFDataPipe events → Channel (buffer)
///   4. Consume from channel → Publish to Kafka
///   5. No polling - fully event-driven
/// 
/// Advantages over polling:
///   - Lower latency (push vs poll)
///   - More efficient (events only when values change)
///   - Scales to 1M+ points
///   - Native SDT (compression/exception deviation)
/// </summary>
public sealed class PIAfSdkIngestionWorker : BackgroundService
{
    private readonly PIAfSdkConnector _connector;
    private readonly PIDataPipeManager _dataPipeManager;
    private readonly IProducer<string, string> _producer;
    private readonly PIWebApiOptions _options; // Reuse same options
    private readonly ILogger<PIAfSdkIngestionWorker> _logger;
    
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    private long _messagesPublished;
    private long _errorCount;
    private long _droppedCount;
    
    public PIAfSdkIngestionWorker(
        PIAfSdkConnector connector,
        PIDataPipeManager dataPipeManager,
        IProducer<string, string> producer,
        IOptions<PIWebApiOptions> options,
        ILogger<PIAfSdkIngestionWorker> logger)
    {
        _connector = connector;
        _dataPipeManager = dataPipeManager;
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PI AF SDK Ingestion Worker starting...");
        
        // Initialize the connector
        try
        {
            await InitializeConnectorAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize PI AF SDK connector");
            return;
        }
        
        // Discover points
        List<string> pointAddresses;
        try
        {
            pointAddresses = await DiscoverPointsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover PI Points");
            return;
        }
        
        if (pointAddresses.Count == 0)
        {
            _logger.LogWarning("No PI Points discovered. Worker will exit.");
            return;
        }
        
        // Subscribe via AFDataPipe
        try
        {
            await _dataPipeManager.SubscribeAsync(pointAddresses, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to PI Points via AFDataPipe");
            return;
        }
        
        _logger.LogInformation(
            "PI AF SDK Ingestion Worker started. Subscribed to {PointCount} points",
            pointAddresses.Count);
        
        // Process updates from channel
        var reader = _dataPipeManager.GetUpdateReader();
        
        await foreach (var update in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await PublishUpdateAsync(update, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errorCount);
                _logger.LogError(ex, "Error publishing update for {Point}", update.SourceAddress);
            }
            
            // Periodic stats logging
            if (_messagesPublished % 10000 == 0)
            {
                var stats = _dataPipeManager.GetStats();
                _logger.LogInformation(
                    "PI Ingestion: {Published} published, {Errors} errors, {Dropped} dropped, {Buffered} buffered",
                    _messagesPublished, _errorCount, _droppedCount, stats.ChannelCount);
            }
        }
        
        _logger.LogInformation(
            "PI AF SDK Ingestion Worker stopped. Published {Messages} messages, {Errors} errors",
            _messagesPublished, _errorCount);
    }
    
    private async Task InitializeConnectorAsync(CancellationToken ct)
    {
        var config = new Abstractions.ConnectorConfiguration
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
            throw new InvalidOperationException("PI AF SDK connector is not available after initialization");
        }
    }
    
    private async Task<List<string>> DiscoverPointsAsync(CancellationToken ct)
    {
        var filters = string.IsNullOrEmpty(_options.PointFilters)
            ? new[] { "*" }
            : _options.PointFilters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        var maxPoints = _options.MaxDiscoveredPoints < 0 ? int.MaxValue : _options.MaxDiscoveredPoints;
        var addresses = new List<string>();
        
        foreach (var filter in filters)
        {
            if (addresses.Count >= maxPoints)
                break;
            
            _logger.LogInformation("Discovering PI Points matching '{Filter}'...", filter);
            
            var discovered = await _connector.DiscoverPointsAsync(
                filter, 
                maxPoints - addresses.Count, 
                ct);
            
            foreach (var point in discovered)
            {
                if (!addresses.Contains(point.SourceAddress))
                {
                    addresses.Add(point.SourceAddress);
                }
            }
            
            _logger.LogInformation("Found {Count} points for filter '{Filter}'", discovered.Count, filter);
        }
        
        _logger.LogInformation("Total points to monitor: {Count}", addresses.Count);
        
        return addresses;
    }
    
    private async Task PublishUpdateAsync(DataPointUpdate update, CancellationToken ct)
    {
        var message = new PIDataPointMessage
        {
            SourceAddress = update.SourceAddress,
            PointName = update.PointName,
            Value = update.Value,
            Timestamp = update.Timestamp,
            Quality = update.Quality.ToString(),
            Units = update.Units,
            DataSourceType = "PIAfSdk",
            DataArchive = _options.DataArchive,
            IngestTimestamp = update.ReceivedAt
        };
        
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        
        await _producer.ProduceAsync(
            "naia.datapoints",
            new Message<string, string>
            {
                Key = update.SourceAddress,
                Value = json
            },
            ct);
        
        Interlocked.Increment(ref _messagesPublished);
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PI AF SDK Ingestion Worker stopping...");
        await base.StopAsync(cancellationToken);
        _producer.Flush(cancellationToken);
        _dataPipeManager.Dispose();
    }
}
