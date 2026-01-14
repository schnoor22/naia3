using Prometheus;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Naia.Infrastructure.Telemetry;

/// <summary>
/// NAIA Prometheus Metrics - Comprehensive observability for the pattern flywheel.
/// 
/// Metrics Categories:
/// 1. Data Ingestion - Points written, latency, errors
/// 2. Pattern Engine - Jobs executed, patterns found, correlations
/// 3. API - Request counts, latencies by endpoint
/// 4. AI/Coral - Conversations, tokens used, response times
/// 5. Storage - QuestDB writes, PostgreSQL queries, Redis hits/misses
/// </summary>
public static class NaiaMetrics
{
    // ==========================================================================
    // ACTIVITY SOURCE - For OpenTelemetry Tracing
    // ==========================================================================
    public static readonly ActivitySource ActivitySource = new("Naia.Api", "3.0.0");
    
    // ==========================================================================
    // DATA INGESTION METRICS
    // ==========================================================================
    
    /// <summary>Total data points written to QuestDB</summary>
    public static readonly Counter DataPointsWritten = Metrics
        .CreateCounter("naia_datapoints_written_total", 
            "Total number of data points written to QuestDB",
            new CounterConfiguration
            {
                LabelNames = new[] { "data_source", "status" }
            });
    
    /// <summary>Data points write latency in seconds</summary>
    public static readonly Histogram DataPointWriteLatency = Metrics
        .CreateHistogram("naia_datapoint_write_duration_seconds",
            "Time taken to write data points to QuestDB",
            new HistogramConfiguration
            {
                LabelNames = new[] { "data_source" },
                Buckets = new[] { .001, .005, .01, .025, .05, .1, .25, .5, 1, 2.5, 5, 10 }
            });
    
    /// <summary>Current write queue depth</summary>
    public static readonly Gauge WriteQueueDepth = Metrics
        .CreateGauge("naia_write_queue_depth",
            "Current number of pending writes in queue",
            new GaugeConfiguration
            {
                LabelNames = new[] { "data_source" }
            });
    
    /// <summary>Dead letter queue size</summary>
    public static readonly Gauge DeadLetterQueueSize = Metrics
        .CreateGauge("naia_dead_letter_queue_size",
            "Number of failed writes in dead letter queue");
    
    // ==========================================================================
    // PATTERN ENGINE METRICS
    // ==========================================================================
    
    /// <summary>Pattern jobs executed</summary>
    public static readonly Counter PatternJobsExecuted = Metrics
        .CreateCounter("naia_pattern_jobs_total",
            "Total pattern engine jobs executed",
            new CounterConfiguration
            {
                LabelNames = new[] { "job_type", "status" }
            });
    
    /// <summary>Pattern job duration</summary>
    public static readonly Histogram PatternJobDuration = Metrics
        .CreateHistogram("naia_pattern_job_duration_seconds",
            "Duration of pattern engine jobs",
            new HistogramConfiguration
            {
                LabelNames = new[] { "job_type" },
                Buckets = new[] { .1, .5, 1, 2.5, 5, 10, 30, 60, 120, 300 }
            });
    
    /// <summary>Correlations discovered</summary>
    public static readonly Counter CorrelationsDiscovered = Metrics
        .CreateCounter("naia_correlations_discovered_total",
            "Total correlations discovered by pattern engine",
            new CounterConfiguration
            {
                LabelNames = new[] { "correlation_type", "strength" }
            });
    
    /// <summary>Suggestions generated</summary>
    public static readonly Counter SuggestionsGenerated = Metrics
        .CreateCounter("naia_suggestions_generated_total",
            "Total pattern suggestions generated",
            new CounterConfiguration
            {
                LabelNames = new[] { "suggestion_type", "status" }
            });
    
    /// <summary>Patterns confirmed vs rejected</summary>
    public static readonly Counter PatternFeedback = Metrics
        .CreateCounter("naia_pattern_feedback_total",
            "User feedback on pattern suggestions",
            new CounterConfiguration
            {
                LabelNames = new[] { "action" } // approved, rejected, edited
            });
    
    /// <summary>Active patterns in the system</summary>
    public static readonly Gauge ActivePatterns = Metrics
        .CreateGauge("naia_active_patterns",
            "Number of active patterns in the system",
            new GaugeConfiguration
            {
                LabelNames = new[] { "pattern_type" }
            });
    
    // ==========================================================================
    // CORAL AI METRICS
    // ==========================================================================
    
    /// <summary>Coral AI conversations</summary>
    public static readonly Counter CoralConversations = Metrics
        .CreateCounter("naia_coral_conversations_total",
            "Total Coral AI conversations",
            new CounterConfiguration
            {
                LabelNames = new[] { "status" }
            });
    
    /// <summary>Coral AI response latency</summary>
    public static readonly Histogram CoralResponseLatency = Metrics
        .CreateHistogram("naia_coral_response_duration_seconds",
            "Coral AI response time",
            new HistogramConfiguration
            {
                LabelNames = new[] { "query_type" },
                Buckets = new[] { .5, 1, 2, 3, 5, 10, 15, 20, 30, 60 }
            });
    
