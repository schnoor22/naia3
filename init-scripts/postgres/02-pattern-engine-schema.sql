-- NAIA v3 Pattern Engine Schema
-- Pattern Flywheel tables for learning and suggestions

-- ===========================================================================
-- PATTERNS (The knowledge base - learned equipment templates)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS patterns (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    category VARCHAR(100) NOT NULL, -- 'Pump', 'Compressor', 'HeatExchanger', 'Motor', 'Tank', 'Valve', etc.
    description TEXT,
    
    -- Learning metrics
    confidence DOUBLE PRECISION DEFAULT 0.60, -- 0-1 scale, updated by feedback
    example_count INT DEFAULT 0, -- How many times this pattern was approved
    rejection_count INT DEFAULT 0, -- How many times rejected
    
    -- Origin
    is_system_pattern BOOLEAN DEFAULT false, -- True for seeded patterns, false for learned
    source VARCHAR(50) DEFAULT 'system', -- 'system', 'learned', 'imported'
    
    -- Timestamps
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    last_matched_at TIMESTAMP WITH TIME ZONE,
    
    CONSTRAINT uq_pattern_name UNIQUE (name)
);

CREATE INDEX idx_patterns_category ON patterns(category);
CREATE INDEX idx_patterns_confidence ON patterns(confidence DESC);
CREATE INDEX idx_patterns_is_system ON patterns(is_system_pattern);

-- ===========================================================================
-- PATTERN ROLES (Expected points within a pattern)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS pattern_roles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pattern_id UUID NOT NULL REFERENCES patterns(id) ON DELETE CASCADE,
    
    name VARCHAR(100) NOT NULL, -- 'Discharge Pressure', 'Suction Temperature', 'Motor Current', etc.
    description TEXT,
    
    -- Naming patterns for matching (regex-like patterns)
    naming_patterns TEXT[], -- Array: {'.*DIS.*PRESS.*', '.*PD.*', '.*DISCH.*P.*'}
    
    -- Expected value ranges for validation
    expected_min DOUBLE PRECISION,
    expected_max DOUBLE PRECISION,
    expected_units VARCHAR(50),
    
    -- Expected update rate for matching
    typical_min DOUBLE PRECISION,
    typical_max DOUBLE PRECISION,
    typical_unit VARCHAR(50),
    typical_update_rate_ms DOUBLE PRECISION,
    
    -- Role importance
    is_required BOOLEAN DEFAULT true,
    weight DOUBLE PRECISION DEFAULT 1.0, -- Importance weight for matching
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_pattern_role_name UNIQUE (pattern_id, name)
);

CREATE INDEX idx_pattern_roles_pattern ON pattern_roles(pattern_id);

-- ===========================================================================
-- BEHAVIORAL CLUSTERS (Detected point groupings)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS behavioral_clusters (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    
    -- Source information
    source_type VARCHAR(50) NOT NULL, -- 'Continuous', 'Import', 'Discovery', 'Manual'
    source_id VARCHAR(255), -- Import session ID, discovery ID, etc.
    data_source_id UUID REFERENCES data_sources(id),
    
    -- Cluster members
    point_ids UUID[] NOT NULL,
    point_names TEXT[] NOT NULL,
    point_count INT NOT NULL,
    
    -- Cluster metrics
    average_correlation DOUBLE PRECISION,
    cohesion_score DOUBLE PRECISION,
    naming_pattern VARCHAR(500),
    common_prefix VARCHAR(255),
    
    -- Status
    status VARCHAR(50) DEFAULT 'pending', -- 'pending', 'matched', 'bound', 'dismissed'
    matched_pattern_id UUID REFERENCES patterns(id),
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_clusters_status ON behavioral_clusters(status);
CREATE INDEX idx_clusters_source ON behavioral_clusters(source_type, source_id);
CREATE INDEX idx_clusters_datasource ON behavioral_clusters(data_source_id);
CREATE INDEX idx_clusters_created ON behavioral_clusters(created_at DESC);

-- ===========================================================================
-- PATTERN SUGGESTIONS (AI-generated match proposals)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS pattern_suggestions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    cluster_id UUID NOT NULL REFERENCES behavioral_clusters(id) ON DELETE CASCADE,
    pattern_id UUID NOT NULL REFERENCES patterns(id) ON DELETE CASCADE,
    
    -- Confidence scores
    overall_confidence DOUBLE PRECISION NOT NULL,
    naming_score DOUBLE PRECISION NOT NULL,
    correlation_score DOUBLE PRECISION NOT NULL,
    range_score DOUBLE PRECISION NOT NULL,
    rate_score DOUBLE PRECISION NOT NULL,
    
    -- Explanation
    reason TEXT NOT NULL, -- Human-readable match explanation
    
    -- Status
    status VARCHAR(50) DEFAULT 'pending', -- 'pending', 'approved', 'rejected', 'deferred', 'expired'
    reviewed_by VARCHAR(255),
    reviewed_at TIMESTAMP WITH TIME ZONE,
    rejection_reason TEXT,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP WITH TIME ZONE DEFAULT (CURRENT_TIMESTAMP + INTERVAL '30 days'),
    
    CONSTRAINT uq_suggestion_cluster_pattern UNIQUE (cluster_id, pattern_id)
);

