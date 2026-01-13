-- NAIA v3 Renewable Energy & Weather Pattern Seeds
-- Adds wind turbine, solar, battery, weather patterns plus anomaly detection
-- Run this migration after 02-pattern-engine-schema.sql

-- ===========================================================================
-- WIND TURBINE PATTERNS
-- ===========================================================================

-- Horizontal Axis Wind Turbine (HAWT) - matches Kelmarsh data
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('22222222-0001-0001-0001-000000000001', 'Horizontal Axis Wind Turbine', 'WindTurbine', 
 'Standard HAWT with rotor, generator, pitch control, and nacelle systems. Matches Kelmarsh-style SCADA data.', 
 0.85, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description, confidence = EXCLUDED.confidence;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('22222222-0001-0001-0001-000000000001', 'Wind Speed', 'Nacelle anemometer wind speed', 
 ARRAY['.*WIND.*SPEED.*', '.*WS.*', '.*ANEM.*', '.*WindSpeed.*'], 0, 35, 'm/s', true, 1.5),
('22222222-0001-0001-0001-000000000001', 'Active Power', 'Electrical power output', 
 ARRAY['.*POWER.*', '.*KW.*', '.*MW.*', '.*Power.*', '.*OUTPUT.*'], -100, 5000, 'kW', true, 1.5),
('22222222-0001-0001-0001-000000000001', 'Rotor Speed', 'Rotor rotational speed', 
 ARRAY['.*ROTOR.*RPM.*', '.*ROTOR.*SPEED.*', '.*RotorRPM.*'], 0, 25, 'RPM', true, 1.2),
('22222222-0001-0001-0001-000000000001', 'Generator Speed', 'Generator rotational speed', 
 ARRAY['.*GEN.*RPM.*', '.*GEN.*SPEED.*', '.*GeneratorRPM.*'], 0, 2000, 'RPM', true, 1.2),
('22222222-0001-0001-0001-000000000001', 'Pitch Angle A', 'Blade A pitch position', 
 ARRAY['.*PITCH.*A.*', '.*BLADE.*A.*', '.*PitchA.*'], -5, 95, 'deg', true, 1.0),
('22222222-0001-0001-0001-000000000001', 'Pitch Angle B', 'Blade B pitch position', 
 ARRAY['.*PITCH.*B.*', '.*BLADE.*B.*', '.*PitchB.*'], -5, 95, 'deg', false, 1.0),
('22222222-0001-0001-0001-000000000001', 'Pitch Angle C', 'Blade C pitch position', 
 ARRAY['.*PITCH.*C.*', '.*BLADE.*C.*', '.*PitchC.*'], -5, 95, 'deg', false, 1.0),
('22222222-0001-0001-0001-000000000001', 'Wind Direction', 'Wind direction from nacelle', 
 ARRAY['.*WIND.*DIR.*', '.*WindDirection.*', '.*WD.*'], 0, 360, 'deg', true, 1.0),
('22222222-0001-0001-0001-000000000001', 'Nacelle Position', 'Nacelle yaw position', 
 ARRAY['.*NAC.*POS.*', '.*YAW.*', '.*NacellePosition.*'], 0, 360, 'deg', true, 1.0),
('22222222-0001-0001-0001-000000000001', 'Nacelle Temperature', 'Internal nacelle temperature', 
 ARRAY['.*NAC.*TEMP.*', '.*NacelleTemp.*', '.*NACELLE.*T.*'], -40, 80, 'C', false, 0.8),
('22222222-0001-0001-0001-000000000001', 'Gearbox Oil Temperature', 'Gearbox lubricant temperature', 
 ARRAY['.*GEAR.*OIL.*TEMP.*', '.*GearOilTemp.*', '.*GBOX.*T.*'], 20, 100, 'C', false, 1.0),
('22222222-0001-0001-0001-000000000001', 'Generator Bearing Front Temp', 'Generator front bearing temperature', 
 ARRAY['.*GEN.*BEAR.*FRONT.*', '.*GenBearingFrontTemp.*', '.*GB.*DE.*'], 20, 120, 'C', false, 1.0),
('22222222-0001-0001-0001-000000000001', 'Generator Bearing Rear Temp', 'Generator rear bearing temperature', 
 ARRAY['.*GEN.*BEAR.*REAR.*', '.*GenBearingRearTemp.*', '.*GB.*NDE.*'], 20, 120, 'C', false, 1.0),
('22222222-0001-0001-0001-000000000001', 'Grid Voltage', 'Grid connection voltage', 
 ARRAY['.*GRID.*VOLT.*', '.*GridVoltage.*', '.*V.*GRID.*'], 300, 750, 'V', false, 0.6),
('22222222-0001-0001-0001-000000000001', 'Grid Frequency', 'Grid frequency', 
 ARRAY['.*GRID.*FREQ.*', '.*GridFrequency.*', '.*HZ.*'], 45, 65, 'Hz', false, 0.6),
('22222222-0001-0001-0001-000000000001', 'Reactive Power', 'Reactive power output', 
 ARRAY['.*REACT.*POWER.*', '.*KVAR.*', '.*ReactivePower.*', '.*Q.*'], -2000, 2000, 'kvar', false, 0.5),
('22222222-0001-0001-0001-000000000001', 'Ambient Temperature', 'Outside air temperature', 
 ARRAY['.*AMB.*TEMP.*', '.*AmbientTemp.*', '.*OAT.*', '.*OUTSIDE.*'], -40, 50, 'C', false, 0.8)
ON CONFLICT (pattern_id, name) DO UPDATE SET naming_patterns = EXCLUDED.naming_patterns, weight = EXCLUDED.weight;

-- Offshore Wind Turbine (larger, different characteristics)
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('22222222-0001-0001-0001-000000000002', 'Offshore Wind Turbine', 'WindTurbine', 
 'Large offshore wind turbine with enhanced monitoring for marine conditions', 
 0.75, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('22222222-0001-0001-0001-000000000002', 'Wind Speed', 'Nacelle anemometer wind speed', 
 ARRAY['.*WIND.*SPEED.*', '.*WS.*'], 0, 40, 'm/s', true, 1.5),
('22222222-0001-0001-0001-000000000002', 'Active Power', 'Electrical power output', 
 ARRAY['.*POWER.*', '.*MW.*'], -500, 15000, 'kW', true, 1.5),
('22222222-0001-0001-0001-000000000002', 'Wave Height', 'Significant wave height', 
 ARRAY['.*WAVE.*', '.*SWH.*', '.*SEA.*'], 0, 15, 'm', false, 1.0),
('22222222-0001-0001-0001-000000000002', 'Platform Acceleration', 'Foundation movement', 
 ARRAY['.*ACCEL.*', '.*MOTION.*', '.*SWAY.*'], 0, 5, 'm/s²', false, 1.0),
('22222222-0001-0001-0001-000000000002', 'Nacelle Humidity', 'Internal nacelle humidity', 
 ARRAY['.*HUMID.*', '.*RH.*NAC.*'], 0, 100, '%', false, 0.8)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- ===========================================================================
-- SOLAR PATTERNS
-- ===========================================================================

-- Utility-Scale Solar Array
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('22222222-0002-0001-0001-000000000001', 'Utility Solar Array', 'Solar', 
 'Utility-scale photovoltaic array with inverters and trackers', 
 0.80, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('22222222-0002-0001-0001-000000000001', 'DC Power', 'DC power from panels', 
 ARRAY['.*DC.*POWER.*', '.*PDC.*', '.*ARRAY.*P.*'], 0, 5000, 'kW', true, 1.5),
('22222222-0002-0001-0001-000000000001', 'AC Power', 'AC power after inverter', 
 ARRAY['.*AC.*POWER.*', '.*PAC.*', '.*INV.*OUT.*'], 0, 5000, 'kW', true, 1.5),
('22222222-0002-0001-0001-000000000001', 'Irradiance GHI', 'Global horizontal irradiance', 
 ARRAY['.*GHI.*', '.*IRRAD.*', '.*SOLAR.*RAD.*'], 0, 1400, 'W/m²', true, 1.2),
('22222222-0002-0001-0001-000000000001', 'Irradiance POA', 'Plane of array irradiance', 
 ARRAY['.*POA.*', '.*PLANE.*ARRAY.*'], 0, 1400, 'W/m²', false, 1.0),
('22222222-0002-0001-0001-000000000001', 'Module Temperature', 'PV module temperature', 
 ARRAY['.*MOD.*TEMP.*', '.*PANEL.*T.*', '.*CELL.*T.*'], -40, 90, 'C', true, 1.0),
('22222222-0002-0001-0001-000000000001', 'Ambient Temperature', 'Ambient air temperature', 
 ARRAY['.*AMB.*TEMP.*', '.*OAT.*', '.*AIR.*T.*'], -40, 50, 'C', false, 0.8),
('22222222-0002-0001-0001-000000000001', 'DC Voltage', 'String DC voltage', 
 ARRAY['.*DC.*VOLT.*', '.*VDC.*', '.*STRING.*V.*'], 0, 1500, 'V', false, 0.8),
('22222222-0002-0001-0001-000000000001', 'DC Current', 'String DC current', 
 ARRAY['.*DC.*CURR.*', '.*IDC.*', '.*STRING.*I.*'], 0, 500, 'A', false, 0.8),
('22222222-0002-0001-0001-000000000001', 'Inverter Efficiency', 'Inverter conversion efficiency', 
 ARRAY['.*EFF.*', '.*EFFICIENCY.*'], 0, 100, '%', false, 0.6),
('22222222-0002-0001-0001-000000000001', 'Tracker Angle', 'Single-axis tracker position', 
 ARRAY['.*TRACK.*', '.*TILT.*', '.*ANGLE.*'], -60, 60, 'deg', false, 0.7),
('22222222-0002-0001-0001-000000000001', 'Performance Ratio', 'Actual vs theoretical output', 
 ARRAY['.*PR.*', '.*PERF.*RATIO.*'], 0, 100, '%', false, 0.9),
('22222222-0002-0001-0001-000000000001', 'Energy Today', 'Cumulative energy production today', 
 ARRAY['.*ENERGY.*DAY.*', '.*KWH.*TODAY.*'], 0, 100000, 'kWh', false, 0.5)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Rooftop/Commercial Solar
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('22222222-0002-0001-0001-000000000002', 'Commercial Rooftop Solar', 'Solar', 
 'Commercial rooftop PV system with string inverters', 
 0.75, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('22222222-0002-0001-0001-000000000002', 'AC Power', 'Total AC power output', 
 ARRAY['.*POWER.*', '.*KW.*', '.*OUTPUT.*'], 0, 500, 'kW', true, 1.5),
('22222222-0002-0001-0001-000000000002', 'Energy Export', 'Energy exported to grid', 
 ARRAY['.*EXPORT.*', '.*GRID.*FEED.*'], 0, 10000, 'kWh', false, 0.8),
('22222222-0002-0001-0001-000000000002', 'Inverter Status', 'Inverter operational status', 
 ARRAY['.*STATUS.*', '.*STATE.*', '.*RUN.*'], 0, 1, '', false, 0.6)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- ===========================================================================
-- BATTERY STORAGE PATTERNS
-- ===========================================================================

-- Grid-Scale Battery Energy Storage System (BESS)
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('22222222-0003-0001-0001-000000000001', 'Battery Energy Storage System', 'EnergyStorage', 
 'Utility-scale lithium-ion battery storage with BMS monitoring', 
 0.80, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('22222222-0003-0001-0001-000000000001', 'State of Charge', 'Battery state of charge', 
 ARRAY['.*SOC.*', '.*STATE.*CHARGE.*', '.*CHARGE.*%.*'], 0, 100, '%', true, 1.5),
('22222222-0003-0001-0001-000000000001', 'State of Health', 'Battery degradation level', 
 ARRAY['.*SOH.*', '.*HEALTH.*', '.*DEGRADATION.*'], 0, 100, '%', false, 1.2),
('22222222-0003-0001-0001-000000000001', 'Power', 'Charge/discharge power (positive=discharge)', 
 ARRAY['.*POWER.*', '.*KW.*', '.*MW.*'], -10000, 10000, 'kW', true, 1.5),
('22222222-0003-0001-0001-000000000001', 'DC Voltage', 'Battery DC bus voltage', 
 ARRAY['.*DC.*V.*', '.*VOLT.*', '.*VDC.*'], 0, 1500, 'V', true, 1.0),
('22222222-0003-0001-0001-000000000001', 'DC Current', 'Battery DC current', 
 ARRAY['.*DC.*CURR.*', '.*IDC.*', '.*AMP.*'], -2000, 2000, 'A', false, 0.8),
('22222222-0003-0001-0001-000000000001', 'Cell Temperature Max', 'Hottest cell temperature', 
 ARRAY['.*CELL.*TEMP.*MAX.*', '.*T.*MAX.*', '.*HOT.*CELL.*'], -20, 60, 'C', true, 1.2),
('22222222-0003-0001-0001-000000000001', 'Cell Temperature Min', 'Coldest cell temperature', 
 ARRAY['.*CELL.*TEMP.*MIN.*', '.*T.*MIN.*', '.*COLD.*CELL.*'], -20, 60, 'C', false, 1.0),
('22222222-0003-0001-0001-000000000001', 'Cell Voltage Max', 'Highest cell voltage', 
 ARRAY['.*CELL.*V.*MAX.*', '.*VCELL.*MAX.*'], 2.5, 4.3, 'V', false, 1.0),
('22222222-0003-0001-0001-000000000001', 'Cell Voltage Min', 'Lowest cell voltage', 
 ARRAY['.*CELL.*V.*MIN.*', '.*VCELL.*MIN.*'], 2.5, 4.3, 'V', false, 1.0),
('22222222-0003-0001-0001-000000000001', 'Coolant Temperature', 'Battery coolant temperature', 
 ARRAY['.*COOL.*TEMP.*', '.*GLYCOL.*T.*'], -20, 50, 'C', false, 0.8),
('22222222-0003-0001-0001-000000000001', 'Cycle Count', 'Number of charge/discharge cycles', 
 ARRAY['.*CYCLE.*', '.*CYCLES.*'], 0, 10000, 'cycles', false, 0.6)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- ===========================================================================
-- WEATHER STATION PATTERNS
-- ===========================================================================

-- Meteorological Station (MET Tower)
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('22222222-0004-0001-0001-000000000001', 'Meteorological Station', 'Weather', 
 'Weather monitoring station with wind, temperature, pressure, and solar sensors', 
 0.85, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('22222222-0004-0001-0001-000000000001', 'Wind Speed', 'Wind speed measurement', 
 ARRAY['.*WIND.*SPEED.*', '.*WS.*', '.*ANEM.*'], 0, 50, 'm/s', true, 1.5),
('22222222-0004-0001-0001-000000000001', 'Wind Direction', 'Wind direction', 
 ARRAY['.*WIND.*DIR.*', '.*WD.*', '.*WDIR.*'], 0, 360, 'deg', true, 1.2),
('22222222-0004-0001-0001-000000000001', 'Air Temperature', 'Ambient air temperature', 
 ARRAY['.*AIR.*TEMP.*', '.*TEMP.*', '.*OAT.*', '.*T.*AIR.*'], -50, 60, 'C', true, 1.2),
('22222222-0004-0001-0001-000000000001', 'Relative Humidity', 'Air relative humidity', 
 ARRAY['.*HUMID.*', '.*RH.*', '.*REL.*HUM.*'], 0, 100, '%', true, 1.0),
('22222222-0004-0001-0001-000000000001', 'Barometric Pressure', 'Atmospheric pressure', 
 ARRAY['.*PRESS.*', '.*BARO.*', '.*HPA.*', '.*MBAR.*'], 900, 1100, 'hPa', true, 1.0),
('22222222-0004-0001-0001-000000000001', 'Solar Radiation GHI', 'Global horizontal irradiance', 
 ARRAY['.*GHI.*', '.*SOLAR.*', '.*IRRAD.*', '.*RAD.*'], 0, 1400, 'W/m²', false, 1.0),
('22222222-0004-0001-0001-000000000001', 'Precipitation', 'Rainfall amount', 
 ARRAY['.*PRECIP.*', '.*RAIN.*', '.*RAINFALL.*'], 0, 500, 'mm', false, 0.8),
('22222222-0004-0001-0001-000000000001', 'Dew Point', 'Dew point temperature', 
 ARRAY['.*DEW.*', '.*DP.*', '.*DEWPOINT.*'], -50, 40, 'C', false, 0.6),
('22222222-0004-0001-0001-000000000001', 'Wind Gust', 'Maximum wind gust', 
 ARRAY['.*GUST.*', '.*MAX.*WIND.*'], 0, 80, 'm/s', false, 0.8),
('22222222-0004-0001-0001-000000000001', 'Air Density', 'Calculated air density', 
 ARRAY['.*DENSITY.*', '.*RHO.*', '.*AIR.*DENS.*'], 0.9, 1.4, 'kg/m³', false, 0.7)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- ===========================================================================
-- GRID/SUBSTATION PATTERNS
-- ===========================================================================

-- Grid Connection Point / Substation
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('22222222-0005-0001-0001-000000000001', 'Grid Connection Point', 'Grid', 
 'Point of interconnection with utility grid, metering and protection', 
 0.80, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('22222222-0005-0001-0001-000000000001', 'Active Power Export', 'Power exported to grid', 
 ARRAY['.*EXPORT.*P.*', '.*GRID.*P.*', '.*POI.*MW.*'], -100, 500, 'MW', true, 1.5),
('22222222-0005-0001-0001-000000000001', 'Reactive Power', 'Reactive power at POI', 
 ARRAY['.*REACT.*', '.*Q.*', '.*MVAR.*'], -200, 200, 'Mvar', true, 1.2),
('22222222-0005-0001-0001-000000000001', 'Voltage', 'Grid voltage', 
 ARRAY['.*VOLT.*', '.*V.*', '.*KV.*'], 0, 500, 'kV', true, 1.0),
('22222222-0005-0001-0001-000000000001', 'Frequency', 'Grid frequency', 
 ARRAY['.*FREQ.*', '.*HZ.*'], 45, 65, 'Hz', true, 1.2),
('22222222-0005-0001-0001-000000000001', 'Power Factor', 'Power factor at POI', 
 ARRAY['.*PF.*', '.*POWER.*FACTOR.*', '.*COS.*'], -1, 1, '', false, 0.8),
('22222222-0005-0001-0001-000000000001', 'Line Current', 'Transmission line current', 
 ARRAY['.*CURR.*', '.*AMP.*', '.*I.*LINE.*'], 0, 5000, 'A', false, 0.8)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- ===========================================================================
-- ANOMALY DETECTION PATTERNS (Critical!)
-- These patterns detect abnormal conditions rather than equipment types
-- ===========================================================================

-- Wind Turbine Underperformance Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('33333333-0001-0001-0001-000000000001', 'Wind Turbine Underperformance', 'Anomaly', 
 'Detects when turbine produces less power than expected for given wind conditions', 
 0.70, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('33333333-0001-0001-0001-000000000001', 'Wind Speed', 'Current wind speed for reference', 
 ARRAY['.*WIND.*SPEED.*', '.*WS.*'], 3, 25, 'm/s', true, 1.5),
('33333333-0001-0001-0001-000000000001', 'Power Output', 'Actual power output', 
 ARRAY['.*POWER.*', '.*KW.*'], 0, 5000, 'kW', true, 1.5),
('33333333-0001-0001-0001-000000000001', 'Power Curve Deviation', 'Deviation from expected power curve', 
 ARRAY['.*DEVIATION.*', '.*DELTA.*P.*', '.*UNDERPERF.*'], -100, 0, '%', false, 2.0)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Bearing Temperature Anomaly
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('33333333-0001-0001-0001-000000000002', 'Bearing Temperature Anomaly', 'Anomaly', 
 'Detects abnormal bearing temperatures indicating potential failure', 
 0.75, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('33333333-0001-0001-0001-000000000002', 'Bearing Temperature', 'Current bearing temperature', 
 ARRAY['.*BEAR.*TEMP.*', '.*TB.*', '.*BEARING.*T.*'], 20, 120, 'C', true, 2.0),
('33333333-0001-0001-0001-000000000002', 'Ambient Temperature', 'Reference ambient temperature', 
 ARRAY['.*AMB.*TEMP.*', '.*OAT.*'], -40, 50, 'C', false, 0.8),
('33333333-0001-0001-0001-000000000002', 'Temperature Rise Rate', 'Rate of temperature increase', 
 ARRAY['.*RATE.*', '.*DELTA.*T.*'], 0, 10, 'C/hr', false, 1.5),
('33333333-0001-0001-0001-000000000002', 'Operating Status', 'Equipment running status', 
 ARRAY['.*STATUS.*', '.*RUN.*', '.*OPERATING.*'], 0, 1, '', false, 0.5)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Pitch System Fault Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('33333333-0001-0001-0001-000000000003', 'Pitch System Imbalance', 'Anomaly', 
 'Detects when blade pitch angles are misaligned, indicating pitch system fault', 
 0.70, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('33333333-0001-0001-0001-000000000003', 'Pitch Angle A', 'Blade A pitch angle', 
 ARRAY['.*PITCH.*A.*', '.*BLADE.*A.*'], -5, 95, 'deg', true, 1.5),
('33333333-0001-0001-0001-000000000003', 'Pitch Angle B', 'Blade B pitch angle', 
 ARRAY['.*PITCH.*B.*', '.*BLADE.*B.*'], -5, 95, 'deg', true, 1.5),
('33333333-0001-0001-0001-000000000003', 'Pitch Angle C', 'Blade C pitch angle', 
 ARRAY['.*PITCH.*C.*', '.*BLADE.*C.*'], -5, 95, 'deg', true, 1.5),
('33333333-0001-0001-0001-000000000003', 'Pitch Deviation', 'Max deviation between blades', 
 ARRAY['.*DEVIATION.*', '.*IMBALANCE.*'], 0, 15, 'deg', false, 2.0)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Gearbox Oil Temperature Anomaly
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('33333333-0001-0001-0001-000000000004', 'Gearbox Oil Temperature Anomaly', 'Anomaly', 
 'Detects abnormal gearbox oil temperatures indicating lubrication or wear issues', 
 0.75, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('33333333-0001-0001-0001-000000000004', 'Gearbox Oil Temperature', 'Current gearbox oil temperature', 
 ARRAY['.*GEAR.*OIL.*TEMP.*', '.*GBOX.*T.*', '.*GearOilTemp.*'], 20, 100, 'C', true, 2.0),
('33333333-0001-0001-0001-000000000004', 'Ambient Temperature', 'Reference ambient temperature', 
 ARRAY['.*AMB.*TEMP.*', '.*OAT.*'], -40, 50, 'C', false, 0.8),
('33333333-0001-0001-0001-000000000004', 'Power Output', 'Current power output (load indicator)', 
 ARRAY['.*POWER.*', '.*KW.*'], 0, 5000, 'kW', false, 1.0)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Grid Voltage Deviation
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('33333333-0001-0001-0001-000000000005', 'Grid Voltage Deviation', 'Anomaly', 
 'Detects grid voltage outside normal operating range', 
 0.80, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('33333333-0001-0001-0001-000000000005', 'Grid Voltage', 'Current grid voltage', 
 ARRAY['.*GRID.*VOLT.*', '.*V.*GRID.*', '.*GridVoltage.*'], 300, 750, 'V', true, 2.0),
('33333333-0001-0001-0001-000000000005', 'Grid Frequency', 'Current grid frequency', 
 ARRAY['.*GRID.*FREQ.*', '.*HZ.*', '.*GridFrequency.*'], 45, 65, 'Hz', true, 1.5),
('33333333-0001-0001-0001-000000000005', 'Voltage Deviation %', 'Percentage from nominal', 
 ARRAY['.*DEV.*', '.*DELTA.*V.*'], -15, 15, '%', false, 1.8)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Solar Soiling/Degradation Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('33333333-0002-0001-0001-000000000001', 'Solar Panel Degradation', 'Anomaly', 
 'Detects reduced solar panel output due to soiling, degradation, or shading', 
 0.70, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('33333333-0002-0001-0001-000000000001', 'DC Power', 'Actual DC power output', 
 ARRAY['.*DC.*POWER.*', '.*PDC.*'], 0, 5000, 'kW', true, 1.5),
('33333333-0002-0001-0001-000000000001', 'Irradiance', 'Solar irradiance', 
 ARRAY['.*GHI.*', '.*IRRAD.*', '.*POA.*'], 0, 1400, 'W/m²', true, 1.5),
('33333333-0002-0001-0001-000000000001', 'Performance Ratio', 'Actual/expected ratio', 
 ARRAY['.*PR.*', '.*PERF.*RATIO.*'], 0, 100, '%', false, 2.0),
('33333333-0002-0001-0001-000000000001', 'Module Temperature', 'Panel temperature', 
 ARRAY['.*MOD.*TEMP.*', '.*CELL.*T.*'], -40, 90, 'C', false, 0.8)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Battery Thermal Runaway Risk
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('33333333-0003-0001-0001-000000000001', 'Battery Thermal Runaway Risk', 'Anomaly', 
 'CRITICAL: Detects conditions that could lead to battery thermal runaway', 
 0.90, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('33333333-0003-0001-0001-000000000001', 'Cell Temperature Max', 'Maximum cell temperature', 
 ARRAY['.*CELL.*TEMP.*MAX.*', '.*T.*MAX.*'], -20, 60, 'C', true, 2.5),
('33333333-0003-0001-0001-000000000001', 'Cell Temperature Delta', 'Temperature spread across cells', 
 ARRAY['.*DELTA.*T.*', '.*SPREAD.*'], 0, 20, 'C', true, 2.0),
('33333333-0003-0001-0001-000000000001', 'Cell Voltage Min', 'Minimum cell voltage', 
 ARRAY['.*CELL.*V.*MIN.*'], 2.5, 4.3, 'V', true, 1.5),
('33333333-0003-0001-0001-000000000001', 'Temperature Rise Rate', 'Rate of temperature increase', 
 ARRAY['.*RATE.*', '.*DT.*'], 0, 5, 'C/min', false, 2.5)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Yaw Misalignment Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('33333333-0001-0001-0001-000000000006', 'Yaw Misalignment', 'Anomaly', 
 'Detects when nacelle yaw does not properly track wind direction', 
 0.70, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('33333333-0001-0001-0001-000000000006', 'Nacelle Position', 'Current nacelle yaw position', 
 ARRAY['.*NAC.*POS.*', '.*YAW.*POS.*', '.*NacellePosition.*'], 0, 360, 'deg', true, 1.5),
('33333333-0001-0001-0001-000000000006', 'Wind Direction', 'Measured wind direction', 
 ARRAY['.*WIND.*DIR.*', '.*WD.*', '.*WindDirection.*'], 0, 360, 'deg', true, 1.5),
('33333333-0001-0001-0001-000000000006', 'Yaw Error', 'Difference between nacelle and wind direction', 
 ARRAY['.*YAW.*ERR.*', '.*MISALIGN.*'], -180, 180, 'deg', false, 2.0),
('33333333-0001-0001-0001-000000000006', 'Power Output', 'Current power output', 
 ARRAY['.*POWER.*', '.*KW.*'], 0, 5000, 'kW', false, 1.0)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Icing Detection Pattern
INSERT INTO patterns (id, name, category, description, confidence, is_system_pattern, source) VALUES
('33333333-0001-0001-0001-000000000007', 'Blade Icing Condition', 'Anomaly', 
 'Detects ice accumulation on wind turbine blades based on performance and weather', 
 0.65, true, 'system')
ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;

INSERT INTO pattern_roles (pattern_id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required, weight) VALUES
('33333333-0001-0001-0001-000000000007', 'Ambient Temperature', 'Current temperature', 
 ARRAY['.*AMB.*TEMP.*', '.*OAT.*', '.*AmbientTemp.*'], -40, 10, 'C', true, 1.5),
('33333333-0001-0001-0001-000000000007', 'Relative Humidity', 'Air humidity', 
 ARRAY['.*HUMID.*', '.*RH.*'], 70, 100, '%', false, 1.0),
('33333333-0001-0001-0001-000000000007', 'Power Output', 'Actual power output', 
 ARRAY['.*POWER.*', '.*KW.*'], 0, 5000, 'kW', true, 1.5),
('33333333-0001-0001-0001-000000000007', 'Wind Speed', 'Current wind speed', 
 ARRAY['.*WIND.*SPEED.*', '.*WS.*'], 3, 25, 'm/s', true, 1.2),
('33333333-0001-0001-0001-000000000007', 'Rotor Speed', 'Current rotor speed', 
 ARRAY['.*ROTOR.*RPM.*'], 0, 25, 'RPM', false, 1.0)
ON CONFLICT (pattern_id, name) DO NOTHING;

-- Add algorithm column if not exists
ALTER TABLE behavioral_clusters ADD COLUMN IF NOT EXISTS algorithm VARCHAR(50);
ALTER TABLE behavioral_clusters ADD COLUMN IF NOT EXISTS average_cohesion DOUBLE PRECISION;

-- ===========================================================================
-- VERIFY SEEDED DATA
-- ===========================================================================
DO $$
DECLARE
    pattern_count INTEGER;
    role_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO pattern_count FROM patterns;
    SELECT COUNT(*) INTO role_count FROM pattern_roles;
    RAISE NOTICE 'Pattern Flywheel seeded with % patterns and % roles', pattern_count, role_count;
END $$;
