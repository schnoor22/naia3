# 🚀 NAIA 24-Hour Learning Test - Complete Startup Guide

**The First Industrial Historian That Learns From You™**

This guide will start the complete NAIA pipeline and run a 24-hour test to demonstrate pattern learning from live PI data.

---

## 📊 Data Flow Architecture

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  PI System  │────▶│    Kafka    │────▶│   QuestDB   │────▶│   Pattern   │
│ (sdhqpisrvr)│     │   (Topic)   │     │  (Historian)│     │   Engine    │
└─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘
       │                   ▲                     │                   │
       │                   │                     │                   │
       └───────────────────┘                     ▼                   ▼
          PIDataIngestion              ┌─────────────────┐  ┌──────────────┐
          Service (API)                │ Redis (Current  │  │  PostgreSQL  │
                                       │ Values + Cache) │  │  (Patterns)  │
                                       └─────────────────┘  └──────────────┘
```

### Pipeline Components

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **PI Connector** | AF SDK / PI Web API | Reads MLR1* tags from PI System |
| **Kafka Producer** | `PIDataIngestionService` | Publishes batches to `naia.datapoints` topic |
| **Kafka Consumer** | `Naia.Ingestion` Worker | Consumes & writes to QuestDB + Redis |
| **Pattern Engine** | Hangfire Jobs | Learns behavioral patterns every 5-15 min |
| **Pattern API** | REST + SignalR | Real-time pattern suggestions to UI |

---

## ⚙️ Step 1: Start Infrastructure (Docker)

All infrastructure runs in Docker containers defined in `docker-compose.yml`.

```powershell
# Navigate to project root
cd c:\naia3

# Start all infrastructure services
docker-compose up -d

# Verify all containers are healthy
docker-compose ps

# Expected output:
# NAME                STATUS              PORTS
# naia-postgres       Up (healthy)        5432
# naia-questdb        Up (healthy)        8812, 9000, 9009
# naia-redis          Up (healthy)        6379
# naia-kafka          Up (healthy)        9092, 29092
# naia-zookeeper      Up (healthy)        2181
# naia-kafka-ui       Up                  8080
```

### Verify Infrastructure

**PostgreSQL:**
```powershell
# Check database exists and schema is initialized
docker exec -it naia-postgres psql -U naia -d naia -c "\dt"

# Expected tables:
# - data_sources
# - points
# - patterns
# - pattern_roles
# - pattern_suggestions
# - behavioral_clusters
# - correlation_cache
# - behavioral_stats
```

**QuestDB:**
```powershell
# Open QuestDB Web Console
Start-Process "http://localhost:9000"

# Run query in console to verify tables:
# SELECT * FROM timeseries LIMIT 10;
# (Will be empty initially - data comes from ingestion)
```

**Kafka:**
```powershell
# Open Kafka UI
Start-Process "http://localhost:8080"

# Check topics exist: naia.datapoints
```

**Redis:**
```powershell
# Test Redis connection
docker exec -it naia-redis redis-cli ping
# Expected: PONG
```

---

## 🔌 Step 2: Verify PI Connection & MLR1 Points

Before starting ingestion, ensure MLR1 points are configured in PostgreSQL.

```powershell
# Check if MLR1 points exist
docker exec -it naia-postgres psql -U naia -d naia -c "SELECT name, source_address, is_enabled, engineering_units FROM points WHERE name LIKE 'MLR1%' ORDER BY name;"

# Expected output:
#      name        | source_address | is_enabled | engineering_units
# -----------------+----------------+------------+-------------------
#  MLR1.Efficiency | MLR1.Efficiency| t          | %
#  MLR1.Power      | MLR1.Power     | t          | kW
#  MLR1.Speed      | MLR1.Speed     | t          | RPM
#  MLR1.Temperature| MLR1.Temperature| t         | °C
```

**If points DON'T exist:**
```powershell
# Create MLR1 points
.\execute_sql.ps1
```

---

## 🚀 Step 3: Start Naia.Api (Producer + Pattern Engine)

The API hosts:
1. **PIDataIngestionService** - Reads from PI → Publishes to Kafka
2. **Hangfire Jobs** - Pattern learning workers
3. **REST API** - Management endpoints
4. **SignalR Hub** - Real-time notifications
5. **Hangfire Dashboard** - Job monitoring at `/hangfire`

```powershell
# Build the API
dotnet build src/Naia.Api/Naia.Api.csproj

