# NAIA Data Sources

## Overview

NAIA supports multiple data sources that flow through a unified Kafka pipeline. All data sources publish to the same Kafka topic (`naia.datapoints`) and follow the same message format, allowing the pattern engine to learn from diverse data.

## 📊 Available Data Sources

### 1. Wind Farm Replay (Kelmarsh)
**Status**: ✅ Active  
**Location**: `data/kelmarsh/scada_2019` (and `scada_2020`)  
**Description**: Real historical data from Kelmarsh Wind Farm (UK) - 6x Senvion MM92 turbines

**Data Available**:
- **SCADA 2019**: 862 MB, full year of 10-minute interval data
- **SCADA 2020**: 1,178 MB, full year of 10-minute interval data
- **Combined**: 2,040 MB of turbine operational data
- **Grid Meter**: 24 MB of grid connection data (2016-2021)
- **PMU**: 197 MB of Phasor Measurement Unit data (2016-2021)

**Readings** (18 per turbine = 108 total points):
- Wind Speed (m/s)
- Power Output (kW)
- Wind Direction (°)
- Nacelle Position (°)
- Rotor Speed (RPM)
- Generator Speed (RPM)
- Blade Pitch A, B, C (°)
- Nacelle Temperature (°C)
- Gear Oil Temperature (°C)
- Generator Bearing Temps (Front/Rear) (°C)
- Ambient Temperature (°C)
- Grid Voltage (V)
- Grid Frequency (Hz)
- Reactive Power (kvar)
- Energy Export (kWh)

**Point Naming**: `KSH_{TurbineNum:000}_{ReadingType}`
- Example: `KSH_001_WindSpeed`, `KSH_003_Power`

**Configuration** (`appsettings.json`):
```json
"WindFarmReplay": {
  "Enabled": true,
  "AutoStart": true,
  "DataDirectory": "data/kelmarsh/scada_2019",
  "TurbineCount": 6,
  "SpeedMultiplier": 60.0,
  "SkipNaN": true,
  "DataYears": [2019]
}
```

**Replay Speed**:
- `SpeedMultiplier: 1.0` = Real-time (10-minute intervals)
- `SpeedMultiplier: 60.0` = 60x faster (1 hour in 1 minute)
- `SpeedMultiplier: 600.0` = 600x faster (1 day in ~2.5 minutes)

**Data Quality**:
- NaN values are automatically filtered out (configurable)
- All timestamps are offset to appear as current data
- Loops continuously when data ends

---

### 2. PI Historian (OSIsoft)
**Status**: ⏸️ Disabled (enable when connected to PI)  
**Configuration**: `PIWebApi.Enabled = true`

Real-time data from OSIsoft PI Data Archive via PI Web API:
- Polling-based or event-driven (AF Data Pipe)
- Supports point discovery via wildcards
- Windows Authentication or basic auth
- Configurable polling interval

---

### 3. OPC UA Simulator
**Status**: 🚧 Stub (implementation pending)  
**Configuration**: `OpcSimulator.Enabled = false`

Simulated multi-site renewable energy:
- **Thornton Wind Farm**: 20 turbines, 2 met towers
- **Desert Star Solar**: 8 inverters, 32 trackers
- **Gateway BESS**: 3 Tesla Megapacks, 3 battery banks

Full implementation requires OPC UA client library.

---

## 🗂️ Data Directory Structure

```
c:\naia3\data\kelmarsh\
├── scada_2019\
│   ├── Turbine_Data_Kelmarsh_1_2019-01-01_-_2020-01-01_228.csv (140 MB)
│   ├── Turbine_Data_Kelmarsh_2_2019-01-01_-_2020-01-01_229.csv (143 MB)
│   ├── Turbine_Data_Kelmarsh_3_2019-01-01_-_2020-01-01_230.csv (143 MB)
│   ├── Turbine_Data_Kelmarsh_4_2019-01-01_-_2020-01-01_231.csv (143 MB)
│   ├── Turbine_Data_Kelmarsh_5_2019-01-01_-_2020-01-01_232.csv (143 MB)
│   ├── Turbine_Data_Kelmarsh_6_2019-01-01_-_2020-01-01_233.csv (143 MB)
│   └── Status_Kelmarsh_{1-6}_*.csv
├── scada_2020\
│   └── (similar structure, 2020 data)
├── grid\
│   └── Device_Data_Kelmarsh_Grid_Meter_*.csv (24 MB)
└── pmu\
    └── Device_Data_Kelmarsh_PMU_*.csv (197 MB)
```

