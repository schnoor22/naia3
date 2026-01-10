-- ============================================================================
-- NAIA v3 Pattern Engine Schema
-- Pattern definitions, roles, suggestions, feedback, and bindings
-- ============================================================================

-- Patterns table: learned equipment/process patterns
CREATE TABLE IF NOT EXISTS patterns (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    description TEXT,
    confidence DOUBLE PRECISION NOT NULL DEFAULT 0.6,
    is_active BOOLEAN NOT NULL DEFAULT true,
    is_system BOOLEAN NOT NULL DEFAULT false,
    tenant_id UUID, -- NULL for global patterns
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by VARCHAR(255),
    CONSTRAINT chk_confidence CHECK (confidence >= 0 AND confidence <= 1)
);

-- Pattern roles: the expected points within a pattern
CREATE TABLE IF NOT EXISTS pattern_roles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pattern_id UUID NOT NULL REFERENCES patterns(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    naming_patterns JSONB, -- Array of regex patterns for name matching
    typical_unit VARCHAR(50),
    typical_min DOUBLE PRECISION,
    typical_max DOUBLE PRECISION,
    typical_update_rate_ms DOUBLE PRECISION,
    is_required BOOLEAN NOT NULL DEFAULT false,
    sort_order INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_pattern_role_name UNIQUE (pattern_id, name)
);

-- Pattern suggestions: proposed matches awaiting user feedback
CREATE TABLE IF NOT EXISTS pattern_suggestions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    cluster_id UUID NOT NULL,
    pattern_id UUID NOT NULL REFERENCES patterns(id) ON DELETE CASCADE,
    overall_confidence DOUBLE PRECISION NOT NULL,
    naming_score DOUBLE PRECISION,
    correlation_score DOUBLE PRECISION,
    range_score DOUBLE PRECISION,
    rate_score DOUBLE PRECISION,
    matched_point_ids JSONB, -- Array of point UUIDs
    role_assignments JSONB, -- Map of pointId -> roleId
    evidence JSONB, -- Array of evidence strings
    status VARCHAR(50) NOT NULL DEFAULT 'pending', -- pending, applied, rejected, modified, expired
    rejection_reason TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    applied_at TIMESTAMPTZ,
    expires_at TIMESTAMPTZ
);

-- Pattern feedback log: history of user feedback for learning
CREATE TABLE IF NOT EXISTS pattern_feedback_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    suggestion_id UUID NOT NULL REFERENCES pattern_suggestions(id),
    pattern_id UUID NOT NULL REFERENCES patterns(id),
    action VARCHAR(50) NOT NULL, -- approved, rejected, modified
    user_id VARCHAR(255),
    rejection_reason TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Pattern modification log: tracks user corrections for learning
CREATE TABLE IF NOT EXISTS pattern_modification_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pattern_id UUID NOT NULL REFERENCES patterns(id),
    modifications JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Point-pattern bindings: links points to patterns and roles
CREATE TABLE IF NOT EXISTS point_pattern_bindings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    point_id UUID NOT NULL REFERENCES points(id) ON DELETE CASCADE,
    pattern_id UUID NOT NULL REFERENCES patterns(id) ON DELETE CASCADE,
    role_id UUID REFERENCES pattern_roles(id) ON DELETE SET NULL,
    created_by VARCHAR(255),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ,
    CONSTRAINT uq_point_pattern UNIQUE (point_id, pattern_id)
);

-- Behavioral clusters: detected point groupings
CREATE TABLE IF NOT EXISTS behavioral_clusters (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    member_point_ids JSONB NOT NULL, -- Array of point UUIDs
    average_cohesion DOUBLE PRECISION NOT NULL,
    min_correlation DOUBLE PRECISION,
    max_correlation DOUBLE PRECISION,
    algorithm VARCHAR(50) NOT NULL,
    detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ
);

-- Correlation cache: stores calculated correlations
CREATE TABLE IF NOT EXISTS correlation_cache (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    point_id_1 UUID NOT NULL REFERENCES points(id) ON DELETE CASCADE,
    point_id_2 UUID NOT NULL REFERENCES points(id) ON DELETE CASCADE,
    correlation DOUBLE PRECISION NOT NULL,
    p_value DOUBLE PRECISION,
    sample_count INT NOT NULL,
    window_start TIMESTAMPTZ NOT NULL,
    window_end TIMESTAMPTZ NOT NULL,
    lag_ms DOUBLE PRECISION,
    is_leading BOOLEAN,
    calculated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_correlation_pair UNIQUE (point_id_1, point_id_2),
    CONSTRAINT chk_correlation_order CHECK (point_id_1 < point_id_2)
);

