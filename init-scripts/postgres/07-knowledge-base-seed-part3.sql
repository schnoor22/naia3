-- NAIA v3 Knowledge Base Seed Data Part 3
-- Equipment Taxonomy and Manufacturer Profiles

-- ===========================================================================
-- EQUIPMENT TAXONOMY (Hierarchical equipment classification)
-- ===========================================================================

-- Level 1: Top-level categories
INSERT INTO knowledge_equipment_taxonomy (id, name, description, parent_id, level, industry, naming_hints) VALUES
('10000000-0001-0000-0000-000000000001', 'Rotating Equipment', 'Mechanical equipment with rotating components', NULL, 1, 'General', ARRAY['pump', 'motor', 'compressor', 'fan', 'turbine', 'gearbox']),
('10000000-0002-0000-0000-000000000001', 'Heat Transfer Equipment', 'Equipment for thermal energy exchange', NULL, 1, 'General', ARRAY['exchanger', 'heater', 'cooler', 'condenser', 'boiler']),
('10000000-0003-0000-0000-000000000001', 'Vessels & Tanks', 'Static containment equipment', NULL, 1, 'General', ARRAY['tank', 'vessel', 'drum', 'separator', 'reactor']),
('10000000-0004-0000-0000-000000000001', 'Power Generation', 'Electricity generation equipment', NULL, 1, 'Energy', ARRAY['generator', 'turbine', 'inverter', 'transformer']),
('10000000-0005-0000-0000-000000000001', 'Renewable Energy', 'Renewable energy systems', NULL, 1, 'Renewable', ARRAY['wind', 'solar', 'hydro', 'geothermal']),
('10000000-0006-0000-0000-000000000001', 'Energy Storage', 'Energy storage systems', NULL, 1, 'Energy Storage', ARRAY['battery', 'bess', 'ess', 'storage']),
('10000000-0007-0000-0000-000000000001', 'Instrumentation', 'Measurement and control devices', NULL, 1, 'General', ARRAY['sensor', 'transmitter', 'analyzer', 'meter']),
('10000000-0008-0000-0000-000000000001', 'Electrical Systems', 'Electrical distribution and protection', NULL, 1, 'Electrical', ARRAY['switchgear', 'breaker', 'transformer', 'mcc']),
('10000000-0009-0000-0000-000000000001', 'HVAC Systems', 'Heating, ventilation, air conditioning', NULL, 1, 'HVAC', ARRAY['ahu', 'chiller', 'cooling', 'heating', 'hvac'])
ON CONFLICT (name, parent_id) DO NOTHING;

-- Level 2: Equipment types under Rotating Equipment
INSERT INTO knowledge_equipment_taxonomy (id, name, description, parent_id, level, industry, naming_hints, typical_point_count) VALUES
('10000000-0001-0001-0000-000000000001', 'Pump', 'Fluid moving equipment', '10000000-0001-0000-0000-000000000001', 2, 'General', ARRAY['pump', 'pmp', 'p-'], 8),
('10000000-0001-0002-0000-000000000001', 'Compressor', 'Gas compression equipment', '10000000-0001-0000-0000-000000000001', 2, 'General', ARRAY['compressor', 'comp', 'c-'], 15),
('10000000-0001-0003-0000-000000000001', 'Motor', 'Electric motor', '10000000-0001-0000-0000-000000000001', 2, 'General', ARRAY['motor', 'mtr', 'm-'], 6),
('10000000-0001-0004-0000-000000000001', 'Fan', 'Air/gas moving fan', '10000000-0001-0000-0000-000000000001', 2, 'General', ARRAY['fan', 'blower'], 5),
('10000000-0001-0005-0000-000000000001', 'Gearbox', 'Speed reduction/increase gearbox', '10000000-0001-0000-0000-000000000001', 2, 'General', ARRAY['gearbox', 'gbox', 'gear', 'gb'], 8)
ON CONFLICT (name, parent_id) DO NOTHING;

