-- NAIA Performance Indexes
-- Task 3: Additional indexes for hot paths
-- Run on both LOCAL and PRODUCTION PostgreSQL instances

-- Index for correlation cache queries with ABS()
-- Used by: Pattern correlation analysis
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_correlation_cache_corr_abs 
ON correlation_cache (ABS(correlation));

-- Index for case-insensitive tag name searches
-- Used by: Point search API, Coral assistant
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_points_tag_name_lower 
ON points (LOWER(tag_name));

-- Index for searching points by description (text search)
-- Used by: Point search, Pattern matching
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_points_description_trgm 
ON points USING GIN (description gin_trgm_ops);

-- Enable trigram extension if not exists (for text search)
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Index for behavioral clusters by data source (partition queries)
-- Used by: BehavioralAnalysisJob
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_clusters_data_source 
ON behavioral_clusters (data_source_id) 
WHERE status = 'pending';

-- Index for suggestions by created date (recent suggestions queries)
-- Used by: Suggestions API, Dashboard
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_suggestions_created_recent 
ON pattern_suggestions (created_at DESC) 
WHERE status = 'Pending';

-- Index for patterns by match count (popular patterns)
-- Used by: Pattern API, Coral assistant
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_patterns_match_count 
ON equipment_patterns (match_count DESC) 
WHERE is_active = true;

-- Index for current values by update time (stale data detection)
-- Used by: Health monitoring, Data flow status
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_current_values_updated 
ON current_values (updated_at DESC);

-- Composite index for point lookup (most common query pattern)
-- Used by: Everywhere
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_points_enabled_source 
ON points (data_source_id, is_enabled) 
WHERE is_enabled = true;

-- =============================================================================
-- READ-ONLY USER FOR SQL CONSOLE (Task 4: Security)
-- =============================================================================

-- Create read-only role if not exists
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'naia_readonly') THEN
        CREATE ROLE naia_readonly WITH LOGIN PASSWORD 'naia_readonly_secure_2026';
    END IF;
END
$$;

-- Grant SELECT on all tables
GRANT USAGE ON SCHEMA public TO naia_readonly;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO naia_readonly;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO naia_readonly;

-- Explicitly revoke any write permissions
REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON ALL TABLES IN SCHEMA public FROM naia_readonly;

-- =============================================================================
-- AUDIT LOG TABLE FOR SQL CONSOLE
-- =============================================================================

CREATE TABLE IF NOT EXISTS sql_audit_log (
    id BIGSERIAL PRIMARY KEY,
    executed_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    user_name VARCHAR(255),
    ip_address VARCHAR(50),
    query_type VARCHAR(20), -- SELECT, INSERT, UPDATE, DELETE, etc.
    query_text TEXT,
    row_count INTEGER,
    execution_ms INTEGER,
    is_master_mode BOOLEAN DEFAULT FALSE,
    error_message TEXT
);

CREATE INDEX IF NOT EXISTS idx_sql_audit_log_time ON sql_audit_log (executed_at DESC);
CREATE INDEX IF NOT EXISTS idx_sql_audit_log_user ON sql_audit_log (user_name);

-- =============================================================================
-- CORAL CONVERSATION HISTORY TABLE
-- =============================================================================

CREATE TABLE IF NOT EXISTS coral_conversations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id VARCHAR(100) NOT NULL,
    role VARCHAR(20) NOT NULL, -- 'user' or 'assistant'
    content TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    metadata JSONB
);

CREATE INDEX IF NOT EXISTS idx_coral_conv_id ON coral_conversations (conversation_id);
CREATE INDEX IF NOT EXISTS idx_coral_conv_time ON coral_conversations (conversation_id, created_at DESC);

-- Auto-cleanup old conversations (keep 7 days)
-- This can be run by a scheduled job
-- DELETE FROM coral_conversations WHERE created_at < NOW() - INTERVAL '7 days';

COMMENT ON TABLE coral_conversations IS 'Stores Coral AI assistant conversation history for context continuity';

-- =============================================================================
-- DEAD LETTER QUEUE TABLE FOR FAILED QUESTDB WRITES
-- =============================================================================

CREATE TABLE IF NOT EXISTS questdb_dead_letters (
    id BIGSERIAL PRIMARY KEY,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    point_id BIGINT NOT NULL,
    timestamp TIMESTAMP WITH TIME ZONE NOT NULL,
    value DOUBLE PRECISION NOT NULL,
    quality INTEGER,
    batch_id VARCHAR(100),
    error_message TEXT,
    retry_count INTEGER DEFAULT 0,
    last_retry_at TIMESTAMP WITH TIME ZONE,
    status VARCHAR(20) DEFAULT 'pending' -- pending, retrying, failed, recovered
);

CREATE INDEX IF NOT EXISTS idx_questdb_dlq_status ON questdb_dead_letters (status, created_at);

-- =============================================================================
-- BEHAVIORAL STATS STAGING TABLE FOR BATCH INSERTS
-- =============================================================================

CREATE TABLE IF NOT EXISTS behavioral_stats_staging (
    point_id UUID NOT NULL,
    point_id_seq BIGINT NOT NULL,
    point_name VARCHAR(500) NOT NULL,
    sample_count INTEGER NOT NULL,
    window_start TIMESTAMP WITH TIME ZONE NOT NULL,
    window_end TIMESTAMP WITH TIME ZONE NOT NULL,
    mean_value DOUBLE PRECISION NOT NULL,
    std_deviation DOUBLE PRECISION NOT NULL,
    min_value DOUBLE PRECISION NOT NULL,
    max_value DOUBLE PRECISION NOT NULL,
    update_rate_hz DOUBLE PRECISION NOT NULL,
    calculated_at TIMESTAMP WITH TIME ZONE NOT NULL
);

-- Staging table is truncated after each batch, no indexes needed

COMMENT ON TABLE behavioral_stats_staging IS 'Temporary staging table for batch inserts of behavioral statistics';
);

CREATE INDEX IF NOT EXISTS idx_dlq_status ON questdb_dead_letters (status) WHERE status = 'pending';
CREATE INDEX IF NOT EXISTS idx_dlq_created ON questdb_dead_letters (created_at DESC);

COMMENT ON TABLE questdb_dead_letters IS 'Dead letter queue for failed QuestDB writes - allows recovery of lost data';

-- Grant access to DLQ for the API
GRANT INSERT, UPDATE, SELECT ON questdb_dead_letters TO naia;
GRANT USAGE, SELECT ON SEQUENCE questdb_dead_letters_id_seq TO naia;
