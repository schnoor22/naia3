using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Domain.ValueObjects;

namespace Naia.Connectors.Replay;

/// <summary>
/// Background worker that replays historical Kelmarsh wind farm data through Kafka.
/// 
/// FLOW:
///   1. On startup: Calculate time offset (align historical data to current time)
///   2. Load data from CSV files for all turbines
///   3. Stream data points at configured speed (real-time or accelerated)
///   4. Publish to Kafka topic 'naia.datapoints' (same as PI connector)
///   5. Loop back to start when complete (if LoopReplay is enabled)
/// 
/// This creates a permanent "data pump" that simulates a live wind farm
/// connected to the system, allowing pattern learning and anomaly detection
/// without needing a real PI historian.
/// 
/// DATA:
/// - 6 wind turbines from Kelmarsh wind farm (UK)
/// - 18+ measurements per turbine (wind speed, power, temperatures, etc.)
/// - 10-minute intervals
/// - Full year of real operational data
/// </summary>
public sealed class WindFarmReplayWorker : BackgroundService
{
    private readonly KelmarshCsvReader _csvReader;
    private readonly IProducer<string, string> _producer;
    private readonly ReplayOptions _options;
    private readonly ILogger<WindFarmReplayWorker> _logger;
    
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    // State tracking
    private TimeSpan _timeOffset;
    private DateTime _currentDataTime;
    private DateTime _dataStartTime;
    private DateTime _dataEndTime;
    private bool _isPaused;
    private long _messagesPublished;
    private long _errorCount;
    private int _loopCount;
    
    // Preloaded data cache for faster replay
    private List<(DateTime Timestamp, List<ReplayDataPoint> Points)>? _dataCache;
    
    public WindFarmReplayWorker(
        KelmarshCsvReader csvReader,
        IProducer<string, string> producer,
        IOptions<ReplayOptions> options,
        ILogger<WindFarmReplayWorker> logger)
    {
        _csvReader = csvReader;
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }
    
    /// <summary>
    /// Manually stop the replay (can be restarted).
    /// </summary>
    public void PauseReplay()
    {
        _isPaused = true;
        _logger.LogInformation("Wind Farm Replay paused at {Time}", _currentDataTime);
    }
    
    /// <summary>
    /// Resume a paused replay.
    /// </summary>
    public void ResumeReplay()
    {
        _isPaused = false;
        _logger.LogInformation("Wind Farm Replay resumed from {Time}", _currentDataTime);
    }
    
    /// <summary>
    /// Get current replay status.
    /// </summary>
    public ReplayStatus GetStatus() => new()
    {
        IsRunning = !_isPaused,
        CurrentDataTime = _currentDataTime,
        DataStartTime = _dataStartTime,
        DataEndTime = _dataEndTime,
        LoopCount = _loopCount,
        MessagesPublished = _messagesPublished,
        ErrorCount = _errorCount,
        SpeedMultiplier = _options.SpeedMultiplier
    };
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("╔════════════════════════════════════════════════════════════════════╗");
        _logger.LogInformation("║   🌬️  WIND FARM REPLAY - Kelmarsh Data Pump                       ║");
        _logger.LogInformation("╠════════════════════════════════════════════════════════════════════╣");
        _logger.LogInformation("║   Site: {Site,-54}║", _options.SiteName);
        _logger.LogInformation("║   Turbines: {Count,-51}║", _options.TurbineCount);
        _logger.LogInformation("║   Speed: {Speed}x (real-time = 1.0){Padding}║", 
            _options.SpeedMultiplier, new string(' ', 36 - _options.SpeedMultiplier.ToString("F1").Length));
        _logger.LogInformation("╚════════════════════════════════════════════════════════════════════╝");
        
        if (!_options.AutoStart)
        {
            _logger.LogInformation("Auto-start disabled. Waiting for manual start...");
            _isPaused = true;
        }
        
