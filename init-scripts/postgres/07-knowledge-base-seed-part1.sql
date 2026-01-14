-- NAIA v3 Knowledge Base Seed Data
-- Industry standards, measurement types, units, abbreviations, and equipment taxonomy
-- This provides the "base intelligence" before any user feedback

-- ===========================================================================
-- INDUSTRY STANDARDS
-- ===========================================================================
INSERT INTO knowledge_industry_standards (standard_code, standard_name, industry, version, description, url) VALUES
-- Wind Energy Standards
('IEC 61400-1', 'Wind turbines - Design requirements', 'Wind', '2019', 'Wind turbine design requirements including external conditions, structural design, and control systems', 'https://webstore.iec.ch/publication/26423'),
('IEC 61400-2', 'Small wind turbines', 'Wind', '2013', 'Design requirements for small wind turbines', 'https://webstore.iec.ch/publication/5426'),
('IEC 61400-12-1', 'Power performance measurements', 'Wind', '2017', 'Power performance measurements of electricity producing wind turbines', 'https://webstore.iec.ch/publication/26603'),
('IEC 61400-25', 'Communications for monitoring and control', 'Wind', '2017', 'Information models and communication protocols for wind power plants', 'https://webstore.iec.ch/publication/31584'),
('IEC 61400-25-2', 'Information models', 'Wind', '2015', 'Logical node classes and data classes for wind power plants', 'https://webstore.iec.ch/publication/22813'),

-- Solar Energy Standards
('IEC 61724-1', 'PV system performance monitoring', 'Solar', '2021', 'Photovoltaic system performance monitoring guidelines for measurement, data exchange and analysis', 'https://webstore.iec.ch/publication/65561'),
('IEC 61724-2', 'Capacity evaluation method', 'Solar', '2016', 'Capacity evaluation method for photovoltaic systems', 'https://webstore.iec.ch/publication/24375'),
('IEC 61724-3', 'Energy evaluation method', 'Solar', '2016', 'Energy evaluation method for photovoltaic systems', 'https://webstore.iec.ch/publication/24376'),
('IEC 62446-1', 'PV systems - Testing and documentation', 'Solar', '2016', 'Requirements for testing, documentation and maintenance of PV systems', 'https://webstore.iec.ch/publication/24457'),

-- Battery Storage Standards
('IEC 62933-1', 'EES systems - Vocabulary', 'Energy Storage', '2018', 'Electrical energy storage systems vocabulary and general terms', 'https://webstore.iec.ch/publication/31681'),
('IEC 62933-2-1', 'EES systems - Unit parameters', 'Energy Storage', '2017', 'Unit parameters and testing methods for EES systems', 'https://webstore.iec.ch/publication/28386'),
('UL 9540', 'Energy Storage Systems and Equipment', 'Energy Storage', '2020', 'Standard for safety of energy storage systems', NULL),

-- General Industrial Standards
('ISA-5.1', 'Instrumentation Symbols and Identification', 'General', '2022', 'Identification and symbols for measurement and control instrumentation', 'https://www.isa.org/standards-and-publications/isa-standards/isa-5-1-2022'),
('ISA-88', 'Batch Control', 'Manufacturing', '2010', 'Batch control standards and terminology', 'https://www.isa.org/isa88'),
('ISA-95', 'Enterprise-Control System Integration', 'Manufacturing', '2018', 'Integration of enterprise and control systems including equipment hierarchies', 'https://www.isa.org/isa95'),
('ISA-18.2', 'Alarm Management', 'General', '2016', 'Management of alarm systems for process industries', 'https://www.isa.org/isa18'),

-- Oil & Gas / Process Standards
('API 670', 'Machinery Protection Systems', 'Oil & Gas', '2014', 'Fourth edition - Machinery protection systems for petroleum, chemical, and gas industries', NULL),
('API 610', 'Centrifugal Pumps', 'Oil & Gas', '2021', 'Centrifugal pumps for petroleum, petrochemical and natural gas industries', NULL),
('API 617', 'Axial and Centrifugal Compressors', 'Oil & Gas', '2022', 'Axial and centrifugal compressors and expander-compressors', NULL),
('API 618', 'Reciprocating Compressors', 'Oil & Gas', '2007', 'Reciprocating compressors for petroleum, chemical, and gas industry services', NULL),