-- Level 3: Pump subtypes
INSERT INTO knowledge_equipment_taxonomy (id, name, description, parent_id, level, industry, naming_hints, typical_point_count) VALUES
('10000000-0001-0001-0001-000000000001', 'Centrifugal Pump', 'Rotodynamic pump using centrifugal force', '10000000-0001-0001-0000-000000000001', 3, 'General', ARRAY['centrifugal', 'cent'], 10),
('10000000-0001-0001-0002-000000000001', 'Positive Displacement Pump', 'Volumetric displacement pump', '10000000-0001-0001-0000-000000000001', 3, 'General', ARRAY['pd', 'positive', 'reciprocating', 'diaphragm'], 12),
('10000000-0001-0001-0003-000000000001', 'Submersible Pump', 'Pump designed for submerged operation', '10000000-0001-0001-0000-000000000001', 3, 'General', ARRAY['submersible', 'sub'], 6)
ON CONFLICT (name, parent_id) DO NOTHING;

-- Level 3: Compressor subtypes
INSERT INTO knowledge_equipment_taxonomy (id, name, description, parent_id, level, industry, naming_hints, typical_point_count) VALUES
('10000000-0001-0002-0001-000000000001', 'Centrifugal Compressor', 'Dynamic compressor using centrifugal force', '10000000-0001-0002-0000-000000000001', 3, 'Oil & Gas', ARRAY['centrifugal', 'cent'], 20),
('10000000-0001-0002-0002-000000000001', 'Reciprocating Compressor', 'Positive displacement piston compressor', '10000000-0001-0002-0000-000000000001', 3, 'Oil & Gas', ARRAY['recip', 'reciprocating', 'piston'], 25),
('10000000-0001-0002-0003-000000000001', 'Screw Compressor', 'Rotary screw positive displacement', '10000000-0001-0002-0000-000000000001', 3, 'General', ARRAY['screw', 'rotary'], 15)
ON CONFLICT (name, parent_id) DO NOTHING;

-- Level 2: Renewable Energy types
INSERT INTO knowledge_equipment_taxonomy (id, name, description, parent_id, level, industry, naming_hints, typical_point_count) VALUES
('10000000-0005-0001-0000-000000000001', 'Wind Turbine', 'Wind energy conversion system', '10000000-0005-0000-0000-000000000001', 2, 'Wind', ARRAY['wind', 'wtg', 'wt', 'turbine'], 50),
('10000000-0005-0002-0000-000000000001', 'Solar PV System', 'Photovoltaic power system', '10000000-0005-0000-0000-000000000001', 2, 'Solar', ARRAY['solar', 'pv', 'photovoltaic'], 30),
('10000000-0005-0003-0000-000000000001', 'Meteorological Station', 'Weather monitoring station', '10000000-0005-0000-0000-000000000001', 2, 'Renewable', ARRAY['met', 'weather', 'mast'], 15)
ON CONFLICT (name, parent_id) DO NOTHING;

-- Level 3: Wind Turbine subtypes
INSERT INTO knowledge_equipment_taxonomy (id, name, description, parent_id, level, industry, naming_hints, typical_point_count) VALUES
('10000000-0005-0001-0001-000000000001', 'Horizontal Axis Wind Turbine', 'Standard HAWT with horizontal rotor axis', '10000000-0005-0001-0000-000000000001', 3, 'Wind', ARRAY['hawt', 'horizontal'], 60),
('10000000-0005-0001-0002-000000000001', 'Vertical Axis Wind Turbine', 'VAWT with vertical rotor axis', '10000000-0005-0001-0000-000000000001', 3, 'Wind', ARRAY['vawt', 'vertical'], 40),
('10000000-0005-0001-0003-000000000001', 'Offshore Wind Turbine', 'Marine/offshore installation', '10000000-0005-0001-0000-000000000001', 3, 'Wind', ARRAY['offshore', 'marine', 'floating'], 80)
ON CONFLICT (name, parent_id) DO NOTHING;

