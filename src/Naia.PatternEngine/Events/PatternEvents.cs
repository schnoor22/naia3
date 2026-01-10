namespace Naia.PatternEngine.Events;

/// <summary>
/// Event published when sufficient behavioral data has been aggregated for a point.
/// Triggers correlation calculation and cluster detection.
/// 
/// Published by: BehavioralAggregator
/// Consumed by: CorrelationProcessor
/// Topic: naia.points.behavior
/// </summary>
public sealed record PointBehaviorUpdated
{
    /// <summary>Point UUID from PostgreSQL</summary>
    public required Guid PointId { get; init; }
    
    /// <summary>Sequential ID for QuestDB queries</summary>
    public required long PointIdSeq { get; init; }
    
    /// <summary>Point name for pattern matching</summary>
    public required string PointName { get; init; }
    
    /// <summary>Data source ID for grouping</summary>
    public Guid? DataSourceId { get; init; }
    
    /// <summary>Minimum value in window</summary>
    public required double MinValue { get; init; }
    
    /// <summary>Maximum value in window</summary>
    public required double MaxValue { get; init; }
    
    /// <summary>Mean value in window</summary>
    public required double MeanValue { get; init; }
    
    /// <summary>Standard deviation in window</summary>
    public required double StdDeviation { get; init; }
    
    /// <summary>Update rate (samples per second)</summary>
    public required double UpdateRateHz { get; init; }
    
    /// <summary>Number of samples in this aggregation</summary>
    public required long SampleCount { get; init; }
    
    /// <summary>Window start time (UTC)</summary>
    public required DateTime WindowStart { get; init; }
    
    /// <summary>Window end time (UTC)</summary>
    public required DateTime WindowEnd { get; init; }
    
    /// <summary>When this event was created</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event published when correlation matrix has been updated.
/// Triggers cluster detection.
/// 
/// Published by: CorrelationProcessor
/// Consumed by: ClusterDetectionWorker
/// Topic: naia.correlations.updated
/// </summary>
public sealed record CorrelationsUpdated
{
    /// <summary>Unique ID for this correlation update</summary>
    public required string CorrelationBatchId { get; init; }
    
    /// <summary>Data source that triggered the update</summary>
    public Guid? DataSourceId { get; init; }
    
    /// <summary>Points involved in this correlation update</summary>
    public required List<Guid> PointIds { get; init; }
    
    /// <summary>Number of significant correlations found (> threshold)</summary>
    public required int SignificantCorrelationCount { get; init; }
    
    /// <summary>Average correlation strength</summary>
    public required double AverageCorrelation { get; init; }
    
    /// <summary>Time window used for correlation calculation</summary>
    public required DateTime WindowStart { get; init; }
    
    /// <summary>Time window end</summary>
    public required DateTime WindowEnd { get; init; }
    
    /// <summary>When this event was created</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event published when a new behavioral cluster is detected.
/// Triggers pattern matching.
/// 
/// Published by: ClusterDetectionWorker
/// Consumed by: PatternMatcherWorker
/// Topic: naia.clusters.created
/// </summary>
public sealed record ClusterCreated
{
    /// <summary>Unique cluster ID</summary>
    public required Guid ClusterId { get; init; }
    
    /// <summary>How the cluster was detected</summary>
    public required ClusterSourceType SourceType { get; init; }
    
    /// <summary>Source identifier (import session, discovery ID, etc.)</summary>
    public string? SourceId { get; init; }
    
    /// <summary>Points in this cluster</summary>
    public required List<Guid> PointIds { get; init; }
    
    /// <summary>Point names (for pattern matching)</summary>
    public required List<string> PointNames { get; init; }
    
    /// <summary>Average intra-cluster correlation</summary>
    public required double AverageCorrelation { get; init; }
    
    /// <summary>Cluster cohesion score (0-1)</summary>
    public required double CohesionScore { get; init; }
    
    /// <summary>Detected common naming pattern</summary>
    public string? NamingPattern { get; init; }
    
    /// <summary>Common prefix extracted from point names</summary>
    public string? CommonPrefix { get; init; }
    
    /// <summary>When this cluster was created</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// How a cluster was detected
/// </summary>
public enum ClusterSourceType
{
    /// <summary>Detected from continuous data streaming</summary>
    Continuous,
    
    /// <summary>Detected from CSV/data import session</summary>
    Import,
    
    /// <summary>Detected from SmartRelay device discovery</summary>
    Discovery,
    
