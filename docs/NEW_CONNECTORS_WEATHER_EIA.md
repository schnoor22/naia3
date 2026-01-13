# New Data Source Connectors: Weather & EIA Grid

## Overview

Two new API-based connectors have been added to NAIA to provide additional time-series data sources for demonstration and industrial use cases:

1. **Weather API Connector** - Real-time weather observations using Open-Meteo (free, no API key)
2. **EIA Grid Data Connector** - US electricity grid data from Energy Information Administration (free API key required)

Both connectors follow the established NAIA connector pattern and integrate seamlessly with the existing data pipeline.

---

## 🌤️ Weather API Connector

### Features
- **Provider**: Open-Meteo (https://open-meteo.com)
- **Cost**: 100% FREE, no API key required, unlimited requests
- **Data**: Real-time weather observations and historical data back to 1940
- **Update Frequency**: Every 15 minutes (configurable polling)
- **Interface**: ICurrentValueConnector, IHistoricalDataConnector, IDiscoverableConnector

### Configuration

#### appsettings.json
```json
{
  "WeatherApi": {
    "Enabled": true,
    "EnableAutoDiscovery": true,
    "BaseUrl": "https://api.open-meteo.com/v1",
    "PollingIntervalMs": 300000,
    "TimeoutSeconds": 30,
    "Locations": [
      "52.5,-0.5",
      "40.0,-105.0",
      "34.05,-118.25"
    ],
    "MaxDiscoveredPoints": 500,
    "Variables": [
      "temperature_2m",
      "relative_humidity_2m",
      "dew_point_2m",
      "pressure_msl",
      "wind_speed_10m",
      "wind_direction_10m",
      "wind_gusts_10m",
      "precipitation",
      "cloud_cover"
    ]
  }
}
```

### Available Weather Variables

| Variable | Description | Units |
|----------|-------------|-------|
| `temperature_2m` | Air temperature at 2m height | °C |
| `relative_humidity_2m` | Relative humidity at 2m | % |
| `dew_point_2m` | Dew point temperature | °C |
| `apparent_temperature` | Feels-like temperature | °C |
| `pressure_msl` | Mean sea level pressure | hPa |
| `surface_pressure` | Surface pressure | hPa |
| `precipitation` | Total precipitation | mm |
| `rain` | Rain only | mm |
| `snowfall` | Snowfall | cm |
| `cloud_cover` | Cloud coverage | % |
| `wind_speed_10m` | Wind speed at 10m | m/s |
| `wind_direction_10m` | Wind direction | ° |
| `wind_gusts_10m` | Wind gusts | m/s |

### Point Naming Convention

Points are auto-discovered and named as:
```
WEATHER_{LAT}_{LON}_{VARIABLE}
```

Examples:
- `WEATHER_N52.50_W00.50_TEMPERATURE2M` (Kelmarsh, UK)
- `WEATHER_N40.00_W105.00_WINDSPEED10M` (Boulder, CO)
- `WEATHER_N34.05_W118.25_HUMIDITY` (Los Angeles, CA)

### Source Address Format
```
weather/{latitude},{longitude}/{variable}
```

Example: `weather/52.5,-0.5/temperature_2m`

### Usage Examples

#### Enable for Kelmarsh Wind Farm
To add weather data for your existing Kelmarsh location:
```json
"Locations": ["52.5,-0.5"]
```

#### Add Multiple Sites
```json
"Locations": [
  "52.5,-0.5",      // Kelmarsh, UK
  "40.7128,-74.006", // New York City
  "51.5074,-0.1278"  // London
]
```

#### Correlate with Wind Farm Data
Weather data will automatically:
- Register as points in PostgreSQL
- Publish to Kafka topic `naia.datapoints`
- Store in QuestDB alongside turbine data
- Appear in UI for charting and analysis
- Feed into pattern engine for correlation detection

### Use Cases
1. **Wind Farm Correlation**: Analyze wind speed vs turbine power output
2. **Temperature Impact**: Study ambient temp effects on generator performance
3. **Maintenance Planning**: Correlate weather events with equipment failures
4. **Forecasting**: Use weather patterns for predictive maintenance

---

## ⚡ EIA Grid Data Connector

### Features
- **Provider**: Energy Information Administration (https://www.eia.gov)
- **Cost**: FREE API key (5,000 requests/hour limit)
- **Data**: Real-time US electricity grid demand, generation, and interchange
- **Update Frequency**: Hourly updates (15-minute polling recommended)
- **Coverage**: All major US regional transmission organizations (RTOs)
- **Interface**: ICurrentValueConnector, IHistoricalDataConnector

### Get API Key
1. Visit: https://www.eia.gov/opendata/
2. Register for free account
3. Get API key from dashboard
4. Add to `appsettings.json`

### Configuration

#### appsettings.json
```json
{
  "EiaGrid": {
    "Enabled": true,
    "ApiKey": "YOUR_API_KEY_HERE",
    "BaseUrl": "https://api.eia.gov/v2",
    "PollingIntervalMs": 900000,
    "TimeoutSeconds": 30,
    "MaxPointsPerRequest": 5000,
    "Series": [
      {
        "Route": "electricity/rto/region-data",
        "SeriesId": "EBA.CISO-ALL.D.H",
        "FriendlyName": "CAISO Demand"
      },
      {
        "Route": "electricity/rto/region-data",
        "SeriesId": "EBA.ERCO-ALL.D.H",
        "FriendlyName": "ERCOT Demand"
      }
    ]
  }
}
```

### Available Series & Regions

#### Major US Grid Operators

| Region Code | Name | Description |
|-------------|------|-------------|
| `CISO` | California ISO | California grid |
| `ERCO` | ERCOT | Texas grid (isolated) |
| `MISO` | Midcontinent ISO | Midwest/Central |
| `NYIS` | NYISO | New York |
| `PJM` | PJM Interconnection | Mid-Atlantic/Great Lakes |
| `SWPP` | Southwest Power Pool | Plains states |
| `ISNE` | ISO New England | New England |
| `SOCO` | Southern Company | Southeast |

#### Data Types (Suffix)

| Suffix | Description | Units |
|--------|-------------|-------|
| `.D.H` | Demand (load) | MW |
| `.NG.H` | Net Generation | MW |
| `.TI.H` | Total Interchange | MW |
| `.DF.H` | Demand Forecast | MW |
| `.ID.H` | Interchange Deliveries | MW |
| `.IR.H` | Interchange Receipts | MW |

### Series Format
```
EBA.{REGION}-ALL.{TYPE}.H
```

Examples:
- `EBA.CISO-ALL.D.H` - California demand
- `EBA.ERCO-ALL.NG.H` - Texas generation
- `EBA.MISO-ALL.TI.H` - MISO interchange

### Point Naming Convention

Points are configured and named as:
```
GRID_{FRIENDLY_NAME}
```

Examples:
- `GRID_CAISO_DEMAND` (California demand in MW)
- `GRID_ERCOT_GENERATION` (Texas generation in MW)
- `GRID_MISO_DEMAND` (Midwest demand in MW)

### Source Address Format
```
eia/{route}/{seriesId}
```

Example: `eia/electricity/rto/region-data/EBA.CISO-ALL.D.H`

### Usage Examples

#### Monitor Major Grids
```json
"Series": [
  {
    "Route": "electricity/rto/region-data",
    "SeriesId": "EBA.CISO-ALL.D.H",
    "FriendlyName": "California Demand"
  },
  {
    "Route": "electricity/rto/region-data",
    "SeriesId": "EBA.ERCO-ALL.D.H",
    "FriendlyName": "Texas Demand"
  },
  {
    "Route": "electricity/rto/region-data",
    "SeriesId": "EBA.NYIS-ALL.D.H",
    "FriendlyName": "New York Demand"
  }
]
```

#### Compare Demand vs Generation
```json
"Series": [
  {
    "Route": "electricity/rto/region-data",
    "SeriesId": "EBA.CISO-ALL.D.H",
    "FriendlyName": "CAISO Demand"
  },
  {
    "Route": "electricity/rto/region-data",
    "SeriesId": "EBA.CISO-ALL.NG.H",
    "FriendlyName": "CAISO Generation"
  }
]
```

### Use Cases
1. **Grid Stability Analysis**: Monitor demand vs generation balance
2. **Renewable Integration**: Study grid behavior during high renewable periods
3. **Demo Data**: Show real utility-scale grid operations
4. **Price Correlation**: Compare grid load with energy prices
5. **Demand Forecasting**: Use historical patterns for prediction

---

## 📊 Data Flow Architecture

Both connectors follow the standard NAIA data pipeline:

```
┌─────────────────────┐
│  Weather API        │
│  Open-Meteo         │
└──────┬──────────────┘
       │ HTTP GET (5 min)
       ↓
┌─────────────────────┐
│ WeatherIngestion    │
│ Worker              │
└──────┬──────────────┘
       │
       ↓
┌─────────────────────┐
│  EIA Grid API       │
│  eia.gov            │
└──────┬──────────────┘
       │ HTTP GET (15 min)
       ↓
┌─────────────────────┐
│ EiaGridIngestion    │
│ Worker              │
└──────┬──────────────┘
       │
       ↓ DataPointBatch
┌─────────────────────┐
│ Kafka Topic         │
│ naia.datapoints     │
└──────┬──────────────┘
       │
       ↓
┌─────────────────────┐
│ Historian Workers   │
│ (QuestDB, Redis)    │
└──────┬──────────────┘
       │
       ↓
┌─────────────────────┐
│ Pattern Engine      │
│ (Correlations)      │
└──────┬──────────────┘
       │
       ↓
┌─────────────────────┐
│ NAIA Web UI         │
│ (Charts, Analysis)  │
└─────────────────────┘
```

---

## 🚀 Quick Start Guide

### 1. Enable Weather Connector (No API Key Needed!)

**In `src/Naia.Ingestion/appsettings.json`:**
```json
"WeatherApi": {
  "Enabled": true,
  "Locations": ["52.5,-0.5"]
}
```

**Restart Naia.Ingestion service:**
```powershell
# Stop if running
Stop-Process -Name "Naia.Ingestion" -ErrorAction SilentlyContinue

# Start ingestion service
cd c:\naia3\src\Naia.Ingestion
dotnet run
```

**Verify in logs:**
```
[INFO] Weather API connector initialized: 1 locations, 9 variables
[INFO] Discovered 9 weather points
[INFO] Starting weather polling loop (interval: 00:05:00)
[INFO] Published weather batch: 9 points to Kafka
```

### 2. Enable EIA Grid Connector (API Key Required)

**Get API Key:**
1. Go to https://www.eia.gov/opendata/
2. Register free account
3. Copy API key

**In `src/Naia.Ingestion/appsettings.json`:**
```json
"EiaGrid": {
  "Enabled": true,
  "ApiKey": "YOUR_API_KEY_HERE"
}
```

**Restart and verify:**
```
[INFO] EIA Grid API connector initialized: 5 series configured
[INFO] Found 5 configured EIA grid series
[INFO] Starting EIA grid polling loop (interval: 00:15:00)
[INFO] Published EIA grid batch: 5 points to Kafka
```

### 3. View Data in UI

1. Navigate to `http://localhost:5000`
2. Go to **Data Sources** page
3. You should see:
   - `Weather API (Open-Meteo)` - Connected
   - `EIA Grid Data API` - Connected
4. Go to **Points** page
5. Search for:
   - `WEATHER_*` - Weather points
   - `GRID_*` - Grid points
6. Click point to view live chart

---

## 🔧 Advanced Configuration

### Custom Weather Locations

Add multiple locations for different sites:
```json
"Locations": [
  "52.5,-0.5",        // Kelmarsh Wind Farm, UK
  "55.9533,-3.1883",  // Edinburgh, UK
  "40.7128,-74.006",  // New York City, USA
  "34.0522,-118.2437" // Los Angeles, USA
]
```

### Adjust Polling Frequency

**Weather (faster updates):**
```json
"PollingIntervalMs": 180000  // 3 minutes
```

**EIA Grid (slower to respect rate limits):**
```json
"PollingIntervalMs": 1800000  // 30 minutes
```

### Select Specific Weather Variables

Only collect what you need:
```json
"Variables": [
  "temperature_2m",
  "wind_speed_10m",
  "wind_direction_10m"
]
```

### Historical Backfill

Both connectors support `IHistoricalDataConnector`:

```csharp
var connector = serviceProvider.GetService<WeatherApiConnector>();
var historicalData = await connector.ReadHistoricalDataAsync(
    "weather/52.5,-0.5/temperature_2m",
    DateTime.UtcNow.AddDays(-30),
    DateTime.UtcNow
);
// Returns 30 days of hourly temperature data
```

---

## 🎯 Demo Scenarios

### Scenario 1: Weather Impact on Wind Farm
1. Enable Weather connector for Kelmarsh location
2. Enable WindFarmReplay connector
3. Chart `KSH_001_Power` vs `WEATHER_N52.50_W00.50_WINDSPEED10M`
4. Pattern engine will detect correlation

### Scenario 2: Multi-Region Grid Analysis
1. Enable EIA connector with CISO, ERCO, MISO
2. Compare demand patterns across regions
3. Identify peak demand times
4. Correlate with time-of-day patterns

### Scenario 3: Complete Energy System View
1. Enable all connectors:
   - Wind Farm Replay (generation)
   - Weather (conditions)
   - EIA Grid (grid demand)
2. Show how renewable generation correlates with weather
3. Compare local generation to grid-scale demand

---

## 📁 File Structure

```
src/Naia.Connectors/
├── Weather/
│   ├── WeatherApiOptions.cs          // Configuration
│   ├── WeatherApiConnector.cs        // Connector implementation
│   └── WeatherIngestionWorker.cs     // Background worker
├── EiaGrid/
│   ├── EiaGridApiOptions.cs          // Configuration
│   ├── EiaGridApiConnector.cs        // Connector implementation
│   └── EiaGridIngestionWorker.cs     // Background worker
└── ServiceCollectionExtensions.cs    // DI registration (updated)

src/Naia.Domain/Entities/
└── DataSource.cs                     // Added WeatherApi=11, EiaGrid=12

src/Naia.Ingestion/
└── appsettings.json                  // Added config sections

src/Naia.Api/
└── appsettings.json                  // Added config sections
```

---

## 🐛 Troubleshooting

### Weather Connector Issues

**Problem**: "Weather API connector not available"
- **Check**: Internet connectivity to `api.open-meteo.com`
- **Solution**: Test with: `curl https://api.open-meteo.com/v1/forecast?latitude=52.5&longitude=-0.5&current=temperature_2m`

**Problem**: No weather points discovered
- **Check**: Locations format is correct: `"latitude,longitude"`
- **Check**: EnableAutoDiscovery is `true`

### EIA Grid Connector Issues

**Problem**: "EIA API key not configured"
- **Check**: ApiKey is set in appsettings.json
- **Solution**: Get key from https://www.eia.gov/opendata/

**Problem**: "EIA Grid API returned 403"
- **Check**: API key is valid
- **Check**: Not exceeding 5,000 requests/hour

**Problem**: "EIA Grid API returned 404"
- **Check**: Series IDs are correct (format: `EBA.REGION-ALL.TYPE.H`)
- **Check**: Route matches series type

### General Issues

**Problem**: No data flowing to UI
1. Check Kafka is running: `docker ps | grep kafka`
2. Check QuestDB is running: `docker ps | grep questdb`
3. Check historian workers in Naia.Api logs
4. Verify points are registered: Query PostgreSQL `point` table

---

## 📊 Performance Considerations

### Weather API
- **No rate limits** - Open-Meteo is unlimited
- **~100ms response time** per location
- **9 variables × N locations** points created
- **Recommended**: 5-10 locations max for typical use

### EIA Grid API
- **Rate limit**: 5,000 requests/hour (~1.4 req/sec)
- **~500ms response time** per series
- **Hourly data** - no need for frequent polling
- **Recommended**: 15-30 minute polling interval

### Database Impact
- Each connector adds 5-50 new points (typical)
- Weather: ~9 points per location
- EIA Grid: 1 point per series
- Minimal QuestDB impact (time-series optimized)
- PostgreSQL: One-time point registration

---

## 🎓 Learning Resources

### Weather Data
- Open-Meteo API Docs: https://open-meteo.com/en/docs
- Historical Weather: https://open-meteo.com/en/docs/historical-weather-api

### EIA Grid Data
- EIA API Portal: https://www.eia.gov/opendata/
- API Documentation: https://www.eia.gov/opendata/documentation.php
- Electricity Data Browser: https://www.eia.gov/electricity/data/browser/

---

## ✅ Next Steps

1. **Enable Weather Connector** - Start collecting weather data for Kelmarsh
2. **Get EIA API Key** - Register and configure grid data
3. **Test Data Flow** - Verify points appear in UI
4. **Explore Correlations** - Let pattern engine discover relationships
5. **Add More Locations** - Expand to multiple weather sites
6. **Customize Series** - Select specific grid regions of interest

---

## 💡 Future Enhancements

Potential additions (not yet implemented):
- **Finnhub Connector** - Real-time financial market data
- **NOAA Tides** - Water level and current data
- **Public Transit APIs** - Real-time vehicle tracking
- **Solar Radiation** - PV system performance data
- **Commodity Prices** - Energy market prices

These would follow the same connector pattern and integrate seamlessly with the existing architecture.