-- ============================================================================
-- Indexes for Pattern Engine tables
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_patterns_tenant ON patterns(tenant_id) WHERE tenant_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_patterns_active ON patterns(is_active) WHERE is_active = true;
CREATE INDEX IF NOT EXISTS idx_patterns_confidence ON patterns(confidence DESC);

CREATE INDEX IF NOT EXISTS idx_pattern_roles_pattern ON pattern_roles(pattern_id);
CREATE INDEX IF NOT EXISTS idx_pattern_roles_naming ON pattern_roles USING GIN (naming_patterns);

CREATE INDEX IF NOT EXISTS idx_suggestions_status ON pattern_suggestions(status);
CREATE INDEX IF NOT EXISTS idx_suggestions_pattern ON pattern_suggestions(pattern_id);
CREATE INDEX IF NOT EXISTS idx_suggestions_cluster ON pattern_suggestions(cluster_id);
CREATE INDEX IF NOT EXISTS idx_suggestions_created ON pattern_suggestions(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_feedback_pattern ON pattern_feedback_log(pattern_id);
CREATE INDEX IF NOT EXISTS idx_feedback_suggestion ON pattern_feedback_log(suggestion_id);
CREATE INDEX IF NOT EXISTS idx_feedback_created ON pattern_feedback_log(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_bindings_point ON point_pattern_bindings(point_id);
CREATE INDEX IF NOT EXISTS idx_bindings_pattern ON point_pattern_bindings(pattern_id);
CREATE INDEX IF NOT EXISTS idx_bindings_role ON point_pattern_bindings(role_id);

CREATE INDEX IF NOT EXISTS idx_clusters_detected ON behavioral_clusters(detected_at DESC);
CREATE INDEX IF NOT EXISTS idx_clusters_cohesion ON behavioral_clusters(average_cohesion DESC);

CREATE INDEX IF NOT EXISTS idx_correlation_point1 ON correlation_cache(point_id_1);
CREATE INDEX IF NOT EXISTS idx_correlation_point2 ON correlation_cache(point_id_2);
CREATE INDEX IF NOT EXISTS idx_correlation_value ON correlation_cache(correlation) WHERE ABS(correlation) > 0.6;

-- ============================================================================
-- Seed data: Common industrial patterns
-- ============================================================================

-- HVAC Air Handling Unit pattern
INSERT INTO patterns (id, name, description, confidence, is_system)
VALUES 
    ('a1b2c3d4-e5f6-7890-abcd-ef1234567890', 
     'HVAC Air Handling Unit', 
     'Standard air handling unit with supply/return air temperatures, damper, and fan status',
     0.75, true)
ON CONFLICT DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, naming_patterns, typical_unit, typical_min, typical_max, typical_update_rate_ms, is_required, sort_order)
VALUES 
    ('a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'Supply Air Temperature', 
     '["supply.*air.*temp", "sat", "sa.*temp", "discharge.*temp"]'::jsonb, '°F', 40, 120, 1000, true, 1),
    ('a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'Return Air Temperature',
     '["return.*air.*temp", "rat", "ra.*temp"]'::jsonb, '°F', 60, 85, 1000, true, 2),
    ('a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'Mixed Air Temperature',
     '["mixed.*air.*temp", "mat", "ma.*temp"]'::jsonb, '°F', 40, 85, 1000, false, 3),
    ('a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'Outside Air Damper',
     '["oa.*damper", "outside.*air.*damper", "oad"]'::jsonb, '%', 0, 100, 5000, false, 4),
    ('a1b2c3d4-e5f6-7890-abcd-ef1234567890', 'Supply Fan Status',
     '["supply.*fan.*status", "sf.*status", "fan.*run"]'::jsonb, NULL, 0, 1, 1000, true, 5)
ON CONFLICT DO NOTHING;

-- Chiller pattern
INSERT INTO patterns (id, name, description, confidence, is_system)
VALUES 
    ('b2c3d4e5-f6a7-8901-bcde-f12345678901',
     'Chiller',
     'Standard chiller with supply/return water temperatures, flow, and status',
     0.70, true)
ON CONFLICT DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, naming_patterns, typical_unit, typical_min, typical_max, typical_update_rate_ms, is_required, sort_order)
VALUES 
    ('b2c3d4e5-f6a7-8901-bcde-f12345678901', 'Chilled Water Supply Temperature',
     '["chwst", "chw.*supply.*temp", "chilled.*water.*supply"]'::jsonb, '°F', 38, 55, 1000, true, 1),
    ('b2c3d4e5-f6a7-8901-bcde-f12345678901', 'Chilled Water Return Temperature',
     '["chwrt", "chw.*return.*temp", "chilled.*water.*return"]'::jsonb, '°F', 50, 65, 1000, true, 2),
    ('b2c3d4e5-f6a7-8901-bcde-f12345678901', 'Chilled Water Flow',
     '["chw.*flow", "chilled.*water.*flow", "gpm"]'::jsonb, 'GPM', 0, 5000, 1000, false, 3),
    ('b2c3d4e5-f6a7-8901-bcde-f12345678901', 'Condenser Water Supply Temperature',
     '["cwst", "cw.*supply.*temp", "condenser.*supply"]'::jsonb, '°F', 70, 95, 1000, false, 4),
    ('b2c3d4e5-f6a7-8901-bcde-f12345678901', 'Chiller Status',
     '["chiller.*status", "chl.*run", "chiller.*enable"]'::jsonb, NULL, 0, 1, 1000, true, 5)