CREATE INDEX idx_suggestions_status ON pattern_suggestions(status);
CREATE INDEX idx_suggestions_cluster ON pattern_suggestions(cluster_id);
CREATE INDEX idx_suggestions_pattern ON pattern_suggestions(pattern_id);
CREATE INDEX idx_suggestions_confidence ON pattern_suggestions(overall_confidence DESC);
CREATE INDEX idx_suggestions_created ON pattern_suggestions(created_at DESC);

-- ===========================================================================
-- PATTERN FEEDBACK LOG (Audit trail of learning)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS pattern_feedback_log (
    id BIGSERIAL PRIMARY KEY,
    suggestion_id UUID NOT NULL REFERENCES pattern_suggestions(id),
    pattern_id UUID NOT NULL REFERENCES patterns(id),
    cluster_id UUID NOT NULL REFERENCES behavioral_clusters(id),
    
    action VARCHAR(50) NOT NULL, -- 'approved', 'rejected', 'deferred'
    user_id VARCHAR(255),
    rejection_reason TEXT,
    
    -- Snapshot of confidence at time of action
    confidence_before DOUBLE PRECISION NOT NULL,
    confidence_after DOUBLE PRECISION,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_feedback_pattern ON pattern_feedback_log(pattern_id);
CREATE INDEX idx_feedback_action ON pattern_feedback_log(action);
CREATE INDEX idx_feedback_created ON pattern_feedback_log(created_at DESC);

-- ===========================================================================
-- POINT PATTERN BINDINGS (Links between points and patterns)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS point_pattern_bindings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    point_id UUID NOT NULL REFERENCES points(id) ON DELETE CASCADE,
    pattern_id UUID NOT NULL REFERENCES patterns(id) ON DELETE CASCADE,
    role_id UUID REFERENCES pattern_roles(id),
    cluster_id UUID REFERENCES behavioral_clusters(id),
    
    -- Binding metadata
    confidence_at_binding DOUBLE PRECISION,
    bound_by VARCHAR(255), -- User who approved
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_point_pattern_binding UNIQUE (point_id, pattern_id)
);

CREATE INDEX idx_bindings_point ON point_pattern_bindings(point_id);
CREATE INDEX idx_bindings_pattern ON point_pattern_bindings(pattern_id);
CREATE INDEX idx_bindings_cluster ON point_pattern_bindings(cluster_id);

-- ===========================================================================
-- CORRELATION CACHE (Pairwise correlations for cluster detection)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS correlation_cache (
    point_id_1 UUID NOT NULL,
    point_id_2 UUID NOT NULL,
    correlation DOUBLE PRECISION NOT NULL,
    sample_count INT NOT NULL,
    window_start TIMESTAMP WITH TIME ZONE NOT NULL,
    window_end TIMESTAMP WITH TIME ZONE NOT NULL,
    calculated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    PRIMARY KEY (point_id_1, point_id_2)
);

CREATE INDEX idx_correlation_point1 ON correlation_cache(point_id_1);
CREATE INDEX idx_correlation_point2 ON correlation_cache(point_id_2);
CREATE INDEX idx_correlation_strength ON correlation_cache(correlation DESC);