-- Electrical Standards
('IEEE C37.1', 'SCADA and Automation Systems', 'Electrical', '2007', 'Standard for SCADA and automation systems', 'https://standards.ieee.org/ieee/C37.1/4047/'),
('IEEE 1159', 'Power Quality', 'Electrical', '2019', 'Recommended practice for monitoring electric power quality', 'https://standards.ieee.org/ieee/1159/6023/'),
('IEC 61850', 'Substation Communication', 'Electrical', '2020', 'Communication networks and systems for power utility automation', 'https://webstore.iec.ch/publication/6028'),

-- HVAC Standards
('ASHRAE 135', 'BACnet', 'HVAC', '2020', 'A Data Communication Protocol for Building Automation and Control Networks', 'https://www.ashrae.org/technical-resources/standards-and-guidelines'),
('ASHRAE Guideline 13', 'Specifying DDC Systems', 'HVAC', '2015', 'Specifying direct digital control systems', NULL)
ON CONFLICT (standard_code) DO UPDATE SET description = EXCLUDED.description;

-- ===========================================================================
-- MEASUREMENT TYPES (Canonical measurement categories)
-- ===========================================================================
INSERT INTO knowledge_measurement_types (canonical_name, description, data_type, category, typical_min, typical_max) VALUES
-- Electrical
('Power', 'Rate of energy transfer', 'numeric', 'Electrical', -100000, 100000),
('Active Power', 'Real power component', 'numeric', 'Electrical', -100000, 100000),
('Reactive Power', 'Imaginary power component', 'numeric', 'Electrical', -100000, 100000),
('Apparent Power', 'Total power magnitude', 'numeric', 'Electrical', 0, 100000),
('Voltage', 'Electrical potential difference', 'numeric', 'Electrical', 0, 1000000),
('Current', 'Electrical current flow', 'numeric', 'Electrical', 0, 100000),
('Frequency', 'Electrical frequency', 'numeric', 'Electrical', 0, 100),
('Power Factor', 'Ratio of real to apparent power', 'numeric', 'Electrical', -1, 1),
('Energy', 'Cumulative energy', 'numeric', 'Electrical', 0, NULL),
('Resistance', 'Electrical resistance', 'numeric', 'Electrical', 0, NULL),

-- Thermal
('Temperature', 'Thermal measurement', 'numeric', 'Thermal', -273, 2000),
('Heat Flow', 'Rate of heat transfer', 'numeric', 'Thermal', 0, NULL),
('Thermal Conductivity', 'Heat conduction ability', 'numeric', 'Thermal', 0, NULL),

-- Mechanical
('Pressure', 'Force per unit area', 'numeric', 'Mechanical', 0, NULL),
('Flow Rate', 'Volume or mass per time', 'numeric', 'Mechanical', 0, NULL),
('Rotational Speed', 'Revolutions per time unit', 'numeric', 'Mechanical', 0, 100000),
('Vibration', 'Mechanical oscillation', 'numeric', 'Mechanical', 0, NULL),
('Torque', 'Rotational force', 'numeric', 'Mechanical', 0, NULL),
('Force', 'Push or pull', 'numeric', 'Mechanical', NULL, NULL),
('Position', 'Linear or angular position', 'numeric', 'Mechanical', NULL, NULL),
('Angle', 'Angular measurement', 'numeric', 'Mechanical', 0, 360),
('Level', 'Liquid or solid level', 'numeric', 'Mechanical', 0, 100),

-- Environmental
('Wind Speed', 'Air velocity measurement', 'numeric', 'Environmental', 0, 100),
('Wind Direction', 'Wind compass direction', 'numeric', 'Environmental', 0, 360),
('Humidity', 'Moisture content in air', 'numeric', 'Environmental', 0, 100),
('Barometric Pressure', 'Atmospheric pressure', 'numeric', 'Environmental', 800, 1200),
('Solar Irradiance', 'Solar radiation intensity', 'numeric', 'Environmental', 0, 1500),
('Precipitation', 'Rainfall amount', 'numeric', 'Environmental', 0, NULL),
('Visibility', 'Optical clarity of air', 'numeric', 'Environmental', 0, NULL),
('Air Quality', 'Air pollutant measurement', 'numeric', 'Environmental', 0, 500),