ON CONFLICT DO NOTHING;

-- Pump pattern
INSERT INTO patterns (id, name, description, confidence, is_system)
VALUES 
    ('c3d4e5f6-a7b8-9012-cdef-123456789012',
     'Pump',
     'Variable speed pump with pressure, flow, and speed feedback',
     0.65, true)
ON CONFLICT DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, naming_patterns, typical_unit, typical_min, typical_max, typical_update_rate_ms, is_required, sort_order)
VALUES 
    ('c3d4e5f6-a7b8-9012-cdef-123456789012', 'Pump Status',
     '["pump.*status", "pump.*run", "p[0-9]+.*status"]'::jsonb, NULL, 0, 1, 1000, true, 1),
    ('c3d4e5f6-a7b8-9012-cdef-123456789012', 'Pump Speed',
     '["pump.*speed", "vfd.*speed", "pump.*%"]'::jsonb, '%', 0, 100, 1000, false, 2),
    ('c3d4e5f6-a7b8-9012-cdef-123456789012', 'Discharge Pressure',
     '["discharge.*press", "pump.*press", "dp"]'::jsonb, 'PSI', 0, 150, 1000, false, 3),
    ('c3d4e5f6-a7b8-9012-cdef-123456789012', 'Flow Rate',
     '["flow.*rate", "pump.*flow", "gpm"]'::jsonb, 'GPM', 0, 2000, 1000, false, 4)
ON CONFLICT DO NOTHING;

-- VAV Box pattern
INSERT INTO patterns (id, name, description, confidence, is_system)
VALUES 
    ('d4e5f6a7-b8c9-0123-defa-234567890123',
     'VAV Box',
     'Variable air volume terminal unit with damper, flow, and reheat',
     0.70, true)
ON CONFLICT DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, naming_patterns, typical_unit, typical_min, typical_max, typical_update_rate_ms, is_required, sort_order)
VALUES 
    ('d4e5f6a7-b8c9-0123-defa-234567890123', 'Damper Position',
     '["vav.*damper", "damper.*pos", "vav.*%"]'::jsonb, '%', 0, 100, 1000, true, 1),
    ('d4e5f6a7-b8c9-0123-defa-234567890123', 'Air Flow',
     '["vav.*flow", "air.*flow", "cfm"]'::jsonb, 'CFM', 0, 2000, 1000, false, 2),
    ('d4e5f6a7-b8c9-0123-defa-234567890123', 'Zone Temperature',
     '["zone.*temp", "room.*temp", "space.*temp"]'::jsonb, '°F', 60, 85, 5000, true, 3),
    ('d4e5f6a7-b8c9-0123-defa-234567890123', 'Zone Setpoint',
     '["zone.*sp", "room.*setpoint", "temp.*setpoint"]'::jsonb, '°F', 65, 80, 10000, false, 4),
    ('d4e5f6a7-b8c9-0123-defa-234567890123', 'Reheat Valve',
     '["reheat.*valve", "htg.*valve", "hw.*valve"]'::jsonb, '%', 0, 100, 1000, false, 5)