-- ===========================================================================
-- BEHAVIORAL STATS (Point behavioral fingerprints for pattern analysis)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS behavioral_stats (
    point_id UUID PRIMARY KEY REFERENCES points(id) ON DELETE CASCADE,
    point_id_seq INT NOT NULL,
    point_name VARCHAR(500) NOT NULL,
    
    -- Analysis window
    sample_count INT NOT NULL,
    window_start TIMESTAMP WITH TIME ZONE NOT NULL,
    window_end TIMESTAMP WITH TIME ZONE NOT NULL,
    
    -- Statistical measures
    mean_value DOUBLE PRECISION NOT NULL,
    std_deviation DOUBLE PRECISION NOT NULL,
    min_value DOUBLE PRECISION NOT NULL,
    max_value DOUBLE PRECISION NOT NULL,
    update_rate_hz DOUBLE PRECISION NOT NULL,
    
    calculated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_behavioral_stats_seq ON behavioral_stats(point_id_seq);
CREATE INDEX idx_behavioral_stats_calculated ON behavioral_stats(calculated_at DESC);

-- Add is_active column to behavioral_clusters if not exists
ALTER TABLE behavioral_clusters ADD COLUMN IF NOT EXISTS is_active BOOLEAN DEFAULT true;
ALTER TABLE behavioral_clusters ADD COLUMN IF NOT EXISTS cohesion DOUBLE PRECISION;
ALTER TABLE behavioral_clusters ADD COLUMN IF NOT EXISTS detected_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

-- Make point_ids column accept just UUIDs without requiring point_names
ALTER TABLE behavioral_clusters ALTER COLUMN point_names DROP NOT NULL;
ALTER TABLE behavioral_clusters ALTER COLUMN point_count DROP NOT NULL;
ALTER TABLE behavioral_clusters ALTER COLUMN source_type DROP NOT NULL;

-- Add pattern is_active if not exists
ALTER TABLE patterns ADD COLUMN IF NOT EXISTS is_active BOOLEAN DEFAULT true;

CREATE INDEX IF NOT EXISTS idx_patterns_active ON patterns(is_active) WHERE is_active = true;
CREATE INDEX IF NOT EXISTS idx_clusters_active ON behavioral_clusters(is_active) WHERE is_active = true;

-- Add bound_at to point_pattern_bindings
ALTER TABLE point_pattern_bindings ADD COLUMN IF NOT EXISTS bound_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

-- ===========================================================================
-- TRIGGERS
-- ===========================================================================

-- Update timestamps
CREATE TRIGGER update_patterns_updated_at BEFORE UPDATE ON patterns
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_clusters_updated_at BEFORE UPDATE ON behavioral_clusters
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- ===========================================================================
-- SEED INITIAL PATTERN LIBRARY
-- Industrial equipment patterns that NAIA knows from day 1
-- ===========================================================================

-- Centrifugal Pump Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('11111111-0001-0001-0001-000000000001', 'Centrifugal Pump', 'Pump', 
 'Standard centrifugal pump with discharge/suction pressures, flow, and motor current', 
 0.85, true, 'system')
ON CONFLICT (name) DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required) VALUES
('11111111-0001-0001-0001-000000000001', 'Discharge Pressure', 'Pump discharge pressure', 
 ARRAY['.*DIS.*PRESS.*', '.*PD.*', '.*DISCH.*P.*', '.*OUT.*PRESS.*'], 0, 500, 'PSI', true),
('11111111-0001-0001-0001-000000000001', 'Suction Pressure', 'Pump suction/inlet pressure', 
 ARRAY['.*SUC.*PRESS.*', '.*PS.*', '.*INLET.*P.*', '.*IN.*PRESS.*'], -15, 100, 'PSI', true),
('11111111-0001-0001-0001-000000000001', 'Flow Rate', 'Pump flow rate', 
 ARRAY['.*FLOW.*', '.*GPM.*', '.*RATE.*', '.*FT.*'], 0, 10000, 'GPM', true),
('11111111-0001-0001-0001-000000000001', 'Motor Current', 'Pump motor amperage', 
 ARRAY['.*AMP.*', '.*CURR.*', '.*MOTOR.*I.*', '.*AMPS.*'], 0, 1000, 'A', false),