-- Level 3: Solar subtypes
INSERT INTO knowledge_equipment_taxonomy (id, name, description, parent_id, level, industry, naming_hints, typical_point_count) VALUES
('10000000-0005-0002-0001-000000000001', 'Utility Scale Solar', 'Large ground-mounted PV system', '10000000-0005-0002-0000-000000000001', 3, 'Solar', ARRAY['utility', 'ground', 'farm'], 40),
('10000000-0005-0002-0002-000000000001', 'Commercial Rooftop Solar', 'Commercial building rooftop PV', '10000000-0005-0002-0000-000000000001', 3, 'Solar', ARRAY['rooftop', 'commercial', 'building'], 20),
('10000000-0005-0002-0003-000000000001', 'Residential Solar', 'Home solar PV system', '10000000-0005-0002-0000-000000000001', 3, 'Solar', ARRAY['residential', 'home', 'house'], 10)
ON CONFLICT (name, parent_id) DO NOTHING;

-- Level 2: Energy Storage types
INSERT INTO knowledge_equipment_taxonomy (id, name, description, parent_id, level, industry, naming_hints, typical_point_count) VALUES
('10000000-0006-0001-0000-000000000001', 'Lithium-Ion BESS', 'Lithium-ion battery storage system', '10000000-0006-0000-0000-000000000001', 2, 'Energy Storage', ARRAY['lithium', 'li-ion', 'lifepo4', 'nmc'], 40),
('10000000-0006-0002-0000-000000000001', 'Flow Battery', 'Redox flow battery system', '10000000-0006-0000-0000-000000000001', 2, 'Energy Storage', ARRAY['flow', 'vanadium', 'redox'], 30),
('10000000-0006-0003-0000-000000000001', 'Flywheel Storage', 'Kinetic energy storage', '10000000-0006-0000-0000-000000000001', 2, 'Energy Storage', ARRAY['flywheel', 'kinetic'], 15)
ON CONFLICT (name, parent_id) DO NOTHING;

-- ===========================================================================
-- MANUFACTURER PROFILES (OEM-specific naming patterns)
-- ===========================================================================

-- Wind Turbine Manufacturers
INSERT INTO knowledge_manufacturer_profiles (manufacturer_name, manufacturer_code, equipment_category, model_pattern, tag_prefix_pattern, tag_structure, is_verified) VALUES
('Vestas', 'VES', 'WindTurbine', 'V*-*MW', 'V{turbine_id}_{point}', 
 '{"format": "V{id}_{component}_{measurement}", "components": ["ROT", "GEN", "NAC", "YAW", "PITCH"], "example": "V001_ROT_Speed"}', true),
('Siemens Gamesa', 'SGRE', 'WindTurbine', 'SG *-* DD', '{site}_{turbine}_{signal}',
 '{"format": "{site}_{WTG}_{component}_{signal}", "example": "ABC_WTG01_GEN_Power"}', true),
('GE Renewable Energy', 'GE', 'WindTurbine', 'GE *-*', 'GE{id}.{signal}',
 '{"format": "GE{id}.{component}.{signal}", "dot_separated": true, "example": "GE001.Generator.Power"}', true),
('Goldwind', 'GW', 'WindTurbine', 'GW*', 'GW_{turbine}_{point}',
 '{"format": "GW_{turbine}_{point}", "example": "GW_T01_ActivePower"}', false),
('Enercon', 'ENE', 'WindTurbine', 'E-*', 'E{id}_{signal}',
 '{"format": "E{id}_{signal}", "example": "E001_Power_Output"}', false),
('Nordex', 'NDX', 'WindTurbine', 'N* series', 'NDX_{site}_{turbine}_{signal}',
 '{"format": "NDX_{site}_{turbine}_{signal}", "example": "NDX_KSH_001_WindSpeed"}', false),
('Senvion', 'SEN', 'WindTurbine', 'MM* / 3.XM*', 'SEN_{wt}_{point}',
 '{"format": "SEN_{wt}_{point}", "example": "SEN_WT01_GenRPM"}', false),
