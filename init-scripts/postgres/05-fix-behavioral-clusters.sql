-- Migration: Fix behavioral_clusters schema (member_point_ids → point_ids)
-- This migrates the old JSONB member_point_ids column to UUID[] point_ids

DO $$
BEGIN
    -- Check if old column exists
    IF EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'behavioral_clusters' AND column_name = 'member_point_ids'
    ) THEN
        -- Add new column if it doesn't exist
        IF NOT EXISTS (
            SELECT 1 FROM information_schema.columns 
            WHERE table_name = 'behavioral_clusters' AND column_name = 'point_ids'
        ) THEN
            ALTER TABLE behavioral_clusters ADD COLUMN point_ids UUID[];
            RAISE NOTICE 'Added column point_ids UUID[]';
        END IF;
        
        -- Migrate data from JSONB to UUID[]
        UPDATE behavioral_clusters
        SET point_ids = ARRAY(
            SELECT jsonb_array_elements_text(member_point_ids)::UUID
        )
        WHERE point_ids IS NULL AND member_point_ids IS NOT NULL;
        
        RAISE NOTICE 'Migrated data from member_point_ids to point_ids';
        
        -- Make point_ids NOT NULL
        ALTER TABLE behavioral_clusters ALTER COLUMN point_ids SET NOT NULL;
        
        -- Drop old column
        ALTER TABLE behavioral_clusters DROP COLUMN member_point_ids;
        RAISE NOTICE 'Dropped old column member_point_ids';
        
    ELSE
        RAISE NOTICE 'Column member_point_ids does not exist, migration not needed';
    END IF;
    
    -- Add other missing columns from current schema
    ALTER TABLE behavioral_clusters ADD COLUMN IF NOT EXISTS point_names TEXT[];
    ALTER TABLE behavioral_clusters ADD COLUMN IF NOT EXISTS point_count INT;
    ALTER TABLE behavioral_clusters ADD COLUMN IF NOT EXISTS cohesion DOUBLE PRECISION;
    ALTER TABLE behavioral_clusters ADD COLUMN IF NOT EXISTS is_active BOOLEAN DEFAULT true;
    ALTER TABLE behavioral_clusters ADD COLUMN IF NOT EXISTS detected_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;
    
    -- Ensure point_count is populated
    UPDATE behavioral_clusters 
    SET point_count = array_length(point_ids, 1)
    WHERE point_count IS NULL;
    
END $$;

-- Verify the migration
SELECT column_name, data_type, is_nullable
FROM information_schema.columns 
WHERE table_name = 'behavioral_clusters' 
ORDER BY ordinal_position;
