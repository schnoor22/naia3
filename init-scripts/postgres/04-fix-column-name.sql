-- NAIA v3 Migration: Fix point_id_seq → point_sequence_id column name
-- This migration aligns the database schema with EF Core mappings
-- Run this ONCE on production databases created with old init script

-- ===========================================================================
-- MIGRATION: Rename point_id_seq to point_sequence_id
-- ===========================================================================

DO $$
BEGIN
    -- Check if the old column name exists
    IF EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'points' AND column_name = 'point_id_seq'
    ) THEN
        -- Rename the column
        ALTER TABLE points RENAME COLUMN point_id_seq TO point_sequence_id;
        RAISE NOTICE 'Renamed column point_id_seq to point_sequence_id';
        
        -- Rename the sequence for consistency
        ALTER SEQUENCE IF EXISTS point_id_seq RENAME TO point_sequence_id_seq;
        RAISE NOTICE 'Renamed sequence point_id_seq to point_sequence_id_seq';
        
        -- Update the default to use the new sequence name
        ALTER TABLE points ALTER COLUMN point_sequence_id SET DEFAULT nextval('point_sequence_id_seq');
        RAISE NOTICE 'Updated default value to use point_sequence_id_seq';
    ELSE
        RAISE NOTICE 'Column point_sequence_id already exists, no migration needed';
    END IF;

    -- Rename index if it exists with old name
    IF EXISTS (
        SELECT 1 FROM pg_indexes 
        WHERE indexname = 'idx_points_seq'
    ) THEN
        ALTER INDEX idx_points_seq RENAME TO idx_points_sequence_id;
        RAISE NOTICE 'Renamed index idx_points_seq to idx_points_sequence_id';
    END IF;
END $$;

-- Verify the migration
SELECT column_name, data_type, column_default
FROM information_schema.columns 
WHERE table_name = 'points' AND column_name = 'point_sequence_id';
