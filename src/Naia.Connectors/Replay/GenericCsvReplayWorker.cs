using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Domain.ValueObjects;

namespace Naia.Connectors.Replay;

/// <summary>
/// Worker that replays generic CSV data from multiple industrial sites.
/// Publishes to Kafka for ingestion pipeline processing.
/// 
/// FLOW:
///   1. Scan all configured sites for CSV files
///   2. Read and preprocess data (timezone conversion, tag transformation)
///   3. Publish to Kafka in timestamp order
///   4. Optionally loop for continuous testing
/// </summary>
public sealed class GenericCsvReplayWorker : BackgroundService
{
    private readonly GenericCsvReader _csvReader;
    private readonly IProducer<string, string> _producer;
    private readonly GenericCsvReplayOptions _options;
    private readonly ILogger<GenericCsvReplayWorker> _logger;
    
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    private long _messagesPublished;
    private long _errorCount;
    private int _loopCount;
    
    public GenericCsvReplayWorker(
        GenericCsvReader csvReader,
        IProducer<string, string> producer,
        IOptions<GenericCsvReplayOptions> options,
        ILogger<GenericCsvReplayWorker> logger)
    {
        _csvReader = csvReader;
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Generic CSV Replay is disabled");
            return;
        }
        
        _logger.LogInformation("╔════════════════════════════════════════════════════════════════════╗");
        _logger.LogInformation("║   📊 MULTI-SITE CSV REPLAY - Industrial Data Pump                 ║");
        _logger.LogInformation("╠════════════════════════════════════════════════════════════════════╣");
        _logger.LogInformation("║   Sites: {Count,-55}║", _options.Sites.Count);
        _logger.LogInformation("║   Loop: {Loop,-56}║", _options.LoopReplay ? "Enabled" : "Disabled");
        _logger.LogInformation("║   Speed: {Speed,-55}║", 
            _options.SpeedMultiplier == 0 ? "Maximum" : $"{_options.SpeedMultiplier}x");
        _logger.LogInformation("╚════════════════════════════════════════════════════════════════════╝");
        
