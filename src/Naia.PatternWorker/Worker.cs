namespace Naia.PatternWorker;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PatternWorker started. Hangfire server is processing pattern analysis jobs.");
        
        // Keep alive - Hangfire server runs in background
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        
        logger.LogInformation("PatternWorker stopping...");
    }
}
