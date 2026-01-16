using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;

namespace Naia.OpcSimulator;

/// <summary>
/// OPC UA Server for a single NAIA site.
/// Configurable for Wind, Solar, or BESS simulation.
/// </summary>
public class NaiaSiteServer : StandardServer
{
    private readonly ILogger _logger;
    private readonly SiteConfiguration _config;
    private SiteNodeManager? _nodeManager;

    public NaiaSiteServer(ILogger logger, SiteConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration)
    {
        _logger.LogInformation("Creating node manager for {SiteType} site...", _config.SiteType);

        var nodeManagers = new List<INodeManager>();
        _nodeManager = new SiteNodeManager(server, configuration, _logger, _config);
        nodeManagers.Add(_nodeManager);

        return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
    }

    protected override ServerProperties LoadServerProperties()
    {
        return new ServerProperties
        {
            ManufacturerName = "NAIA Energy",
            ProductName = $"NAIA {_config.SiteName} OPC UA Simulator",
            ProductUri = "http://naia.energy/OpcSimulator",
            SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
            BuildNumber = Utils.GetAssemblyBuildNumber(),
            BuildDate = Utils.GetAssemblyTimestamp()
        };
    }

    public new void Stop()
    {
        _logger.LogInformation("Stopping OPC UA server for {SiteName}...", _config.SiteName);
        _nodeManager?.Dispose();
        Dispose();
    }
}

/// <summary>
/// Node Manager that creates the site-specific address space.
/// </summary>
public class SiteNodeManager : CustomNodeManager2, IDisposable
{
    private const string Namespace = "http://naia.energy/OpcSimulator";
    
    private readonly ILogger _logger;
    private readonly SiteConfiguration _config;
    private readonly List<BaseDataVariableState> _dynamicNodes = new();
    private readonly Random _random = new();
    private Timer? _simulationTimer;

    // Simulation state
    private readonly Dictionary<string, EquipmentState> _equipment = new();
    private bool _breakerATripped = false;
    private bool _breakerBTripped = false;
    private double _ambientTemp = 25.0;
    private double _windSpeed = 8.0;
    private double _solarIrradiance = 800.0;