# Start the API
cd src/Naia.Api
dotnet run

# You should see:
# ═══════════════════════════════════════════════════════════════════
#   NAIA API Starting
#   Listening on: http://localhost:5052
# ═══════════════════════════════════════════════════════════════════
# info: Naia.Api.Services.PIDataIngestionService[0]
#       ═══════════════════════════════════════════════════════════════════
#         PI → Kafka Ingestion Started
#         Publishing to: naia.datapoints
#       ═══════════════════════════════════════════════════════════════════
# info: Hangfire.BackgroundJobServer[0]
#       Starting Hangfire Server using job storage: 'PostgreSqlStorage'
# info: Hangfire.Server.BackgroundServerProcess[0]
#       Server naia-pattern-001:12345 started with queues: analysis, matching, learning, maintenance, default
```

### Start PI Ingestion

Open a new PowerShell window:

```powershell
# Start PI data ingestion (publishes MLR1* points to Kafka every 1 second)
Invoke-WebRequest -Uri "http://localhost:5052/api/ingestion/start" -Method POST

# Response:
# {
#   "success": true,
#   "message": "PI data ingestion started - publishing to Kafka",
#   "pointsCount": 4
# }
```

### Verify Data is Flowing to Kafka

```powershell
# Check Kafka UI: http://localhost:8080
Start-Process "http://localhost:8080"

# Navigate to: Topics → naia.datapoints → Messages
# You should see batches of MLR1 data arriving every 1-5 seconds
```

---

## 🔄 Step 4: Start Naia.Ingestion (Consumer)

The ingestion worker consumes from Kafka and writes to QuestDB + Redis.

```powershell
# Open a NEW PowerShell window
cd c:\naia3\src\Naia.Ingestion

# Build the worker
dotnet build

# Start the worker
dotnet run

# You should see:
# ═══════════════════════════════════════════════════════════════════
#   NAIA Ingestion Worker Starting
#   The First Industrial Historian That Learns From You
# ═══════════════════════════════════════════════════════════════════
# info: Naia.Ingestion.Worker[0]
#       Starting ingestion pipeline...
# info: Naia.Infrastructure.Pipeline.KafkaToQuestDbPipeline[0]
#       Starting Kafka consumer for topic: naia.datapoints
# info: Naia.Infrastructure.Pipeline.KafkaToQuestDbPipeline[0]
#       Consumer assigned partitions: [naia.datapoints[0]]
# info: Naia.Ingestion.Worker[0]
#       Pipeline Health: ✓ | Processed: 12 batches, 48 points | Throughput: 4.2/s | Avg Latency: 23.5ms
```

### Verify Data in QuestDB

```powershell
# Open QuestDB Web Console
Start-Process "http://localhost:9000"
```

**Run SQL Query:**
```sql
-- Check latest data for MLR1 points
SELECT 
    timestamp, 
    point_name, 
    value, 
    quality
FROM timeseries 
WHERE point_name LIKE 'MLR1%'
ORDER BY timestamp DESC 
LIMIT 100;
```

**Expected Output:**
```
timestamp                    | point_name         | value  | quality
-----------------------------+--------------------+--------+--------
2026-01-10T15:42:10.123Z    | MLR1.Speed         | 1850.2 | Good
2026-01-10T15:42:10.123Z    | MLR1.Power         | 125.8  | Good
2026-01-10T15:42:10.123Z    | MLR1.Temperature   | 82.3   | Good
2026-01-10T15:42:10.123Z    | MLR1.Efficiency    | 94.5   | Good
...
```

### Verify Current Values in Redis

```powershell
# Check Redis cache for MLR1 points
docker exec -it naia-redis redis-cli KEYS "current:*MLR1*"

# Expected:
# 1) "current:point-id-1"
# 2) "current:point-id-2"
# 3) "current:point-id-3"
# 4) "current:point-id-4"