        try
        {
            // Initialize - load data and calculate offset
            await InitializeAsync(stoppingToken);
            
            // Main replay loop
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_isPaused)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }
                
                await RunReplayLoopAsync(stoppingToken);
                
                if (_options.LoopReplay)
                {
                    _loopCount++;
                    _logger.LogInformation("Replay complete (loop {Count}). Restarting from beginning...", _loopCount);
                    await InitializeAsync(stoppingToken); // Recalculate offset for new loop
                }
                else
                {
                    _logger.LogInformation("Replay complete. Stopping worker.");
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Wind Farm Replay shutting down...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Wind Farm Replay worker");
            throw;
        }
        
        _logger.LogInformation(
            "Wind Farm Replay stopped. Total: {Messages} messages, {Errors} errors, {Loops} loops",
            _messagesPublished, _errorCount, _loopCount);
    }
    
    private async Task InitializeAsync(CancellationToken ct)
    {
        _logger.LogInformation("Initializing replay data...");
        
        // Get available data range
        var timeRange = _csvReader.GetDataTimeRange();
        if (!timeRange.HasValue)
        {
            throw new InvalidOperationException(
                $"No data files found in {_options.DataDirectory}. " +
                $"Please ensure Kelmarsh CSV files are present.");
        }
        
        _dataStartTime = timeRange.Value.Start;
        _dataEndTime = timeRange.Value.End;
        
        _logger.LogInformation("Data range: {Start} to {End}", _dataStartTime, _dataEndTime);
        
        // Calculate time offset to align data start with current time
        var now = DateTime.UtcNow;
        _timeOffset = now - _dataStartTime;
        _currentDataTime = _dataStartTime;
        
        _logger.LogInformation("Time offset: {Days} days (data will appear as current)", _timeOffset.TotalDays);
        
        // Preload data into memory for faster replay
        await PreloadDataAsync(ct);
    }
    
    private async Task PreloadDataAsync(CancellationToken ct)
    {
        _logger.LogInformation("Preloading data into memory...");
        
        var allPoints = new Dictionary<DateTime, List<ReplayDataPoint>>();
        
        foreach (var (turbineNum, filePath) in _csvReader.GetTurbineFiles())
        {
            ct.ThrowIfCancellationRequested();
            
            _logger.LogDebug("Loading turbine {Num} from {File}...", turbineNum, filePath);
            
            foreach (var batch in _csvReader.ReadTurbineDataBatched(
                turbineNum, filePath, batchSize: 5000))
            {
                foreach (var point in batch)
                {
                    if (!allPoints.TryGetValue(point.OriginalTimestamp, out var list))
                    {
                        list = new List<ReplayDataPoint>();
                        allPoints[point.OriginalTimestamp] = list;
                    }
                    list.Add(point);
                }
            }
        }
        
        // Sort by timestamp and cache
        _dataCache = allPoints
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
        
        var totalPoints = _dataCache.Sum(x => x.Points.Count);
        _logger.LogInformation(
            "Preloaded {Points:N0} points across {Timestamps:N0} timestamps from {Turbines} turbines",
            totalPoints, _dataCache.Count, _options.TurbineCount);
        
        await Task.CompletedTask;
    }
    
    private async Task RunReplayLoopAsync(CancellationToken ct)
    {
        if (_dataCache == null || _dataCache.Count == 0)
        {
            _logger.LogWarning("No data loaded. Cannot replay.");
            return;
        }
        
        var dataIntervalMs = _options.DataIntervalMinutes * 60 * 1000;
        var replayIntervalMs = (int)(dataIntervalMs / _options.SpeedMultiplier);
        
        _logger.LogInformation(
            "Starting replay: {Interval}ms between batches ({Speed}x speed)",
            replayIntervalMs, _options.SpeedMultiplier);
        
        foreach (var (timestamp, points) in _dataCache)
        {
            ct.ThrowIfCancellationRequested();
            
            if (_isPaused)
            {
                // Wait until unpaused
                while (_isPaused && !ct.IsCancellationRequested)
                {
                    await Task.Delay(500, ct);
                }
            }
            
            _currentDataTime = timestamp;
            var adjustedTimestamp = timestamp + _timeOffset;
            
            // Publish all points for this timestamp
            await PublishBatchAsync(points, adjustedTimestamp, ct);
            
            // Wait for next interval (adjusted for speed)
            await Task.Delay(replayIntervalMs, ct);
        }
    }
    
    private async Task PublishBatchAsync(
        List<ReplayDataPoint> points, 
        DateTime adjustedTimestamp, 
        CancellationToken ct)
    {
        // Convert replay points to DataPoint format expected by the consumer
        // Note: PointSequenceId will be looked up later by the ingestion pipeline
        var dataPoints = points.Select((p, idx) => new DataPoint
        {
            PointSequenceId = 0, // Will be resolved by consumer via PointName lookup
            PointName = p.PointName,
            Timestamp = adjustedTimestamp,
            Value = p.Value,
            Quality = DataQuality.Good,
            DataSourceId = "WindFarmReplay",
            SourceTag = p.SourceAddress,
            ReceivedAt = DateTime.UtcNow
        }).ToList();
        
        // Create batch (matching DataPointBatch format expected by consumer)
        var batch = new DataPointBatch
        {
            Points = dataPoints,
            BatchId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            DataSourceId = "WindFarmReplay"
        };
        
        try
        {
            var json = JsonSerializer.Serialize(batch, _jsonOptions);
            
            await _producer.ProduceAsync(
                _options.KafkaTopic,
                new Message<string, string>
                {
                    Key = $"replay-{batch.BatchId}",
                    Value = json
                },
                ct);
            
            Interlocked.Add(ref _messagesPublished, points.Count);
            
            if (_messagesPublished % 5000 == 0)
            {
                _logger.LogInformation(
                    "Replay: {Published:N0} messages | Data time: {DataTime:HH:mm} | Loop: {Loop}",
                    _messagesPublished, _currentDataTime, _loopCount);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _logger.LogError(ex, "Error publishing replay batch");
        }
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Wind Farm Replay stopping...");
        await base.StopAsync(cancellationToken);
        _producer.Flush(cancellationToken);
    }
}

/// <summary>
/// Current status of the replay worker.
/// </summary>
public sealed class ReplayStatus
{
    public bool IsRunning { get; init; }
    public DateTime CurrentDataTime { get; init; }
    public DateTime DataStartTime { get; init; }
    public DateTime DataEndTime { get; init; }
    public int LoopCount { get; init; }
    public long MessagesPublished { get; init; }
    public long ErrorCount { get; init; }
    public double SpeedMultiplier { get; init; }
}
