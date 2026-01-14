-- NAIA v3 Knowledge Base Schema
-- Semantic intelligence layer for pattern matching before user feedback
-- This "seeds" the system with industry knowledge, standards, and terminology

-- ===========================================================================
-- INDUSTRY STANDARDS (IEC, ISA, IEEE, API, etc.)
-- Provides authoritative naming conventions and terminology
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_industry_standards (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    standard_code VARCHAR(50) NOT NULL,           -- 'IEC 61400', 'ISA-95', 'API 670'
    standard_name VARCHAR(255) NOT NULL,          -- 'Wind turbines - Design requirements'
    industry VARCHAR(100) NOT NULL,               -- 'Wind', 'Solar', 'Oil & Gas', 'Manufacturing'
    version VARCHAR(50),                          -- '2019', 'Rev 4', etc.
    description TEXT,
    url VARCHAR(500),                             -- Link to standard documentation
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_standard_code UNIQUE (standard_code)
);

CREATE INDEX idx_standards_industry ON knowledge_industry_standards(industry);
CREATE INDEX idx_standards_code ON knowledge_industry_standards(standard_code);

-- ===========================================================================
-- MEASUREMENT TYPES (The fundamental "what is being measured")
-- Maps units, abbreviations, and synonyms to canonical measurement types
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_measurement_types (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    canonical_name VARCHAR(100) NOT NULL,         -- 'Power', 'Temperature', 'Pressure'
    description TEXT,
    data_type VARCHAR(50) DEFAULT 'numeric',      -- 'numeric', 'boolean', 'string', 'datetime'
    category VARCHAR(100),                        -- 'Electrical', 'Thermal', 'Mechanical', 'Environmental'
    typical_min DOUBLE PRECISION,
    typical_max DOUBLE PRECISION,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_measurement_type UNIQUE (canonical_name)
);

CREATE INDEX idx_measurement_category ON knowledge_measurement_types(category);

-- ===========================================================================
-- UNIT MAPPINGS (kW → Power, °C → Temperature, etc.)
-- Bidirectional lookup: unit → measurement type, measurement type → units
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_unit_mappings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    measurement_type_id UUID NOT NULL REFERENCES knowledge_measurement_types(id) ON DELETE CASCADE,
    unit_symbol VARCHAR(50) NOT NULL,             -- 'kW', '°C', 'psi', 'bar'
    unit_name VARCHAR(100),                       -- 'kilowatt', 'degrees Celsius', 'pounds per square inch'
    unit_system VARCHAR(50),                      -- 'SI', 'Imperial', 'Custom'
    conversion_factor DOUBLE PRECISION,           -- Factor to convert to base unit
    base_unit VARCHAR(50),                        -- The SI/standard base unit
    is_common BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_unit_symbol UNIQUE (unit_symbol)
);

CREATE INDEX idx_unit_measurement ON knowledge_unit_mappings(measurement_type_id);
CREATE INDEX idx_unit_symbol ON knowledge_unit_mappings(unit_symbol);

-- ===========================================================================
-- ABBREVIATION DICTIONARY (Common industry abbreviations)
-- Helps parse tag names like "GEN_BRG_DE_TEMP" → Generator Bearing Drive End Temperature
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_abbreviations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    abbreviation VARCHAR(50) NOT NULL,            -- 'GEN', 'BRG', 'TEMP', 'GBOX'
    expansion VARCHAR(255) NOT NULL,              -- 'Generator', 'Bearing', 'Temperature', 'Gearbox'
    context VARCHAR(100),                         -- 'Wind', 'Solar', 'General', 'Oil & Gas'
    priority INT DEFAULT 100,                     -- Higher = more likely match when ambiguous
    measurement_type_id UUID REFERENCES knowledge_measurement_types(id),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_abbreviation_context UNIQUE (abbreviation, context)
);

CREATE INDEX idx_abbrev_text ON knowledge_abbreviations(abbreviation);
CREATE INDEX idx_abbrev_context ON knowledge_abbreviations(context);

-- ===========================================================================
-- EQUIPMENT TAXONOMY (Hierarchical equipment classification)
-- Pump → Centrifugal Pump → API 610 Centrifugal Pump
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_equipment_taxonomy (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    description TEXT,
    parent_id UUID REFERENCES knowledge_equipment_taxonomy(id),
    level INT NOT NULL DEFAULT 1,                 -- 1=Category, 2=Type, 3=Subtype, 4=Variant
    industry VARCHAR(100),                        -- Primary industry association
    standard_id UUID REFERENCES knowledge_industry_standards(id),
    naming_hints TEXT[],                          -- Common name fragments: ['pump', 'pmp', 'p-']
    typical_point_count INT,                      -- Expected number of monitoring points
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_equipment_name_parent UNIQUE (name, parent_id)
);

CREATE INDEX idx_taxonomy_parent ON knowledge_equipment_taxonomy(parent_id);
CREATE INDEX idx_taxonomy_level ON knowledge_equipment_taxonomy(level);
CREATE INDEX idx_taxonomy_industry ON knowledge_equipment_taxonomy(industry);

