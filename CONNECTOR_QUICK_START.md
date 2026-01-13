# Quick Start: Weather & EIA Grid Connectors

## ✅ Implementation Complete

Two new API-based data connectors have been successfully added to NAIA:

### 🌤️ Weather API Connector
- **Provider**: Open-Meteo (free, unlimited, no API key)
- **Status**: ✅ Compiled & Ready
- **Points**: 9 variables per location (temp, humidity, wind, etc.)
- **Update**: Every 5 minutes (configurable)

### ⚡ EIA Grid Data Connector
- **Provider**: US Energy Information Administration
- **Status**: ✅ Compiled & Ready
- **Points**: 5 major US grid operators (demand data)
- **Update**: Every 15 minutes (configurable)
- **Requires**: Free API key from https://www.eia.gov/opendata/

---

## 🚀 Enable Weather Connector (3 Steps)

### 1. Edit Configuration
**File**: `src/Naia.Ingestion/appsettings.json`

```json
"WeatherApi": {
  "Enabled": true,  // Change from false to true
  "Locations": ["52.5,-0.5"]  // Kelmarsh, UK (default)
}
```

### 2. Start Ingestion Service
```powershell
cd c:\naia3\src\Naia.Ingestion
dotnet run
```

### 3. Verify in Logs
Look for:
```
[INFO] Weather API connector initialized: 1 locations, 9 variables
[INFO] Discovered 9 weather points for monitoring
[INFO] Starting weather polling loop (interval: 00:05:00)
[INFO] Published weather batch: 9 points to Kafka
```

---

## ⚡ Enable EIA Grid Connector (4 Steps)

### 1. Get API Key
1. Visit: https://www.eia.gov/opendata/
2. Click "Register" (free, instant)
3. Copy your API key from dashboard

### 2. Edit Configuration
**File**: `src/Naia.Ingestion/appsettings.json`

```json
"EiaGrid": {
  "Enabled": true,  // Change from false to true
  "ApiKey": "YOUR_API_KEY_HERE"  // Paste your key
}
```

### 3. Start Ingestion Service
```powershell
cd c:\naia3\src\Naia.Ingestion
dotnet run
```

### 4. Verify in Logs
Look for:
```
[INFO] EIA Grid API connector initialized: 5 series configured
[INFO] Found 5 configured EIA grid series
[INFO] Starting EIA grid polling loop (interval: 00:15:00)
[INFO] Published EIA grid batch: 5 points to Kafka
```

---

## 📊 View Data in UI

1. Open browser: `http://localhost:5000`
2. Navigate to **Points** page
3. Search for:
   - `WEATHER_*` - Weather data points
   - `GRID_*` - US grid data points
4. Click any point to see live chart

---

## 🎯 What Points Are Created?

### Weather Points (per location)
```
WEATHER_N52.50_W00.50_TEMPERATURE2M     (°C)
WEATHER_N52.50_W00.50_RELATIVEHUMIDITY2M (%)
WEATHER_N52.50_W00.50_DEWPOINT2M        (°C)
WEATHER_N52.50_W00.50_PRESSUREMSL       (hPa)
WEATHER_N52.50_W00.50_WINDSPEED10M      (m/s)
WEATHER_N52.50_W00.50_WINDDIRECTION10M  (°)
WEATHER_N52.50_W00.50_WINDGUSTS10M      (m/s)
WEATHER_N52.50_W00.50_PRECIPITATION     (mm)
WEATHER_N52.50_W00.50_CLOUDCOVER        (%)
```

### EIA Grid Points (default config)
```
GRID_CAISO_DEMAND   (MW) - California
GRID_ERCOT_DEMAND   (MW) - Texas
GRID_MISO_DEMAND    (MW) - Midwest
GRID_NYISO_DEMAND   (MW) - New York
GRID_PJM_DEMAND     (MW) - Mid-Atlantic
```

---

## 🔧 Customize Configuration

### Add Multiple Weather Locations
```json
"Locations": [
  "52.5,-0.5",        // Kelmarsh, UK
  "40.7128,-74.006",  // New York
  "34.0522,-118.2437" // Los Angeles
]
```

### Select Specific Weather Variables
```json
"Variables": [
  "temperature_2m",
  "wind_speed_10m",
  "precipitation"
]
```

### Add More Grid Regions
```json
"Series": [
  {
    "Route": "electricity/rto/region-data",
    "SeriesId": "EBA.CISO-ALL.D.H",
    "FriendlyName": "California Demand"
  },
  {
    "Route": "electricity/rto/region-data",
    "SeriesId": "EBA.CISO-ALL.NG.H",
    "FriendlyName": "California Generation"
  }
]
```

---

## 📁 Files Created

```
src/Naia.Connectors/
├── Weather/
│   ├── WeatherApiOptions.cs           ✅ Created
│   ├── WeatherApiConnector.cs         ✅ Created
│   └── WeatherIngestionWorker.cs      ✅ Created
├── EiaGrid/
│   ├── EiaGridApiOptions.cs           ✅ Created
│   ├── EiaGridApiConnector.cs         ✅ Created
│   └── EiaGridIngestionWorker.cs      ✅ Created
└── ServiceCollectionExtensions.cs     ✅ Updated

src/Naia.Domain/Entities/
└── DataSource.cs                      ✅ Updated (added enum values)

src/Naia.Ingestion/
└── appsettings.json                   ✅ Updated (added config sections)

src/Naia.Api/
└── appsettings.json                   ✅ Updated (added config sections)

docs/
└── NEW_CONNECTORS_WEATHER_EIA.md      ✅ Created (full documentation)
```

---

## ✅ Build Status

```
✅ Naia.Connectors - Built successfully
✅ Naia.Ingestion - Built successfully
✅ Naia.Api - Built successfully
✅ Full solution - Built successfully
```

---

## 🎓 Next Steps

1. **Test Weather Connector**: Enable it and watch data flow
2. **Get EIA API Key**: Takes 30 seconds to register
3. **Correlate Data**: View wind farm + weather together
4. **Explore Patterns**: Let pattern engine find correlations

---

## 📖 Full Documentation

See [`docs/NEW_CONNECTORS_WEATHER_EIA.md`](../docs/NEW_CONNECTORS_WEATHER_EIA.md) for:
- Detailed API documentation
- All available weather variables
- Complete EIA series catalog
- Advanced configuration options
- Troubleshooting guide
- Use case examples

---

## 💡 Tips

- **Weather**: No setup required, just enable and run
- **EIA Grid**: Get API key first (free, instant signup)
- **Both**: Data publishes to same Kafka topic as other connectors
- **Pattern Engine**: Will automatically detect correlations
- **Historical**: Both support backfill via `IHistoricalDataConnector`

---

## 🐛 Troubleshooting

### Weather not working?
- Check internet connection
- Test: `curl https://api.open-meteo.com/v1/forecast?latitude=52.5&longitude=-0.5&current=temperature_2m`

### EIA Grid not working?
- Verify API key is correct
- Check you're not over rate limit (5000/hour)
- Ensure `Enabled: true` in config

### No data in UI?
- Verify Kafka is running: `docker ps | grep kafka`
- Check QuestDB is running: `docker ps | grep questdb`
- Review logs in Naia.Ingestion console

---

**Status**: ✅ Ready to deploy & test!