        try
        {
            do
            {
                await ReplayAllSitesAsync(stoppingToken);
                
                if (_options.LoopReplay)
                {
                    _loopCount++;
                    _logger.LogInformation("✓ Replay loop {Count} complete. Restarting...", _loopCount);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Brief pause between loops
                }
                else
                {
                    break;
                }
            } 
            while (!stoppingToken.IsCancellationRequested);
            
            _logger.LogInformation("✓ Generic CSV Replay completed gracefully");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Generic CSV Replay shutting down...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Generic CSV Replay worker");
            throw;
        }
        finally
        {
            _logger.LogInformation(
                "Generic CSV Replay stopped. Total: {Messages:N0} messages published, {Errors} errors",
                _messagesPublished, _errorCount);
        }
    }
    
    /// <summary>
    /// Replay data from all configured sites.
    /// </summary>
    private async Task ReplayAllSitesAsync(CancellationToken ct)
    {
        // Step 1: Load all data from all sites with per-site offset tracking
        _logger.LogInformation("Loading data from {Count} sites...", _options.Sites.Count);
        
        var allData = new List<(CsvDataPoint Point, SiteReplayConfig Site)>();
        var siteOffsets = new Dictionary<string, TimeSpan>();
        
        foreach (var site in _options.Sites)
        {
            _logger.LogInformation("  Loading site: {Name} ({Id}) [Offset: {Offset}s]", 
                site.SiteName, site.SiteId, site.StartOffsetSeconds);
            
            // Track per-site offset for staggering
            siteOffsets[site.SiteId] = TimeSpan.FromSeconds(site.StartOffsetSeconds);
            
            var siteFiles = _csvReader.ScanSiteFiles(site).ToList();
            _logger.LogInformation("    Found {Count} CSV files", siteFiles.Count);
            
            int fileCount = 0;
            foreach (var (tagName, filePath) in siteFiles)
            {
                fileCount++;
                if (fileCount % 50 == 0)
                {
                    _logger.LogInformation("    Processing file {Current}/{Total}...", fileCount, siteFiles.Count);
                }
                
                var points = _csvReader.ReadFile(filePath, tagName, site).ToList();
                
                foreach (var point in points)
                {
                    allData.Add((point, site));
                }
            }
            
            _logger.LogInformation("  ✓ Site {Name}: {Files} files, {Points:N0} data points", 
                site.SiteName, siteFiles.Count, allData.Count(d => d.Site.SiteId == site.SiteId));
        }
        
        if (allData.Count == 0)
        {
            _logger.LogWarning("No data loaded from any site. Check configuration and data directories.");
            return;
        }
        
        // Step 2: Sort by timestamp for chronological replay (with site offsets applied)
        _logger.LogInformation("Sorting {Count:N0} data points by timestamp (with per-site staggering)...", allData.Count);
        
        // Apply per-site offsets when sorting to stagger publishing
        allData.Sort((a, b) => 
        {
            var timeA = a.Point.Timestamp + siteOffsets.GetValueOrDefault(a.Site.SiteId, TimeSpan.Zero);
            var timeB = b.Point.Timestamp + siteOffsets.GetValueOrDefault(b.Site.SiteId, TimeSpan.Zero);
            return timeA.CompareTo(timeB);
        });
        
        var startTime = allData.First().Point.Timestamp;
        var endTime = allData.Last().Point.Timestamp;
        var duration = endTime - startTime;
        
        _logger.LogInformation("Data range: {Start:yyyy-MM-dd HH:mm} to {End:yyyy-MM-dd HH:mm} (Duration: {Duration})",
            startTime, endTime, duration);
        
        // Step 3: Publish to Kafka in batches
        _logger.LogInformation("Publishing to Kafka topic: {Topic}", _options.KafkaTopic);
        
        int published = 0;
        int batchesPublished = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DateTime? lastTimestamp = null;
        
        const int batchSize = 1000; // Publish in batches of 1000 points
        var currentBatch = new List<DataPoint>();
        
        foreach (var (point, site) in allData)
        {
            if (ct.IsCancellationRequested)
                break;
            
            // Handle speed multiplier (throttle if not max speed)
            if (_options.SpeedMultiplier > 0 && lastTimestamp.HasValue)
            {
                var realInterval = point.Timestamp - lastTimestamp.Value;
                var replayInterval = TimeSpan.FromMilliseconds(
                    realInterval.TotalMilliseconds / _options.SpeedMultiplier);
                
                if (replayInterval.TotalMilliseconds > 0)
                {
                    await Task.Delay(replayInterval, ct);
                }
            }
            
            lastTimestamp = point.Timestamp;
            
            // Filter by bad status handling
            if (point.Status != "Good" && point.Status != "192")
            {
                if (_options.BadStatusHandling == "Skip")
                {
                    continue;
                }
                else if (_options.BadStatusHandling == "ConvertToNull")
                {
                    point.Value = 0; // Or use NaN if your system supports it
                }
                // "Store" = do nothing, keep as-is
            }
            
            // Create DataPoint message
            var dataPoint = new DataPoint
            {
                PointSequenceId = 0, // Will be resolved by ingestion pipeline via PointName lookup
                PointName = point.TagName,
                Timestamp = point.Timestamp,
                Value = point.Value,
                Quality = point.Status == "Good" || point.Status == "192" ? DataQuality.Good : DataQuality.Bad,
                DataSourceId = site.SiteId,
                SourceTag = point.SourceTag,
                ReceivedAt = DateTime.UtcNow
            };
            
            currentBatch.Add(dataPoint);
            
            // Publish batch when full
            if (currentBatch.Count >= batchSize)
            {
                await PublishBatchAsync(currentBatch, site.SiteId, ct);
                published += currentBatch.Count;
                batchesPublished++;
                currentBatch.Clear();
                
                // Progress logging
                if (published % 10000 == 0)
                {
                    var rate = published / sw.Elapsed.TotalSeconds;
                    _logger.LogInformation("  Progress: {Published:N0} / {Total:N0} ({Percent:F1}%) - {Rate:F0} msg/s",
                        published, allData.Count, (published * 100.0 / allData.Count), rate);
                }
            }
        }
        
        // Publish any remaining points
        if (currentBatch.Count > 0)
        {
            await PublishBatchAsync(currentBatch, _options.Sites.FirstOrDefault()?.SiteId ?? "GenericCsvReplay", ct);
            published += currentBatch.Count;
            batchesPublished++;
        }
        
        sw.Stop();
        var avgRate = published / sw.Elapsed.TotalSeconds;
        
        _logger.LogInformation(
            "✓ Published {Count:N0} data points in {Batches} batches, {Duration} ({Rate:F0} msg/s average)",
            published, batchesPublished, sw.Elapsed, avgRate);
    }
    
    /// <summary>
    /// Publish a batch of data points to Kafka.
    /// </summary>
    private async Task PublishBatchAsync(List<DataPoint> points, string dataSourceId, CancellationToken ct)
    {
        if (points.Count == 0)
            return;
        
        var batch = new DataPointBatch
        {
            Points = points.ToList(), // Create copy to avoid mutation
            BatchId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            DataSourceId = dataSourceId
        };
        
        try
        {
            var json = JsonSerializer.Serialize(batch, _jsonOptions);
            var message = new Message<string, string>
            {
                Key = $"csv-replay-{batch.BatchId}",
                Value = json
            };
            
            await _producer.ProduceAsync(_options.KafkaTopic, message, ct);
            _messagesPublished += points.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish batch with {Count} points", points.Count);
            _errorCount += points.Count;
        }
    }
}
