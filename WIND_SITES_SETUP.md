# Wind Sites & Master Access Setup Complete

## 1. MASTER AUTHENTICATION OVERRIDE ✅

Added system-wide master access token for critical operations.

### Setup
Set this environment variable on the server:
```bash
export NAIA_MASTER_TOKEN="your-secret-master-key"
```

Change `your-secret-master-key` to something secure and random.

### Usage
Send requests with master token in header:
```bash
# Option 1: X-Master-Token header
curl -H "X-Master-Token: your-secret-master-key" http://localhost:5000/api/points

# Option 2: Bearer token
curl -H "Authorization: Bearer your-secret-master-key" http://localhost:5000/api/points
```

### Code Changes
- **File**: `src/Naia.Api/Middleware/MasterAccessMiddleware.cs`
- **Added to Program.cs**: Master middleware early in pipeline
- **No database dependency**: Environment variable only
- **Logs**: Warns when master access is used (IP address logged)

---

## 2. FILE PERMISSION FIX ✅

Fixed UnauthorizedAccessException when deleting CSV files.

### Changes
```bash
# Command run on server:
chown -R naia:naia /opt/naia/data/solar/
chmod -R u+w /opt/naia/data/solar/
```

**Result**: API can now delete CSV files from solar data directory without permission errors.

---

## 3. TWO NEW WIND SITES PROCESSED ✅

### Data Extracted
- **ELT1** (El Toro Wind): EX5R → ELT1 prefix
  - 1,566 CSV files processed
  - Location: `data/wind_processed/elt1/`
  
- **BLX1** (Blixton Wind): CHFL → BLX1 prefix
  - 9,728 CSV files processed  
  - Location: `data/wind_processed/blx1/`

### Total: 11,294 CSV files ready for upload

### File Anonymization
All filenames automatically converted:
- `EX5R-ZEMA-RT LMP.csv` → `ELT1-ZEMA-RT LMP.csv`
- `CHFLGhost_Tag.csv` → `BLX1Ghost_Tag.csv`

---

## 4. NEXT STEPS - UPLOAD TO SERVER

Run the upload script from naia3 root directory:
```bash
bash upload_wind_data.sh
```

This will:
1. Create `/opt/naia/data/wind/{elt1,blx1}` directories on server
2. Upload all 11,294 CSV files
3. Set proper permissions (naia user ownership)
4. Verify file counts

---

## 5. CLEANUP (After Verification)

Run ONLY after confirming files are on server:
```bash
python cleanup_wind_files.py
```

This removes from local machine:
- `data/wind_processed/` (11,294 files)
- `data/extracted/ELT1/` (2,182 files)
- `data/extracted/BLX1/` (9,728 files)

Total cleanup: ~12.5 GB freed locally

---

## API Changes Deployed

- Master authentication middleware ✅
- File permission fix ✅  
- Live search on points page ✅
- Copy-to-clipboard for error messages ✅
- Connection pool exhaustion fix ✅

All changes built and deployed to server. API restarted successfully.

---

## Quick Reference

**Master Token Header**:
```
X-Master-Token: your-secret-master-key
```

**Wind Site Data Location (Server)**:
```
/opt/naia/data/wind/elt1/    (1,566 files)
/opt/naia/data/wind/blx1/    (9,728 files)
```

**Next Configuration Steps**:
1. Register ELT1 and BLX1 data sources in database
2. Configure GenericCsvReplayWorker for wind sites
3. Set time-of-day matching for replay
4. Monitor ingestion to QuestDB