ON CONFLICT DO NOTHING;

-- Boiler pattern
INSERT INTO patterns (id, name, description, confidence, is_system)
VALUES 
    ('e5f6a7b8-c9d0-1234-efab-345678901234',
     'Boiler',
     'Hot water boiler with supply/return temperatures and status',
     0.65, true)
ON CONFLICT DO NOTHING;

INSERT INTO pattern_roles (pattern_id, name, naming_patterns, typical_unit, typical_min, typical_max, typical_update_rate_ms, is_required, sort_order)
VALUES 
    ('e5f6a7b8-c9d0-1234-efab-345678901234', 'Hot Water Supply Temperature',
     '["hwst", "hw.*supply.*temp", "boiler.*supply"]'::jsonb, '°F', 140, 200, 1000, true, 1),
    ('e5f6a7b8-c9d0-1234-efab-345678901234', 'Hot Water Return Temperature',
     '["hwrt", "hw.*return.*temp", "boiler.*return"]'::jsonb, '°F', 120, 180, 1000, true, 2),
    ('e5f6a7b8-c9d0-1234-efab-345678901234', 'Boiler Status',
     '["boiler.*status", "boiler.*run", "blr.*enable"]'::jsonb, NULL, 0, 1, 1000, true, 3),
    ('e5f6a7b8-c9d0-1234-efab-345678901234', 'Firing Rate',
     '["firing.*rate", "boiler.*%", "modulation"]'::jsonb, '%', 0, 100, 1000, false, 4)
ON CONFLICT DO NOTHING;

-- ============================================================================
-- View: Active pattern suggestions with pattern details
-- ============================================================================

CREATE OR REPLACE VIEW v_active_suggestions AS
SELECT 
    s.id AS suggestion_id,
    s.cluster_id,
    p.id AS pattern_id,
    p.name AS pattern_name,
    p.description AS pattern_description,
    s.overall_confidence,
    s.naming_score,
    s.correlation_score,
    s.range_score,
    s.rate_score,
    s.matched_point_ids,
    s.role_assignments,
    s.evidence,
    s.status,
    s.created_at
FROM pattern_suggestions s
JOIN patterns p ON p.id = s.pattern_id
WHERE s.status = 'pending'
  AND (s.expires_at IS NULL OR s.expires_at > NOW())
ORDER BY s.overall_confidence DESC, s.created_at DESC;

-- ============================================================================
-- View: Pattern performance metrics
-- ============================================================================

CREATE OR REPLACE VIEW v_pattern_metrics AS
SELECT 
    p.id AS pattern_id,
    p.name AS pattern_name,
    p.confidence,
    p.is_system,
    COUNT(DISTINCT pb.point_id) AS bound_point_count,
    COUNT(DISTINCT CASE WHEN pf.action = 'approved' THEN pf.id END) AS approval_count,
    COUNT(DISTINCT CASE WHEN pf.action = 'rejected' THEN pf.id END) AS rejection_count,
    COUNT(DISTINCT CASE WHEN pf.action = 'modified' THEN pf.id END) AS modification_count,
    CASE 
        WHEN COUNT(DISTINCT pf.id) > 0 
        THEN COUNT(DISTINCT CASE WHEN pf.action = 'approved' THEN pf.id END)::DOUBLE PRECISION / COUNT(DISTINCT pf.id)
        ELSE NULL 
    END AS approval_rate
FROM patterns p
LEFT JOIN point_pattern_bindings pb ON pb.pattern_id = p.id
LEFT JOIN pattern_feedback_log pf ON pf.pattern_id = p.id
WHERE p.is_active = true
GROUP BY p.id, p.name, p.confidence, p.is_system
ORDER BY p.confidence DESC;

COMMENT ON TABLE patterns IS 'Learned equipment/process patterns for automatic point binding';
COMMENT ON TABLE pattern_roles IS 'Expected point roles within a pattern';
COMMENT ON TABLE pattern_suggestions IS 'AI-generated suggestions for pattern matches';
COMMENT ON TABLE pattern_feedback_log IS 'User feedback on suggestions for continuous learning';
COMMENT ON TABLE point_pattern_bindings IS 'Links between points and patterns';
COMMENT ON TABLE behavioral_clusters IS 'Detected clusters of correlated points';
COMMENT ON TABLE correlation_cache IS 'Cached pairwise correlations between points';
