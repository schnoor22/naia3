using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Naia.Application.Abstractions;
using Naia.Connectors.Abstractions;
using Naia.Connectors.PI;
using Naia.Domain.Entities;
using Naia.Domain.ValueObjects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Naia.Api.Services;

/// <summary>
/// Industrial-grade PI data ingestion service that publishes to Kafka.
/// 
/// DATA FLOW:
///   PI System (AF SDK) → PIDataIngestionService → Kafka (naia.datapoints)
///                                                      ↓
///                                               Naia.Ingestion Worker
///                                                      ↓
///                                               QuestDB + Redis
/// 
/// This is the PRODUCER side of the historian pipeline.
/// The CONSUMER side (Naia.Ingestion) processes the Kafka messages.
/// 
/// WHY KAFKA?
/// 1. Decoupling: PI connector doesn't need to know about storage
/// 2. Buffering: Handles bursts when QuestDB is slow
/// 3. Replay: Can reprocess historical data from topic
/// 4. Fan-out: Pattern Engine also consumes from same topic
/// 5. Exactly-once: Idempotency via batch IDs
/// </summary>
public sealed class PIDataIngestionService : IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataPointProducer _kafkaProducer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PIDataIngestionService> _logger;
    
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _ingestionTask;
    private bool _isRunning;
    
    // Metrics
    private readonly ConcurrentDictionary<string, IngestionMetrics> _metrics = new();
    private DateTime _startTime;
    private long _totalPointsPublished;
    private long _totalBatchesPublished;
    private long _totalErrors;
    private DateTime _lastPublishTime;

    public PIDataIngestionService(
        IServiceScopeFactory scopeFactory,
        IDataPointProducer kafkaProducer,
        IConfiguration configuration,
        ILogger<PIDataIngestionService> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaProducer = kafkaProducer;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Start continuous data ingestion from PI System to Kafka
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("PI ingestion already running");
            return;
        }

        // Initialize PI connector if not already initialized
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            
            var connector = sp.GetService<PIWebApiConnector>() as ICurrentValueConnector;
            
            if (connector != null && !connector.IsAvailable)
            {
                // Build connector configuration from appsettings
                var config = new ConnectorConfiguration
                {
                    ConnectionString = _configuration.GetValue<string>("PIWebApi:BaseUrl") ?? "sdhqpisrvr01",
                    PiDataArchive = _configuration.GetValue<string>("PIWebApi:DataArchive") ?? "sdhqpisrvr01",
                    AfServerName = _configuration.GetValue<string>("PIWebApi:AfServer") ?? "",
                    Timeout = TimeSpan.FromSeconds(_configuration.GetValue<int>("PIWebApi:TimeoutSeconds", 30)),
                    UseWindowsAuth = _configuration.GetValue<bool>("PIWebApi:UseWindowsAuth", true),
                    MaxConcurrentRequests = _configuration.GetValue<int>("PIWebApi:MaxConcurrentRequests", 10)
                };
                
                _logger.LogInformation("Initializing PI connector: DataArchive={Archive}, AFServer={AFServer}",
                    config.PiDataArchive, config.AfServerName ?? "None");
                
                await connector.InitializeAsync(config, cancellationToken);
                
                if (connector.IsAvailable)
                {
                    _logger.LogInformation("PI connector initialized successfully");
                }
                else
                {
                    _logger.LogWarning("PI connector initialization completed but IsAvailable is false");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize PI connector - will retry in polling loop");
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;
        _startTime = DateTime.UtcNow;
        _ingestionTask = IngestionLoopAsync(_cancellationTokenSource.Token);
        
        _logger.LogInformation("═══════════════════════════════════════════════════════════════════");
        _logger.LogInformation("  PI → Kafka Ingestion Started");
        _logger.LogInformation("  Publishing to: naia.datapoints");
        _logger.LogInformation("═══════════════════════════════════════════════════════════════════");
        
        await Task.Delay(100, cancellationToken); // Let it initialize
    }

    /// <summary>
    /// Stop data ingestion gracefully
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("Stopping PI ingestion - flushing pending messages...");
        
        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        
        try
        {
            if (_ingestionTask != null)
            {
                await _ingestionTask.WaitAsync(TimeSpan.FromSeconds(30));
            }
            
            // Ensure all messages are sent to Kafka
            await _kafkaProducer.FlushAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Stop timed out - some messages may not have been sent");
        }
        
        _logger.LogInformation(
            "PI ingestion stopped. Published {Batches} batches, {Points} points total",
            _totalBatchesPublished, _totalPointsPublished);
    }

    /// <summary>
    /// Get current ingestion status and metrics
    /// </summary>
    public object GetStatus()
    {
        var uptime = _isRunning ? DateTime.UtcNow - _startTime : TimeSpan.Zero;
        var rate = uptime.TotalSeconds > 0 ? _totalPointsPublished / uptime.TotalSeconds : 0;
        
        // Match the UI's IngestionStatus interface
        return new
        {
            isRunning = _isRunning,
            pointsConfigured = _metrics.Count,
            pollInterval = 5000, // Default poll interval in ms
            lastPollTime = _lastPublishTime == default ? null : (DateTime?)_lastPublishTime,
            messagesPublished = _totalPointsPublished,
            errors = _totalErrors,
            
            // Additional metrics for detailed views
            uptime = uptime.ToString(@"hh\:mm\:ss"),
            totalBatchesPublished = _totalBatchesPublished,
            pointsPerSecond = Math.Round(rate, 2),
            metrics = _metrics.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)new
                {
                    pointsPublished = kvp.Value.PointsPublished,
                    lastValue = kvp.Value.LastValue,
                    lastTimestamp = kvp.Value.LastTimestamp
                })
        };
    }

    /// <summary>
    /// Main ingestion loop - polls PI and publishes to Kafka
    /// </summary>
    private async Task IngestionLoopAsync(CancellationToken cancellationToken)
    {
        // Check if PI connector is available before starting loop
        using (var scope = _scopeFactory.CreateScope())
        {
            var connector = scope.ServiceProvider.GetService<PIWebApiConnector>() as ICurrentValueConnector;
            if (connector == null || !connector.IsAvailable)
            {
                _logger.LogInformation("PI connector not configured - ingestion loop disabled. Use replay worker for data ingestion.");
                return; // Exit immediately, don't poll
            }
        }
        
        var pollIntervalMs = _configuration.GetValue("PIWebApi:PollIntervalMs", 5000);
        
        _logger.LogInformation("Ingestion loop starting - poll interval: {Interval}ms", pollIntervalMs);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollAndPublishAsync(cancellationToken);
                await Task.Delay(pollIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalErrors);
                _logger.LogError(ex, "Error in ingestion loop - will retry in 10s");
                
                try
                {
                    await Task.Delay(10000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        
        _logger.LogInformation("Ingestion loop ended");
    }

    /// <summary>
    /// Poll PI for current values and publish to Kafka
    /// </summary>
    private async Task PollAndPublishAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        
        // Get PI connector
        var connector = sp.GetService<PIWebApiConnector>() as ICurrentValueConnector;
            
        if (connector == null || !connector.IsAvailable)
        {
            _logger.LogDebug("PI connector not available - skipping poll");
            return;
        }
        
        // Get registered points from PostgreSQL - filter to PI data sources only
        var pointRepo = sp.GetRequiredService<IPointRepository>();
        var allEnabledPoints = (await pointRepo.GetEnabledAsync(cancellationToken)).ToList();
        
        // Only poll points from PI data sources (PiWebApi or PiAfSdk)
        var points = allEnabledPoints
            .Where(p => p.DataSource != null && 
                       (p.DataSource.SourceType == DataSourceType.PiWebApi || 
                        p.DataSource.SourceType == DataSourceType.PiAfSdk))
            .ToList();
        
        if (points.Count == 0)
        {
            // Log with info level periodically so user can see ingestion is waiting for points
            if (DateTime.UtcNow.Second % 30 == 0)
                _logger.LogInformation("⏳ Waiting for PI points (found {Total} total enabled points)", allEnabledPoints.Count);
            return;
        }
        
        // Filter to points with valid source addresses and sequence IDs
        var validPoints = points
            .Where(p => !string.IsNullOrEmpty(p.SourceAddress) && p.PointSequenceId.HasValue)
            .ToList();
            
        if (validPoints.Count == 0)
        {
            _logger.LogDebug("No valid points with source addresses - skipping poll");
            return;
        }
        
        // Read current values from PI
        var sourceAddresses = validPoints.Select(p => p.SourceAddress!).ToArray();
        var values = await connector.ReadCurrentValuesAsync(sourceAddresses, cancellationToken);
        
        if (values.Count == 0)
        {
            _logger.LogDebug("No values returned from PI");
            return;
        }
        
        // Convert to DataPoints and create batch
        var dataPoints = new List<DataPoint>();
        var dataSourceId = validPoints.First().DataSourceId.ToString();
        
        foreach (var point in validPoints)
        {
            if (!values.TryGetValue(point.SourceAddress!, out var value))
                continue;
            
            // Extract numeric value from JsonElement if needed
            double numericValue;
            if (value.Value is System.Text.Json.JsonElement jsonElement)
            {
                // Check if it's a number
                if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    numericValue = jsonElement.GetDouble();
                }
                else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    // Digital/discrete point with string value - skip it
                    _logger.LogDebug("Skipping digital point {Point} with string value: {Value}", 
                        point.Name, jsonElement.GetString());
                    continue;
                }
                else
                {
                    _logger.LogWarning("Skipping point {Point} with unsupported value type: {Type}", 
                        point.Name, jsonElement.ValueKind);
                    continue;
                }
            }
            else if (value.Value is double d)
            {
                numericValue = d;
            }
            else
            {
                // Try to convert, but skip if it fails
                if (!double.TryParse(value.Value?.ToString(), out numericValue))
                {
                    _logger.LogDebug("Skipping point {Point} with non-numeric value: {Value}", 
                        point.Name, value.Value);
                    continue;
                }
            }
            
            // Validate that the value is finite (not NaN or Infinity)
            if (!double.IsFinite(numericValue))
            {
                _logger.LogWarning("Skipping point {Point} with invalid value: {Value}", 
                    point.Name, numericValue);
                continue;
            }
            
            // Validate timestamp - QuestDB requires timestamps >= 1970-01-01
            var timestamp = value.Timestamp;
            if (timestamp < new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            {
                _logger.LogWarning("Invalid timestamp {Timestamp} for point {Point}, using current time", 
                    timestamp, point.Name);
                timestamp = DateTime.UtcNow;
            }
                
            var dataPoint = DataPoint.FromPi(
                point.PointSequenceId!.Value,
                point.Name,
                timestamp,
                numericValue,
                value.Quality == Naia.Connectors.Abstractions.DataQuality.Good,
                dataSourceId,
                point.SourceAddress);
                
            dataPoints.Add(dataPoint);
            
            // Update metrics
            _metrics.AddOrUpdate(
                point.Name,
                new IngestionMetrics
                {
                    PointsPublished = 1,
                    LastValue = numericValue,
                    LastTimestamp = value.Timestamp
                },
                (_, existing) =>
                {
                    existing.PointsPublished++;
                    existing.LastValue = numericValue;
                    existing.LastTimestamp = value.Timestamp;
                    return existing;
                });
        }
        
        if (dataPoints.Count == 0)
        {
            _logger.LogDebug("No data points to publish");
            return;
        }
        
        // Create batch and publish to Kafka
        var batch = DataPointBatch.Create(dataPoints, dataSourceId);
        var result = await _kafkaProducer.PublishAsync(batch, cancellationToken);
        
        if (result.Success)
        {
            Interlocked.Add(ref _totalPointsPublished, dataPoints.Count);
            Interlocked.Increment(ref _totalBatchesPublished);
            _lastPublishTime = DateTime.UtcNow;
            
            _logger.LogDebug(
                "Published batch {BatchId}: {Count} points to {Topic}[{Partition}]@{Offset}",
                batch.BatchId, dataPoints.Count, result.Topic, result.Partition, result.Offset);
        }
        else
        {
            Interlocked.Increment(ref _totalErrors);
            _logger.LogWarning(
                "Failed to publish batch {BatchId}: {Error}",
                batch.BatchId, result.ErrorMessage);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cancellationTokenSource?.Dispose();
    }
    
    private class IngestionMetrics
    {
        public long PointsPublished { get; set; }
        public double LastValue { get; set; }
        public DateTime LastTimestamp { get; set; }
    }
}
