# 🚀 START HERE - NAIA 24-Hour Test Quick Start

**Status Check:** Everything is already running! ✅

## ✅ Current Infrastructure Status

All Docker containers are UP and HEALTHY:
- ✅ PostgreSQL (port 5432)
- ✅ QuestDB (port 9000, 8812)
- ✅ Redis (port 6379)
- ✅ Kafka (port 9092)
- ✅ Zookeeper (port 2181)
- ✅ Kafka UI (port 8080)

## 🎯 Simple 3-Step Startup

### Step 1: Build the Solution (1 minute)

```powershell
dotnet build Naia.sln --configuration Release
```

### Step 2: Start Naia.Api (Producer + Pattern Engine)

Open **PowerShell Window #1**:
```powershell
cd c:\naia3\src\Naia.Api
dotnet run --configuration Release
```

Wait for: `Now listening on: http://localhost:5052`

Then in **PowerShell Window #2**, start PI ingestion:
```powershell
Invoke-RestMethod -Uri "http://localhost:5052/api/ingestion/start" -Method POST
```

###Step 3: Start Naia.Ingestion (Consumer)

Open **PowerShell Window #3**:
```powershell
cd c:\naia3\src\Naia.Ingestion
dotnet run --configuration Release
```

Wait for: `Pipeline Health: ✓`

---

## 🎉 THAT'S IT! System is Running

### Check Data is Flowing

**QuestDB:** http://localhost:9000
```sql
SELECT COUNT(*) FROM timeseries WHERE point_name LIKE 'MLR1%';
```

**Hangfire Dashboard:** http://localhost:5052/hangfire  
(See all pattern jobs running)

**Kafka UI:** http://localhost:8080  
(See messages flowing)

---

## ⏰ What Happens Next

| Time | Event |
|------|-------|
| **5 min** | First behavioral stats calculated |
| **15 min** | Correlations detected |
| **30 min** | First pattern suggestion appears! |
| **1 hour** | Pattern learning processes approvals |

### Check for Suggestions

```powershell
# Get pending suggestions
Invoke-RestMethod "http://localhost:5052/api/suggestions/pending"

# Approve a suggestion (copy ID from above)
$id = "PASTE-SUGGESTION-ID-HERE"
Invoke-RestMethod -Uri "http://localhost:5052/api/suggestions/$id/approve" -Method POST
```

---

## 📊 Monitor (Optional)

```powershell
.\monitor_24h_test.ps1
```

Shows live dashboard with:
- Container status
- Data flow metrics  
- Pattern suggestions
- Hangfire jobs

---

## 🎯 Success Checklist

After 24 hours, you should have:

- [ ] 300,000+ time-series records in QuestDB
- [ ] 4+ behavioral statistics calculated
- [ ] 6+ correlations detected
- [ ] 1+ behavioral cluster formed
- [ ] 1+ pattern suggestion created
- [ ] At least 1 suggestion approved
- [ ] Pattern confidence increased after approval

---

## 🆘 Troubleshooting

**No data in QuestDB?**
```powershell
# Check PI ingestion status
Invoke-RestMethod "http://localhost:5052/api/ingestion/status"
```

**No suggestions after 1 hour?**
```powershell
# Check Hangfire dashboard
Start-Process "http://localhost:5052/hangfire"
# Verify all jobs show "Next Execution" times
```

**Pattern suggestions to view:****
```powershell
# List suggestions
Invoke-RestMethod "http://localhost:5052/api/suggestions/pending" | ConvertTo-Json
```

---

## 📚 Full Documentation

- Complete guide: [START_HISTORIAN_24H_TEST.md](START_HISTORIAN_24H_TEST.md)
- System overview: [READY_TO_START.md](READY_TO_START.md)
- Architecture: [docs/architecture/](docs/architecture/)

---

**The First Industrial Historian That Learns From You™**  
*Let it run for 24 hours and watch NAIA learn your patterns!* 🧠

---

## Quick Reference Commands

```powershell
# Check infrastructure
docker-compose ps

# View QuestDB data
Start-Process "http://localhost:9000"

# View Hangfire jobs
Start-Process "http://localhost:5052/hangfire"

# View Kafka messages
Start-Process "http://localhost:8080"

# Get API docs
Start-Process "http://localhost:5052/swagger"

# Stop everything
# Ctrl+C in each PowerShell window
# Then: docker-compose down
```