('11111111-0001-0001-0001-000000000001', 'Discharge Temperature', 'Pump discharge temperature', 
 ARRAY['.*DIS.*TEMP.*', '.*TD.*', '.*OUT.*TEMP.*'], -50, 500, '°F', false)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Reciprocating Compressor Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('11111111-0001-0001-0001-000000000002', 'Reciprocating Compressor', 'Compressor', 
 'Reciprocating compressor with stage pressures, temperatures, and vibration', 
 0.85, true, 'system')
ON CONFLICT (name) DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required) VALUES
('11111111-0001-0001-0001-000000000002', 'Suction Pressure', 'Compressor suction pressure', 
 ARRAY['.*SUC.*PRESS.*', '.*PS.*', '.*1ST.*STG.*P.*', '.*INLET.*'], 0, 500, 'PSI', true),
('11111111-0001-0001-0001-000000000002', 'Discharge Pressure', 'Compressor discharge pressure', 
 ARRAY['.*DIS.*PRESS.*', '.*PD.*', '.*FINAL.*PRESS.*', '.*OUT.*'], 0, 5000, 'PSI', true),
('11111111-0001-0001-0001-000000000002', 'Suction Temperature', 'Compressor suction temperature', 
 ARRAY['.*SUC.*TEMP.*', '.*TS.*', '.*INLET.*T.*'], -100, 300, '°F', true),
('11111111-0001-0001-0001-000000000002', 'Discharge Temperature', 'Compressor discharge temperature', 
 ARRAY['.*DIS.*TEMP.*', '.*TD.*', '.*OUT.*T.*'], 0, 500, '°F', true),
('11111111-0001-0001-0001-000000000002', 'Vibration', 'Compressor vibration', 
 ARRAY['.*VIB.*', '.*VIBRATION.*', '.*VEL.*'], 0, 2, 'in/s', false),
('11111111-0001-0001-0001-000000000002', 'Rod Load', 'Compressor rod load', 
 ARRAY['.*ROD.*LOAD.*', '.*LOAD.*', '.*RL.*'], 0, 100000, 'lbf', false)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Heat Exchanger Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('11111111-0001-0001-0001-000000000003', 'Shell & Tube Heat Exchanger', 'HeatExchanger', 
 'Shell and tube heat exchanger with inlet/outlet temperatures for both sides', 
 0.80, true, 'system')
ON CONFLICT (name) DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required) VALUES
('11111111-0001-0001-0001-000000000003', 'Shell Inlet Temperature', 'Hot side inlet temperature', 
 ARRAY['.*SHELL.*IN.*TEMP.*', '.*HOT.*IN.*', '.*TI.*SHELL.*'], -50, 1000, '°F', true),
('11111111-0001-0001-0001-000000000003', 'Shell Outlet Temperature', 'Hot side outlet temperature', 
 ARRAY['.*SHELL.*OUT.*TEMP.*', '.*HOT.*OUT.*', '.*TO.*SHELL.*'], -50, 1000, '°F', true),
('11111111-0001-0001-0001-000000000003', 'Tube Inlet Temperature', 'Cold side inlet temperature', 
 ARRAY['.*TUBE.*IN.*TEMP.*', '.*COLD.*IN.*', '.*TI.*TUBE.*'], -50, 1000, '°F', true),
('11111111-0001-0001-0001-000000000003', 'Tube Outlet Temperature', 'Cold side outlet temperature', 
 ARRAY['.*TUBE.*OUT.*TEMP.*', '.*COLD.*OUT.*', '.*TO.*TUBE.*'], -50, 1000, '°F', true),
('11111111-0001-0001-0001-000000000003', 'Differential Pressure', 'Pressure drop across exchanger', 
 ARRAY['.*DP.*', '.*DIFF.*PRESS.*', '.*DELTA.*P.*'], 0, 50, 'PSI', false)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Electric Motor Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('11111111-0001-0001-0001-000000000004', 'Electric Motor', 'Motor', 
 'AC induction motor with current, voltage, temperature, and vibration monitoring', 
 0.85, true, 'system')
ON CONFLICT (name) DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required) VALUES
('11111111-0001-0001-0001-000000000004', 'Motor Current', 'Motor amperage draw', 
 ARRAY['.*AMP.*', '.*CURR.*', '.*MOTOR.*I.*', '.*IA.*', '.*IB.*', '.*IC.*'], 0, 2000, 'A', true),
