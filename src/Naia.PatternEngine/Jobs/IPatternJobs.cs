using Hangfire;
using Hangfire.Server;

namespace Naia.PatternEngine.Jobs;

/// <summary>
/// Behavioral analysis job interface - calculates statistics for all active points.
/// Runs every 5 minutes to maintain fresh behavioral fingerprints.
/// </summary>
public interface IBehavioralAnalysisJob
{
    /// <summary>
    /// Analyze behavioral patterns for all active points from QuestDB time-series data.
    /// Calculates: mean, stddev, min, max, update rate, change frequency.
    /// Results stored in PostgreSQL behavioral_stats table and cached in Redis.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    [Queue("analysis")]
    Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken);
}

/// <summary>
/// Correlation analysis job interface - calculates pairwise correlations between points.
/// Runs every 15 minutes using QuestDB ASOF JOIN for time-aligned correlation.
/// </summary>
public interface ICorrelationAnalysisJob
{
    /// <summary>
    /// Calculate Pearson correlations between points with similar behavioral fingerprints.
    /// Uses QuestDB ASOF JOIN for efficient time-aligned correlation computation.
    /// Groups points by update rate and value range to reduce O(n²) complexity.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 120, 300])]
    [Queue("analysis")]
    Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken);
}

/// <summary>
/// Cluster detection job interface - groups correlated points into behavioral clusters.
/// Runs every 15 minutes after correlation analysis.
/// </summary>
public interface IClusterDetectionJob
{
    /// <summary>
    /// Detect behavioral clusters using Louvain community detection and DBSCAN.
    /// Groups points that exhibit correlated behavior into equipment-candidate clusters.
    /// Minimum cluster size: 3 points. Maximum: 50 points. Minimum cohesion: 0.50.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 120, 300])]
    [Queue("analysis")]
    Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken);
}

/// <summary>
/// Pattern matching job interface - matches clusters against known equipment patterns.
/// Runs every 15 minutes after cluster detection.
/// </summary>
public interface IPatternMatchingJob
{
    /// <summary>
    /// Match detected clusters against the pattern library using multi-factor scoring:
    /// - 30% Naming similarity (regex patterns)
    /// - 40% Correlation patterns (behavioral fingerprint match)
    /// - 20% Value range similarity
    /// - 10% Update rate similarity
    /// Creates suggestions for matches above 50% confidence.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 120, 300])]
    [Queue("matching")]
    Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken);
}

/// <summary>
/// Pattern learning job interface - updates pattern confidence based on user feedback.
/// Runs every hour and processes pending feedback.
/// </summary>
public interface IPatternLearningJob
{
    /// <summary>
    /// Process user feedback (approvals/rejections) to update pattern confidence:
    /// - Approved: +5% confidence, bind points to pattern
    /// - Rejected: -3% confidence, log rejection reason
    /// - Deferred: No change, logged for analysis
    /// Also applies confidence decay (0.5% per day of inactivity).
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 120, 300])]
    [Queue("learning")]
    Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken);
}

/// <summary>
/// Maintenance job interface - cleanup and housekeeping tasks.
/// Runs daily to maintain system health.
/// </summary>
public interface IMaintenanceJob
{
    /// <summary>
    /// Cleanup expired suggestions, old correlation cache entries, and orphaned clusters.
    /// Runs retention policies for behavioral stats and feedback logs.
    /// </summary>
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [300, 600])]
    [Queue("maintenance")]
    Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken);
}