    /// <summary>Manually created by user</summary>
    Manual
}

/// <summary>
/// Event published when a pattern match suggestion is created.
/// Notifies users and triggers UI updates.
/// 
/// Published by: PatternMatcherWorker
/// Consumed by: API (notifications), UI (real-time updates)
/// Topic: naia.suggestions.created
/// </summary>
public sealed record SuggestionCreated
{
    /// <summary>Unique suggestion ID</summary>
    public required Guid SuggestionId { get; init; }
    
    /// <summary>Cluster this suggestion is for</summary>
    public required Guid ClusterId { get; init; }
    
    /// <summary>Pattern that was matched</summary>
    public required Guid PatternId { get; init; }
    
    /// <summary>Pattern name (for display)</summary>
    public required string PatternName { get; init; }
    
    /// <summary>Overall confidence score (0-1)</summary>
    public required double OverallConfidence { get; init; }
    
    /// <summary>Naming similarity score (0-1)</summary>
    public required double NamingScore { get; init; }
    
    /// <summary>Correlation similarity score (0-1)</summary>
    public required double CorrelationScore { get; init; }
    
    /// <summary>Value range similarity score (0-1)</summary>
    public required double RangeScore { get; init; }
    
    /// <summary>Update rate similarity score (0-1)</summary>
    public required double RateScore { get; init; }
    
    /// <summary>Human-readable reason for match</summary>
    public required string Reason { get; init; }
    
    /// <summary>Number of points in the cluster</summary>
    public required int PointCount { get; init; }
    
    /// <summary>When this suggestion was created</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event published when user approves or rejects a suggestion.
/// Triggers pattern learning and confidence updates.
/// 
/// Published by: API (when user acts on suggestion)
/// Consumed by: PatternLearnerWorker
/// Topic: naia.patterns.feedback
/// </summary>
public sealed record PatternFeedback
{
    /// <summary>Suggestion that was acted upon</summary>
    public required Guid SuggestionId { get; init; }
    
    /// <summary>Pattern that was suggested</summary>
    public required Guid PatternId { get; init; }
    
    /// <summary>Cluster that was matched</summary>
    public required Guid ClusterId { get; init; }
    
    /// <summary>User action</summary>
    public required FeedbackAction Action { get; init; }
    
    /// <summary>User who took the action</summary>
    public string? UserId { get; init; }
    
    /// <summary>Reason for rejection (if rejected)</summary>
    public string? RejectionReason { get; init; }
    
    /// <summary>Confidence at time of action</summary>
    public required double ConfidenceAtAction { get; init; }
    
    /// <summary>Element created (if approved)</summary>
    public Guid? CreatedElementId { get; init; }
    
    /// <summary>When this feedback was received</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// User feedback action type
/// </summary>
public enum FeedbackAction
{
    /// <summary>User approved the suggestion</summary>
    Approved,
    
    /// <summary>User rejected the suggestion</summary>
    Rejected,
    
    /// <summary>User deferred decision</summary>
    Deferred
}

/// <summary>
/// Event published when a pattern is updated (learned or confidence changed).
/// Triggers re-evaluation of existing clusters.
/// 
/// Published by: PatternLearnerWorker
/// Consumed by: PatternMatcherWorker (re-evaluate pending clusters)
/// Topic: naia.patterns.updated
/// </summary>
public sealed record PatternUpdated
{
    /// <summary>Pattern that was updated</summary>
    public required Guid PatternId { get; init; }
    
    /// <summary>Pattern name</summary>
    public required string PatternName { get; init; }
    
    /// <summary>What type of update occurred</summary>
    public required PatternUpdateType UpdateType { get; init; }
    
    /// <summary>Previous confidence level</summary>
    public double? OldConfidence { get; init; }
    
    /// <summary>New confidence level</summary>
    public required double NewConfidence { get; init; }
    
    /// <summary>Number of examples that contributed to this pattern</summary>
    public required int ExampleCount { get; init; }
    
    /// <summary>Number of signatures in this pattern</summary>
    public required int SignatureCount { get; init; }
    
    /// <summary>When this update occurred</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Type of pattern update
/// </summary>
public enum PatternUpdateType
{
    /// <summary>New pattern created</summary>
    Created,
    
    /// <summary>Confidence increased (approval)</summary>
    ConfidenceIncreased,
    
    /// <summary>Confidence decreased (rejection)</summary>
    ConfidenceDecreased,
    
    /// <summary>New signature added</summary>
    SignatureAdded,
    
    /// <summary>Confidence decayed due to inactivity</summary>
    ConfidenceDecayed
}