---

## 🔄 Data Flow Architecture

```
┌─────────────────┐
│  Data Sources   │
├─────────────────┤
│ • Kelmarsh CSVs │
│ • PI Historian  │──┐
│ • OPC Simulator │  │
└─────────────────┘  │
                     │ Kafka Producer
                     ▼
        ┌────────────────────────┐
        │   Kafka Topic          │
        │  naia.datapoints       │
        └────────────────────────┘
                     │ Kafka Consumer
                     ▼
        ┌────────────────────────┐
        │  Ingestion Pipeline    │
        │  • Deduplication       │
        │  • Validation          │
        │  • Time-series Store   │
        └────────────────────────┘
                     │
          ┌──────────┴───────────┐
          ▼                      ▼
    ┌──────────┐          ┌──────────┐
    │ QuestDB  │          │  Redis   │
    │(History) │          │ (Cache)  │
    └──────────┘          └──────────┘
          │                      │
          └──────────┬───────────┘
                     ▼
        ┌────────────────────────┐
        │   Pattern Engine       │
        │  • Anomaly Detection   │
        │  • Pattern Learning    │
        └────────────────────────┘
```

---

## 📝 Adding New Data

### To add 2020 data:
1. Update `appsettings.json`:
   ```json
   "DataDirectory": "data/kelmarsh/scada_2020",
   "DataYears": [2020]
   ```
2. Or combine both years:
   ```json
   "DataDirectory": "data/kelmarsh",
   "DataYears": [2019, 2020]
   ```

### To add Grid/PMU data:
Requires extending `KelmarshCsvReader` to support device-level data (different CSV structure than turbine data).

---

## ⚙️ NaN Handling

**Problem**: Kelmarsh data contains "NaN" for missing/erroneous measurements

**Solution**: 
- `SkipNaN: true` (default) - Filters out NaN values before publishing to Kafka
- `SkipNaN: false` - Publishes all values including NaN (handled downstream)

**Recommendation**: Keep `SkipNaN: true` for cleaner data and better pattern learning

---

## 🎯 Why This Matters

1. **Continuous Data Flow**: System always has fresh data for learning
2. **Realistic Patterns**: Real operational data with actual anomalies
3. **Scale Testing**: 108+ points at 10-minute intervals = ~15,000 points/hour
4. **Pattern Diversity**: Wind turbines exhibit complex, non-linear behavior
5. **Ground Truth**: Known operational events can validate anomaly detection

---

## 📊 Data Statistics

| Metric | Value |
|--------|-------|
| Total Files | 28 CSV files |
| Total Size | 2,262 MB |
| Turbines | 6 |
| Measurements/Turbine | 18 |
| Total Points | 108 |
| Interval | 10 minutes |
| Time Span | 2+ years (2019-2021) |
| Records | ~1M per turbine/year |

---

## 🚀 Quick Start

1. **Build the solution**:
   ```powershell
   dotnet build Naia.sln
   ```

2. **Ensure Kafka is running**:
   ```powershell
   docker-compose up -d kafka
   ```

3. **Start the ingestion worker**:
   ```powershell
   cd src\Naia.Ingestion
   dotnet run
   ```

4. **Watch the data flow**:
   - Kafka messages: `naia.datapoints` topic
   - QuestDB: `http://localhost:9000` (query `point_data` table)
   - Redis: Current values cached by point name

---

## 🔧 Troubleshooting

### No data files found
- Check `DataDirectory` path in appsettings.json
- Ensure CSV files are extracted from ZIP archives
- Verify file naming matches pattern: `Turbine_Data_Kelmarsh_{num}_*.csv`

### Too many NaN values
- Enable `SkipNaN: true` to filter them out
- Check data quality for specific turbines/periods
- Consider using 2020 data (may have better coverage)

### Replay too slow/fast
- Adjust `SpeedMultiplier`:
  - Demo/testing: 60-600x
  - Realistic simulation: 1x
  - Load testing: 1000x+

---

**Data Source**: [Kelmarsh Wind Farm SCADA Data](https://zenodo.org/record/5841834) (Publicly available dataset)
