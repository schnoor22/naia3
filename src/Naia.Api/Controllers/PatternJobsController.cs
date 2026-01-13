using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Naia.PatternEngine.Jobs;

namespace Naia.Api.Controllers;

/// <summary>
/// API endpoints for manually triggering pattern analysis jobs.
/// Useful for testing, demos, or when you want immediate analysis
/// rather than waiting for the scheduled CRON intervals.
/// </summary>
[ApiController]
[Route("api/pattern-jobs")]
public class PatternJobsController : ControllerBase
{
    private readonly ILogger<PatternJobsController> _logger;
    private readonly IRecurringJobManager _recurringJobManager;

    public PatternJobsController(
        ILogger<PatternJobsController> logger,
        IRecurringJobManager recurringJobManager)
    {
        _logger = logger;
        _recurringJobManager = recurringJobManager;
    }

    /// <summary>
    /// Trigger all pattern analysis jobs in sequence.
    /// This runs: Behavioral → Correlation → Cluster → Matching
    /// </summary>
    [HttpPost("trigger-all")]
    public IActionResult TriggerAll()
    {
        _logger.LogInformation("🚀 Manual trigger: Running full pattern analysis pipeline");

        // Trigger in the correct sequence with dependencies
        // Each job is enqueued, allowing Hangfire to manage execution
        RecurringJob.TriggerJob("pattern-behavioral-analysis");
        
        // Enqueue subsequent jobs with delays to allow previous jobs to complete
        BackgroundJob.Schedule(() => TriggerCorrelationAnalysis(), TimeSpan.FromSeconds(30));
        BackgroundJob.Schedule(() => TriggerClusterDetection(), TimeSpan.FromSeconds(60));
        BackgroundJob.Schedule(() => TriggerPatternMatching(), TimeSpan.FromSeconds(90));

        return Ok(new
        {
            message = "Pattern analysis pipeline triggered",
            jobs = new[]
            {
                "pattern-behavioral-analysis (now)",
                "pattern-correlation-analysis (30s)",
                "pattern-cluster-detection (60s)",
                "pattern-matching (90s)"
            }
        });
    }

    /// <summary>
    /// Trigger only the behavioral analysis job.
    /// Calculates statistics (min, max, mean, stddev, update rate) for each point.
    /// </summary>
    [HttpPost("trigger-behavioral")]
    public IActionResult TriggerBehavioral()
    {
        _logger.LogInformation("🔬 Manual trigger: Behavioral analysis");
        RecurringJob.TriggerJob("pattern-behavioral-analysis");
        return Ok(new { message = "Behavioral analysis triggered", job = "pattern-behavioral-analysis" });
    }

    /// <summary>
    /// Trigger only the correlation analysis job.
    /// Calculates Pearson correlations between points in the same data source.
    /// </summary>
    [HttpPost("trigger-correlation")]
    public IActionResult TriggerCorrelation()
    {
        _logger.LogInformation("🔗 Manual trigger: Correlation analysis");
        RecurringJob.TriggerJob("pattern-correlation-analysis");
        return Ok(new { message = "Correlation analysis triggered", job = "pattern-correlation-analysis" });
    }

    [NonAction]
    public void TriggerCorrelationAnalysis()
    {
        RecurringJob.TriggerJob("pattern-correlation-analysis");
    }

    /// <summary>
    /// Trigger only the cluster detection job.
    /// Uses Louvain algorithm to find groups of correlated points.
    /// </summary>
    [HttpPost("trigger-cluster")]
    public IActionResult TriggerCluster()
    {
        _logger.LogInformation("🌐 Manual trigger: Cluster detection");
        RecurringJob.TriggerJob("pattern-cluster-detection");
        return Ok(new { message = "Cluster detection triggered", job = "pattern-cluster-detection" });
    }

    [NonAction]
    public void TriggerClusterDetection()
    {
        RecurringJob.TriggerJob("pattern-cluster-detection");
    }

    /// <summary>
    /// Trigger only the pattern matching job.
    /// Matches detected clusters against the pattern library.
    /// </summary>
    [HttpPost("trigger-matching")]
    public IActionResult TriggerMatching()
    {
        _logger.LogInformation("🎯 Manual trigger: Pattern matching");
        RecurringJob.TriggerJob("pattern-matching");
        return Ok(new { message = "Pattern matching triggered", job = "pattern-matching" });
    }

    [NonAction]
    public void TriggerPatternMatching()
    {
        RecurringJob.TriggerJob("pattern-matching");
    }

    /// <summary>
    /// Trigger the pattern learning job.
    /// Processes feedback from approved/rejected suggestions to update confidence.
    /// </summary>
    [HttpPost("trigger-learning")]
    public IActionResult TriggerLearning()
    {
        _logger.LogInformation("📚 Manual trigger: Pattern learning");
        RecurringJob.TriggerJob("pattern-learning");
        return Ok(new { message = "Pattern learning triggered", job = "pattern-learning" });
    }

    /// <summary>
    /// Get the status of pattern analysis (counts from key tables).
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromServices] Naia.Infrastructure.Persistence.NaiaDbContext db)
    {
        using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        var stats = new Dictionary<string, object>();

        // Query counts from pattern tables
        var tables = new[] 
        { 
            "behavioral_stats", 
            "correlation_cache", 
            "behavioral_clusters", 
            "pattern_suggestions",
            "patterns",
            "pattern_roles"
        };

        foreach (var table in tables)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
                var count = await cmd.ExecuteScalarAsync();
                stats[table] = count ?? 0;
            }
            catch
            {
                stats[table] = "error";
            }
        }

        // Get pending suggestions count
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pattern_suggestions WHERE status = 'pending'";
            var pending = await cmd.ExecuteScalarAsync();
            stats["pending_suggestions"] = pending ?? 0;
        }
        catch
        {
            stats["pending_suggestions"] = "error";
        }

        return Ok(stats);
    }
}