('Envision', 'ENV', 'WindTurbine', 'EN*', 'ENV{id}_{meas}',
 '{"format": "ENV{id}_{measurement}", "example": "ENV01_ActivePower"}', false),
('MingYang', 'MY', 'WindTurbine', 'MySE*', 'MY_{turbine}_{signal}',
 '{"format": "MY_{turbine}_{signal}", "example": "MY_T001_Power"}', false)
ON CONFLICT (manufacturer_name, model_pattern) DO UPDATE SET tag_structure = EXCLUDED.tag_structure;

-- Solar Inverter Manufacturers
INSERT INTO knowledge_manufacturer_profiles (manufacturer_name, manufacturer_code, equipment_category, model_pattern, tag_prefix_pattern, tag_structure, is_verified) VALUES
('SMA', 'SMA', 'Solar', 'Sunny Central *', 'SMA_{inv}_{signal}',
 '{"format": "SMA_{inverter}_{signal}", "example": "SMA_INV01_AcPower"}', true),
('Huawei', 'HW', 'Solar', 'SUN2000-*', 'HW_{string}_{signal}',
 '{"format": "HW_{string}_{signal}", "example": "HW_STR01_DcVoltage"}', true),
('Sungrow', 'SG', 'Solar', 'SG*', 'SG_{inv}_{point}',
 '{"format": "SG_{inverter}_{point}", "example": "SG_INV1_Power"}', false),
('KACO', 'KACO', 'Solar', 'blueplanet *', 'KACO_{id}_{signal}',
 '{"format": "KACO_{id}_{signal}", "example": "KACO_01_AcPower"}', false),
('ABB/FIMER', 'ABB', 'Solar', 'PVS-*', 'ABB_{inv}_{meas}',
 '{"format": "ABB_{inverter}_{measurement}", "example": "ABB_INV01_Power"}', false),
('Enphase', 'ENP', 'Solar', 'IQ*', 'ENP_{micro}_{signal}',
 '{"format": "ENP_{microinverter}_{signal}", "microinverter_level": true}', false),
('SolarEdge', 'SE', 'Solar', 'SE*', 'SE_{inv}_{signal}',
 '{"format": "SE_{inverter}_{signal}", "optimizer_level": true}', false)
ON CONFLICT (manufacturer_name, model_pattern) DO UPDATE SET tag_structure = EXCLUDED.tag_structure;

-- Battery/BESS Manufacturers
INSERT INTO knowledge_manufacturer_profiles (manufacturer_name, manufacturer_code, equipment_category, model_pattern, tag_prefix_pattern, tag_structure, is_verified) VALUES
('Tesla', 'TSLA', 'EnergyStorage', 'Megapack*', 'TSLA_{pack}_{signal}',
 '{"format": "TSLA_{pack}_{signal}", "example": "TSLA_MP01_SOC"}', true),
('Fluence', 'FLU', 'EnergyStorage', 'Gridstack*', 'FLU_{unit}_{signal}',
 '{"format": "FLU_{unit}_{signal}", "example": "FLU_U01_StateOfCharge"}', true),
('BYD', 'BYD', 'EnergyStorage', 'B-Box*', 'BYD_{rack}_{signal}',
 '{"format": "BYD_{rack}_{signal}", "example": "BYD_R01_CellTempMax"}', false),
('LG Energy Solution', 'LGES', 'EnergyStorage', 'RESU*', 'LG_{unit}_{signal}',
 '{"format": "LG_{unit}_{signal}", "example": "LG_01_SOC"}', false),
('Samsung SDI', 'SDI', 'EnergyStorage', 'E3*', 'SDI_{bank}_{signal}',
 '{"format": "SDI_{bank}_{signal}", "example": "SDI_B1_Power"}', false)
ON CONFLICT (manufacturer_name, model_pattern) DO UPDATE SET tag_structure = EXCLUDED.tag_structure;

