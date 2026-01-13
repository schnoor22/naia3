# CSV Replay System - Complete Guide

## Overview

The Generic CSV Replay Connector is designed to replay historical industrial data from CSV files into NAIA for training pattern recognition and testing. It supports:

✅ Multiple sites with different timezones  
✅ Privacy via prefix stripping (remove first N chars like "MSR1SINV11A01" → "01")  
✅ Flexible CSV formats (timestamp/value/status)  
✅ Timezone conversion (local → UTC)  
✅ Bad status handling (Skip/Store/ConvertToNull)  
✅ Speed multiplier for fast replay  
✅ Loop mode for continuous testing  

## Architecture

```
CSV Files → GenericCsvReader → GenericCsvReplayWorker → Kafka (naia.datapoints)
                                                           ↓
                                              Naia.Ingestion Worker
                                                           ↓
                                              QuestDB + Redis + Auto-register
```

**Key Points:**
- CSV Replay publishes to Kafka topic `naia.datapoints`
- Naia.Ingestion worker (SEPARATE PROCESS) consumes from Kafka
- Auto-registration happens in IngestionPipeline (creates Points automatically)
- QuestDB stores timeseries data with quality field
- Redis caches current values

## Data Format

### Expected CSV Structure

```csv
Timestamp,Value,Status
2024-01-01 00:00:00,123.45,Good
2024-01-01 00:01:00,123.67,Good
2024-01-01 00:02:00,0.0,Bad
```

**Requirements:**
- One CSV file per tag
- Tag name in filename (e.g., `MSR1SINV11A01.csv`)
- Columns: Timestamp (local time), Value (double), Status (optional)
- Column names configurable in appsettings

### Supported Status Values

- `Good` / `192` = Good data quality
- `Bad` / `0` = Bad data quality
- Any other value = Uncertain

## Step 1: Prepare Data

### Directory Structure

```
C:\data\
  site1-solar\
    MSR1SINV11A01.csv
    MSR1SINV11A02.csv
    MSR1SINV11A03.csv
    ...
  site2-wind\
    WINDTOWER01.csv
    WINDTOWER02.csv
    ...
```

### Run Preprocessing Script

For each site, run:

```powershell
.\preprocess-site-data.ps1 `
  -SiteDir "C:\data\site1-solar" `
  -SiteId "SITE1" `
  -SiteName "Solar Alpha" `
  -Timezone "America/Chicago" `
  -StripPrefix 11
```

**What it does:**
- Scans all CSV files
- Extracts tag names from filenames
- Removes first 11 characters (privacy)
- Adds site prefix "SITE1_"
- Generates appsettings JSON snippet

**Output:**
```json
{
  "SiteId": "SITE1",
  "SiteName": "Solar Alpha",
  "Enabled": true,
  "DataDirectory": "C:\\data\\site1-solar",
  "Timezone": "America/Chicago",
  "StripPrefixLength": 11,
  "TagPrefix": "SITE1_",
  "TagNameSource": "Filename",
  "BadStatusHandling": "Skip",
  "CsvFormat": {
    "Delimiter": ",",
    "TimestampColumn": "Timestamp",
    "ValueColumn": "Value",
    "StatusColumn": "Status",
    "SkipRowsBeforeHeader": 0
  }
}
```

## Step 2: Configure Appsettings

Add to `appsettings.json`:

```json
{
  "GenericCsvReplay": {
    "Enabled": true,
    "KafkaTopic": "naia.datapoints",
    "SpeedMultiplier": 1.0,
    "LoopReplay": false,
    "Sites": [
      {
        "SiteId": "SITE1",
        "SiteName": "Solar Alpha BESS",
        "Enabled": true,
        "DataDirectory": "C:\\data\\site1-solar",
        "Timezone": "America/Chicago",
        "StripPrefixLength": 11,
        "TagPrefix": "SITE1_",
        "TagNameSource": "Filename",
        "BadStatusHandling": "Skip",
        "CsvFormat": {
          "Delimiter": ",",
          "TimestampColumn": "Timestamp",
          "ValueColumn": "Value",
          "StatusColumn": "Status",
          "SkipRowsBeforeHeader": 0
        }
      },
      {
        "SiteId": "SITE2",
        "SiteName": "Wind Farm Beta",
        "Enabled": true,
        "DataDirectory": "C:\\data\\site2-wind",
        "Timezone": "Europe/London",
        "StripPrefixLength": 8,
        "TagPrefix": "SITE2_",
        "TagNameSource": "Filename",
        "BadStatusHandling": "Store",
        "CsvFormat": {
          "Delimiter": ",",
          "TimestampColumn": "Timestamp",
          "ValueColumn": "Value",
          "StatusColumn": "Status",
          "SkipRowsBeforeHeader": 0
        }
      }
    ]
  }
}
```

### Configuration Options

#### Site-Level Options

| Option | Description | Example |
|--------|-------------|---------|
| `SiteId` | Unique site identifier | `"SITE1"` |
| `SiteName` | Human-readable name | `"Solar Alpha BESS"` |
| `Enabled` | Enable/disable site | `true` |
| `DataDirectory` | Path to CSV files | `"C:\\data\\site1"` |
| `Timezone` | IANA timezone | `"America/Chicago"` |
| `StripPrefixLength` | Remove first N chars | `11` |
| `TagPrefix` | Add prefix to tags | `"SITE1_"` |
| `TagNameSource` | Where to find tag name | `"Filename"` |
| `BadStatusHandling` | How to handle bad data | `"Skip"` / `"Store"` / `"ConvertToNull"` |

#### CSV Format Options

| Option | Description | Example |
|--------|-------------|---------|
| `Delimiter` | Column separator | `","` |
| `TimestampColumn` | Timestamp column name/index | `"Timestamp"` or `"0"` |
| `ValueColumn` | Value column name/index | `"Value"` or `"1"` |
| `StatusColumn` | Status column name/index (optional) | `"Status"` or `"2"` |
| `SkipRowsBeforeHeader` | Rows to skip before header | `0` |
| `TimestampFormat` | Parse format (optional) | `"yyyy-MM-dd HH:mm:ss"` |

#### Global Options

| Option | Description | Default |
|--------|-------------|---------|
| `SpeedMultiplier` | Replay speed (1.0 = real-time, 10.0 = 10x faster) | `1.0` |
| `LoopReplay` | Restart from beginning when done | `false` |
| `KafkaTopic` | Kafka topic for datapoints | `"naia.datapoints"` |

### Bad Status Handling

- **Skip**: Ignore bad data points (don't send to Kafka)
- **Store**: Keep bad data, mark quality=Bad in QuestDB
- **ConvertToNull**: Set value to 0.0, mark quality=Bad

## Step 3: Register Connector in Program.cs

In `src/Naia.Ingestion/Program.cs`, add:

```csharp
// CSV Replay Connector (historical data)
builder.Services.AddGenericCsvReplayConnector(configuration);
```

This is already included if you followed the installation guide.

## Step 4: Deploy and Run

### Deploy Ingestion Worker

```powershell
# Build and publish
dotnet publish src/Naia.Ingestion -c Release -o publish-ingestion

