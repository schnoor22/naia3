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
    
    /// <summary>Hangfire job scheduling settings</summary>
    public HangfireOptions Hangfire { get; set; } = new();
    
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
    
    /// <summary>Maintenance job settings</summary>
    public MaintenanceOptions? Maintenance { get; set; } = new();
}

/// <summary>
/// Hangfire job scheduling configuration
/// </summary>
public sealed class HangfireOptions
{
    /// <summary>Enable Hangfire dashboard</summary>
    public bool EnableDashboard { get; set; } = true;
    
    /// <summary>Dashboard path (e.g., "/hangfire")</summary>
    public string DashboardPath { get; set; } = "/hangfire";
    
    /// <summary>Worker count for job processing</summary>
    public int WorkerCount { get; set; } = 5;
    
    /// <summary>CRON schedule for behavioral analysis job (default: every 15 minutes)</summary>
    public string BehavioralAnalysisCron { get; set; } = "*/15 * * * *";
    
    /// <summary>CRON schedule for correlation analysis job (default: every 30 minutes)</summary>
    public string CorrelationAnalysisCron { get; set; } = "*/30 * * * *";
    
    /// <summary>CRON schedule for cluster detection job (default: every 30 minutes, offset by 10)</summary>
    public string ClusterDetectionCron { get; set; } = "10/30 * * * *";
    
    /// <summary>CRON schedule for pattern matching job (default: every 30 minutes, offset by 20)</summary>
    public string PatternMatchingCron { get; set; } = "20/30 * * * *";
    
    /// <summary>CRON schedule for pattern learning job (default: hourly)</summary>
    public string PatternLearningCron { get; set; } = "0 * * * *";
    
    /// <summary>CRON schedule for maintenance job (default: daily at 3 AM)</summary>
    public string MaintenanceCron { get; set; } = "0 3 * * *";
}

/// <summary>
/// Maintenance job settings
/// </summary>
public sealed class MaintenanceOptions
{
    /// <summary>Retention period in days for old data</summary>
    public int RetentionDays { get; set; } = 90;
}

/// <summary>
/// Configuration for behavioral aggregation
/// </summary>
public sealed class BehavioralAggregatorOptions
{
    /// <summary>Minimum samples needed before calculating behavior</summary>
    public int MinSamplesForBehavior { get; set; } = 50;
    
    /// <summary>Window size for behavioral analysis (hours)</summary>
    public int WindowHours { get; set; } = 24;
    
    /// <summary>Batch size for processing points</summary>
    public int BatchSize { get; set; } = 100;
    
    /// <summary>TTL for Redis behavioral stats cache (hours)</summary>
    public int CacheTtlHours { get; set; } = 48;
}

/// <summary>
/// Configuration for correlation processing
/// </summary>
public sealed class CorrelationProcessorOptions
{
    /// <summary>Minimum correlation to consider significant</summary>
    public double MinCorrelation { get; set; } = 0.60;
    
    /// <summary>Time window for correlation calculation (hours)</summary>
    public int WindowHours { get; set; } = 168; // 7 days
    
    /// <summary>Minimum samples required per point for correlation</summary>
    public int MinSamples { get; set; } = 100;
    
    /// <summary>TTL for Redis correlation cache (hours)</summary>
    public int CacheTtlHours { get; set; } = 24;
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
