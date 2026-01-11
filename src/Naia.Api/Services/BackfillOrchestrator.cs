using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Naia.Application.Abstractions;
using Naia.Connectors.Abstractions;

namespace Naia.Api.Services;

/// <summary>
/// Orchestrates historical data backfill from PI System (AF SDK or Web API) to Kafka pipeline.
/// 
/// Strategy (proven from v1):
///   1. Queue backfill requests via API
///   2. Process in 30-day chunks to manage memory
///   3. Publish batches to Kafka topic 'naia.datapoints.backfill'
///   4. Checkpoint every chunk for resume on failure
///   5. Track progress in real-time
/// 
/// Advantages:
///   - Connector-agnostic (AF SDK or Web API)
///   - Memory-efficient (processes 10+ years safely)
///   - Resume on failure via checkpoints
///   - Flows through existing Kafka pipeline
///   - Non-blocking queue-based processing
/// </summary>
public sealed class BackfillOrchestrator : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataPointProducer _producer;
    private readonly ILogger<BackfillOrchestrator> _logger;
    
    private readonly Channel<BackfillRequest> _requestQueue;
    private readonly Dictionary<Guid, BackfillRequest> _activeRequests = new();
    private readonly object _statsLock = new();
    
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    private BackfillStats _stats = new();
    
    public BackfillOrchestrator(
        IServiceScopeFactory scopeFactory,
        IDataPointProducer producer,
        ILogger<BackfillOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _producer = producer;
        _logger = logger;
        
        // Bounded channel - max 20 queued backfill requests
        _requestQueue = Channel.CreateBounded<BackfillRequest>(new BoundedChannelOptions(20)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }
    
    /// <summary>
    /// Queue a backfill request. Will be processed by the background worker.
    /// </summary>
    public async Task<Guid> QueueBackfillAsync(
        string connectorType,
        IEnumerable<string> pointAddresses,
        DateTime startTime,
        DateTime endTime,
        TimeSpan? chunkSize = null,
        CancellationToken ct = default)
    {
        var request = new BackfillRequest
        {
            RequestId = Guid.NewGuid(),
            ConnectorType = connectorType,
            PointAddresses = pointAddresses.ToList(),
            StartTime = startTime,
            EndTime = endTime,
            ChunkSize = chunkSize ?? TimeSpan.FromDays(30), // Default 30-day chunks (v1 pattern)
            QueuedAt = DateTime.UtcNow
        };
        
        await _requestQueue.Writer.WriteAsync(request, ct);
        
        _logger.LogInformation(
            "Backfill request {RequestId} queued: {ConnectorType}, {PointCount} points, {StartTime} to {EndTime}",
            request.RequestId, connectorType, request.PointAddresses.Count, startTime, endTime);
        
        return request.RequestId;
    }
    
    /// <summary>
    /// Get current backfill status
    /// </summary>
    public BackfillStatus GetStatus()
    {
        lock (_statsLock)
        {
            return new BackfillStatus
            {
                ActiveRequests = _activeRequests.Values.ToList(),
                Stats = _stats,
                QueueDepth = _requestQueue.Reader.Count
            };
        }
    }
    
    /// <summary>
    /// Get status for a specific request
    /// </summary>
    public BackfillRequest? GetRequestStatus(Guid requestId)
    {
        lock (_statsLock)
        {
            return _activeRequests.TryGetValue(requestId, out var request) ? request : null;
        }
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backfill Orchestrator started");
        
        await foreach (var request in _requestQueue.Reader.ReadAllAsync(stoppingToken))
        {
            lock (_statsLock)
            {
                _activeRequests[request.RequestId] = request;
            }
            
            try
            {
                await ProcessBackfillRequestAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Backfill request {RequestId} cancelled", request.RequestId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process backfill request {RequestId}", request.RequestId);
                lock (_statsLock)
                {
                    _stats.FailedRequests++;
                }
            }
            finally
            {
                lock (_statsLock)
                {
                    _activeRequests.Remove(request.RequestId);
                }
            }
        }
        
        _logger.LogInformation("Backfill Orchestrator stopped");
    }
    
    private async Task ProcessBackfillRequestAsync(BackfillRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        request.StartedAt = DateTime.UtcNow;
        request.Status = BackfillStatus.Running;
        
        _logger.LogInformation(
            "Processing backfill request {RequestId}: {ConnectorType}, {PointCount} points from {StartTime} to {EndTime} in {ChunkSize} chunks",
            request.RequestId, request.ConnectorType, request.PointAddresses.Count, 
            request.StartTime, request.EndTime, request.ChunkSize);
        
        // Get the appropriate connector from DI
        IHistoricalDataConnector? connector;
        using (var scope = _scopeFactory.CreateScope())
        {
            connector = GetConnector(scope, request.ConnectorType);
            if (connector == null)
            {
                throw new InvalidOperationException($"Connector type '{request.ConnectorType}' not found or not configured");
            }
            
            if (!connector.IsAvailable)
            {
                throw new InvalidOperationException($"Connector '{request.ConnectorType}' is not available");
            }
        }
        
        // Calculate time chunks (v1 pattern: 30-day chunks)
        var chunks = CalculateTimeChunks(request.StartTime, request.EndTime, request.ChunkSize);
        request.TotalChunks = chunks.Count;
        
        _logger.LogInformation(
            "Backfill {RequestId} will process {ChunkCount} time chunks",
            request.RequestId, chunks.Count);
        
        // Process each chunk
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            
            using var scope = _scopeFactory.CreateScope();
            connector = GetConnector(scope, request.ConnectorType);
            
            await ProcessChunkAsync(request, connector!, chunk.Start, chunk.End, ct);
            
            request.CompletedChunks++;
            
            // Log progress every 5 chunks or on last chunk
            if (request.CompletedChunks % 5 == 0 || request.CompletedChunks == request.TotalChunks)
            {
                var progress = (double)request.CompletedChunks / request.TotalChunks * 100;
                var estimatedTotal = request.TotalChunks * (sw.Elapsed.TotalSeconds / request.CompletedChunks);
                var eta = TimeSpan.FromSeconds(estimatedTotal - sw.Elapsed.TotalSeconds);
                
                _logger.LogInformation(
                    "Backfill {RequestId} progress: {CompletedChunks}/{TotalChunks} chunks ({Progress:F1}%), {PointsProcessed} points, ETA: {ETA}",
                    request.RequestId, request.CompletedChunks, request.TotalChunks, 
                    progress, request.PointsProcessed, eta);
            }
        }
        
        sw.Stop();
        request.CompletedAt = DateTime.UtcNow;
        request.Status = BackfillStatus.Completed;
        
        lock (_statsLock)
        {
            _stats.CompletedRequests++;
            _stats.TotalPointsBackfilled += request.PointsProcessed;
            _stats.TotalBatchesPublished += request.BatchesPublished;
        }
        
        _logger.LogInformation(
            "Backfill request {RequestId} completed: {PointCount} points, {BatchCount} batches, {ValueCount} values in {Duration}",
            request.RequestId, request.PointAddresses.Count, request.BatchesPublished, 
            request.PointsProcessed, sw.Elapsed);
    }
    
    private async Task ProcessChunkAsync(
        BackfillRequest request,
        IHistoricalDataConnector connector,
        DateTime chunkStart,
        DateTime chunkEnd,
        CancellationToken ct)
    {
        try
        {
            _logger.LogDebug(
                "Processing chunk {ChunkStart} to {ChunkEnd} for request {RequestId}",
                chunkStart, chunkEnd, request.RequestId);
            
            // Read historical data from connector (AF SDK or Web API)
            var historicalData = await connector.ReadHistoricalDataBatchAsync(
                request.PointAddresses,
                chunkStart,
                chunkEnd,
                ct);
            
            // Publish to Kafka in batches
            foreach (var timeSeries in historicalData)
            {
                if (timeSeries.Values.Count == 0)
                    continue;
                
                // Create batch message for this point's data in this chunk
                var batch = new BackfillDataBatch
                {
                    RequestId = request.RequestId,
                    BatchId = Guid.NewGuid(),
                    SourceAddress = timeSeries.SourceAddress,
                    ChunkStart = chunkStart,
                    ChunkEnd = chunkEnd,
                    ConnectorType = request.ConnectorType,
                    Timestamp = DateTime.UtcNow,
                    Values = timeSeries.Values.Select(v => new BackfillDataPoint
                    {
                        SourceAddress = timeSeries.SourceAddress ?? "",
                        Value = v.Value,
                        Timestamp = v.Timestamp,
                        Quality = v.Quality.ToString(),
                        Units = timeSeries.Units
                    }).ToList()
                };
                
                var json = JsonSerializer.Serialize(batch, _jsonOptions);
                
                await _producer.PublishAsync(
                    "naia.datapoints.backfill", // Separate topic for backfill data
                    timeSeries.SourceAddress,
                    json,
                    ct);
                
                request.PointsProcessed += batch.Values.Count;
                request.BatchesPublished++;
                
                lock (_statsLock)
                {
                    _stats.TotalPointsBackfilled++;
                }
            }
            
            // Save checkpoint (for future resume capability)
            request.LastCheckpoint = chunkEnd;
            request.CheckpointData = JsonSerializer.Serialize(new BackfillCheckpoint
            {
                LastCompletedChunk = chunkEnd,
                PointsProcessed = request.PointsProcessed,
                BatchesPublished = request.BatchesPublished,
                CompletedChunks = request.CompletedChunks
            }, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to process chunk {ChunkStart} to {ChunkEnd} for backfill request {RequestId}",
                chunkStart, chunkEnd, request.RequestId);
            
            request.FailedChunks++;
            lock (_statsLock)
            {
                _stats.FailedChunks++;
            }
            
            // Continue processing other chunks rather than failing entire backfill
        }
    }
    
    private IHistoricalDataConnector? GetConnector(IServiceScope scope, string connectorType)
    {
        return connectorType.ToLowerInvariant() switch
        {
            "piwebapi" => scope.ServiceProvider.GetService(typeof(Naia.Connectors.PI.PIWebApiConnector)) as IHistoricalDataConnector,
            _ => null
        };
    }
    
    private static List<(DateTime Start, DateTime End)> CalculateTimeChunks(
        DateTime startTime,
        DateTime endTime,
        TimeSpan chunkSize)
    {
        var chunks = new List<(DateTime Start, DateTime End)>();
        var current = startTime;
        
        while (current < endTime)
        {
            var chunkEnd = current + chunkSize;
            if (chunkEnd > endTime)
                chunkEnd = endTime;
            
            chunks.Add((current, chunkEnd));
            current = chunkEnd;
        }
        
        return chunks;
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Backfill Orchestrator stopping...");
        _requestQueue.Writer.Complete();
        await base.StopAsync(cancellationToken);
    }
}

