using Naia.Application.Abstractions;

namespace Naia.Ingestion;

/// <summary>
/// Main ingestion worker - consumes data from Kafka and persists to QuestDB + Redis.
/// 
/// This is the core historian ingestion engine. It:
/// 1. Consumes DataPointBatch messages from Kafka (naia.datapoints topic)
/// 2. Deduplicates using Redis idempotency store
/// 3. Writes time-series data to QuestDB
/// 4. Updates current value cache in Redis
/// 5. Commits Kafka offsets only AFTER successful processing
/// 
/// GUARANTEES:
/// - At-least-once delivery (Kafka consumer)
/// - Exactly-once processing (idempotency store)
/// - Zero data loss (manual offset commits)
/// 
/// SCALING:
/// - Deploy multiple instances for horizontal scaling
/// - Kafka partitions distribute load automatically
/// - Each instance gets exclusive partitions via consumer group
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("═══════════════════════════════════════════════════════════════════");
        _logger.LogInformation("  NAIA Ingestion Worker Starting");
        _logger.LogInformation("  The First Industrial Historian That Learns From You");
        _logger.LogInformation("═══════════════════════════════════════════════════════════════════");
        
        // Wait a moment for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        
        // Create a scope for the pipeline (it has scoped dependencies)
        using var scope = _scopeFactory.CreateScope();
        
        IIngestionPipeline? pipeline = null;
        
        try
        {
            pipeline = scope.ServiceProvider.GetRequiredService<IIngestionPipeline>();
            
            _logger.LogInformation("Starting ingestion pipeline...");
            await pipeline.StartAsync(stoppingToken);
            
            _logger.LogInformation("Pipeline running - consuming from Kafka, writing to QuestDB + Redis");
            _logger.LogInformation("Press Ctrl+C to stop gracefully");
            
            // Monitor pipeline health periodically
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                
                var health = await pipeline.GetHealthAsync(stoppingToken);
                var metrics = await pipeline.GetMetricsAsync(stoppingToken);
                
                if (health.IsHealthy)
                {
                    _logger.LogInformation(
                        "Pipeline Health: ✓ | Processed: {Total} batches, {Points} points | " +
                        "Throughput: {Rate}/s | Avg Latency: {Latency}ms",
                        metrics.TotalBatchesProcessed,
                        metrics.TotalPointsProcessed,
                        metrics.PointsPerSecond.ToString("F1"),
                        metrics.AverageProcessingMs.ToString("F1"));
                }
                else
                {
                    _logger.LogWarning(
                        "Pipeline Health: ✗ | State: {State} | Error: {Error}",
                        health.State,
                        health.ErrorMessage ?? "Unknown");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Shutdown requested - stopping gracefully...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in ingestion worker - shutting down");
            _lifetime.StopApplication();
            throw;
        }
        finally
        {
            if (pipeline != null)
            {
                _logger.LogInformation("Stopping pipeline - draining in-flight messages...");
                await pipeline.StopAsync(CancellationToken.None);
                
                var finalMetrics = await pipeline.GetMetricsAsync(CancellationToken.None);
                _logger.LogInformation(
                    "Pipeline stopped. Final stats: {Batches} batches, {Points} points processed",
                    finalMetrics.TotalBatchesProcessed,
                    finalMetrics.TotalPointsProcessed);
            }
        }
        
        _logger.LogInformation("═══════════════════════════════════════════════════════════════════");
        _logger.LogInformation("  NAIA Ingestion Worker Stopped");
        _logger.LogInformation("═══════════════════════════════════════════════════════════════════");
    }
}
