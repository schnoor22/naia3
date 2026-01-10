namespace Naia.PatternEngine.Configuration;

/// <summary>
/// Configuration for the Pattern Flywheel system.
/// Loaded from appsettings.json "PatternFlywheel" section.
/// </summary>
public sealed class PatternFlywheelOptions
{
    public const string SectionName = "PatternFlywheel";
    
    /// <summary>Enable/disable the pattern flywheel</summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>Kafka configuration</summary>
    public PatternKafkaOptions Kafka { get; set; } = new();
    
    /// <summary>Behavioral aggregation settings</summary>
    public BehavioralAggregatorOptions BehavioralAggregator { get; set; } = new();
    
    /// <summary>Correlation processing settings</summary>
    public CorrelationProcessorOptions CorrelationProcessor { get; set; } = new();
    
    /// <summary>Cluster detection settings</summary>
    public ClusterDetectionOptions ClusterDetection { get; set; } = new();
    
    /// <summary>Pattern matching settings</summary>
    public PatternMatchingOptions PatternMatching { get; set; } = new();
    
    /// <summary>Pattern learning settings</summary>
    public PatternLearningOptions PatternLearning { get; set; } = new();
}

/// <summary>
/// Kafka-specific configuration for pattern flywheel
/// </summary>
public sealed class PatternKafkaOptions
{
    /// <summary>Kafka bootstrap servers</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";
    
    /// <summary>Consumer group for behavioral aggregator</summary>
    public string BehavioralAggregatorGroupId { get; set; } = "naia-behavioral-aggregator";
    
    /// <summary>Consumer group for correlation processor</summary>
    public string CorrelationProcessorGroupId { get; set; } = "naia-correlation-processor";
    
    /// <summary>Consumer group for cluster detection</summary>
    public string ClusterDetectionGroupId { get; set; } = "naia-cluster-detection";
    
    /// <summary>Consumer group for pattern matcher</summary>
    public string PatternMatcherGroupId { get; set; } = "naia-pattern-matcher";
    
    /// <summary>Consumer group for pattern learner</summary>
    public string PatternLearnerGroupId { get; set; } = "naia-pattern-learner";
    
    // Topic names
    public string DataPointsTopic { get; set; } = "naia.datapoints";
    public string PointsBehaviorTopic { get; set; } = "naia.points.behavior";
    public string CorrelationsUpdatedTopic { get; set; } = "naia.correlations.updated";
    public string ClustersCreatedTopic { get; set; } = "naia.clusters.created";
    public string SuggestionsCreatedTopic { get; set; } = "naia.suggestions.created";
    public string PatternsFeedbackTopic { get; set; } = "naia.patterns.feedback";
    public string PatternsUpdatedTopic { get; set; } = "naia.patterns.updated";
}

/// <summary>
/// Configuration for behavioral aggregation
/// </summary>
public sealed class BehavioralAggregatorOptions
{
    /// <summary>Minimum samples needed before publishing behavior event</summary>
    public int MinSamplesForBehavior { get; set; } = 50;
    
    /// <summary>Window size for behavioral analysis (hours)</summary>
    public int WindowSizeHours { get; set; } = 24;
    
    /// <summary>How often to publish aggregated behavior (seconds)</summary>
    public int PublishIntervalSeconds { get; set; } = 60;
    
    /// <summary>Maximum points to track in memory</summary>
    public int MaxPointsInMemory { get; set; } = 100000;
    
    /// <summary>Redis key prefix for behavioral stats</summary>
    public string RedisKeyPrefix { get; set; } = "naia:behavior:";
    
    /// <summary>TTL for Redis behavioral stats (hours)</summary>
    public int RedisTtlHours { get; set; } = 48;
}

/// <summary>
/// Configuration for correlation processing
/// </summary>
public sealed class CorrelationProcessorOptions
{
    /// <summary>Minimum correlation to consider significant</summary>
    public double MinCorrelationThreshold { get; set; } = 0.60;
    
    /// <summary>Change threshold to trigger publish (e.g., 0.10 = 10% change)</summary>
    public double ChangeThresholdForPublish { get; set; } = 0.10;
    
    /// <summary>Maximum correlation pairs to calculate per batch</summary>
    public int MaxPairsPerBatch { get; set; } = 1000;
    
    /// <summary>Time window for correlation calculation (hours)</summary>
    public int CorrelationWindowHours { get; set; } = 168; // 7 days
    
    /// <summary>Minimum samples required per point for correlation</summary>
    public int MinSamplesForCorrelation { get; set; } = 100;
    
    /// <summary>Redis key prefix for correlation cache</summary>
    public string RedisKeyPrefix { get; set; } = "naia:corr:";
    
    /// <summary>TTL for Redis correlation cache (hours)</summary>
    public int RedisTtlHours { get; set; } = 24;
}

/// <summary>
/// Configuration for cluster detection
/// </summary>
public sealed class ClusterDetectionOptions
{
    /// <summary>Clustering algorithm to use</summary>
    public string Algorithm { get; set; } = "Louvain"; // or "DBSCAN"
    
    /// <summary>Minimum points in a cluster</summary>
    public int MinClusterSize { get; set; } = 3;
    
    /// <summary>Maximum points in a cluster</summary>
    public int MaxClusterSize { get; set; } = 50;
    
    /// <summary>Minimum cluster cohesion (average intra-cluster correlation)</summary>
    public double MinCohesion { get; set; } = 0.50;
    
    /// <summary>DBSCAN epsilon (distance threshold)</summary>
    public double DbscanEpsilon { get; set; } = 0.3;
    
    /// <summary>DBSCAN minimum points</summary>
    public int DbscanMinPoints { get; set; } = 3;
}

/// <summary>
/// Configuration for pattern matching
/// </summary>
public sealed class PatternMatchingOptions
{
    /// <summary>Minimum confidence to create a suggestion</summary>
    public double MinConfidenceForSuggestion { get; set; } = 0.50;
    
    /// <summary>Weight for naming similarity in overall score</summary>
    public double NamingWeight { get; set; } = 0.30;
    
    /// <summary>Weight for correlation similarity in overall score</summary>
    public double CorrelationWeight { get; set; } = 0.40;
    
    /// <summary>Weight for value range similarity in overall score</summary>
    public double RangeWeight { get; set; } = 0.20;
    
    /// <summary>Weight for update rate similarity in overall score</summary>
    public double RateWeight { get; set; } = 0.10;
    
    /// <summary>Maximum suggestions per cluster</summary>
    public int MaxSuggestionsPerCluster { get; set; } = 5;
}

/// <summary>
/// Configuration for pattern learning
/// </summary>
public sealed class PatternLearningOptions
{
    /// <summary>Confidence increase per approval (0-1)</summary>
    public double ConfidenceIncreasePerApproval { get; set; } = 0.05;
    
    /// <summary>Confidence decrease per rejection (0-1)</summary>
    public double ConfidenceDecreasePerRejection { get; set; } = 0.03;
    
    /// <summary>Daily confidence decay rate (0-1)</summary>
    public double ConfidenceDecayPerDay { get; set; } = 0.005;
    
    /// <summary>Minimum confidence floor (patterns never go below this)</summary>
    public double MinConfidenceFloor { get; set; } = 0.30;
    
    /// <summary>Maximum patterns per tenant</summary>
    public int MaxPatternsPerTenant { get; set; } = 1000;
    
    /// <summary>Minimum bound points to learn a new pattern</summary>
    public int MinPointsForPattern { get; set; } = 5;
    
    /// <summary>Initial confidence for new patterns</summary>
    public double InitialPatternConfidence { get; set; } = 0.60;
}