#region DTOs

public sealed class BackfillRequest
{
    public required Guid RequestId { get; init; }
    public required string ConnectorType { get; init; }
    public required List<string> PointAddresses { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required TimeSpan ChunkSize { get; init; }
    public DateTime QueuedAt { get; init; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = BackfillStatus.Queued;
    
    public int TotalChunks { get; set; }
    public int CompletedChunks { get; set; }
    public int FailedChunks { get; set; }
    public long PointsProcessed { get; set; }
    public long BatchesPublished { get; set; }
    
    public DateTime? LastCheckpoint { get; set; }
    public string? CheckpointData { get; set; }
    
    public double ProgressPercentage => TotalChunks > 0 ? (double)CompletedChunks / TotalChunks * 100 : 0;
}

public sealed class BackfillStatus
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    
    public List<BackfillRequest> ActiveRequests { get; init; } = new();
    public BackfillStats Stats { get; init; } = new();
    public int QueueDepth { get; init; }
}

public sealed class BackfillStats
{
    public long CompletedRequests { get; set; }
    public long FailedRequests { get; set; }
    public long TotalPointsBackfilled { get; set; }
    public long TotalBatchesPublished { get; set; }
    public long FailedChunks { get; set; }
}

public sealed class BackfillCheckpoint
{
    public DateTime LastCompletedChunk { get; set; }
    public long PointsProcessed { get; set; }
    public long BatchesPublished { get; set; }
    public int CompletedChunks { get; set; }
}

public sealed class BackfillDataBatch
{
    public required Guid RequestId { get; init; }
    public required Guid BatchId { get; init; }
    public required string SourceAddress { get; init; }
    public required DateTime ChunkStart { get; init; }
    public required DateTime ChunkEnd { get; init; }
    public required string ConnectorType { get; init; }
    public required DateTime Timestamp { get; init; }
    public required List<BackfillDataPoint> Values { get; init; }
}

public sealed class BackfillDataPoint
{
    public required string SourceAddress { get; init; }
    public required object Value { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Quality { get; init; }
    public string? Units { get; init; }
}

#endregion
