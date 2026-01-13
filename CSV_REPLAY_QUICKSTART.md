# CSV Replay - Quick Start (Data Arrival Checklist)

## When Data Arrives Tomorrow (5-6 Sites)

### ✅ Step 1: Organize Data
```
C:\data\
  site1\
    *.csv
  site2\
    *.csv
  site3\
    *.csv
  ...
```

### ✅ Step 2: Get Critical Info for EACH Site

**For EACH site, you need:**
1. 📍 **Timezone** - REQUIRED for UTC conversion
   - Example: `America/Chicago`, `America/New_York`, `Europe/London`
   - [Find timezone](https://en.wikipedia.org/wiki/List_of_tz_database_time_zones)

2. 🏷️ **Tag Prefix to Strip** - Privacy setting
   - Look at one CSV filename
   - Count how many characters to remove
   - Example: `MSR1SINV11A01.csv` → Remove first 11 chars → `01`

3. 🆔 **Site ID** - Short identifier
   - Example: `SITE1`, `SOLAR_ALPHA`, `WIND_BETA`

4. 📝 **Site Name** - Human-readable
   - Example: `"Solar Alpha BESS"`, `"Wind Farm Beta"`

### ✅ Step 3: Run Preprocessing (FOR EACH SITE)

```powershell
# Example for Site 1
.\preprocess-site-data.ps1 `
  -SiteDir "C:\data\site1" `
  -SiteId "SITE1" `
  -SiteName "Solar Alpha BESS" `
  -Timezone "America/Chicago" `
  -StripPrefix 11

# Example for Site 2
.\preprocess-site-data.ps1 `
  -SiteDir "C:\data\site2" `
  -SiteId "SITE2" `
  -SiteName "Wind Farm Beta" `
  -Timezone "America/New_York" `
  -StripPrefix 8
```

**Output:** JSON configuration for appsettings

### ✅ Step 4: Configure Appsettings

Copy ALL generated JSON snippets into `appsettings.json`:

```json
{
  "GenericCsvReplay": {
    "Enabled": true,
    "KafkaTopic": "naia.datapoints",
    "SpeedMultiplier": 10.0,
    "LoopReplay": false,
    "Sites": [
      // PASTE SITE 1 CONFIG HERE
      // PASTE SITE 2 CONFIG HERE
      // PASTE SITE 3 CONFIG HERE
      // ...
    ]
  }
}
```

### ✅ Step 5: Deploy to Pi

```powershell
# Build
dotnet publish src/Naia.Ingestion -c Release -o publish-ingestion

# Deploy files
scp -r publish-ingestion/* pi@naia-pi:/opt/naia/ingestion/

# Deploy config
scp appsettings.json pi@naia-pi:/opt/naia/ingestion/

# Also copy CSV data to Pi
scp -r C:\data\* pi@naia-pi:/opt/naia/data/

# Restart worker
ssh pi@naia-pi "sudo systemctl restart naia-ingestion"
```

### ✅ Step 6: Monitor

```bash
# Watch auto-registration
ssh pi@naia-pi "journalctl -u naia-ingestion -f | grep 'Auto-registered'"

# Watch progress
ssh pi@naia-pi "journalctl -u naia-ingestion -f | grep 'Published'"

# Check QuestDB
ssh pi@naia-pi
curl -G "http://localhost:9000/exec" --data-urlencode "query=SELECT COUNT(*) FROM timeseries"
```

### ✅ Step 7: Verify in UI

1. Open: `http://naia-pi:5050/points`
2. Look for auto-registered tags:
   - `SITE1_01`, `SITE1_02`, ...
   - `SITE2_TOWER01`, `SITE2_TOWER02`, ...
3. Check Equipment Type = "Unknown" (expected)
4. Verify timestamps look correct (UTC)

---

## Quick Troubleshooting

### No points showing up?
- Check: `sudo systemctl status naia-ingestion`
- Check logs: `journalctl -u naia-ingestion -n 100`
- Verify `Enabled: true` in config

### Wrong timestamps?
- Double-check timezone setting
- Look at one CSV file - is it local time or UTC?
- Verify timezone matches site location

### Too slow?
- Increase `SpeedMultiplier` to 50.0 or 100.0
- Monitor Pi CPU: `ssh pi@naia-pi "top"`

### CSV format issues?
- Check column names match config
- Verify delimiter (comma vs semicolon)
- Look for header row in CSV

---

## Expected Timeline

| Speed | Time to Load 1 Month |
|-------|---------------------|
| 10x | ~3 hours |
| 50x | ~36 minutes |
| 100x | ~18 minutes |

**Recommendation:** Start with `SpeedMultiplier: 50.0` for initial testing.

---

## After Data Loads

1. ✅ Review `/points` page - all tags registered?
2. ✅ Manually tag equipment types on SITE1 (first site)
3. ✅ Let pattern engine analyze
4. ✅ Approve suggestions for other sites
5. ✅ Test natural language queries

---

## Files Created

- ✅ `GenericCsvReplayOptions.cs` - Configuration classes
- ✅ `GenericCsvReader.cs` - CSV reader with timezone conversion
- ✅ `GenericCsvReplayWorker.cs` - Background worker
- ✅ `ServiceCollectionExtensions.cs` - DI registration (UPDATED)
- ✅ `preprocess-site-data.ps1` - Preprocessing script
- ✅ `appsettings.GenericCsvReplay.json` - Example config
- ✅ `CSV_REPLAY_GUIDE.md` - Full documentation

---

## Need Help?

See [CSV_REPLAY_GUIDE.md](CSV_REPLAY_GUIDE.md) for complete documentation.