-- Status/State
('Status', 'Operational state', 'string', 'Status', NULL, NULL),
('Alarm', 'Alarm condition', 'boolean', 'Status', NULL, NULL),
('Setpoint', 'Target value', 'numeric', 'Status', NULL, NULL),
('Mode', 'Operating mode', 'string', 'Status', NULL, NULL),
('Command', 'Control command', 'string', 'Status', NULL, NULL),
('Count', 'Cumulative count', 'numeric', 'Status', 0, NULL),
('Duration', 'Time period', 'numeric', 'Status', 0, NULL),

-- Battery/Energy Storage
('State of Charge', 'Battery charge percentage', 'numeric', 'Energy Storage', 0, 100),
('State of Health', 'Battery health percentage', 'numeric', 'Energy Storage', 0, 100),
('Depth of Discharge', 'Discharge depth', 'numeric', 'Energy Storage', 0, 100),
('Cycle Count', 'Charge/discharge cycles', 'numeric', 'Energy Storage', 0, NULL),
('Cell Voltage', 'Individual cell voltage', 'numeric', 'Energy Storage', 0, 5),

-- Performance
('Efficiency', 'Output/input ratio', 'numeric', 'Performance', 0, 100),
('Capacity Factor', 'Actual vs rated output ratio', 'numeric', 'Performance', 0, 100),
('Availability', 'Uptime percentage', 'numeric', 'Performance', 0, 100),
('Performance Ratio', 'Actual vs expected ratio', 'numeric', 'Performance', 0, 100)
ON CONFLICT (canonical_name) DO UPDATE SET description = EXCLUDED.description;