('11111111-0001-0001-0001-000000000004', 'Motor Voltage', 'Motor supply voltage', 
 ARRAY['.*VOLT.*', '.*V.*MOTOR.*', '.*VA.*', '.*VB.*', '.*VC.*'], 0, 15000, 'V', false),
('11111111-0001-0001-0001-000000000004', 'Winding Temperature', 'Motor winding temperature', 
 ARRAY['.*WIND.*TEMP.*', '.*STATOR.*T.*', '.*TW.*'], 0, 400, '°F', true),
('11111111-0001-0001-0001-000000000004', 'Bearing Temperature DE', 'Drive end bearing temperature', 
 ARRAY['.*DE.*TEMP.*', '.*DRIVE.*BEAR.*', '.*TB.*DE.*'], 0, 250, '°F', false),
('11111111-0001-0001-0001-000000000004', 'Bearing Temperature NDE', 'Non-drive end bearing temperature', 
 ARRAY['.*NDE.*TEMP.*', '.*NON.*DRIVE.*', '.*TB.*NDE.*'], 0, 250, '°F', false),
('11111111-0001-0001-0001-000000000004', 'Vibration', 'Motor vibration level', 
 ARRAY['.*VIB.*', '.*VIBRATION.*'], 0, 1, 'in/s', false)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Storage Tank Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('11111111-0001-0001-0001-000000000005', 'Storage Tank', 'Tank', 
 'Atmospheric or pressurized storage tank with level, temperature, and pressure', 
 0.80, true, 'system')
ON CONFLICT (name) DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required) VALUES
('11111111-0001-0001-0001-000000000005', 'Level', 'Tank level measurement', 
 ARRAY['.*LEVEL.*', '.*LVL.*', '.*LT.*', '.*LI.*'], 0, 100, '%', true),
('11111111-0001-0001-0001-000000000005', 'Temperature', 'Tank contents temperature', 
 ARRAY['.*TANK.*TEMP.*', '.*TT.*', '.*TI.*'], -50, 500, '°F', false),
('11111111-0001-0001-0001-000000000005', 'Pressure', 'Tank pressure (if pressurized)', 
 ARRAY['.*TANK.*PRESS.*', '.*PT.*', '.*PI.*'], 0, 500, 'PSI', false),
('11111111-0001-0001-0001-000000000005', 'Inlet Flow', 'Flow into tank', 
 ARRAY['.*IN.*FLOW.*', '.*INLET.*F.*', '.*FI.*IN.*'], 0, 10000, 'GPM', false),
('11111111-0001-0001-0001-000000000005', 'Outlet Flow', 'Flow out of tank', 
 ARRAY['.*OUT.*FLOW.*', '.*OUTLET.*F.*', '.*FI.*OUT.*'], 0, 10000, 'GPM', false)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Control Valve Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('11111111-0001-0001-0001-000000000006', 'Control Valve', 'Valve', 
 'Modulating control valve with position, upstream/downstream pressures', 
 0.80, true, 'system')
ON CONFLICT (name) DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required) VALUES
('11111111-0001-0001-0001-000000000006', 'Position', 'Valve position/opening', 
 ARRAY['.*POS.*', '.*POSITION.*', '.*OPEN.*', '.*%.*OPEN.*', '.*ZT.*'], 0, 100, '%', true),
('11111111-0001-0001-0001-000000000006', 'Setpoint', 'Valve position setpoint', 
 ARRAY['.*SP.*', '.*SETPOINT.*', '.*SET.*'], 0, 100, '%', false),
('11111111-0001-0001-0001-000000000006', 'Upstream Pressure', 'Pressure before valve', 
 ARRAY['.*UP.*PRESS.*', '.*INLET.*P.*', '.*P1.*'], 0, 1000, 'PSI', false),
('11111111-0001-0001-0001-000000000006', 'Downstream Pressure', 'Pressure after valve', 
 ARRAY['.*DOWN.*PRESS.*', '.*OUTLET.*P.*', '.*P2.*'], 0, 1000, 'PSI', false),