    /// <summary>AI tokens used (estimated)</summary>
    public static readonly Counter AiTokensUsed = Metrics
        .CreateCounter("naia_ai_tokens_used_total",
            "Estimated AI tokens consumed",
            new CounterConfiguration
            {
                LabelNames = new[] { "model", "direction" } // input, output
            });
    
    /// <summary>Knowledge base queries</summary>
    public static readonly Counter KnowledgeBaseQueries = Metrics
        .CreateCounter("naia_knowledge_base_queries_total",
            "Knowledge base lookup queries",
            new CounterConfiguration
            {
                LabelNames = new[] { "query_type", "cache_hit" }
            });
    
    // ==========================================================================
    // STORAGE METRICS
    // ==========================================================================
    
    /// <summary>QuestDB query latency</summary>
    public static readonly Histogram QuestDbQueryLatency = Metrics
        .CreateHistogram("naia_questdb_query_duration_seconds",
            "QuestDB query execution time",
            new HistogramConfiguration
            {
                LabelNames = new[] { "query_type" },
                Buckets = new[] { .001, .005, .01, .025, .05, .1, .25, .5, 1, 2.5, 5 }
            });
    
    /// <summary>PostgreSQL query latency</summary>
    public static readonly Histogram PostgresQueryLatency = Metrics
        .CreateHistogram("naia_postgres_query_duration_seconds",
            "PostgreSQL query execution time",
            new HistogramConfiguration
            {
                LabelNames = new[] { "operation" },
                Buckets = new[] { .001, .005, .01, .025, .05, .1, .25, .5, 1, 2.5, 5 }
            });
    
    /// <summary>Redis cache operations</summary>
    public static readonly Counter RedisCacheOperations = Metrics
        .CreateCounter("naia_redis_operations_total",
            "Redis cache operations",
            new CounterConfiguration
            {
                LabelNames = new[] { "operation", "result" } // get/set, hit/miss
            });
    
    /// <summary>Active database connections</summary>
    public static readonly Gauge ActiveConnections = Metrics
        .CreateGauge("naia_active_connections",
            "Active database connections",
            new GaugeConfiguration
            {
                LabelNames = new[] { "database" } // postgres, questdb, redis
            });
    
    // ==========================================================================
    // API METRICS
    // ==========================================================================
    
    /// <summary>HTTP request duration (built-in from prometheus-net.AspNetCore)</summary>
    // Note: prometheus-net.AspNetCore auto-generates http_request_duration_seconds
    
    /// <summary>Rate limit rejections</summary>
    public static readonly Counter RateLimitRejections = Metrics
        .CreateCounter("naia_rate_limit_rejections_total",
            "Requests rejected due to rate limiting",
            new CounterConfiguration
            {
                LabelNames = new[] { "policy", "endpoint" }
            });
    
    /// <summary>Active SignalR connections</summary>
    public static readonly Gauge SignalRConnections = Metrics
        .CreateGauge("naia_signalr_connections",
            "Active SignalR hub connections",
            new GaugeConfiguration
            {
                LabelNames = new[] { "hub" }
            });
    
    /// <summary>SignalR messages sent</summary>
    public static readonly Counter SignalRMessagesSent = Metrics
        .CreateCounter("naia_signalr_messages_total",
            "SignalR messages broadcast",
            new CounterConfiguration
            {
                LabelNames = new[] { "hub", "event_type" }
            });
    
    // ==========================================================================
    // SYSTEM METRICS
    // ==========================================================================
    
    /// <summary>Background job queue depth (Hangfire)</summary>
    public static readonly Gauge HangfireQueueDepth = Metrics
        .CreateGauge("naia_hangfire_queue_depth",
            "Pending jobs in Hangfire queues",
            new GaugeConfiguration
            {
                LabelNames = new[] { "queue" }
            });
    
    /// <summary>Application uptime</summary>
    public static readonly Gauge ApplicationUptime = Metrics
        .CreateGauge("naia_application_uptime_seconds",
            "Application uptime in seconds");
    
    /// <summary>Last successful data ingestion timestamp</summary>
    public static readonly Gauge LastIngestionTimestamp = Metrics
        .CreateGauge("naia_last_ingestion_timestamp",
            "Unix timestamp of last successful data ingestion",
            new GaugeConfiguration
            {
                LabelNames = new[] { "data_source" }
            });
    
    // ==========================================================================
    // HELPER METHODS
    // ==========================================================================
    
    /// <summary>
    /// Track operation duration with automatic histogram recording.
    /// Usage: using (NaiaMetrics.TrackDuration(histogram, labels)) { ... }
    /// </summary>
    public static Prometheus.ITimer TrackDuration(Histogram histogram, params string[] labelValues)
    {
        return histogram.WithLabels(labelValues).NewTimer();
    }
    
    /// <summary>
    /// Create a new trace span for OpenTelemetry.
    /// Usage: using var span = NaiaMetrics.StartSpan("operation.name");
    /// </summary>
    public static Activity? StartSpan(string operationName, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(operationName, kind);
    }
    
    /// <summary>
    /// Record a counter increment with labels.
    /// </summary>
    public static void IncrementCounter(Counter counter, double value, params string[] labelValues)
    {
        counter.WithLabels(labelValues).Inc(value);
    }
}