# Get a current value
docker exec -it naia-redis redis-cli GET "current:point-id-1"
# Expected: JSON with {timestamp, value, quality}
```

---

## 🧠 Step 5: Monitor Pattern Learning

The Pattern Engine runs Hangfire jobs on CRON schedules:

| Job | Schedule | What It Does |
|-----|----------|-------------|
| **BehavioralAnalysis** | Every 5 min | Calculates mean, stddev, min, max, rate-of-change for each point |
| **CorrelationAnalysis** | Every 15 min | Finds correlations between points using ASOF JOIN |
| **ClusterDetection** | Every 15 min (+5m) | Groups highly-correlated points into clusters (Louvain) |
| **PatternMatching** | Every 15 min (+10m) | Matches clusters to known patterns (pump, compressor, etc.) |
| **PatternLearning** | Hourly | Processes user feedback (approvals/rejections) |
| **Maintenance** | Daily 3 AM | Cleans up old data |

### Hangfire Dashboard

```powershell
# Open Hangfire dashboard
Start-Process "http://localhost:5052/hangfire"
```

**Dashboard Features:**
- **Jobs** - See all scheduled jobs and their next run time
- **Recurring Jobs** - View CRON schedules
- **Succeeded Jobs** - See completed jobs with execution time
- **Failed Jobs** - See errors (with retry button)
- **Servers** - See connected Hangfire workers

### Watch Pattern Engine Logs

In the `Naia.Api` console window, you should see:

```
info: Naia.PatternEngine.Jobs.BehavioralAnalysisJob[0]
      ═══════════════════════════════════════════════════════════════
        Behavioral Analysis Job Started
        Analyzing: 4 active points
      ═══════════════════════════════════════════════════════════════
info: Naia.PatternEngine.Jobs.BehavioralAnalysisJob[0]
      [MLR1.Speed] Stats: μ=1847.3 σ=42.1 min=1720.5 max=1950.2 roc=2.3/s
info: Naia.PatternEngine.Jobs.BehavioralAnalysisJob[0]
      [MLR1.Power] Stats: μ=124.8 σ=8.9 min=105.2 max=142.5 roc=1.1/s
info: Naia.PatternEngine.Jobs.BehavioralAnalysisJob[0]
      Cached 4 behavioral fingerprints (48h TTL)
      
info: Naia.PatternEngine.Jobs.CorrelationAnalysisJob[0]
      ═══════════════════════════════════════════════════════════════
        Correlation Analysis Job Started
        Analyzing: 6 point pairs
      ═══════════════════════════════════════════════════════════════
info: Naia.PatternEngine.Jobs.CorrelationAnalysisJob[0]
      [MLR1.Speed ↔ MLR1.Power] Correlation: 0.87 (Strong positive)
info: Naia.PatternEngine.Jobs.CorrelationAnalysisJob[0]
      [MLR1.Power ↔ MLR1.Temperature] Correlation: 0.76 (Moderate positive)
info: Naia.PatternEngine.Jobs.CorrelationAnalysisJob[0]
      Stored 6 correlations in cache (24h TTL)
```

---

## 📈 Step 6: Check Pattern Suggestions

After ~30-45 minutes, the pattern engine should start generating suggestions.

### REST API

```powershell
# Get pending pattern suggestions
Invoke-RestMethod -Uri "http://localhost:5052/api/suggestions/pending"
```

**Expected Response:**
```json
{
  "pendingCount": 2,
  "suggestions": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "patternName": "Motor-Drive",
      "confidence": 0.82,
      "pointCount": 4,
      "commonPrefix": "MLR1",
      "status": "pending",
      "createdAt": "2026-01-10T16:15:00Z",
      "reason": "Strong correlation between speed, power, temperature. Naming matches motor pattern."
    }
  ]
}
```

### SignalR Real-Time Notifications

If you have a web UI connected via SignalR:

```javascript
// UI receives real-time notifications
connection.on("SuggestionCreated", (suggestion) => {
  console.log("New pattern suggestion:", suggestion);
  // Show toast notification to user
});

connection.on("ClusterDetected", (cluster) => {
  console.log("Behavioral cluster detected:", cluster);
});
```

---

## ✅ Step 7: Approve Pattern Suggestions

When you see a good suggestion, approve it to teach NAIA:

```powershell
# Approve a suggestion
$suggestionId = "550e8400-e29b-41d4-a716-446655440000"
Invoke-RestMethod -Uri "http://localhost:5052/api/suggestions/$suggestionId/approve" -Method POST