('11111111-0001-0001-0001-000000000006', 'Differential Pressure', 'Pressure drop across valve', 
 ARRAY['.*DP.*', '.*DIFF.*P.*', '.*DELTA.*'], 0, 500, 'PSI', false)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Cooling Tower Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('11111111-0001-0001-0001-000000000007', 'Cooling Tower', 'CoolingSystem', 
 'Evaporative cooling tower with basin temp, approach, fan status', 
 0.75, true, 'system')
ON CONFLICT (name) DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required) VALUES
('11111111-0001-0001-0001-000000000007', 'Supply Temperature', 'Cold water supply temperature', 
 ARRAY['.*SUPPLY.*TEMP.*', '.*CWS.*T.*', '.*COLD.*'], 32, 120, '°F', true),
('11111111-0001-0001-0001-000000000007', 'Return Temperature', 'Hot water return temperature', 
 ARRAY['.*RETURN.*TEMP.*', '.*CWR.*T.*', '.*HOT.*RETURN.*'], 32, 150, '°F', true),
('11111111-0001-0001-0001-000000000007', 'Basin Level', 'Cooling tower basin level', 
 ARRAY['.*BASIN.*LVL.*', '.*BASIN.*LEVEL.*', '.*SUMP.*'], 0, 100, '%', false),
('11111111-0001-0001-0001-000000000007', 'Fan Speed', 'Cooling tower fan speed', 
 ARRAY['.*FAN.*SPD.*', '.*FAN.*SPEED.*', '.*RPM.*'], 0, 100, '%', false),
('11111111-0001-0001-0001-000000000007', 'Ambient Temperature', 'Outside air temperature', 
 ARRAY['.*AMB.*TEMP.*', '.*OAT.*', '.*OUTSIDE.*'], -40, 130, '°F', false)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Gas Turbine Pattern (Complex example)
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('11111111-0001-0001-0001-000000000008', 'Gas Turbine', 'Turbine', 
 'Industrial gas turbine with exhaust temps, compressor pressures, vibration', 
 0.70, true, 'system')
ON CONFLICT (name) DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required) VALUES
('11111111-0001-0001-0001-000000000008', 'Exhaust Temperature', 'Turbine exhaust temperature', 
 ARRAY['.*EXH.*TEMP.*', '.*T4.*', '.*EGT.*'], 500, 1500, '°F', true),
('11111111-0001-0001-0001-000000000008', 'Compressor Discharge Pressure', 'CDP', 
 ARRAY['.*CDP.*', '.*COMP.*DIS.*P.*', '.*P3.*'], 0, 500, 'PSI', true),
('11111111-0001-0001-0001-000000000008', 'Fuel Flow', 'Fuel gas flow rate', 
 ARRAY['.*FUEL.*FLOW.*', '.*GAS.*FLOW.*', '.*FF.*'], 0, 100000, 'SCFH', true),
('11111111-0001-0001-0001-000000000008', 'Generator Output', 'Electrical power output', 
 ARRAY['.*MW.*', '.*POWER.*', '.*GEN.*OUT.*', '.*LOAD.*'], 0, 500, 'MW', true),
('11111111-0001-0001-0001-000000000008', 'Vibration', 'Turbine bearing vibration', 
 ARRAY['.*VIB.*', '.*VIBRATION.*'], 0, 1, 'in/s', false),
('11111111-0001-0001-0001-000000000008', 'Speed', 'Turbine shaft speed', 
 ARRAY['.*SPEED.*', '.*RPM.*', '.*N2.*'], 0, 15000, 'RPM', false)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- ===========================================================================
-- COMMENTS
-- ===========================================================================
COMMENT ON TABLE patterns IS 'Equipment pattern templates - NAIA''s learned knowledge base';
COMMENT ON TABLE pattern_roles IS 'Expected measurement points within each pattern';
COMMENT ON TABLE behavioral_clusters IS 'Detected groups of correlated points';
COMMENT ON TABLE pattern_suggestions IS 'AI-generated pattern match proposals awaiting review';
COMMENT ON TABLE pattern_feedback_log IS 'Audit trail of human feedback for learning';
COMMENT ON TABLE point_pattern_bindings IS 'Links between points and their assigned patterns';
COMMENT ON TABLE correlation_cache IS 'Cached pairwise correlations for cluster detection';