-- ===========================================================================
-- SEMANTIC SYNONYMS (Alternative terminology mappings)
-- ===========================================================================

INSERT INTO knowledge_synonyms (canonical_term, synonym, context, similarity_score) VALUES
-- Power synonyms
('Active Power', 'Real Power', 'Electrical', 1.0),
('Active Power', 'P', 'Electrical', 0.9),
('Active Power', 'kW Output', 'Electrical', 0.95),
('Active Power', 'True Power', 'Electrical', 1.0),
('Reactive Power', 'Q', 'Electrical', 0.9),
('Reactive Power', 'VAR', 'Electrical', 0.95),
('Reactive Power', 'Imaginary Power', 'Electrical', 1.0),

-- Temperature synonyms
('Temperature', 'Temp', 'General', 0.95),
('Ambient Temperature', 'Outside Air Temperature', 'General', 1.0),
('Ambient Temperature', 'OAT', 'General', 0.95),
('Ambient Temperature', 'External Temperature', 'General', 0.95),

-- Wind synonyms
('Wind Speed', 'WS', 'Wind', 0.9),
('Wind Speed', 'Anemometer Reading', 'Wind', 0.9),
('Wind Direction', 'WD', 'Wind', 0.9),
('Wind Direction', 'Wind Vane', 'Wind', 0.85),
('Nacelle Position', 'Yaw Position', 'Wind', 0.95),
('Nacelle Position', 'Yaw Angle', 'Wind', 0.95),

-- Solar synonyms
('Irradiance', 'Solar Radiation', 'Solar', 1.0),
('Irradiance', 'Insolation', 'Solar', 0.9),
('Global Horizontal Irradiance', 'GHI', 'Solar', 0.95),
('Plane of Array Irradiance', 'POA', 'Solar', 0.95),
('Module Temperature', 'Panel Temperature', 'Solar', 1.0),
('Module Temperature', 'Cell Temperature', 'Solar', 0.95),
('Performance Ratio', 'PR', 'Solar', 0.9),

-- Battery synonyms
('State of Charge', 'SOC', 'Energy Storage', 0.95),
('State of Charge', 'Charge Level', 'Energy Storage', 0.9),
('State of Health', 'SOH', 'Energy Storage', 0.95),
('State of Health', 'Battery Health', 'Energy Storage', 0.9),
('Depth of Discharge', 'DOD', 'Energy Storage', 0.95),

-- Mechanical synonyms
('Rotational Speed', 'RPM', 'Mechanical', 0.95),
('Rotational Speed', 'Speed', 'Mechanical', 0.8),
('Differential Pressure', 'DP', 'Mechanical', 0.9),
('Differential Pressure', 'Delta P', 'Mechanical', 0.95),

-- Equipment synonyms
('Generator', 'Gen', 'General', 0.9),
('Gearbox', 'Gear Box', 'General', 1.0),
('Gearbox', 'GBOX', 'General', 0.9),
('Drive End', 'DE', 'General', 0.9),
('Non-Drive End', 'NDE', 'General', 0.9),
('Non-Drive End', 'Free End', 'General', 0.85),
('Bearing', 'BRG', 'General', 0.9)
ON CONFLICT (canonical_term, synonym, context) DO UPDATE SET similarity_score = EXCLUDED.similarity_score;

-- ===========================================================================
-- STANDARD NAMING CONVENTIONS (IEC, ISA patterns)
-- ===========================================================================

-- IEC 61400-25 Wind Turbine naming patterns
INSERT INTO knowledge_naming_conventions (standard_id, pattern_regex, pattern_description, component_template, component_definitions, confidence_boost, priority)
SELECT 
    s.id,
    patterns.regex,
    patterns.description,
    patterns.template,
    patterns.definitions::jsonb,
    patterns.boost,
    patterns.priority
