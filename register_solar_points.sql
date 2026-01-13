-- Register Solar Inverter Points for CSV Replay
-- SITE1: 5 inverters, SITE2: 3 inverters
-- These points connect the CSV data streams to the metadata database

-- Create or get the solar data source
DO $$
DECLARE
    v_datasource_id UUID;
    v_point_id UUID;
    v_seq_id INT;
BEGIN
    -- Create solar data source if it doesn't exist
    INSERT INTO data_sources (id, name, source_type, connection_string, is_enabled, created_at)
    VALUES (
        gen_random_uuid(),
        'Solar CSV Replay',
        'GenericCsvReplay',
        'file:///opt/naia/data/solar',
        true,
        NOW()
    )
    ON CONFLICT (name) DO UPDATE SET
        is_enabled = true,
        updated_at = NOW()
    RETURNING id INTO v_datasource_id;

    -- If the data source already existed, get its ID
    IF v_datasource_id IS NULL THEN
        SELECT id INTO v_datasource_id FROM data_sources WHERE name = 'Solar CSV Replay';
    END IF;

    RAISE NOTICE 'Data source ID: %', v_datasource_id;

    -- Register SITE1 Inverter Points
    FOR i IN 1..5 LOOP
        -- Get next sequence ID
        SELECT COALESCE(MAX(point_sequence_id), 0) + 1 INTO v_seq_id FROM points;
        
        -- Check if point already exists
        SELECT id INTO v_point_id 
        FROM points 
        WHERE data_source_id = v_datasource_id 
        AND address = 'SITE1_INV0' || i || '_ActivePower';
        
        IF v_point_id IS NULL THEN
            INSERT INTO points (
                id, name, address, description, 
                engineering_units, value_type, data_source_id,
                point_sequence_id, created_at
            )
            VALUES (
                gen_random_uuid(),
                'SITE1_INV0' || i || '_ActivePower',
                'SITE1_INV0' || i || '_ActivePower',
                'SITE1 Inverter 0' || i || ' Active Power',
                'kW',
                'Float64',
                v_datasource_id,
                v_seq_id,
                NOW()
            )
            RETURNING id INTO v_point_id;
            
            RAISE NOTICE 'Registered SITE1_INV0%_ActivePower (SeqId: %, PointId: %)', i, v_seq_id, v_point_id;
        ELSE
            RAISE NOTICE 'SITE1_INV0%_ActivePower already exists (PointId: %)', i, v_point_id;
        END IF;
    END LOOP;

    -- Register SITE2 Inverter Points
    FOR i IN 1..3 LOOP
        -- Get next sequence ID
        SELECT COALESCE(MAX(point_sequence_id), 0) + 1 INTO v_seq_id FROM points;
        
        -- Check if point already exists
        SELECT id INTO v_point_id 
        FROM points 
        WHERE data_source_id = v_datasource_id 
        AND address = 'SITE2_INV0' || i || '_ActivePower';
        
        IF v_point_id IS NULL THEN
            INSERT INTO points (
                id, name, address, description, 
                engineering_units, value_type, data_source_id,
                point_sequence_id, created_at
            )
            VALUES (
                gen_random_uuid(),
                'SITE2_INV0' || i || '_ActivePower',
                'SITE2_INV0' || i || '_ActivePower',
                'SITE2 Inverter 0' || i || ' Active Power',
                'kW',
                'Float64',
                v_datasource_id,
                v_seq_id,
                NOW()
            )
            RETURNING id INTO v_point_id;
            
            RAISE NOTICE 'Registered SITE2_INV0%_ActivePower (SeqId: %, PointId: %)', i, v_seq_id, v_point_id;
        ELSE
            RAISE NOTICE 'SITE2_INV0%_ActivePower already exists (PointId: %)', i, v_point_id;
        END IF;
    END LOOP;

END $$;

-- Verify registration
SELECT p.point_sequence_id, p.name, p.address, p.engineering_units, ds.name as data_source
FROM points p
JOIN data_sources ds ON p.data_source_id = ds.id
WHERE ds.name = 'Solar CSV Replay'
ORDER BY p.point_sequence_id;