-- ===========================================================================
-- STANDARD TAG NAMING CONVENTIONS
-- IEC 61400-25-2 for wind, IEC 61724-1 for solar, ISA-5.1 for general
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_naming_conventions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    standard_id UUID REFERENCES knowledge_industry_standards(id),
    equipment_type_id UUID REFERENCES knowledge_equipment_taxonomy(id),
    
    pattern_regex VARCHAR(500) NOT NULL,          -- Regex pattern for matching
    pattern_description TEXT,                     -- Human explanation of the pattern
    measurement_type_id UUID REFERENCES knowledge_measurement_types(id),
    
    -- Parsed components
    component_template VARCHAR(500),              -- Template showing components: '{site}_{unit}_{point}'
    component_definitions JSONB,                  -- {"site": "3 char site code", "unit": "equipment ID", ...}
    
    confidence_boost DOUBLE PRECISION DEFAULT 0.10, -- Bonus confidence when this pattern matches
    priority INT DEFAULT 100,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_naming_standard ON knowledge_naming_conventions(standard_id);
CREATE INDEX idx_naming_equipment ON knowledge_naming_conventions(equipment_type_id);

-- ===========================================================================
-- SEMANTIC SYNONYMS (Handles variations in terminology)
-- "Active Power" = "Real Power" = "P" = "kW Output"
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_synonyms (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    canonical_term VARCHAR(255) NOT NULL,         -- The preferred/standard term
    synonym VARCHAR(255) NOT NULL,                -- Alternative term
    context VARCHAR(100),                         -- Where this synonym applies
    similarity_score DOUBLE PRECISION DEFAULT 1.0, -- 1.0 = exact match, 0.8 = close match
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_synonym UNIQUE (canonical_term, synonym, context)
);

CREATE INDEX idx_synonym_canonical ON knowledge_synonyms(canonical_term);
CREATE INDEX idx_synonym_synonym ON knowledge_synonyms(synonym);

-- ===========================================================================
-- MANUFACTURER EQUIPMENT PROFILES (OEM-specific patterns)
-- Vestas, Siemens Gamesa, GE, Goldwind, etc.
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_manufacturer_profiles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    manufacturer_name VARCHAR(255) NOT NULL,
    manufacturer_code VARCHAR(50),                -- Short code: 'VES', 'SGRE', 'GE'
    equipment_category VARCHAR(100) NOT NULL,     -- 'WindTurbine', 'Solar', 'BESS'
    model_pattern VARCHAR(255),                   -- 'V110-2.0', 'SG 8.0-167 DD'
    
    -- Naming convention specifics
    tag_prefix_pattern VARCHAR(255),              -- How they typically prefix tags
    tag_structure JSONB,                          -- Typical tag structure breakdown
    
    -- Documentation
    documentation_url VARCHAR(500),
    manual_reference VARCHAR(255),
    
    is_verified BOOLEAN DEFAULT false,            -- Has been manually verified
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_manufacturer_model UNIQUE (manufacturer_name, model_pattern)
);

CREATE INDEX idx_manufacturer_name ON knowledge_manufacturer_profiles(manufacturer_name);
CREATE INDEX idx_manufacturer_category ON knowledge_manufacturer_profiles(equipment_category);

-- ===========================================================================
-- DOCUMENT CONTEXT STORE (For manufacturer manuals, specifications, etc.)
-- Processed document chunks for contextual understanding
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(500) NOT NULL,
    document_type VARCHAR(100) NOT NULL,          -- 'Manual', 'Specification', 'Standard', 'Guide'
    source_type VARCHAR(100),                     -- 'Manufacturer', 'Standard Body', 'Internal'
    
    -- Associations
    manufacturer_id UUID REFERENCES knowledge_manufacturer_profiles(id),
    standard_id UUID REFERENCES knowledge_industry_standards(id),
    equipment_type_id UUID REFERENCES knowledge_equipment_taxonomy(id),
    
    -- Content
    file_path VARCHAR(1000),
    file_hash VARCHAR(64),                        -- SHA-256 of original file
    total_pages INT,
    processed_at TIMESTAMP WITH TIME ZONE,
    
    -- Metadata
    language VARCHAR(10) DEFAULT 'en',
    version VARCHAR(50),
    publish_date DATE,
    
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_documents_manufacturer ON knowledge_documents(manufacturer_id);
CREATE INDEX idx_documents_type ON knowledge_documents(document_type);

-- ===========================================================================
-- DOCUMENT CHUNKS (Semantic chunks for RAG-style retrieval)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_document_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES knowledge_documents(id) ON DELETE CASCADE,
    
    chunk_index INT NOT NULL,
    content TEXT NOT NULL,
    content_type VARCHAR(50),                     -- 'text', 'table', 'list', 'heading'
    
    -- Location in document
    page_number INT,
    section_title VARCHAR(500),
    
    -- Embeddings (stored as array for pgvector compatibility)
    -- Note: If using pgvector, change to: embedding vector(1536)
    embedding_model VARCHAR(100),
    embedding DOUBLE PRECISION[],
    
    -- Extracted entities
    mentioned_points TEXT[],                      -- Tag names mentioned in this chunk
    mentioned_equipment TEXT[],
    mentioned_measurements TEXT[],
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_chunks_document ON knowledge_document_chunks(document_id);
CREATE INDEX idx_chunks_content ON knowledge_document_chunks USING gin(to_tsvector('english', content));