# Response:
# {
#   "success": true,
#   "message": "Suggestion approved - pattern confidence increased"
# }
```

**What Happens When You Approve:**
1. Suggestion status → `approved`
2. Pattern confidence increases by +5%
3. Point-to-pattern bindings are created
4. PatternLearningJob processes feedback on next hourly run
5. Future similar clusters get higher confidence scores

**This is how NAIA learns!** 🧠

---

## 📊 Step 8: Monitor 24-Hour Test

### Health Metrics Dashboard

Create a simple monitoring script:

```powershell
# Save as: c:\naia3\monitor_24h_test.ps1

while ($true) {
    Clear-Host
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  NAIA 24-Hour Learning Test - Status" -ForegroundColor Cyan
    Write-Host "  Started: $(Get-Date)" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    
    # Check Docker containers
    Write-Host "`n[Docker Infrastructure]" -ForegroundColor Yellow
    docker-compose ps | Select-String "Up"
    
    # Check Kafka lag
    Write-Host "`n[Kafka Consumer Lag]" -ForegroundColor Yellow
    $kafkaStats = Invoke-RestMethod "http://localhost:8080/api/consumer-groups"
    # Parse and display lag
    
    # Check QuestDB row count
    Write-Host "`n[QuestDB Time-Series Data]" -ForegroundColor Yellow
    # Query QuestDB for row count
    
    # Check pattern suggestions
    Write-Host "`n[Pattern Suggestions]" -ForegroundColor Yellow
    $suggestions = Invoke-RestMethod "http://localhost:5052/api/suggestions/stats"
    Write-Host "  Pending: $($suggestions.pendingCount)"
    Write-Host "  Approved Today: $($suggestions.approvedToday)"
    Write-Host "  Rejected Today: $($suggestions.rejectedToday)"
    Write-Host "  Approval Rate: $($suggestions.approvalRate)%"
    
    # Check Hangfire job stats
    Write-Host "`n[Hangfire Jobs - Last Run]" -ForegroundColor Yellow
    # Parse Hangfire API for job stats
    
    Start-Sleep -Seconds 60
}
```

### Key Metrics to Track

| Metric | Good | Warning | Critical |
|--------|------|---------|----------|
| **Kafka Consumer Lag** | < 100 | 100-1000 | > 1000 |
| **QuestDB Ingest Rate** | > 1 point/sec | 0.1-1 point/sec | < 0.1 point/sec |
| **Avg Processing Latency** | < 50ms | 50-200ms | > 200ms |
| **Redis Memory Usage** | < 256MB | 256-400MB | > 400MB |
| **Pattern Suggestions** | 1+ per hour | 1 per day | None after 4h |
| **Hangfire Job Success** | 100% | 90-99% | < 90% |

---

## 🧪 Expected Results After 24 Hours

### Data Volume
```
MLR1 Points: 4 points
Ingestion Rate: ~4 points/second (1 point per point per second)
24 Hours = 86,400 seconds
Total Data Points: ~345,600 time-series records
```

### Pattern Learning Progression

**Hour 1-2: Data Gathering**
- Behavioral stats calculated every 5 min
- Redis cache populated with fingerprints
- No suggestions yet (need more data)

**Hour 3-6: Correlation Discovery**
- Correlations between MLR1.Speed ↔ MLR1.Power detected
- Correlations between MLR1.Power ↔ MLR1.Temperature detected
- First cluster detected: [MLR1.Speed, MLR1.Power, MLR1.Temperature, MLR1.Efficiency]

**Hour 7-12: Pattern Matching**
- Cluster matched against "Motor-Drive" pattern (naming + correlation)
- First suggestion created with ~75-85% confidence
- Waiting for user approval

**Hour 13-24: Learning & Refinement**
- User approves suggestion → Pattern confidence increases to 90%+
- Future similar clusters get suggested with higher confidence
- Pattern library grows with user feedback

### Database State

**PostgreSQL - Patterns:**
```sql
SELECT name, category, confidence, application_count, approval_count 
FROM patterns 
ORDER BY confidence DESC;

-- Expected:
--   name        | category | confidence | application_count | approval_count
-- --------------+----------+------------+-------------------+----------------
--   Motor-Drive | Equipment|    0.90    |        1          |       1
```

**PostgreSQL - Behavioral Stats:**
```sql
SELECT point_name, mean_value, stddev, min_value, max_value, sample_count
FROM behavioral_stats
WHERE point_name LIKE 'MLR1%'
ORDER BY last_calculated_at DESC
LIMIT 4;