    public SiteNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration,
        ILogger logger,
        SiteConfiguration config)
        : base(server, configuration, Namespace)
    {
        _logger = logger;
        _config = config;
        SystemContext.NodeIdFactory = this;

        SetNamespaces(new[] { Namespace });
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            base.CreateAddressSpace(externalReferences);
            
            _logger.LogInformation("Building address space for {SiteType} site...", _config.SiteType);

            // Create root folder for this site
            var siteRoot = CreateFolderState(null, _config.SiteId, _config.SiteName, $"{_config.SiteType} simulation site");
            siteRoot.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            AddPredefinedNode(SystemContext, siteRoot);

            // Add reverse reference
            if (externalReferences != null)
            {
                externalReferences[ObjectIds.ObjectsFolder] = new List<IReference>
                {
                    new NodeStateReference(ReferenceTypeIds.Organizes, false, siteRoot.NodeId)
                };
            }

            // Create site-specific nodes
            switch (_config.SiteType)
            {
                case SiteType.Wind:
                    CreateWindSite(siteRoot);
                    break;
                case SiteType.Solar:
                    CreateSolarSite(siteRoot);
                    break;
                case SiteType.Bess:
                    CreateBessSite(siteRoot);
                    break;
            }

            _logger.LogInformation("✅ Created {Count} data points for {SiteName}", _dynamicNodes.Count, _config.SiteName);

            // Start simulation
            _simulationTimer = new Timer(UpdateSimulation, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(_config.UpdateIntervalMs));
        }
    }

    #region Wind Site Creation

    private void CreateWindSite(FolderState root)
    {
        // Turbines folder
        var turbinesFolder = CreateFolderState(root, "Turbines", "Wind Turbines", "GE and Vestas turbines");
        AddPredefinedNode(SystemContext, turbinesFolder);

        // GE Turbines (2.5MW each)
        for (int i = 1; i <= _config.GeTurbineCount; i++)
        {
            CreateWindTurbine(turbinesFolder, $"GE-2.5-{i:D3}", "GE", 2500);
        }

        // Vestas Turbines (2.0MW each)
        for (int i = 1; i <= _config.VestasTurbineCount; i++)
        {
            CreateWindTurbine(turbinesFolder, $"V110-{i:D3}", "Vestas", 2000);
        }

        // Met Towers
        var metFolder = CreateFolderState(root, "MetTowers", "Meteorological Towers", "Weather monitoring");
        AddPredefinedNode(SystemContext, metFolder);

        for (int i = 1; i <= _config.MetTowerCount; i++)
        {
            CreateMetTower(metFolder, $"MET-{i:D2}");
        }

        // Substation
        var subFolder = CreateFolderState(root, "Substation", "Substation", "Main electrical substation");
        AddPredefinedNode(SystemContext, subFolder);
        CreateWindSubstation(subFolder, "SUB-WIND-01");
    }

    private void CreateWindTurbine(FolderState parent, string id, string manufacturer, double ratedPowerKw)
    {
        var folder = CreateFolderState(parent, id, id, $"{manufacturer} wind turbine");
        AddPredefinedNode(SystemContext, folder);

        var state = new EquipmentState { Id = id, Manufacturer = manufacturer, RatedPower = ratedPowerKw };
        _equipment[id] = state;

        // Core measurements
        CreateVariable(folder, $"{id}.Power", "Active Power", "kW", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.WindSpeed", "Wind Speed", "m/s", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.RotorSpeed", "Rotor Speed", "RPM", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.GeneratorSpeed", "Generator Speed", "RPM", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.PitchAngle", "Blade Pitch Angle", "deg", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.NacelleDirection", "Nacelle Direction", "deg", DataTypeIds.Double, 0.0);
        
        // Electrical
        CreateVariable(folder, $"{id}.VoltageL1", "Voltage L1", "V", DataTypeIds.Double, 690.0);
        CreateVariable(folder, $"{id}.VoltageL2", "Voltage L2", "V", DataTypeIds.Double, 690.0);
        CreateVariable(folder, $"{id}.VoltageL3", "Voltage L3", "V", DataTypeIds.Double, 690.0);
        CreateVariable(folder, $"{id}.CurrentL1", "Current L1", "A", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.CurrentL2", "Current L2", "A", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.CurrentL3", "Current L3", "A", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.PowerFactor", "Power Factor", "", DataTypeIds.Double, 0.95);
        CreateVariable(folder, $"{id}.Frequency", "Grid Frequency", "Hz", DataTypeIds.Double, 60.0);

        // Temperatures
        CreateVariable(folder, $"{id}.GearboxTemp", "Gearbox Temperature", "°C", DataTypeIds.Double, 40.0);
        CreateVariable(folder, $"{id}.GeneratorTemp", "Generator Temperature", "°C", DataTypeIds.Double, 50.0);
        CreateVariable(folder, $"{id}.BearingTemp", "Main Bearing Temperature", "°C", DataTypeIds.Double, 35.0);
        CreateVariable(folder, $"{id}.AmbientTemp", "Ambient Temperature", "°C", DataTypeIds.Double, 25.0);

        // Status
        CreateVariable(folder, $"{id}.Status", "Operating Status", "0=Stop,1=Run,2=Fault", DataTypeIds.Int32, 1);
        CreateVariable(folder, $"{id}.AvailabilityStatus", "Availability", "0=Unavail,1=Avail", DataTypeIds.Int32, 1);
        CreateVariable(folder, $"{id}.GridConnection", "Grid Connection", "0=Off,1=On", DataTypeIds.Int32, 1);

        // Cumulative
        CreateVariable(folder, $"{id}.TotalEnergy", "Total Energy Production", "kWh", DataTypeIds.Double, _random.NextDouble() * 1000000);
        CreateVariable(folder, $"{id}.OperatingHours", "Operating Hours", "h", DataTypeIds.Double, _random.NextDouble() * 50000);
    }

    private void CreateMetTower(FolderState parent, string id)
    {
        var folder = CreateFolderState(parent, id, id, "Meteorological tower");
        AddPredefinedNode(SystemContext, folder);

        // Multiple height measurements
        CreateVariable(folder, $"{id}.WindSpeed80m", "Wind Speed 80m", "m/s", DataTypeIds.Double, 8.0);
        CreateVariable(folder, $"{id}.WindSpeed60m", "Wind Speed 60m", "m/s", DataTypeIds.Double, 7.5);
        CreateVariable(folder, $"{id}.WindSpeed40m", "Wind Speed 40m", "m/s", DataTypeIds.Double, 7.0);
        CreateVariable(folder, $"{id}.WindDir80m", "Wind Direction 80m", "deg", DataTypeIds.Double, 270.0);
        CreateVariable(folder, $"{id}.WindDir60m", "Wind Direction 60m", "deg", DataTypeIds.Double, 270.0);
        CreateVariable(folder, $"{id}.AmbientTemp", "Ambient Temperature", "°C", DataTypeIds.Double, 25.0);
        CreateVariable(folder, $"{id}.Humidity", "Relative Humidity", "%", DataTypeIds.Double, 60.0);
        CreateVariable(folder, $"{id}.Pressure", "Barometric Pressure", "hPa", DataTypeIds.Double, 1013.0);
        CreateVariable(folder, $"{id}.Precipitation", "Precipitation", "mm/h", DataTypeIds.Double, 0.0);
    }

    private void CreateWindSubstation(FolderState parent, string id)
    {
        var folder = CreateFolderState(parent, id, id, "Wind farm substation");
        AddPredefinedNode(SystemContext, folder);

        // POI Measurements
        CreateVariable(folder, $"{id}.TotalPower", "Total Active Power", "MW", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.ReactivePower", "Total Reactive Power", "MVAR", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.Voltage", "POI Voltage", "kV", DataTypeIds.Double, 34.5);
        CreateVariable(folder, $"{id}.Frequency", "Grid Frequency", "Hz", DataTypeIds.Double, 60.0);
        
        // Breakers (for relationship simulation)
        CreateVariable(folder, $"{id}.BreakerA", "Breaker A Status", "0=Trip,1=Closed", DataTypeIds.Int32, 1);
        CreateVariable(folder, $"{id}.BreakerB", "Breaker B Status", "0=Trip,1=Closed", DataTypeIds.Int32, 1);
        
        // Transformer
        CreateVariable(folder, $"{id}.TransformerTemp", "Transformer Temperature", "°C", DataTypeIds.Double, 45.0);
        CreateVariable(folder, $"{id}.OilTemp", "Transformer Oil Temperature", "°C", DataTypeIds.Double, 40.0);
    }

    #endregion

    #region Solar Site Creation

    private void CreateSolarSite(FolderState root)
    {
        // Create Brixton Solar BESS site structure: 8 inverter blocks × 8 inverters + 4 BESS blocks
        // root is already created with SiteId, add BUXOM/A01 structure below it
        var buxomFolder = CreateFolderState(root, "BUXOM", "BUXOM", "Brixton Solar BESS");
        AddPredefinedNode(SystemContext, buxomFolder);
        
        var a01 = CreateFolderState(buxomFolder, "A01", "Area 01", "Main solar and storage area");
        AddPredefinedNode(SystemContext, a01);

        // Site-level aggregation
        CreateVariable(a01, "Site.ActivePower", "Total Active Power", "kW", DataTypeIds.Double, 0.0);
        CreateVariable(a01, "Site.AvailablePower", "Available Power", "kW", DataTypeIds.Double, 0.0);
        CreateVariable(a01, "Site.EnergyDay", "Energy Today", "kWh", DataTypeIds.Double, 0.0);
        CreateVariable(a01, "Site.EnergyTotal", "Total Energy", "MWh", DataTypeIds.Double, 0.0);

        // Create 8 inverter blocks (F1A through F1H)
        string[] blockNames = { "F1A", "F1B", "F1C", "F1D", "F1E", "F1F", "F1G", "F1H" };
        
        foreach (var blockName in blockNames)
        {
            var blockFolder = CreateFolderState(a01, blockName, $"Block {blockName}", $"Inverter block {blockName}");
            AddPredefinedNode(SystemContext, blockFolder);

            // 8 inverters per block (INV01 through INV08)
            for (int invNum = 1; invNum <= 8; invNum++)
            {
                string invId = $"INV{invNum:D2}";
                var invFolder = CreateFolderState(blockFolder, invId, invId, $"String inverter {invNum}");
                AddPredefinedNode(SystemContext, invFolder);
                
                var invSubFolder = CreateFolderState(invFolder, $"inv{invNum:D2}", $"inv{invNum:D2}", "Inverter data");
                AddPredefinedNode(SystemContext, invSubFolder);

                string tagPrefix = $"{blockName}-{invId}";
                _equipment[tagPrefix] = new EquipmentState { Id = tagPrefix, Manufacturer = "SMA", RatedPower = 125 };

                // AC Power measurements
                CreateVariable(invSubFolder, $"{tagPrefix}.PAC", "Active Power", "kW", DataTypeIds.Double, 0.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.QAC", "Reactive Power", "kVAR", DataTypeIds.Double, 0.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.SAC", "Apparent Power", "kVA", DataTypeIds.Double, 0.0);
                
                // AC Voltages
                CreateVariable(invSubFolder, $"{tagPrefix}.VAC_AB", "Voltage L1-L2", "V", DataTypeIds.Double, 480.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.VAC_BC", "Voltage L2-L3", "V", DataTypeIds.Double, 480.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.VAC_CA", "Voltage L3-L1", "V", DataTypeIds.Double, 480.0);
                
                // AC Currents
                CreateVariable(invSubFolder, $"{tagPrefix}.IAC_A", "Current L1", "A", DataTypeIds.Double, 0.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.IAC_B", "Current L2", "A", DataTypeIds.Double, 0.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.IAC_C", "Current L3", "A", DataTypeIds.Double, 0.0);
                
                // DC Side
                CreateVariable(invSubFolder, $"{tagPrefix}.VDC", "DC Voltage", "V", DataTypeIds.Double, 800.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.IDC", "DC Current", "A", DataTypeIds.Double, 0.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.PDC", "DC Power", "kW", DataTypeIds.Double, 0.0);
                
                // Temperatures
                CreateVariable(invSubFolder, $"{tagPrefix}.TempCab", "Cabinet Temp", "°C", DataTypeIds.Double, 35.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.TempHS", "Heatsink Temp", "°C", DataTypeIds.Double, 45.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.TempAmb", "Ambient Temp", "°C", DataTypeIds.Double, 30.0);
                
                // Energy counters
                CreateVariable(invSubFolder, $"{tagPrefix}.E_Day", "Energy Today", "kWh", DataTypeIds.Double, 0.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.E_Total", "Total Energy", "MWh", DataTypeIds.Double, _random.NextDouble() * 500);
                
                // Status and grid
                CreateVariable(invSubFolder, $"{tagPrefix}.Status", "Status", "0=Off,1=On,2=Fault", DataTypeIds.Int32, 1);
                CreateVariable(invSubFolder, $"{tagPrefix}.Frequency", "Grid Frequency", "Hz", DataTypeIds.Double, 60.0);
                CreateVariable(invSubFolder, $"{tagPrefix}.PF", "Power Factor", "", DataTypeIds.Double, 0.98);
                
                // String diagnostics (20 strings per inverter)
                for (int str = 1; str <= 20; str++)
                {
                    CreateVariable(invSubFolder, $"{tagPrefix}.Str{str:D2}_V", $"String {str} Voltage", "V", DataTypeIds.Double, 800.0);
                    CreateVariable(invSubFolder, $"{tagPrefix}.Str{str:D2}_I", $"String {str} Current", "A", DataTypeIds.Double, 5.0);
                }
            }
        }

        // Create 4 BESS blocks (B01 through B04)
        var bessFolder = CreateFolderState(a01, "BESS", "BESS", "Battery Energy Storage System");
        AddPredefinedNode(SystemContext, bessFolder);
        
        for (int bessNum = 1; bessNum <= 4; bessNum++)
        {
            string bessId = $"B{bessNum:D2}";
            var bessBlockFolder = CreateFolderState(bessFolder, bessId, bessId, $"BESS Block {bessNum}");
            AddPredefinedNode(SystemContext, bessBlockFolder);
            
            _equipment[bessId] = new EquipmentState { Id = bessId, Manufacturer = "Tesla", RatedPower = 1000 };

            CreateVariable(bessBlockFolder, $"{bessId}.SOC", "State of Charge", "%", DataTypeIds.Double, 50.0);
            CreateVariable(bessBlockFolder, $"{bessId}.SOH", "State of Health", "%", DataTypeIds.Double, 95.0);
            CreateVariable(bessBlockFolder, $"{bessId}.Power", "Power", "kW", DataTypeIds.Double, 0.0);
            CreateVariable(bessBlockFolder, $"{bessId}.Voltage", "Voltage", "V", DataTypeIds.Double, 800.0);
            CreateVariable(bessBlockFolder, $"{bessId}.Current", "Current", "A", DataTypeIds.Double, 0.0);
            CreateVariable(bessBlockFolder, $"{bessId}.Temp", "Temperature", "°C", DataTypeIds.Double, 25.0);
            CreateVariable(bessBlockFolder, $"{bessId}.Status", "Status", "0=Idle,1=Charge,2=Discharge,3=Fault", DataTypeIds.Int32, 0);
        }

        // Met station
        var metFolder = CreateFolderState(a01, "Met", "Met Station", "Solar meteorological station");
        AddPredefinedNode(SystemContext, metFolder);
        CreateVariable(metFolder, "MET.GHI", "Global Horizontal Irradiance", "W/m²", DataTypeIds.Double, 800.0);
        CreateVariable(metFolder, "MET.POA", "Plane of Array Irradiance", "W/m²", DataTypeIds.Double, 850.0);
        CreateVariable(metFolder, "MET.AmbTemp", "Ambient Temperature", "°C", DataTypeIds.Double, 30.0);
        CreateVariable(metFolder, "MET.ModTemp", "Module Temperature", "°C", DataTypeIds.Double, 50.0);
        CreateVariable(metFolder, "MET.WindSpeed", "Wind Speed", "m/s", DataTypeIds.Double, 5.0);
        CreateVariable(metFolder, "MET.WindDir", "Wind Direction", "deg", DataTypeIds.Double, 180.0);
        CreateVariable(metFolder, "MET.Humidity", "Relative Humidity", "%", DataTypeIds.Double, 60.0);
        CreateVariable(metFolder, "MET.Pressure", "Barometric Pressure", "hPa", DataTypeIds.Double, 1013.0);
    }

    private void CreateInverter(FolderState parent, string id, string manufacturer, double ratedPowerKw)
    {
        var folder = CreateFolderState(parent, id, id, $"{manufacturer} central inverter");
        AddPredefinedNode(SystemContext, folder);

        var state = new EquipmentState { Id = id, Manufacturer = manufacturer, RatedPower = ratedPowerKw };
        _equipment[id] = state;

        // DC Side
        CreateVariable(folder, $"{id}.DcVoltage", "DC Voltage", "V", DataTypeIds.Double, 800.0);
        CreateVariable(folder, $"{id}.DcCurrent", "DC Current", "A", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.DcPower", "DC Power", "kW", DataTypeIds.Double, 0.0);

        // AC Side
        CreateVariable(folder, $"{id}.AcPower", "AC Power", "kW", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.AcVoltageL1", "AC Voltage L1", "V", DataTypeIds.Double, 480.0);
        CreateVariable(folder, $"{id}.AcVoltageL2", "AC Voltage L2", "V", DataTypeIds.Double, 480.0);
        CreateVariable(folder, $"{id}.AcVoltageL3", "AC Voltage L3", "V", DataTypeIds.Double, 480.0);
        CreateVariable(folder, $"{id}.AcCurrentL1", "AC Current L1", "A", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.AcCurrentL2", "AC Current L2", "A", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.AcCurrentL3", "AC Current L3", "A", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.Frequency", "Grid Frequency", "Hz", DataTypeIds.Double, 60.0);
        CreateVariable(folder, $"{id}.PowerFactor", "Power Factor", "", DataTypeIds.Double, 0.98);

        // Efficiency & Performance
        CreateVariable(folder, $"{id}.Efficiency", "Conversion Efficiency", "%", DataTypeIds.Double, 98.0);
        CreateVariable(folder, $"{id}.PerformanceRatio", "Performance Ratio", "%", DataTypeIds.Double, 85.0);

        // Temperatures
        CreateVariable(folder, $"{id}.CabinetTemp", "Cabinet Temperature", "°C", DataTypeIds.Double, 35.0);
        CreateVariable(folder, $"{id}.HeatsinkTemp", "Heatsink Temperature", "°C", DataTypeIds.Double, 45.0);
        CreateVariable(folder, $"{id}.ModuleTemp", "Module Temperature", "°C", DataTypeIds.Double, 50.0);

        // Status
        CreateVariable(folder, $"{id}.Status", "Operating Status", "0=Stop,1=Run,2=Fault", DataTypeIds.Int32, 1);
        CreateVariable(folder, $"{id}.FaultCode", "Fault Code", "", DataTypeIds.Int32, 0);

        // Cumulative
        CreateVariable(folder, $"{id}.TotalEnergy", "Total Energy Production", "kWh", DataTypeIds.Double, _random.NextDouble() * 500000);
    }

    private void CreateTracker(FolderState parent, string id)
    {
        var folder = CreateFolderState(parent, id, id, "Single-axis solar tracker");
        AddPredefinedNode(SystemContext, folder);

        CreateVariable(folder, $"{id}.Angle", "Tracker Angle", "deg", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.TargetAngle", "Target Angle", "deg", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.TrackingMode", "Tracking Mode", "0=Manual,1=Auto,2=Stow", DataTypeIds.Int32, 1);
        CreateVariable(folder, $"{id}.WindStow", "Wind Stow Active", "0=No,1=Yes", DataTypeIds.Int32, 0);
        CreateVariable(folder, $"{id}.MotorCurrent", "Motor Current", "A", DataTypeIds.Double, 0.5);
    }

    private void CreateCollectionNode(FolderState parent, string id)
    {
        var folder = CreateFolderState(parent, id, id, "DC collection node");
        AddPredefinedNode(SystemContext, folder);

        CreateVariable(folder, $"{id}.DcVoltage", "DC Bus Voltage", "V", DataTypeIds.Double, 800.0);
        CreateVariable(folder, $"{id}.DcCurrent", "DC Bus Current", "A", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.Power", "Power", "kW", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.StringCount", "Connected Strings", "", DataTypeIds.Int32, 16);
    }

    private void CreateSolarMetStation(FolderState parent, string id)
    {
        var folder = CreateFolderState(parent, id, id, "Solar met station");
        AddPredefinedNode(SystemContext, folder);

        CreateVariable(folder, $"{id}.GHI", "Global Horizontal Irradiance", "W/m²", DataTypeIds.Double, 800.0);
        CreateVariable(folder, $"{id}.POA", "Plane of Array Irradiance", "W/m²", DataTypeIds.Double, 850.0);
        CreateVariable(folder, $"{id}.DNI", "Direct Normal Irradiance", "W/m²", DataTypeIds.Double, 700.0);
        CreateVariable(folder, $"{id}.DHI", "Diffuse Horizontal Irradiance", "W/m²", DataTypeIds.Double, 150.0);
        CreateVariable(folder, $"{id}.AmbientTemp", "Ambient Temperature", "°C", DataTypeIds.Double, 30.0);
        CreateVariable(folder, $"{id}.ModuleTemp", "Module Temperature", "°C", DataTypeIds.Double, 45.0);
        CreateVariable(folder, $"{id}.WindSpeed", "Wind Speed", "m/s", DataTypeIds.Double, 3.0);
        CreateVariable(folder, $"{id}.WindDir", "Wind Direction", "deg", DataTypeIds.Double, 180.0);
        CreateVariable(folder, $"{id}.Humidity", "Relative Humidity", "%", DataTypeIds.Double, 30.0);
    }

    private void CreateSolarSubstation(FolderState parent, string id)
    {
        var folder = CreateFolderState(parent, id, id, "Solar substation");
        AddPredefinedNode(SystemContext, folder);

        CreateVariable(folder, $"{id}.TotalPower", "Total AC Power", "MW", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.ReactivePower", "Reactive Power", "MVAR", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.Voltage", "POI Voltage", "kV", DataTypeIds.Double, 34.5);
        CreateVariable(folder, $"{id}.Frequency", "Grid Frequency", "Hz", DataTypeIds.Double, 60.0);
        CreateVariable(folder, $"{id}.PowerFactor", "Power Factor", "", DataTypeIds.Double, 0.98);
        CreateVariable(folder, $"{id}.ExportLimit", "Export Limit", "MW", DataTypeIds.Double, 100.0);
        CreateVariable(folder, $"{id}.CurtailmentActive", "Curtailment Active", "0=No,1=Yes", DataTypeIds.Int32, 0);
    }

    #endregion

    #region BESS Site Creation

    private void CreateBessSite(FolderState root)
    {
        // Power Conversion Systems (Tesla Megapack)
        var pcsFolder = CreateFolderState(root, "PCS", "Power Conversion Systems", "Tesla Megapack PCS units");
        AddPredefinedNode(SystemContext, pcsFolder);

        for (int i = 1; i <= _config.TeslaPcsCount; i++)
        {
            CreatePcs(pcsFolder, $"TESLA-MP-{i:D2}", "Tesla", 25000);
        }

        // Battery Banks (BYD)
        var banksFolder = CreateFolderState(root, "Banks", "Battery Banks", "BYD battery banks");
        AddPredefinedNode(SystemContext, banksFolder);

        for (int i = 1; i <= _config.BydBankCount; i++)
        {
            CreateBatteryBank(banksFolder, $"BYD-BANK-{i:D2}", "BYD", 100000);
        }

        // Substation
        var subFolder = CreateFolderState(root, "Substation", "Substation", "Grid interconnection");
        AddPredefinedNode(SystemContext, subFolder);
        CreateBessSubstation(subFolder, "SUB-BESS-01");
    }

    private void CreatePcs(FolderState parent, string id, string manufacturer, double ratedPowerKw)
    {
        var folder = CreateFolderState(parent, id, id, $"{manufacturer} power conversion system");
        AddPredefinedNode(SystemContext, folder);

        var state = new EquipmentState { Id = id, Manufacturer = manufacturer, RatedPower = ratedPowerKw };
        _equipment[id] = state;

        // Power Flow
        CreateVariable(folder, $"{id}.ActivePower", "Active Power", "kW", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.ReactivePower", "Reactive Power", "kVAR", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.ApparentPower", "Apparent Power", "kVA", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.PowerFactor", "Power Factor", "", DataTypeIds.Double, 0.98);
        CreateVariable(folder, $"{id}.OperatingMode", "Operating Mode", "0=Standby,1=Charge,2=Discharge", DataTypeIds.Int32, 0);

        // DC Side (Battery)
        CreateVariable(folder, $"{id}.DcVoltage", "DC Bus Voltage", "V", DataTypeIds.Double, 800.0);
        CreateVariable(folder, $"{id}.DcCurrent", "DC Current", "A", DataTypeIds.Double, 0.0);

        // AC Side (Grid)
        CreateVariable(folder, $"{id}.AcVoltageL1", "AC Voltage L1", "V", DataTypeIds.Double, 480.0);
        CreateVariable(folder, $"{id}.AcVoltageL2", "AC Voltage L2", "V", DataTypeIds.Double, 480.0);
        CreateVariable(folder, $"{id}.AcVoltageL3", "AC Voltage L3", "V", DataTypeIds.Double, 480.0);
        CreateVariable(folder, $"{id}.Frequency", "Grid Frequency", "Hz", DataTypeIds.Double, 60.0);

        // Efficiency
        CreateVariable(folder, $"{id}.RoundTripEfficiency", "Round-Trip Efficiency", "%", DataTypeIds.Double, 92.0);

        // Temperatures
        CreateVariable(folder, $"{id}.InverterTemp", "Inverter Temperature", "°C", DataTypeIds.Double, 35.0);
        CreateVariable(folder, $"{id}.CabinetTemp", "Cabinet Temperature", "°C", DataTypeIds.Double, 30.0);

        // Status
        CreateVariable(folder, $"{id}.Status", "Operating Status", "0=Off,1=On,2=Fault", DataTypeIds.Int32, 1);
        CreateVariable(folder, $"{id}.GridConnected", "Grid Connected", "0=No,1=Yes", DataTypeIds.Int32, 1);
    }

    private void CreateBatteryBank(FolderState parent, string id, string manufacturer, double capacityKwh)
    {
        var folder = CreateFolderState(parent, id, id, $"{manufacturer} battery bank");
        AddPredefinedNode(SystemContext, folder);

        // State of Charge
        CreateVariable(folder, $"{id}.SOC", "State of Charge", "%", DataTypeIds.Double, 50.0 + _random.NextDouble() * 30);
        CreateVariable(folder, $"{id}.SOH", "State of Health", "%", DataTypeIds.Double, 98.0);
        CreateVariable(folder, $"{id}.DOD", "Depth of Discharge", "%", DataTypeIds.Double, 20.0);

        // Electrical
        CreateVariable(folder, $"{id}.Voltage", "Bank Voltage", "V", DataTypeIds.Double, 800.0);
        CreateVariable(folder, $"{id}.Current", "Bank Current", "A", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.Power", "Bank Power", "kW", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.AvailableEnergy", "Available Energy", "kWh", DataTypeIds.Double, capacityKwh * 0.5);
        CreateVariable(folder, $"{id}.Capacity", "Rated Capacity", "kWh", DataTypeIds.Double, capacityKwh);

        // Temperatures
        CreateVariable(folder, $"{id}.MaxCellTemp", "Max Cell Temperature", "°C", DataTypeIds.Double, 28.0);
        CreateVariable(folder, $"{id}.MinCellTemp", "Min Cell Temperature", "°C", DataTypeIds.Double, 26.0);
        CreateVariable(folder, $"{id}.AvgCellTemp", "Avg Cell Temperature", "°C", DataTypeIds.Double, 27.0);
        CreateVariable(folder, $"{id}.CoolingStatus", "Cooling System Status", "0=Off,1=On", DataTypeIds.Int32, 0);

        // Safety
        CreateVariable(folder, $"{id}.MaxCellVoltage", "Max Cell Voltage", "V", DataTypeIds.Double, 3.7);
        CreateVariable(folder, $"{id}.MinCellVoltage", "Min Cell Voltage", "V", DataTypeIds.Double, 3.6);
        CreateVariable(folder, $"{id}.CellImbalance", "Cell Imbalance", "mV", DataTypeIds.Double, 15.0);

        // Status
        CreateVariable(folder, $"{id}.Status", "Bank Status", "0=Off,1=Standby,2=Charge,3=Discharge", DataTypeIds.Int32, 1);
        CreateVariable(folder, $"{id}.AlarmCount", "Active Alarms", "", DataTypeIds.Int32, 0);

        // Cycles
        CreateVariable(folder, $"{id}.CycleCount", "Total Cycle Count", "", DataTypeIds.Int32, _random.Next(100, 500));
    }

    private void CreateBessSubstation(FolderState parent, string id)
    {
        var folder = CreateFolderState(parent, id, id, "BESS substation");
        AddPredefinedNode(SystemContext, folder);

        CreateVariable(folder, $"{id}.TotalPower", "Total Power", "MW", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.NetFlow", "Net Power Flow", "MW", DataTypeIds.Double, 0.0);
        CreateVariable(folder, $"{id}.Voltage", "POI Voltage", "kV", DataTypeIds.Double, 138.0);
        CreateVariable(folder, $"{id}.Frequency", "Grid Frequency", "Hz", DataTypeIds.Double, 60.0);
        CreateVariable(folder, $"{id}.TotalSOC", "Aggregate SOC", "%", DataTypeIds.Double, 50.0);
        CreateVariable(folder, $"{id}.AvailableCharge", "Available Charge Capacity", "MWh", DataTypeIds.Double, 150.0);
        CreateVariable(folder, $"{id}.AvailableDischarge", "Available Discharge Capacity", "MWh", DataTypeIds.Double, 150.0);
        CreateVariable(folder, $"{id}.GridServiceMode", "Grid Service Mode", "0=None,1=FreqReg,2=PeakShave,3=Arb", DataTypeIds.Int32, 0);
    }

    #endregion

    #region Helpers

    private FolderState CreateFolderState(NodeState? parent, string path, string name, string description)
    {
        var folder = new FolderState(parent)
        {
            SymbolicName = path,
            ReferenceTypeId = ReferenceTypes.Organizes,
            TypeDefinitionId = ObjectTypeIds.FolderType,
            NodeId = new NodeId(path, NamespaceIndex),
            BrowseName = new QualifiedName(path, NamespaceIndex),
            DisplayName = new LocalizedText("en", name),
            Description = new LocalizedText("en", description),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            EventNotifier = EventNotifiers.None
        };

        // Add folder as child of parent (required for browse to work!)
        parent?.AddChild(folder);

        return folder;
    }

    private void CreateVariable(FolderState parent, string path, string name, string units, NodeId dataType, object initialValue)
    {
        var variable = new BaseDataVariableState(parent)
        {
            SymbolicName = path,
            ReferenceTypeId = ReferenceTypes.Organizes,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            NodeId = new NodeId(path, NamespaceIndex),
            BrowseName = new QualifiedName(path, NamespaceIndex),
            DisplayName = new LocalizedText("en", name),
            Description = new LocalizedText("en", $"{name} ({units})"),
            DataType = dataType,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentReadOrWrite,
            UserAccessLevel = AccessLevels.CurrentReadOrWrite,
            MinimumSamplingInterval = 100,
            Historizing = false,
            Value = initialValue,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow
        };

        // Add variable as child of parent (required for browse to work!)
        parent.AddChild(variable);
        
        AddPredefinedNode(SystemContext, variable);
        _dynamicNodes.Add(variable);
    }

    #endregion

    #region Simulation

    private void UpdateSimulation(object? state)
    {
        lock (Lock)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Update environmental conditions
                UpdateEnvironment(now);

                // Update site-specific simulation
                switch (_config.SiteType)
                {
                    case SiteType.Wind:
                        UpdateWindSimulation(now);
                        break;
                    case SiteType.Solar:
                        UpdateSolarSimulation(now);
                        break;
                    case SiteType.Bess:
                        UpdateBessSimulation(now);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Simulation update error");
            }
        }
    }

    private void UpdateEnvironment(DateTime now)
    {
        // Slowly varying environmental conditions
        _ambientTemp += (_random.NextDouble() - 0.5) * 0.1;
        _ambientTemp = Math.Clamp(_ambientTemp, 10, 40);

        _windSpeed += (_random.NextDouble() - 0.5) * 0.5;
        _windSpeed = Math.Clamp(_windSpeed, 2, 25);

        // Solar irradiance follows time of day pattern
        var hour = now.Hour + now.Minute / 60.0;
        var solarFactor = Math.Max(0, Math.Sin((hour - 6) / 12 * Math.PI)); // 6 AM to 6 PM
        _solarIrradiance = 1000 * solarFactor * (0.9 + _random.NextDouble() * 0.2);
    }

    private void UpdateWindSimulation(DateTime now)
    {
        double totalPower = 0;

        foreach (var node in _dynamicNodes)
        {
            var path = node.BrowseName.Name;
            if (path.Contains("GE-2.5-") || path.Contains("V110-"))
            {
                var parts = path.Split('.');
                if (parts.Length < 2) continue;
                var turbineId = parts[0];
                var measurement = parts[1];

                // Check if turbine is affected by breaker trip
                bool isGe = turbineId.StartsWith("GE-");
                bool affectedByBreaker = (isGe && _breakerATripped) || (!isGe && _breakerBTripped);

                UpdateTurbineValue(node, turbineId, measurement, affectedByBreaker, now, ref totalPower);
            }
            else if (path.Contains("MET-"))
            {
                UpdateMetTowerValue(node, path, now);
            }
            else if (path.Contains("SUB-WIND"))
            {
                if (path.EndsWith(".TotalPower"))
                    UpdateNodeValue(node, totalPower / 1000.0, now); // MW
                else if (path.EndsWith(".BreakerA"))
                    UpdateNodeValue(node, _breakerATripped ? 0 : 1, now);
                else if (path.EndsWith(".BreakerB"))
                    UpdateNodeValue(node, _breakerBTripped ? 0 : 1, now);
            }
        }
    }

    private void UpdateTurbineValue(BaseDataVariableState node, string turbineId, string measurement, 
        bool affectedByBreaker, DateTime now, ref double totalPower)
    {
        if (affectedByBreaker)
        {
            // Turbine offline due to breaker trip
            if (measurement == "Power" || measurement == "CurrentL1" || measurement == "CurrentL2" || measurement == "CurrentL3")
                UpdateNodeValue(node, 0.0, now);
            else if (measurement == "Status")
                UpdateNodeValue(node, 0, now);
            return;
        }

        if (_equipment.TryGetValue(turbineId, out var eq))
        {
            switch (measurement)
            {
                case "Power":
                    // Power curve based on wind speed
                    var powerRatio = CalculatePowerCurve(_windSpeed, eq.RatedPower);
                    var power = eq.RatedPower * powerRatio * (0.95 + _random.NextDouble() * 0.1);
                    UpdateNodeValue(node, power, now);
                    totalPower += power;
                    break;
                case "WindSpeed":
                    UpdateNodeValue(node, _windSpeed + (_random.NextDouble() - 0.5) * 0.5, now);
                    break;
                case "RotorSpeed":
                    var rpm = Math.Min(15.0, _windSpeed * 1.2 + _random.NextDouble() * 0.5);
                    UpdateNodeValue(node, rpm, now);
                    break;
                case "GeneratorSpeed":
                    UpdateNodeValue(node, 1500 + _random.NextDouble() * 100, now);
                    break;
                case "PitchAngle":
                    var pitch = _windSpeed > 12 ? (_windSpeed - 12) * 3 : 0;
                    UpdateNodeValue(node, pitch + _random.NextDouble() * 0.5, now);
                    break;
                case "AmbientTemp":
                    UpdateNodeValue(node, _ambientTemp, now);
                    break;
                case "GearboxTemp":
                    UpdateNodeValue(node, 40 + _windSpeed * 0.5 + _random.NextDouble() * 2, now);
                    break;
                case "GeneratorTemp":
                    UpdateNodeValue(node, 50 + _windSpeed * 0.8 + _random.NextDouble() * 3, now);
                    break;
            }
        }
    }

    private void UpdateMetTowerValue(BaseDataVariableState node, string path, DateTime now)
    {
        if (path.EndsWith(".WindSpeed80m"))
            UpdateNodeValue(node, _windSpeed, now);
        else if (path.EndsWith(".WindSpeed60m"))
            UpdateNodeValue(node, _windSpeed * 0.95, now);
        else if (path.EndsWith(".WindSpeed40m"))
            UpdateNodeValue(node, _windSpeed * 0.9, now);
        else if (path.EndsWith(".AmbientTemp"))
            UpdateNodeValue(node, _ambientTemp, now);
    }

    private void UpdateSolarSimulation(DateTime now)
    {
        double totalPower = 0;
        double totalAvailablePower = 0;

        foreach (var node in _dynamicNodes)
        {
            var path = node.BrowseName.Name;
            
            // Brixton Solar structure: F1A-INV01.PAC, B01.SOC, MET.GHI, etc.
            if (path.StartsWith("F1") && path.Contains("-INV"))
            {
                // Inverter tag: F1A-INV01.PAC
                var parts = path.Split('.');
                if (parts.Length < 2) continue;
                var inverterId = parts[0]; // e.g., "F1A-INV01"
                var measurement = parts[1];

                UpdateBrixtonInverterValue(node, inverterId, measurement, now, ref totalPower, ref totalAvailablePower);
            }
            else if (path.StartsWith("B") && (path.StartsWith("B01") || path.StartsWith("B02") || 
                     path.StartsWith("B03") || path.StartsWith("B04")))
            {
                // BESS tag: B01.SOC
                var parts = path.Split('.');
                if (parts.Length < 2) continue;
                var bessId = parts[0];
                var measurement = parts[1];

                UpdateBrixtonBessValue(node, bessId, measurement, now);
            }
            else if (path.StartsWith("MET."))
            {
                // Met station: MET.GHI
                UpdateBrixtonMetValue(node, path, now);
            }
            else if (path.StartsWith("Site."))
            {
                // Site aggregation
                if (path.EndsWith(".ActivePower"))
                    UpdateNodeValue(node, totalPower, now);
                else if (path.EndsWith(".AvailablePower"))
                    UpdateNodeValue(node, totalAvailablePower, now);
            }
            // Legacy support for old simulator tags
            else if (path.Contains("SMA-SC-"))
            {
                var parts = path.Split('.');
                if (parts.Length < 2) continue;
                var inverterId = parts[0];
                var measurement = parts[1];

                UpdateInverterValue(node, inverterId, measurement, now, ref totalPower);
            }
            else if (path.Contains("MET-SOLAR"))
            {
                UpdateSolarMetValue(node, path, now);
            }
        }
    }

    private void UpdateBrixtonInverterValue(BaseDataVariableState node, string inverterId, string measurement, 
        DateTime now, ref double totalPower, ref double totalAvailablePower)
    {
        if (_equipment.TryGetValue(inverterId, out var eq))
        {
            var irradianceRatio = _solarIrradiance / 1000.0;
            var power = eq.RatedPower * irradianceRatio * 0.85 * (0.95 + _random.NextDouble() * 0.1);
            var availPower = eq.RatedPower * irradianceRatio * 0.95;

            switch (measurement)
            {
                case "PAC":
                    UpdateNodeValue(node, power, now);
                    totalPower += power;
                    totalAvailablePower += availPower;
                    break;
                case "QAC":
                    UpdateNodeValue(node, power * 0.1 * (_random.NextDouble() - 0.5), now);
                    break;
                case "SAC":
                    var reactive = power * 0.1;
                    UpdateNodeValue(node, Math.Sqrt(power * power + reactive * reactive), now);
                    break;
                case "VAC_AB":
                case "VAC_BC":
                case "VAC_CA":
                    UpdateNodeValue(node, 480.0 + _random.NextDouble() * 10 - 5, now);
                    break;
                case "IAC_A":
                case "IAC_B":
                case "IAC_C":
                    UpdateNodeValue(node, power * 1000 / (480 * Math.Sqrt(3)), now);
                    break;
                case "VDC":
                    UpdateNodeValue(node, 800.0 + _random.NextDouble() * 20 - 10, now);
                    break;
                case "IDC":
                    UpdateNodeValue(node, power * 1000 / 800.0, now);
                    break;
                case "PDC":
                    UpdateNodeValue(node, power / 0.98, now);
                    break;
                case "TempCab":
                    UpdateNodeValue(node, _ambientTemp + 10 + irradianceRatio * 5, now);
                    break;
                case "TempHS":
                    UpdateNodeValue(node, _ambientTemp + 20 + irradianceRatio * 10, now);
                    break;
                case "TempAmb":
                    UpdateNodeValue(node, _ambientTemp, now);
                    break;
                case "E_Day":
                    // Cumulative energy today (should reset at midnight, simplified here)
                    var dailyEnergy = (DateTime.Now.Hour * 60 + DateTime.Now.Minute) * power / 60.0;
                    UpdateNodeValue(node, dailyEnergy, now);
                    break;
                case "Status":
                    UpdateNodeValue(node, irradianceRatio > 0.1 ? 1 : 0, now);
                    break;
                case "Frequency":
                    UpdateNodeValue(node, 60.0 + _random.NextDouble() * 0.1 - 0.05, now);
                    break;
                case "PF":
                    UpdateNodeValue(node, 0.98 + _random.NextDouble() * 0.04 - 0.02, now);
                    break;
                default:
                    // String measurements: Str01_V, Str01_I, etc.
                    if (measurement.StartsWith("Str") && measurement.EndsWith("_V"))
                        UpdateNodeValue(node, 800.0 * irradianceRatio + _random.NextDouble() * 10 - 5, now);
                    else if (measurement.StartsWith("Str") && measurement.EndsWith("_I"))
                        UpdateNodeValue(node, 5.0 * irradianceRatio + _random.NextDouble() * 0.5, now);
                    break;
            }
        }
    }

    private void UpdateBrixtonBessValue(BaseDataVariableState node, string bessId, string measurement, DateTime now)
    {
        if (_equipment.TryGetValue(bessId, out var eq))
        {
            // Simple BESS simulation
            var hour = now.Hour;
            var isCharging = hour >= 10 && hour < 15; // Charge during peak solar
            var isDischarging = hour >= 18 && hour < 21; // Discharge during evening peak

            switch (measurement)
            {
                case "SOC":
                    var soc = 50.0 + Math.Sin(hour * Math.PI / 12) * 30; // 20-80% range
                    UpdateNodeValue(node, Math.Clamp(soc, 20, 80), now);
                    break;
                case "SOH":
                    UpdateNodeValue(node, 95.0 + _random.NextDouble() * 2, now);
                    break;
                case "Power":
                    var power = isCharging ? eq.RatedPower * 0.8 : (isDischarging ? -eq.RatedPower * 0.6 : 0);
                    UpdateNodeValue(node, power + _random.NextDouble() * 50 - 25, now);
                    break;
                case "Voltage":
                    UpdateNodeValue(node, 800.0 + _random.NextDouble() * 20 - 10, now);
                    break;
                case "Current":
                    var current = isCharging ? 1000.0 : (isDischarging ? -800.0 : 0);
                    UpdateNodeValue(node, current + _random.NextDouble() * 50 - 25, now);
                    break;
                case "Temp":
                    UpdateNodeValue(node, 25.0 + _random.NextDouble() * 5, now);
                    break;
                case "Status":
                    var status = isCharging ? 1 : (isDischarging ? 2 : 0);
                    UpdateNodeValue(node, status, now);
                    break;
            }
        }
    }

    private void UpdateBrixtonMetValue(BaseDataVariableState node, string path, DateTime now)
    {
        if (path.EndsWith(".GHI"))
            UpdateNodeValue(node, _solarIrradiance, now);
        else if (path.EndsWith(".POA"))
            UpdateNodeValue(node, _solarIrradiance * 1.05, now);
        else if (path.EndsWith(".AmbTemp"))
            UpdateNodeValue(node, _ambientTemp, now);
        else if (path.EndsWith(".ModTemp"))
            UpdateNodeValue(node, _ambientTemp + 20 + _solarIrradiance / 50, now);
        else if (path.EndsWith(".WindSpeed"))
            UpdateNodeValue(node, _windSpeed, now);
        else if (path.EndsWith(".WindDir"))
            UpdateNodeValue(node, 180.0 + _random.NextDouble() * 40 - 20, now);
        else if (path.EndsWith(".Humidity"))
            UpdateNodeValue(node, 60.0 + _random.NextDouble() * 20 - 10, now);
        else if (path.EndsWith(".Pressure"))
            UpdateNodeValue(node, 1013.0 + _random.NextDouble() * 10 - 5, now);
    }

    private void UpdateInverterValue(BaseDataVariableState node, string inverterId, string measurement, 
        DateTime now, ref double totalPower)
    {
        if (_equipment.TryGetValue(inverterId, out var eq))
        {
            var irradianceRatio = _solarIrradiance / 1000.0;
            
            switch (measurement)
            {
                case "AcPower":
                case "DcPower":
                    var power = eq.RatedPower * irradianceRatio * 0.85 * (0.95 + _random.NextDouble() * 0.1);
                    UpdateNodeValue(node, power, now);
                    if (measurement == "AcPower") totalPower += power;
                    break;
                case "DcCurrent":
                    UpdateNodeValue(node, eq.RatedPower * irradianceRatio / 800.0, now);
                    break;
                case "Efficiency":
                    UpdateNodeValue(node, 97 + _random.NextDouble() * 2, now);
                    break;
                case "CabinetTemp":
                    UpdateNodeValue(node, _ambientTemp + 10 + irradianceRatio * 5, now);
                    break;
            }
        }
    }

    private void UpdateSolarMetValue(BaseDataVariableState node, string path, DateTime now)
    {
        if (path.EndsWith(".GHI"))
            UpdateNodeValue(node, _solarIrradiance, now);
        else if (path.EndsWith(".POA"))
            UpdateNodeValue(node, _solarIrradiance * 1.05, now);
        else if (path.EndsWith(".AmbientTemp"))
            UpdateNodeValue(node, _ambientTemp + 5, now);
        else if (path.EndsWith(".ModuleTemp"))
            UpdateNodeValue(node, _ambientTemp + 20 + _solarIrradiance / 50, now);
    }

    private void UpdateTrackerValue(BaseDataVariableState node, string path, DateTime now)
    {
        if (path.EndsWith(".Angle"))
        {
            // Track the sun through the day
            var hour = now.Hour + now.Minute / 60.0;
            var angle = (hour - 12) * 7.5; // +/- 90 degrees over 12 hours
            UpdateNodeValue(node, angle + _random.NextDouble() * 0.5, now);
        }
    }

    private void UpdateBessSimulation(DateTime now)
    {
        double totalPower = 0;
        double totalSoc = 0;
        int bankCount = 0;

        foreach (var node in _dynamicNodes)
        {
            var path = node.BrowseName.Name;
            
            if (path.Contains("TESLA-MP-"))
            {
                UpdatePcsValue(node, path, now, ref totalPower);
            }
            else if (path.Contains("BYD-BANK-"))
            {
                UpdateBankValue(node, path, now, ref totalSoc, ref bankCount);
            }
            else if (path.Contains("SUB-BESS"))
            {
                if (path.EndsWith(".TotalPower"))
                    UpdateNodeValue(node, totalPower / 1000.0, now);
                else if (path.EndsWith(".TotalSOC"))
                    UpdateNodeValue(node, bankCount > 0 ? totalSoc / bankCount : 50, now);
            }
        }
    }

    private void UpdatePcsValue(BaseDataVariableState node, string path, DateTime now, ref double totalPower)
    {
        // Simulate charge/discharge cycling
        var cyclePhase = (now.Minute % 30) / 30.0 * 2 * Math.PI;
        var powerFactor = Math.Sin(cyclePhase);
        
        if (path.EndsWith(".ActivePower"))
        {
            var power = 25000 * powerFactor * (0.8 + _random.NextDouble() * 0.2);
            UpdateNodeValue(node, power, now);
            totalPower += power;
        }
        else if (path.EndsWith(".OperatingMode"))
        {
            UpdateNodeValue(node, powerFactor > 0.1 ? 2 : (powerFactor < -0.1 ? 1 : 0), now);
        }
    }

    private void UpdateBankValue(BaseDataVariableState node, string path, DateTime now, ref double totalSoc, ref int bankCount)
    {
        if (path.EndsWith(".SOC"))
        {
            var soc = 50 + Math.Sin(now.Minute / 30.0 * Math.PI) * 20;
            UpdateNodeValue(node, soc, now);
            totalSoc += soc;
            bankCount++;
        }
        else if (path.EndsWith(".AvgCellTemp"))
        {
            UpdateNodeValue(node, 25 + _random.NextDouble() * 5, now);
        }
    }

    private double CalculatePowerCurve(double windSpeed, double ratedPower)
    {
        // Simplified power curve
        if (windSpeed < 3) return 0; // Cut-in
        if (windSpeed > 25) return 0; // Cut-out
        if (windSpeed >= 12) return 1.0; // Rated
        return Math.Pow((windSpeed - 3) / 9, 3); // Cubic power curve
    }

    private void UpdateNodeValue(BaseDataVariableState node, object value, DateTime timestamp)
    {
        node.Value = value;
        node.Timestamp = timestamp;
        node.StatusCode = StatusCodes.Good;
        node.ClearChangeMasks(SystemContext, false);
    }

    public new void Dispose()
    {
        _simulationTimer?.Dispose();
        _simulationTimer = null;
        base.Dispose();
    }

    #endregion
}

/// <summary>
/// Equipment state for simulation tracking.
/// </summary>
public class EquipmentState
{
    public string Id { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public double RatedPower { get; set; }
    public bool IsOnline { get; set; } = true;
}
