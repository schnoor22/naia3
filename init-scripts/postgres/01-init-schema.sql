-- NAIA v3 Initial Schema
-- PostgreSQL metadata database

-- ===========================================================================
-- DATA SOURCES
-- ===========================================================================
CREATE TABLE IF NOT EXISTS data_sources (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    source_type VARCHAR(50) NOT NULL, -- 'OPC-UA', 'PI', 'Modbus', 'MQTT', etc.
    connection_string TEXT,
    configuration JSONB,
    is_enabled BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_data_source_name UNIQUE (name)
);

CREATE INDEX idx_data_sources_type ON data_sources(source_type);
CREATE INDEX idx_data_sources_enabled ON data_sources(is_enabled);

-- ===========================================================================
-- POINTS
-- ===========================================================================
-- Sequence for efficient time-series storage in QuestDB
CREATE SEQUENCE IF NOT EXISTS point_sequence_id_seq START 1;

CREATE TABLE IF NOT EXISTS points (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    -- IMPORTANT: Column name matches EF Core mapping in NaiaDbContext.cs
    point_sequence_id BIGINT DEFAULT nextval('point_sequence_id_seq') UNIQUE NOT NULL,
    name VARCHAR(500) NOT NULL,
    data_source_id UUID REFERENCES data_sources(id) ON DELETE CASCADE,
    address VARCHAR(500), -- OPC NodeId, PI tag, Modbus register, etc.
    description TEXT,
    engineering_units VARCHAR(50),
    point_type VARCHAR(50) DEFAULT 'Input', -- 'Input', 'Calculated', 'Manual'
    data_type VARCHAR(50) DEFAULT 'Float64', -- 'Float64', 'Int32', 'Boolean', 'String'
    
    -- Compression settings (SDT - Swinging Door Trending)
    compression_enabled BOOLEAN DEFAULT true,
    compression_deviation DOUBLE PRECISION DEFAULT 0.5, -- Percentage
    compression_min_interval INT DEFAULT 0, -- Seconds
    compression_max_interval INT DEFAULT 600, -- Seconds (10 min)
    
    -- Quality & metadata
    is_enabled BOOLEAN DEFAULT true,
    scan_rate_ms INT DEFAULT 1000,
    metadata JSONB,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_point_name UNIQUE (name)
);

CREATE INDEX idx_points_data_source ON points(data_source_id);
CREATE INDEX idx_points_enabled ON points(is_enabled);
CREATE INDEX idx_points_sequence_id ON points(point_sequence_id);
CREATE INDEX idx_points_address ON points(address);

-- ===========================================================================
-- CURRENT VALUES (Snapshot Cache)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS current_values (
    point_id UUID PRIMARY KEY REFERENCES points(id) ON DELETE CASCADE,
    timestamp TIMESTAMP WITH TIME ZONE NOT NULL,
    value_float DOUBLE PRECISION,
    value_int BIGINT,
    value_string TEXT,
    quality SMALLINT DEFAULT 0, -- 0=Good, 1=Uncertain, 2=Bad
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_current_values_timestamp ON current_values(timestamp);
CREATE INDEX idx_current_values_quality ON current_values(quality);

-- ===========================================================================
-- IMPORT SESSIONS (for data replay and batch imports)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS import_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    source_file TEXT,
    import_type VARCHAR(50), -- 'CSV', 'PI_Backfill', 'Manual'
    status VARCHAR(50) DEFAULT 'Pending', -- 'Pending', 'InProgress', 'Completed', 'Failed'
    
    total_records BIGINT DEFAULT 0,
    processed_records BIGINT DEFAULT 0,
    failed_records BIGINT DEFAULT 0,
    
    started_at TIMESTAMP WITH TIME ZONE,
    completed_at TIMESTAMP WITH TIME ZONE,
    error_message TEXT,
    
    created_by VARCHAR(255),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    metadata JSONB
);

CREATE INDEX idx_import_sessions_status ON import_sessions(status);
CREATE INDEX idx_import_sessions_type ON import_sessions(import_type);
CREATE INDEX idx_import_sessions_created ON import_sessions(created_at DESC);

-- ===========================================================================
-- SYSTEM CONFIGURATION
-- ===========================================================================
CREATE TABLE IF NOT EXISTS system_config (
    key VARCHAR(255) PRIMARY KEY,
    value TEXT,
    description TEXT,
    data_type VARCHAR(50) DEFAULT 'String', -- 'String', 'Int', 'Float', 'Boolean', 'JSON'
    category VARCHAR(100),
    is_read_only BOOLEAN DEFAULT false,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Insert default configuration
INSERT INTO system_config (key, value, description, category) VALUES
('system.version', '3.0.0', 'NAIA system version', 'System'),
('historian.max_points', '10000000', 'Maximum number of points supported', 'Historian'),
('ingestion.batch_size', '10000', 'Default batch size for ingestion', 'Ingestion'),
('compression.default_deviation', '0.5', 'Default compression deviation (%)', 'Compression')
ON CONFLICT (key) DO NOTHING;

-- ===========================================================================
-- AUDIT LOG
-- ===========================================================================
CREATE TABLE IF NOT EXISTS audit_log (
    id BIGSERIAL PRIMARY KEY,
    timestamp TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    entity_type VARCHAR(100),
    entity_id UUID,
    action VARCHAR(50), -- 'Created', 'Updated', 'Deleted'
    user_id VARCHAR(255),
    changes JSONB,
    ip_address INET
);

CREATE INDEX idx_audit_log_timestamp ON audit_log(timestamp DESC);
CREATE INDEX idx_audit_log_entity ON audit_log(entity_type, entity_id);
CREATE INDEX idx_audit_log_user ON audit_log(user_id);

-- ===========================================================================
-- FUNCTIONS & TRIGGERS
-- ===========================================================================

-- Update timestamp trigger function
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Apply triggers
CREATE TRIGGER update_data_sources_updated_at BEFORE UPDATE ON data_sources
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_points_updated_at BEFORE UPDATE ON points
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_current_values_updated_at BEFORE UPDATE ON current_values
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

COMMENT ON DATABASE naia IS 'NAIA v3 - Industrial AI Data Platform';