-- ===========================================================================
-- SEED QUALITY TRACKING (Track effectiveness of seeds)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_seed_effectiveness (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    seed_type VARCHAR(100) NOT NULL,              -- 'abbreviation', 'pattern', 'synonym', etc.
    seed_id UUID NOT NULL,                        -- Reference to the seed record
    
    -- Usage stats
    times_matched INT DEFAULT 0,
    times_contributed_to_match INT DEFAULT 0,     -- Led to successful pattern match
    times_user_approved INT DEFAULT 0,
    times_user_rejected INT DEFAULT 0,
    
    -- Calculated effectiveness
    effectiveness_score DOUBLE PRECISION,         -- 0-1, recalculated periodically
    last_used_at TIMESTAMP WITH TIME ZONE,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_seed_effectiveness_type ON knowledge_seed_effectiveness(seed_type);
CREATE INDEX idx_seed_effectiveness_score ON knowledge_seed_effectiveness(effectiveness_score DESC);

-- ===========================================================================
-- SEED MANAGEMENT LOG (Audit trail for seed changes)
-- ===========================================================================
CREATE TABLE IF NOT EXISTS knowledge_seed_audit_log (
    id BIGSERIAL PRIMARY KEY,
    action VARCHAR(50) NOT NULL,                  -- 'create', 'update', 'delete', 'disable', 'enable'
    seed_type VARCHAR(100) NOT NULL,
    seed_id UUID NOT NULL,
    
    user_id VARCHAR(255),
    user_role VARCHAR(100),                       -- 'seed_master', 'admin', 'system'
    
    changes_json JSONB,                           -- What changed
    reason TEXT,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_audit_seed ON knowledge_seed_audit_log(seed_type, seed_id);
CREATE INDEX idx_audit_created ON knowledge_seed_audit_log(created_at DESC);

-- ===========================================================================
-- HELPER FUNCTIONS
-- ===========================================================================

-- Function to parse a tag name using abbreviation dictionary
CREATE OR REPLACE FUNCTION parse_tag_name(tag_name TEXT, context_hint TEXT DEFAULT 'General')
RETURNS TABLE(
    abbreviation TEXT,
    expansion TEXT,
    measurement_type TEXT
) AS $$
BEGIN
    RETURN QUERY
    WITH parts AS (
        SELECT unnest(string_to_array(
            regexp_replace(upper(tag_name), '[^A-Z0-9]', '_', 'g'),
            '_'
        )) AS part
    )
    SELECT DISTINCT ON (p.part)
        p.part::TEXT as abbreviation,
        ka.expansion::TEXT,
        kmt.canonical_name::TEXT as measurement_type
    FROM parts p
    LEFT JOIN knowledge_abbreviations ka ON ka.abbreviation = p.part 
        AND (ka.context = context_hint OR ka.context = 'General')
    LEFT JOIN knowledge_measurement_types kmt ON kmt.id = ka.measurement_type_id
    WHERE p.part <> ''
    ORDER BY p.part, ka.priority DESC NULLS LAST;
END;
$$ LANGUAGE plpgsql;

-- Function to infer measurement type from unit
CREATE OR REPLACE FUNCTION infer_measurement_from_unit(unit_text TEXT)
RETURNS TABLE(
    measurement_type TEXT,
    confidence DOUBLE PRECISION
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        kmt.canonical_name::TEXT,
        CASE 
            WHEN kum.is_common THEN 0.95
            ELSE 0.75
        END as confidence
    FROM knowledge_unit_mappings kum
    JOIN knowledge_measurement_types kmt ON kmt.id = kum.measurement_type_id
    WHERE lower(kum.unit_symbol) = lower(unit_text)
       OR lower(kum.unit_name) = lower(unit_text)
    LIMIT 1;
END;
$$ LANGUAGE plpgsql;

-- Function to get equipment hierarchy path
CREATE OR REPLACE FUNCTION get_equipment_path(equipment_id UUID)
RETURNS TEXT AS $$
DECLARE
    path TEXT := '';
    current_id UUID := equipment_id;
    current_name TEXT;
    parent UUID;
BEGIN
    LOOP
        SELECT name, parent_id INTO current_name, parent
        FROM knowledge_equipment_taxonomy
        WHERE id = current_id;
        
        IF current_name IS NULL THEN
            EXIT;
        END IF;
        
        IF path = '' THEN
            path := current_name;
        ELSE
            path := current_name || ' > ' || path;
        END IF;
        
        current_id := parent;
        EXIT WHEN current_id IS NULL;
    END LOOP;
    
    RETURN path;
END;
$$ LANGUAGE plpgsql;

-- Grant permissions
GRANT SELECT ON ALL TABLES IN SCHEMA public TO naia;
GRANT INSERT, UPDATE, DELETE ON knowledge_seed_audit_log TO naia;
GRANT INSERT, UPDATE ON knowledge_seed_effectiveness TO naia;
