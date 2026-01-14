-- NAIA v3 Fix: Populate missing point_sequence_id values
-- This script fixes points that were created but don't have sequence IDs assigned

DO $$
DECLARE
    points_updated INT := 0;
BEGIN
    -- Update all points that have NULL point_sequence_id
    -- This assigns them the next value from the sequence
    UPDATE points
    SET point_sequence_id = nextval('points_point_sequence_id_seq')
    WHERE point_sequence_id IS NULL;
    
    GET DIAGNOSTICS points_updated = ROW_COUNT;
    
    RAISE NOTICE 'Fixed % points with missing sequence IDs', points_updated;
    
    -- Verify the fix
    IF EXISTS (SELECT 1 FROM points WHERE point_sequence_id IS NULL LIMIT 1) THEN
        RAISE WARNING 'Some points still have NULL sequence IDs after migration!';
    ELSE
        RAISE NOTICE 'All points now have sequence IDs assigned';
    END IF;
    
    -- Show statistics
    RAISE NOTICE 'Total points: %', (SELECT COUNT(*) FROM points);
    RAISE NOTICE 'Points with sequence IDs: %', (SELECT COUNT(*) FROM points WHERE point_sequence_id IS NOT NULL);
    RAISE NOTICE 'Max sequence ID: %', (SELECT COALESCE(MAX(point_sequence_id), 0) FROM points);
END $$;

-- Update the sequence to the correct next value
-- This ensures future inserts don't have conflicts
SELECT setval('points_point_sequence_id_seq', (SELECT COALESCE(MAX(point_sequence_id), 0) + 1 FROM points), false);

-- Verify no duplicates
DO $$
DECLARE
    duplicate_count INT;
BEGIN
    SELECT COUNT(*) INTO duplicate_count
    FROM (
        SELECT point_sequence_id
        FROM points
        WHERE point_sequence_id IS NOT NULL
        GROUP BY point_sequence_id
        HAVING COUNT(*) > 1
    ) dups;
    
    IF duplicate_count > 0 THEN
        RAISE WARNING 'Found % duplicate sequence IDs!', duplicate_count;
    ELSE
        RAISE NOTICE 'No duplicate sequence IDs found - all good!';
    END IF;
END $$;
