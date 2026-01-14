-- NAIA v3 Knowledge Base Seed Data Part 2
-- Abbreviation dictionary and equipment taxonomy

-- ===========================================================================
-- ABBREVIATION DICTIONARY
-- Comprehensive list of industry abbreviations for tag name parsing
-- ===========================================================================

-- Get measurement type IDs for reference
DO $$
DECLARE
    temp_id UUID;
    power_id UUID;
    react_power_id UUID;
    voltage_id UUID;
    current_id UUID;
    freq_id UUID;
    pressure_id UUID;
    flow_id UUID;
    rpm_id UUID;
    vibration_id UUID;
    position_id UUID;
    angle_id UUID;
    level_id UUID;
    wind_speed_id UUID;
    wind_dir_id UUID;
    humidity_id UUID;
    irradiance_id UUID;
    soc_id UUID;
    efficiency_id UUID;
BEGIN
    SELECT id INTO temp_id FROM knowledge_measurement_types WHERE canonical_name = 'Temperature';
    SELECT id INTO power_id FROM knowledge_measurement_types WHERE canonical_name = 'Power';
    SELECT id INTO react_power_id FROM knowledge_measurement_types WHERE canonical_name = 'Reactive Power';
    SELECT id INTO voltage_id FROM knowledge_measurement_types WHERE canonical_name = 'Voltage';
    SELECT id INTO current_id FROM knowledge_measurement_types WHERE canonical_name = 'Current';
    SELECT id INTO freq_id FROM knowledge_measurement_types WHERE canonical_name = 'Frequency';
    SELECT id INTO pressure_id FROM knowledge_measurement_types WHERE canonical_name = 'Pressure';
    SELECT id INTO flow_id FROM knowledge_measurement_types WHERE canonical_name = 'Flow Rate';
    SELECT id INTO rpm_id FROM knowledge_measurement_types WHERE canonical_name = 'Rotational Speed';
    SELECT id INTO vibration_id FROM knowledge_measurement_types WHERE canonical_name = 'Vibration';
    SELECT id INTO position_id FROM knowledge_measurement_types WHERE canonical_name = 'Position';
    SELECT id INTO angle_id FROM knowledge_measurement_types WHERE canonical_name = 'Angle';
    SELECT id INTO level_id FROM knowledge_measurement_types WHERE canonical_name = 'Level';
    SELECT id INTO wind_speed_id FROM knowledge_measurement_types WHERE canonical_name = 'Wind Speed';
    SELECT id INTO wind_dir_id FROM knowledge_measurement_types WHERE canonical_name = 'Wind Direction';
    SELECT id INTO humidity_id FROM knowledge_measurement_types WHERE canonical_name = 'Humidity';
    SELECT id INTO irradiance_id FROM knowledge_measurement_types WHERE canonical_name = 'Solar Irradiance';
    SELECT id INTO soc_id FROM knowledge_measurement_types WHERE canonical_name = 'State of Charge';
    SELECT id INTO efficiency_id FROM knowledge_measurement_types WHERE canonical_name = 'Efficiency';

    -- Temperature abbreviations
    INSERT INTO knowledge_abbreviations (abbreviation, expansion, context, priority, measurement_type_id) VALUES
    ('TEMP', 'Temperature', 'General', 100, temp_id),
    ('TMP', 'Temperature', 'General', 90, temp_id),
    ('T', 'Temperature', 'General', 50, temp_id),
    ('DEGC', 'Degrees Celsius', 'General', 80, temp_id),
    ('DEGF', 'Degrees Fahrenheit', 'General', 80, temp_id),
    ('OAT', 'Outside Air Temperature', 'General', 100, temp_id),
    ('AMB', 'Ambient', 'General', 100, NULL),
    ('AMBIENT', 'Ambient', 'General', 100, NULL),
    ('NAC', 'Nacelle', 'Wind', 100, NULL),
    ('NACELLE', 'Nacelle', 'Wind', 100, NULL),
    ('GBOX', 'Gearbox', 'General', 100, NULL),
    ('GEAR', 'Gearbox', 'General', 90, NULL),
    ('GEARBOX', 'Gearbox', 'General', 100, NULL),
    ('OIL', 'Oil/Lubricant', 'General', 100, NULL),
    ('LUBE', 'Lubricant', 'General', 100, NULL),
    ('BRG', 'Bearing', 'General', 100, NULL),
    ('BEAR', 'Bearing', 'General', 90, NULL),
    ('BEARING', 'Bearing', 'General', 100, NULL),
    ('DE', 'Drive End', 'General', 100, NULL),
    ('NDE', 'Non-Drive End', 'General', 100, NULL),
    ('FRONT', 'Front/Drive End', 'General', 80, NULL),
    ('REAR', 'Rear/Non-Drive End', 'General', 80, NULL),
    ('COOL', 'Coolant', 'General', 100, NULL),
    ('COOLANT', 'Coolant', 'General', 100, NULL),
    ('CELL', 'Battery Cell', 'Energy Storage', 100, NULL),
    ('MOD', 'Module', 'Solar', 100, NULL),
    ('MODULE', 'Module', 'Solar', 100, NULL),
    ('PANEL', 'Panel', 'Solar', 100, NULL)
    ON CONFLICT (abbreviation, context) DO UPDATE SET expansion = EXCLUDED.expansion;

    -- Power/Electrical abbreviations
    INSERT INTO knowledge_abbreviations (abbreviation, expansion, context, priority, measurement_type_id) VALUES
    ('P', 'Power', 'General', 50, power_id),
    ('PWR', 'Power', 'General', 100, power_id),
    ('POWER', 'Power', 'General', 100, power_id),
    ('KW', 'Kilowatts', 'General', 100, power_id),
    ('MW', 'Megawatts', 'General', 100, power_id),
    ('WATT', 'Watts', 'General', 100, power_id),
    ('OUTPUT', 'Output Power', 'General', 80, power_id),
    ('OUT', 'Output', 'General', 70, NULL),
    ('ACT', 'Active', 'General', 80, power_id),
    ('ACTIVE', 'Active', 'General', 80, power_id),
    ('REACT', 'Reactive', 'General', 80, react_power_id),
    ('REACTIVE', 'Reactive', 'General', 80, react_power_id),
    ('Q', 'Reactive Power', 'Electrical', 70, react_power_id),
    ('KVAR', 'Kilovolt-Ampere Reactive', 'General', 100, react_power_id),
    ('MVAR', 'Megavolt-Ampere Reactive', 'General', 100, react_power_id),
    ('V', 'Voltage', 'General', 50, voltage_id),
    ('VOLT', 'Voltage', 'General', 100, voltage_id),
    ('VOLTAGE', 'Voltage', 'General', 100, voltage_id),
    ('KV', 'Kilovolts', 'General', 100, voltage_id),
    ('I', 'Current', 'Electrical', 50, current_id),
    ('CURR', 'Current', 'General', 100, current_id),
    ('CURRENT', 'Current', 'General', 100, current_id),
    ('AMP', 'Amperage', 'General', 100, current_id),
    ('AMPS', 'Amperage', 'General', 100, current_id),
    ('FREQ', 'Frequency', 'General', 100, freq_id),
    ('FREQUENCY', 'Frequency', 'General', 100, freq_id),
    ('HZ', 'Hertz', 'General', 100, freq_id),
    ('PF', 'Power Factor', 'General', 100, NULL),
    ('DC', 'Direct Current', 'General', 100, NULL),
    ('AC', 'Alternating Current', 'General', 100, NULL),
    ('GRID', 'Grid/Utility', 'General', 100, NULL),
    ('GEN', 'Generator', 'General', 100, NULL),
    ('GENERATOR', 'Generator', 'General', 100, NULL),
    ('INV', 'Inverter', 'Solar', 100, NULL),
    ('INVERTER', 'Inverter', 'Solar', 100, NULL)
    ON CONFLICT (abbreviation, context) DO UPDATE SET expansion = EXCLUDED.expansion;

    -- Pressure/Flow abbreviations
    INSERT INTO knowledge_abbreviations (abbreviation, expansion, context, priority, measurement_type_id) VALUES
    ('PRESS', 'Pressure', 'General', 100, pressure_id),
    ('PRESSURE', 'Pressure', 'General', 100, pressure_id),
    ('PSI', 'Pounds per Square Inch', 'General', 100, pressure_id),
    ('BAR', 'Bar', 'General', 100, pressure_id),
    ('HPA', 'Hectopascal', 'General', 100, pressure_id),
    ('BARO', 'Barometric', 'General', 100, pressure_id),
    ('DP', 'Differential Pressure', 'General', 100, pressure_id),
    ('DIS', 'Discharge', 'General', 100, NULL),
    ('DISCH', 'Discharge', 'General', 100, NULL),
    ('DISCHARGE', 'Discharge', 'General', 100, NULL),
    ('SUC', 'Suction', 'General', 100, NULL),
    ('SUCTION', 'Suction', 'General', 100, NULL),
    ('IN', 'Inlet', 'General', 80, NULL),
    ('INLET', 'Inlet', 'General', 100, NULL),
    ('FLOW', 'Flow Rate', 'General', 100, flow_id),
    ('FLW', 'Flow Rate', 'General', 90, flow_id),
    ('GPM', 'Gallons per Minute', 'General', 100, flow_id),
    ('CFM', 'Cubic Feet per Minute', 'General', 100, flow_id),
    ('SCFM', 'Standard CFM', 'General', 100, flow_id)
    ON CONFLICT (abbreviation, context) DO UPDATE SET expansion = EXCLUDED.expansion;

    -- Rotational/Mechanical abbreviations
    INSERT INTO knowledge_abbreviations (abbreviation, expansion, context, priority, measurement_type_id) VALUES
    ('RPM', 'Revolutions per Minute', 'General', 100, rpm_id),
    ('SPEED', 'Speed', 'General', 100, rpm_id),
    ('SPD', 'Speed', 'General', 90, rpm_id),
    ('ROTOR', 'Rotor', 'Wind', 100, NULL),
    ('ROT', 'Rotor/Rotation', 'General', 80, NULL),
    ('VIB', 'Vibration', 'General', 100, vibration_id),
    ('VIBRATION', 'Vibration', 'General', 100, vibration_id),
    ('ACCEL', 'Acceleration', 'General', 100, vibration_id),
    ('POS', 'Position', 'General', 100, position_id),
    ('POSITION', 'Position', 'General', 100, position_id),
    ('ANGLE', 'Angle', 'General', 100, angle_id),
    ('ANG', 'Angle', 'General', 90, angle_id),
    ('DEG', 'Degrees', 'General', 100, angle_id),
    ('PITCH', 'Pitch Angle', 'Wind', 100, angle_id),
    ('YAW', 'Yaw Position', 'Wind', 100, angle_id),
    ('TILT', 'Tilt Angle', 'Solar', 100, angle_id),
    ('TRACK', 'Tracker', 'Solar', 100, angle_id),
    ('TRACKER', 'Tracker', 'Solar', 100, angle_id),
    ('BLADE', 'Blade', 'Wind', 100, NULL),
    ('A', 'Phase A / Blade A', 'General', 30, NULL),
    ('B', 'Phase B / Blade B', 'General', 30, NULL),
    ('C', 'Phase C / Blade C', 'General', 30, NULL),
    ('LVL', 'Level', 'General', 100, level_id),
    ('LEVEL', 'Level', 'General', 100, level_id),
    ('MOTOR', 'Motor', 'General', 100, NULL),
    ('MTR', 'Motor', 'General', 90, NULL),
    ('PUMP', 'Pump', 'General', 100, NULL),
    ('PMP', 'Pump', 'General', 90, NULL),
    ('COMP', 'Compressor', 'General', 100, NULL),
    ('COMPRESSOR', 'Compressor', 'General', 100, NULL),
    ('FAN', 'Fan', 'General', 100, NULL),
    ('VALVE', 'Valve', 'General', 100, NULL),
    ('VLV', 'Valve', 'General', 90, NULL),
    ('DAMPER', 'Damper', 'HVAC', 100, NULL)
    ON CONFLICT (abbreviation, context) DO UPDATE SET expansion = EXCLUDED.expansion;

    -- Wind-specific abbreviations
    INSERT INTO knowledge_abbreviations (abbreviation, expansion, context, priority, measurement_type_id) VALUES
    ('WS', 'Wind Speed', 'Wind', 100, wind_speed_id),
    ('WIND', 'Wind', 'Wind', 100, NULL),
    ('WD', 'Wind Direction', 'Wind', 100, wind_dir_id),
    ('WDIR', 'Wind Direction', 'Wind', 100, wind_dir_id),
    ('DIR', 'Direction', 'General', 80, NULL),
    ('DIRECTION', 'Direction', 'General', 80, NULL),
    ('ANEM', 'Anemometer', 'Wind', 100, wind_speed_id),
    ('GUST', 'Wind Gust', 'Wind', 100, wind_speed_id),
    ('WTG', 'Wind Turbine Generator', 'Wind', 100, NULL),
    ('WT', 'Wind Turbine', 'Wind', 90, NULL),
    ('TURBINE', 'Turbine', 'Wind', 100, NULL),
    ('TUR', 'Turbine', 'Wind', 80, NULL),
    ('TOWER', 'Tower', 'Wind', 100, NULL),
    ('TWR', 'Tower', 'Wind', 90, NULL),
    ('HUB', 'Hub', 'Wind', 100, NULL)
    ON CONFLICT (abbreviation, context) DO UPDATE SET expansion = EXCLUDED.expansion;

    -- Solar-specific abbreviations
    INSERT INTO knowledge_abbreviations (abbreviation, expansion, context, priority, measurement_type_id) VALUES
    ('GHI', 'Global Horizontal Irradiance', 'Solar', 100, irradiance_id),
    ('POA', 'Plane of Array Irradiance', 'Solar', 100, irradiance_id),
    ('DNI', 'Direct Normal Irradiance', 'Solar', 100, irradiance_id),
    ('DHI', 'Diffuse Horizontal Irradiance', 'Solar', 100, irradiance_id),
    ('IRRAD', 'Irradiance', 'Solar', 100, irradiance_id),
    ('RAD', 'Radiation', 'Solar', 80, irradiance_id),
    ('SOLAR', 'Solar', 'Solar', 100, NULL),
    ('PV', 'Photovoltaic', 'Solar', 100, NULL),
    ('ARRAY', 'Array', 'Solar', 100, NULL),
    ('STRING', 'String', 'Solar', 100, NULL),
    ('STR', 'String', 'Solar', 90, NULL),
    ('MPPT', 'Maximum Power Point Tracker', 'Solar', 100, NULL),
    ('PR', 'Performance Ratio', 'Solar', 100, efficiency_id),
    ('PERF', 'Performance', 'Solar', 90, efficiency_id),
    ('EFF', 'Efficiency', 'General', 100, efficiency_id),
    ('EFFICIENCY', 'Efficiency', 'General', 100, efficiency_id)
    ON CONFLICT (abbreviation, context) DO UPDATE SET expansion = EXCLUDED.expansion;

    -- Battery/Energy Storage abbreviations
    INSERT INTO knowledge_abbreviations (abbreviation, expansion, context, priority, measurement_type_id) VALUES
    ('SOC', 'State of Charge', 'Energy Storage', 100, soc_id),
    ('SOH', 'State of Health', 'Energy Storage', 100, NULL),
    ('DOD', 'Depth of Discharge', 'Energy Storage', 100, NULL),
    ('BESS', 'Battery Energy Storage System', 'Energy Storage', 100, NULL),
    ('ESS', 'Energy Storage System', 'Energy Storage', 100, NULL),
    ('BAT', 'Battery', 'Energy Storage', 100, NULL),
    ('BATTERY', 'Battery', 'Energy Storage', 100, NULL),
    ('PACK', 'Battery Pack', 'Energy Storage', 100, NULL),
    ('RACK', 'Battery Rack', 'Energy Storage', 100, NULL),
    ('BMS', 'Battery Management System', 'Energy Storage', 100, NULL),
    ('CHARGE', 'Charge', 'Energy Storage', 100, NULL),
    ('DCHG', 'Discharge', 'Energy Storage', 100, NULL),
    ('CYCLE', 'Cycle Count', 'Energy Storage', 100, NULL)
    ON CONFLICT (abbreviation, context) DO UPDATE SET expansion = EXCLUDED.expansion;

    -- Environmental abbreviations  
    INSERT INTO knowledge_abbreviations (abbreviation, expansion, context, priority, measurement_type_id) VALUES
    ('RH', 'Relative Humidity', 'General', 100, humidity_id),
    ('HUMID', 'Humidity', 'General', 100, humidity_id),
    ('HUMIDITY', 'Humidity', 'General', 100, humidity_id),
    ('PRECIP', 'Precipitation', 'General', 100, NULL),
    ('RAIN', 'Rainfall', 'General', 100, NULL),
    ('RAINFALL', 'Rainfall', 'General', 100, NULL),
    ('DEW', 'Dew Point', 'General', 100, temp_id),
    ('DEWPOINT', 'Dew Point', 'General', 100, temp_id),
    ('MET', 'Meteorological', 'General', 100, NULL),
    ('WEATHER', 'Weather', 'General', 100, NULL),
    ('AIR', 'Air', 'General', 80, NULL),
    ('OUTDOOR', 'Outdoor', 'General', 80, NULL),
    ('OUTSIDE', 'Outside', 'General', 80, NULL),
    ('INDOOR', 'Indoor', 'General', 80, NULL),
    ('INSIDE', 'Inside', 'General', 80, NULL),
    ('DENSITY', 'Density', 'General', 100, NULL),
    ('RHO', 'Air Density', 'Wind', 100, NULL)
    ON CONFLICT (abbreviation, context) DO UPDATE SET expansion = EXCLUDED.expansion;

    -- Status/Control abbreviations
    INSERT INTO knowledge_abbreviations (abbreviation, expansion, context, priority, measurement_type_id) VALUES
    ('STS', 'Status', 'General', 100, NULL),
    ('STATUS', 'Status', 'General', 100, NULL),
    ('STATE', 'State', 'General', 100, NULL),
    ('MODE', 'Mode', 'General', 100, NULL),
    ('CMD', 'Command', 'General', 100, NULL),
    ('COMMAND', 'Command', 'General', 100, NULL),
    ('SP', 'Setpoint', 'General', 100, NULL),
    ('SETPOINT', 'Setpoint', 'General', 100, NULL),
    ('SET', 'Setpoint', 'General', 80, NULL),
    ('PV', 'Process Value', 'General', 70, NULL),
    ('CV', 'Control Value', 'General', 70, NULL),
    ('ALM', 'Alarm', 'General', 100, NULL),
    ('ALARM', 'Alarm', 'General', 100, NULL),
    ('FLT', 'Fault', 'General', 100, NULL),
    ('FAULT', 'Fault', 'General', 100, NULL),
    ('ERR', 'Error', 'General', 100, NULL),
    ('ERROR', 'Error', 'General', 100, NULL),
    ('WARN', 'Warning', 'General', 100, NULL),
    ('WARNING', 'Warning', 'General', 100, NULL),
    ('RUN', 'Running', 'General', 100, NULL),
    ('RUNNING', 'Running', 'General', 100, NULL),
    ('STOP', 'Stopped', 'General', 100, NULL),
    ('STOPPED', 'Stopped', 'General', 100, NULL),
    ('START', 'Start', 'General', 100, NULL),
    ('ENABLE', 'Enable', 'General', 100, NULL),
    ('DISABLE', 'Disable', 'General', 100, NULL),
    ('ON', 'On', 'General', 80, NULL),
    ('OFF', 'Off', 'General', 80, NULL),
    ('OPEN', 'Open', 'General', 100, NULL),
    ('CLOSE', 'Close', 'General', 100, NULL),
    ('CLOSED', 'Closed', 'General', 100, NULL),
    ('AUTO', 'Automatic', 'General', 100, NULL),
    ('MANUAL', 'Manual', 'General', 100, NULL),
    ('MAN', 'Manual', 'General', 90, NULL),
    ('LOCAL', 'Local', 'General', 100, NULL),
    ('REMOTE', 'Remote', 'General', 100, NULL),
    ('AVAIL', 'Available', 'General', 100, NULL),
    ('AVAILABLE', 'Available', 'General', 100, NULL),
    ('UNAVAIL', 'Unavailable', 'General', 100, NULL),
    ('READY', 'Ready', 'General', 100, NULL),
    ('TRIP', 'Trip/Tripped', 'General', 100, NULL),
    ('TRIPPED', 'Tripped', 'General', 100, NULL),
    ('RESET', 'Reset', 'General', 100, NULL)
    ON CONFLICT (abbreviation, context) DO UPDATE SET expansion = EXCLUDED.expansion;

    -- Numeric/Qualifier abbreviations
    INSERT INTO knowledge_abbreviations (abbreviation, expansion, context, priority, measurement_type_id) VALUES
    ('MIN', 'Minimum', 'General', 100, NULL),
    ('MAX', 'Maximum', 'General', 100, NULL),
    ('AVG', 'Average', 'General', 100, NULL),
    ('AVERAGE', 'Average', 'General', 100, NULL),
    ('MEAN', 'Mean', 'General', 100, NULL),
    ('SUM', 'Sum/Total', 'General', 100, NULL),
    ('TOTAL', 'Total', 'General', 100, NULL),
    ('TOT', 'Total', 'General', 90, NULL),
    ('CUM', 'Cumulative', 'General', 100, NULL),
    ('CUMULATIVE', 'Cumulative', 'General', 100, NULL),
    ('INST', 'Instantaneous', 'General', 100, NULL),
    ('INSTANTANEOUS', 'Instantaneous', 'General', 100, NULL),
    ('RAW', 'Raw/Unprocessed', 'General', 100, NULL),
    ('CALC', 'Calculated', 'General', 100, NULL),
    ('CALCULATED', 'Calculated', 'General', 100, NULL),
    ('ACT', 'Actual', 'General', 80, NULL),
    ('ACTUAL', 'Actual', 'General', 80, NULL),
    ('REF', 'Reference', 'General', 100, NULL),
    ('REFERENCE', 'Reference', 'General', 100, NULL),
    ('DELTA', 'Delta/Change', 'General', 100, NULL),
    ('DIFF', 'Difference', 'General', 100, NULL),
    ('RATE', 'Rate', 'General', 100, NULL),
    ('PCT', 'Percent', 'General', 100, NULL),
    ('PERCENT', 'Percent', 'General', 100, NULL)
    ON CONFLICT (abbreviation, context) DO UPDATE SET expansion = EXCLUDED.expansion;

END $$;