# Copy files to Pi
scp -r publish-ingestion/* pi@naia-pi:/opt/naia/ingestion/

# Copy updated appsettings
scp appsettings.json pi@naia-pi:/opt/naia/ingestion/

# Restart service
ssh pi@naia-pi "sudo systemctl restart naia-ingestion"
```

### Monitor Logs

```bash
# Watch auto-registration
ssh pi@naia-pi "journalctl -u naia-ingestion -f | grep 'Auto-registered'"

# Example output:
# [11:23:45] Auto-registered point: SITE1_01 (id: 1001)
# [11:23:46] Auto-registered point: SITE1_02 (id: 1002)
# [11:23:47] Auto-registered point: SITE1_03 (id: 1003)

# Watch replay progress
ssh pi@naia-pi "journalctl -u naia-ingestion -f | grep 'Published'"

# Example output:
# [11:24:00] Published 10000 points in 5.2s (1923 pts/sec)
# [11:24:05] Published 20000 points in 10.1s (1980 pts/sec)
```

## Step 5: Verify Data

### Check QuestDB

```bash
ssh pi@naia-pi
curl -G "http://localhost:9000/exec" --data-urlencode "query=SELECT COUNT(*) FROM timeseries"

# Should see increasing count as data loads
```

### Check Points Page

1. Open NAIA UI: `http://naia-pi:5050`
2. Navigate to `/points`
3. Verify all tags auto-registered:
   - `SITE1_01`, `SITE1_02`, `SITE1_03`, ...
   - `SITE2_TOWER01`, `SITE2_TOWER02`, ...

### Check Redis Current Values

```bash
ssh pi@naia-pi "redis-cli keys 'point:*:current' | wc -l"

# Should match number of auto-registered points
```

## Pattern Training Workflow

### 1. Let Data Load (1-2 hours)

With `SpeedMultiplier: 10.0`, a month of data takes ~3 hours to load.

### 2. Review Points Page

- All tags should be auto-registered
- Equipment Type = "Unknown" initially
- Check timezone conversion worked (timestamps in UTC)

### 3. Manually Tag First Site

- Find equipment patterns in SITE1 tags
- Assign Equipment Types:
  - `SITE1_01` → "Inverter"
  - `SITE1_02` → "Inverter"
  - `SITE1_METER` → "Meter"
  - etc.

### 4. Pattern Engine Training

- NAIA learns from SITE1 manual tagging
- Analyzes tag naming patterns
- Generates suggestions for SITE2, SITE3, etc.

### 5. Approve Suggestions

- Review high-confidence suggestions
- Approve bulk equipment type assignments
- System propagates patterns across all sites

### 6. Test Queries

With multiple sites loaded:
- "Show total solar production across all sites"
- "Compare inverter efficiency SITE1 vs SITE2"
- "Identify underperforming equipment"

## Timezone Reference

Common timezones for US sites:

| Timezone | Examples |
|----------|----------|
| `America/New_York` | Eastern Time (ET) |
| `America/Chicago` | Central Time (CT) |
| `America/Denver` | Mountain Time (MT) |
| `America/Los_Angeles` | Pacific Time (PT) |
| `America/Phoenix` | Arizona (no DST) |
| `UTC` | Universal Time |
| `Europe/London` | UK |

**CRITICAL:** Always use IANA timezone identifiers, not abbreviations like "CST" or "EST".

Find full list: https://en.wikipedia.org/wiki/List_of_tz_database_time_zones

## Troubleshooting

### No Points Auto-Registered

**Check:**
1. Is `Enabled: true` in GenericCsvReplay config?
2. Are CSV files in correct directory?
3. Is Naia.Ingestion worker running? `sudo systemctl status naia-ingestion`
4. Check logs: `journalctl -u naia-ingestion -f`

### Wrong Timestamps

**Check:**
1. Verify timezone setting matches site location
2. Check if CSV timestamps are local or UTC
3. Look for DST transitions (March/November)

### Data Not Showing in UI

**Check:**
1. QuestDB has data: `SELECT COUNT(*) FROM timeseries`
2. Redis has current values: `redis-cli keys 'point:*:current'`
3. Points are registered: Check `/points` page
4. API is running: `sudo systemctl status naia-api`

### Bad Data Quality

**Check:**
1. Review `BadStatusHandling` setting
2. Check Status column values in CSV
3. Look for "Status=Bad" in logs
4. Query QuestDB: `SELECT quality, COUNT(*) FROM timeseries GROUP BY quality`

### Slow Replay

**Increase `SpeedMultiplier`:**
- 1.0 = real-time (1 hour of data takes 1 hour)
- 10.0 = 10x faster (1 hour of data takes 6 minutes)
- 100.0 = 100x faster (1 month of data takes ~7 hours)

**Warning:** Very high speeds (>100x) may overwhelm Kafka/QuestDB on Pi.

### Memory Issues

**Symptoms:**
- Process crashes
- "Out of memory" errors

**Solutions:**
1. Reduce sites loaded simultaneously (disable some in config)
2. Increase Pi RAM allocation
3. Use smaller CSV files (split by month)

## Performance Tuning

### Recommended Settings

**For Testing (fast):**
```json
{
  "SpeedMultiplier": 100.0,
  "LoopReplay": false
}
```

**For Training (realistic):**
```json
{
  "SpeedMultiplier": 10.0,
  "LoopReplay": false
}
```

**For Stress Testing:**
```json
{
  "SpeedMultiplier": 1000.0,
  "LoopReplay": true
}
```

### Expected Throughput

| Speed | Real-Time Equivalent | Load Time (1 month) |
|-------|---------------------|---------------------|
| 1x | 1 hour = 1 hour | 720 hours (30 days) |
| 10x | 1 hour = 6 min | 72 hours (3 days) |
| 100x | 1 hour = 36 sec | 7.2 hours |
| 1000x | 1 hour = 3.6 sec | 43 minutes |

**Note:** Actual performance depends on:
- Number of points per site
- CSV file size
- Pi hardware specs
- Kafka/QuestDB performance

## Privacy Notes

### Prefix Stripping

Original tag: `MSR1SINV11A01`
After stripping 11 chars: `01`
With site prefix: `SITE1_01`

This removes site-specific identifiers while preserving equipment numbering.

### What Gets Stored

- ✅ Tag names (after prefix stripping and site prefix)
- ✅ Timestamps (converted to UTC)
- ✅ Values (double precision)
- ✅ Quality flags (Good/Bad/Uncertain)
- ❌ Original site identifiers (stripped)
- ❌ Original tag prefixes (removed)

## Next Steps

1. ✅ Build and test CSV replay system
2. ✅ Prepare real data from 5-6 sites
3. 🔄 Run preprocessing script for each site
4. 🔄 Deploy to Pi and verify data loads
5. 🔄 Manually tag first site equipment types
6. 🔄 Train pattern engine on multi-site data
7. 🔄 Test natural language queries across sites

## Related Documentation

- [START_HERE.md](START_HERE.md) - Project overview
- [LIVE_INGESTION_GUIDE.md](LIVE_INGESTION_GUIDE.md) - Live data ingestion
- [CONNECTOR_QUICK_START.md](CONNECTOR_QUICK_START.md) - Building new connectors
- [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md) - Debugging tips