-- ===========================================================================
-- UNIT MAPPINGS (Map units to measurement types)
-- ===========================================================================
INSERT INTO knowledge_unit_mappings (measurement_type_id, unit_symbol, unit_name, unit_system, conversion_factor, base_unit, is_common)
SELECT mt.id, units.symbol, units.name, units.system, units.factor, units.base, units.common
FROM knowledge_measurement_types mt
JOIN (VALUES
    -- Power
    ('Power', 'W', 'watt', 'SI', 1, 'W', true),
    ('Power', 'kW', 'kilowatt', 'SI', 1000, 'W', true),
    ('Power', 'MW', 'megawatt', 'SI', 1000000, 'W', true),
    ('Power', 'GW', 'gigawatt', 'SI', 1000000000, 'W', false),
    ('Power', 'hp', 'horsepower', 'Imperial', 745.7, 'W', true),
    ('Active Power', 'kW', 'kilowatt', 'SI', 1000, 'W', true),
    ('Active Power', 'MW', 'megawatt', 'SI', 1000000, 'W', true),
    ('Reactive Power', 'kvar', 'kilovolt-ampere reactive', 'SI', 1000, 'var', true),
    ('Reactive Power', 'Mvar', 'megavolt-ampere reactive', 'SI', 1000000, 'var', true),
    ('Reactive Power', 'var', 'volt-ampere reactive', 'SI', 1, 'var', true),
    ('Apparent Power', 'VA', 'volt-ampere', 'SI', 1, 'VA', true),
    ('Apparent Power', 'kVA', 'kilovolt-ampere', 'SI', 1000, 'VA', true),
    ('Apparent Power', 'MVA', 'megavolt-ampere', 'SI', 1000000, 'VA', true),
    
    -- Voltage
    ('Voltage', 'V', 'volt', 'SI', 1, 'V', true),
    ('Voltage', 'kV', 'kilovolt', 'SI', 1000, 'V', true),
    ('Voltage', 'mV', 'millivolt', 'SI', 0.001, 'V', true),
    
    -- Current
    ('Current', 'A', 'ampere', 'SI', 1, 'A', true),
    ('Current', 'mA', 'milliampere', 'SI', 0.001, 'A', true),
    ('Current', 'kA', 'kiloampere', 'SI', 1000, 'A', false),
    
    -- Frequency
    ('Frequency', 'Hz', 'hertz', 'SI', 1, 'Hz', true),
    ('Frequency', 'kHz', 'kilohertz', 'SI', 1000, 'Hz', false),
    ('Frequency', 'MHz', 'megahertz', 'SI', 1000000, 'Hz', false),
    
    -- Energy
    ('Energy', 'Wh', 'watt-hour', 'SI', 1, 'Wh', true),
    ('Energy', 'kWh', 'kilowatt-hour', 'SI', 1000, 'Wh', true),
    ('Energy', 'MWh', 'megawatt-hour', 'SI', 1000000, 'Wh', true),
    ('Energy', 'GWh', 'gigawatt-hour', 'SI', 1000000000, 'Wh', false),
    ('Energy', 'J', 'joule', 'SI', 0.000277778, 'Wh', false),
    ('Energy', 'BTU', 'British thermal unit', 'Imperial', 0.293071, 'Wh', false),
    
    -- Temperature
    ('Temperature', '°C', 'degrees Celsius', 'SI', 1, '°C', true),
    ('Temperature', 'C', 'degrees Celsius', 'SI', 1, '°C', true),
    ('Temperature', '°F', 'degrees Fahrenheit', 'Imperial', NULL, '°F', true),
    ('Temperature', 'F', 'degrees Fahrenheit', 'Imperial', NULL, '°F', true),
    ('Temperature', 'K', 'kelvin', 'SI', NULL, 'K', false),
    ('Temperature', 'degC', 'degrees Celsius', 'SI', 1, '°C', true),
    ('Temperature', 'degF', 'degrees Fahrenheit', 'Imperial', NULL, '°F', true),
    
    -- Pressure
    ('Pressure', 'Pa', 'pascal', 'SI', 1, 'Pa', false),
    ('Pressure', 'kPa', 'kilopascal', 'SI', 1000, 'Pa', true),
    ('Pressure', 'MPa', 'megapascal', 'SI', 1000000, 'Pa', true),
    ('Pressure', 'bar', 'bar', 'SI', 100000, 'Pa', true),
    ('Pressure', 'mbar', 'millibar', 'SI', 100, 'Pa', true),
    ('Pressure', 'psi', 'pounds per square inch', 'Imperial', 6894.76, 'Pa', true),
    ('Pressure', 'psig', 'psi gauge', 'Imperial', 6894.76, 'Pa', true),
    ('Pressure', 'psia', 'psi absolute', 'Imperial', 6894.76, 'Pa', true),
    ('Pressure', 'inHg', 'inches of mercury', 'Imperial', 3386.39, 'Pa', false),
    ('Pressure', 'mmHg', 'millimeters of mercury', 'SI', 133.322, 'Pa', false),
    ('Pressure', 'hPa', 'hectopascal', 'SI', 100, 'Pa', true),
    ('Barometric Pressure', 'hPa', 'hectopascal', 'SI', 100, 'Pa', true),
    ('Barometric Pressure', 'mbar', 'millibar', 'SI', 100, 'Pa', true),
    ('Barometric Pressure', 'inHg', 'inches of mercury', 'Imperial', 3386.39, 'Pa', true),
    
    -- Flow
    ('Flow Rate', 'm³/h', 'cubic meters per hour', 'SI', 1, 'm³/h', true),
    ('Flow Rate', 'm3/h', 'cubic meters per hour', 'SI', 1, 'm³/h', true),
    ('Flow Rate', 'l/s', 'liters per second', 'SI', 3.6, 'm³/h', true),
    ('Flow Rate', 'L/s', 'liters per second', 'SI', 3.6, 'm³/h', true),
    ('Flow Rate', 'l/min', 'liters per minute', 'SI', 0.06, 'm³/h', true),
    ('Flow Rate', 'GPM', 'gallons per minute', 'Imperial', 0.227125, 'm³/h', true),
    ('Flow Rate', 'gpm', 'gallons per minute', 'Imperial', 0.227125, 'm³/h', true),
    ('Flow Rate', 'CFM', 'cubic feet per minute', 'Imperial', 1.69901, 'm³/h', true),
    ('Flow Rate', 'SCFM', 'standard cubic feet per minute', 'Imperial', 1.69901, 'm³/h', true),
    ('Flow Rate', 'kg/s', 'kilograms per second', 'SI', NULL, 'kg/s', true),
    ('Flow Rate', 'kg/h', 'kilograms per hour', 'SI', NULL, 'kg/h', true),
    
    -- Rotational Speed
    ('Rotational Speed', 'RPM', 'revolutions per minute', 'SI', 1, 'RPM', true),
    ('Rotational Speed', 'rpm', 'revolutions per minute', 'SI', 1, 'RPM', true),
    ('Rotational Speed', 'rad/s', 'radians per second', 'SI', 9.5493, 'RPM', false),
    ('Rotational Speed', 'Hz', 'hertz (revolutions per second)', 'SI', 60, 'RPM', false),
    
    -- Position/Angle
    ('Position', 'mm', 'millimeter', 'SI', 0.001, 'm', true),
    ('Position', 'cm', 'centimeter', 'SI', 0.01, 'm', true),
    ('Position', 'm', 'meter', 'SI', 1, 'm', true),
    ('Position', 'in', 'inch', 'Imperial', 0.0254, 'm', true),
    ('Position', 'ft', 'foot', 'Imperial', 0.3048, 'm', true),
    ('Angle', 'deg', 'degrees', 'SI', 1, 'deg', true),
    ('Angle', '°', 'degrees', 'SI', 1, 'deg', true),
    ('Angle', 'rad', 'radians', 'SI', 57.2958, 'deg', false),
    
    -- Level
    ('Level', '%', 'percent', 'SI', 1, '%', true),
    ('Level', 'm', 'meters', 'SI', 1, 'm', true),
    ('Level', 'ft', 'feet', 'Imperial', 0.3048, 'm', true),
    ('Level', 'in', 'inches', 'Imperial', 0.0254, 'm', true),
    
    -- Wind
    ('Wind Speed', 'm/s', 'meters per second', 'SI', 1, 'm/s', true),
    ('Wind Speed', 'mph', 'miles per hour', 'Imperial', 0.44704, 'm/s', true),
    ('Wind Speed', 'km/h', 'kilometers per hour', 'SI', 0.277778, 'm/s', true),
    ('Wind Speed', 'kts', 'knots', 'Imperial', 0.514444, 'm/s', true),
    ('Wind Direction', 'deg', 'degrees', 'SI', 1, 'deg', true),
    ('Wind Direction', '°', 'degrees', 'SI', 1, 'deg', true),
    
    -- Solar
    ('Solar Irradiance', 'W/m²', 'watts per square meter', 'SI', 1, 'W/m²', true),
    ('Solar Irradiance', 'W/m2', 'watts per square meter', 'SI', 1, 'W/m²', true),
    ('Solar Irradiance', 'kW/m²', 'kilowatts per square meter', 'SI', 1000, 'W/m²', false),
    
    -- Humidity
    ('Humidity', '%', 'percent relative humidity', 'SI', 1, '%', true),
    ('Humidity', '%RH', 'percent relative humidity', 'SI', 1, '%', true),
    
    -- Vibration
    ('Vibration', 'mm/s', 'millimeters per second', 'SI', 1, 'mm/s', true),
    ('Vibration', 'in/s', 'inches per second', 'Imperial', 25.4, 'mm/s', true),
    ('Vibration', 'g', 'g-force', 'SI', NULL, 'g', true),
    ('Vibration', 'mils', 'mils peak-to-peak', 'Imperial', 0.0254, 'mm', true),
    
    -- Efficiency/Ratio
    ('Efficiency', '%', 'percent', 'SI', 1, '%', true),
    ('Power Factor', 'PF', 'power factor', 'SI', 1, 'PF', true),
    ('Capacity Factor', '%', 'percent', 'SI', 1, '%', true),
    ('Availability', '%', 'percent', 'SI', 1, '%', true),
    ('Performance Ratio', '%', 'percent', 'SI', 1, '%', true),
    
    -- State of Charge/Health
    ('State of Charge', '%', 'percent', 'SI', 1, '%', true),
    ('State of Charge', 'SOC', 'state of charge', 'SI', 1, '%', true),
    ('State of Health', '%', 'percent', 'SI', 1, '%', true),
    ('State of Health', 'SOH', 'state of health', 'SI', 1, '%', true),
    ('Depth of Discharge', '%', 'percent', 'SI', 1, '%', true),
    
    -- Cell Voltage
    ('Cell Voltage', 'V', 'volt', 'SI', 1, 'V', true),
    ('Cell Voltage', 'mV', 'millivolt', 'SI', 0.001, 'V', true),
    
    -- Precipitation
    ('Precipitation', 'mm', 'millimeters', 'SI', 1, 'mm', true),
    ('Precipitation', 'in', 'inches', 'Imperial', 25.4, 'mm', true),
    
    -- Duration
    ('Duration', 's', 'seconds', 'SI', 1, 's', true),
    ('Duration', 'min', 'minutes', 'SI', 60, 's', true),
    ('Duration', 'h', 'hours', 'SI', 3600, 's', true),
    ('Duration', 'hr', 'hours', 'SI', 3600, 's', true)
) AS units(mtype, symbol, name, system, factor, base, common)
ON mt.canonical_name = units.mtype
ON CONFLICT (unit_symbol) DO UPDATE SET measurement_type_id = EXCLUDED.measurement_type_id;