-- Stats should be updated every 5 minutes
```

**QuestDB - Time-Series:**
```sql
SELECT 
    COUNT(*) as total_records,
    MIN(timestamp) as first_record,
    MAX(timestamp) as last_record,
    COUNT(DISTINCT point_name) as unique_points
FROM timeseries
WHERE point_name LIKE 'MLR1%';

-- Expected:
-- total_records | first_record              | last_record               | unique_points
-- --------------+---------------------------+---------------------------+--------------
-- 345,600       | 2026-01-10T00:00:00.000Z | 2026-01-11T00:00:00.000Z | 4
```

---

## 🎯 Success Criteria

✅ **Infrastructure:** All Docker containers running healthy for 24h  
✅ **Data Flow:** Continuous data from PI → Kafka → QuestDB with < 100ms latency  
✅ **Pattern Engine:** All 6 Hangfire jobs executing on schedule with 100% success rate  
✅ **Correlations:** Strong correlations (> 0.7) detected between Speed/Power/Temp  
✅ **Clusters:** At least 1 behavioral cluster detected containing all 4 MLR1 points  
✅ **Suggestions:** At least 1 pattern suggestion created for Motor-Drive pattern  
✅ **Learning:** Pattern confidence increases after user approval  
✅ **Cache Hit Rate:** Redis cache hit rate > 80% for behavioral fingerprints  

---

## 🔧 Troubleshooting

### Issue: No data in QuestDB

**Check:**
```powershell
# 1. Is PI ingestion running?
Invoke-RestMethod "http://localhost:5052/api/ingestion/status"

# 2. Are messages in Kafka?
# Open Kafka UI: http://localhost:8080 → Topics → naia.datapoints → Messages

# 3. Is Naia.Ingestion consumer running?
# Check console logs for "Pipeline Health: ✓"

# 4. Check QuestDB logs
docker logs naia-questdb -f
```

### Issue: Pattern suggestions not appearing

**Check:**
```powershell
# 1. Is there enough data? (Need at least 2-3 hours)
# Run behavioral analysis query in QuestDB:
SELECT point_name, COUNT(*) as records
FROM timeseries
WHERE point_name LIKE 'MLR1%'
  AND timestamp > dateadd('h', -3, now())
GROUP BY point_name;

# 2. Are Hangfire jobs running?
# Open http://localhost:5052/hangfire → Recurring Jobs
# Check "Next Execution" and "Last Execution" timestamps

# 3. Check for errors in Hangfire dashboard
# Open http://localhost:5052/hangfire → Failed Jobs

# 4. Check pattern engine logs in API console
# Look for "Behavioral Analysis Job Started"
```

### Issue: High Kafka consumer lag

**Check:**
```powershell
# 1. Is QuestDB slow?
docker stats naia-questdb
# CPU should be < 50%, Memory < 2GB

# 2. Is Ingestion worker keeping up?
# Check console: "Throughput: X.X/s"
# Should be > 1 point/sec

# 3. Check Kafka partition assignment
# In Kafka UI → Consumer Groups → naia-ingestion-group
# Should show offset increasing steadily
```

---

## 📝 Next Steps After 24 Hours

1. **Review Suggestions:** Check all pending suggestions and approve/reject
2. **Analyze Patterns:** Use Swagger UI at `http://localhost:5052/swagger` to explore pattern APIs
3. **Export Results:** Query QuestDB for statistical analysis
4. **Scale Test:** Add more points (MLR2, MLR3, etc.) to test clustering with larger datasets
5. **UI Development:** Build React/Vue UI to visualize patterns and suggestions in real-time

---

## 🎉 Congratulations!

You've successfully demonstrated **The First Industrial Historian That Learns From You™**

NAIA is now:
- ✅ Ingesting live PI data
- ✅ Calculating behavioral statistics
- ✅ Detecting correlations
- ✅ Forming equipment clusters
- ✅ Matching against pattern library
- ✅ Learning from your feedback

**This is just the beginning.** As you add more sites, NAIA's pattern library grows, and suggestions get smarter. By site #11, NAIA knows your organization's naming conventions, equipment correlations, and operational patterns.

---

*Built on: Kafka + QuestDB + PostgreSQL + Redis + Hangfire + .NET 8*  
*Tested on: Windows 11, Docker Desktop 4.28+*  
*Date: January 10, 2026*