FROM knowledge_industry_standards s
CROSS JOIN (VALUES
    ('IEC 61400-25', '^([A-Z]{2,4})_?(\d{1,3})_(.+)$', 'IEC 61400-25 style: {Site}_{Turbine}_{Signal}', '{site}_{turbine}_{signal}', '{"site": "2-4 char site code", "turbine": "turbine number", "signal": "measurement name"}', 0.15, 100),
    ('IEC 61400-25', '^WTG_?(\d+)\.(.+)$', 'IEC 61400-25 dot notation: WTG{n}.{signal}', 'WTG_{number}.{signal}', '{"number": "turbine number", "signal": "logical node path"}', 0.12, 90),
    ('IEC 61400-25', '^([A-Z]+)(\d+)_([A-Z]+)_(.+)$', 'Extended format: {Type}{ID}_{Component}_{Measurement}', '{type}{id}_{component}_{measurement}', '{"type": "equipment type code", "id": "equipment ID", "component": "subsystem", "measurement": "measured value"}', 0.10, 80)
) AS patterns(standard, regex, description, template, definitions, boost, priority)
WHERE s.standard_code = patterns.standard
ON CONFLICT DO NOTHING;

-- IEC 61724 Solar PV naming patterns
INSERT INTO knowledge_naming_conventions (standard_id, pattern_regex, pattern_description, component_template, component_definitions, confidence_boost, priority)
SELECT 
    s.id,
    patterns.regex,
    patterns.description,
    patterns.template,
    patterns.definitions::jsonb,
    patterns.boost,
    patterns.priority
FROM knowledge_industry_standards s
CROSS JOIN (VALUES
    ('IEC 61724-1', '^INV_?(\d+)_(.+)$', 'Solar inverter format: INV_{number}_{signal}', 'INV_{number}_{signal}', '{"number": "inverter number", "signal": "measurement"}', 0.12, 100),
    ('IEC 61724-1', '^STR_?(\d+)_(.+)$', 'Solar string format: STR_{number}_{signal}', 'STR_{number}_{signal}', '{"number": "string number", "signal": "measurement"}', 0.10, 90),
    ('IEC 61724-1', '^([A-Z]+)(\d+)_([A-Z]+)(\d*)_(.+)$', 'Hierarchical: {Array}{n}_{Inverter}{m}_{Signal}', '{array}{n}_{inverter}{m}_{signal}', '{"array": "array ID", "inverter": "inverter ID", "signal": "measurement"}', 0.10, 80)
) AS patterns(standard, regex, description, template, definitions, boost, priority)
WHERE s.standard_code = patterns.standard
ON CONFLICT DO NOTHING;

-- ISA-5.1 Instrument tag patterns
INSERT INTO knowledge_naming_conventions (standard_id, pattern_regex, pattern_description, component_template, component_definitions, confidence_boost, priority)
SELECT 
    s.id,
    patterns.regex,
    patterns.description,
    patterns.template,
    patterns.definitions::jsonb,
    patterns.boost,
    patterns.priority
FROM knowledge_industry_standards s
CROSS JOIN (VALUES
    ('ISA-5.1', '^([A-Z]{2,4})[-_]?(\d{2,5})([A-Z])?$', 'ISA tag: {Function}{Loop}{Suffix}', '{function}{loop}{suffix}', '{"function": "ISA function letters (PT, FT, TT, etc.)", "loop": "loop number", "suffix": "optional suffix letter"}', 0.15, 100),
    ('ISA-5.1', '^([A-Z])([A-Z])[-_]?(\d+)$', 'ISA transmitter: {Type}{Variable}_{Loop}', '{type}{variable}_{loop}', '{"type": "T=transmitter, I=indicator, C=controller", "variable": "P=pressure, T=temp, F=flow, L=level", "loop": "loop number"}', 0.12, 90)
) AS patterns(standard, regex, description, template, definitions, boost, priority)
WHERE s.standard_code = patterns.standard
ON CONFLICT DO NOTHING;

-- Grant permissions
GRANT SELECT ON ALL TABLES IN SCHEMA public TO naia;
