# PI AF SDK - Quick Test Guide

## API is Running ✅
- URL: http://localhost:5052
- Swagger UI: http://localhost:5052/swagger

## Quick Tests

### 1. Test Connection (Do This First!)
```powershell
Invoke-RestMethod -Uri "http://localhost:5052/api/pi/initialize" -Method Post
```
**Expected**: `{"status":"connected","connectorType":"AF SDK","dataArchive":"sdhqpisrvr01"}`

### 2. Discover PI Points
```powershell
# Find temperature points
Invoke-RestMethod -Uri "http://localhost:5052/api/pi/points?filter=*TEMP*"

# Find SINUSOID test points
Invoke-RestMethod -Uri "http://localhost:5052/api/pi/points?filter=SINUSOID*"

# Get all points (WARNING: May return 1M+ points!)
Invoke-RestMethod -Uri "http://localhost:5052/api/pi/points?filter=*"
```

### 3. Read Current Values
```powershell
# Single point
Invoke-RestMethod -Uri "http://localhost:5052/api/pi/points/SINUSOID/current"

# Batch read
$tags = @("SINUSOID", "SINUSOIDU", "CDT158") | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5052/api/pi/points/current" `
    -Method Post -Body $tags -ContentType "application/json"
```

### 4. Read Historical Data
```powershell
$start = (Get-Date).AddHours(-1).ToString("o")
$end = (Get-Date).ToString("o")
Invoke-RestMethod -Uri "http://localhost:5052/api/pi/points/SINUSOID/history?startTime=$start&endTime=$end"
```

### 5. Get Point Metadata
```powershell
Invoke-RestMethod -Uri "http://localhost:5052/api/pi/points/SINUSOID/metadata"
```

## Troubleshooting

### Connection Fails
- Verify PI AF SDK is installed: `C:\Program Files (x86)\PIPC\AF\PublicAssemblies\4.0\OSIsoft.AFSDK.dll`
- Check server name in appsettings.json: `"DataArchive": "sdhqpisrvr01"`
- Ensure Windows auth is enabled: `"UseWindowsAuth": true`
- Check network connectivity to PI server

### Point Not Found
- Verify point exists in PI: Open PI System Explorer
- Check tag name spelling (case-sensitive)
- Try wildcard search first: `*PARTIAL_NAME*`

### Slow Discovery
- Normal for 1M+ points (30-60 seconds)
- Use specific filters: `SITE1.*` instead of `*`
- Limit initial discovery in worker code

## Using Swagger UI (Easier!)

1. Navigate to: http://localhost:5052/swagger
2. Expand each endpoint
3. Click "Try it out"
4. Fill in parameters
5. Click "Execute"
6. See response below

## Enable Live Streaming (After Testing)

Edit `appsettings.json`:
```json
{
  "PIWebApi": {
    "Enabled": true  // Change from false to true
  }
}
```

Restart API:
```powershell
dotnet run
```

Worker will auto-discover and stream to Kafka!

## Health Check
```powershell
Invoke-RestMethod -Uri "http://localhost:5052/health"
```

Shows status of:
- PostgreSQL ✅
- QuestDB ✅
- Redis ✅
- PI Connection ✅

---
**Next Step**: Test the `/api/pi/initialize` endpoint in Swagger UI!
