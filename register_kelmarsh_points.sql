-- Register Kelmarsh Wind Farm replay data as points in PostgreSQL
-- This maps the existing QuestDB data (point_ids 0-89) to PostgreSQL points

-- Create Kelmarsh data source
INSERT INTO data_sources (id, name, source_type, status, connection_string, is_enabled, created_at)
VALUES (
    '00000000-0000-0000-0000-000000000001'::uuid,
    'Kelmarsh Wind Farm Replay',
    'Replay',
    'Connected',
    'File: c:\naia3\data\kelmarsh\scada_2019',
    true,
    NOW()
)
ON CONFLICT (name) DO UPDATE SET 
    source_type = EXCLUDED.source_type,
    status = EXCLUDED.status;

-- Create points for 6 turbines × 15 readings
-- Turbine readings: WindSpeed, Power, WindDirection, RotorRPM, GeneratorRPM, 
-- PitchA, PitchB, PitchC, NacelleTemp, GearOilTemp, GenBearingFrontTemp, 
-- GenBearingRearTemp, AmbientTemp, GridVoltage, GridFrequency

DO $$
DECLARE
    turbine_num INT;
    reading_idx INT;
    seq_id BIGINT;
    point_guid UUID;
    readings TEXT[] := ARRAY['WindSpeed', 'Power', 'WindDirection', 'RotorRPM', 'GeneratorRPM', 
                             'PitchA', 'PitchB', 'PitchC', 'NacelleTemp', 'GearOilTemp', 
                             'GenBearingFrontTemp', 'GenBearingRearTemp', 'AmbientTemp', 
                             'GridVoltage', 'GridFrequency'];
    units TEXT[] := ARRAY['m/s', 'kW', 'degrees', 'rpm', 'rpm', 
                          'degrees', 'degrees', 'degrees', '°C', '°C', 
                          '°C', '°C', '°C', 'V', 'Hz'];
BEGIN
    seq_id := 0;
    
    FOR turbine_num IN 1..6 LOOP
        FOR reading_idx IN 1..15 LOOP
            point_guid := gen_random_uuid();
            
            INSERT INTO points (
                id, 
                point_sequence_id,
                name, 
                description,
                engineering_units,
                value_type,
                kind,
                source_address,
                data_source_id,
                compression_enabled,
                compression_deviation,
                compression_min_interval_seconds,
                compression_max_interval_seconds,
                exception_enabled,
                exception_deviation,
                scale_zero,
                scale_span,
                alert_on_out_of_range,
                is_enabled,
                created_at,
                updated_at
            ) VALUES (
                point_guid,
                seq_id,
                'KSH_' || LPAD(turbine_num::TEXT, 3, '0') || '_' || readings[reading_idx],
                'Kelmarsh Turbine ' || turbine_num || ' - ' || readings[reading_idx],
                units[reading_idx],
                'Float64',
                'Input',
                'KSH_' || LPAD(turbine_num::TEXT, 3, '0') || '_' || readings[reading_idx],
                '00000000-0000-0000-0000-000000000001'::uuid,
                true,
                0.5,
                0,
                600,
                true,
                0.1,
                0,
                100,
                false,
                true,
                NOW(),
                NOW()
            )
            ON CONFLICT (name) DO UPDATE SET
                description = EXCLUDED.description,
                updated_at = NOW();
            
            seq_id := seq_id + 1;
        END LOOP;
    END LOOP;
    
    RAISE NOTICE 'Created % Kelmarsh replay points (sequence IDs 0-%)', seq_id, seq_id-1;
END $$;

-- Verify
SELECT COUNT(*) as total_points FROM points WHERE data_source_id = '00000000-0000-0000-0000-000000000001'::uuid;
SELECT MIN(point_sequence_id) as min_seq, MAX(point_sequence_id) as max_seq FROM points WHERE data_source_id = '00000000-0000-0000-0000-000000000001'::uuid;
