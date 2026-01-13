# NAIA QuestDB Data Flow - One-Page Quick Reference

**Issue:** Trends page shows `count:0` and empty data array

---

## THE PIPELINE (What Should Happen)

```
Data Source → Kafka → Ingestion Worker → Dedup (Redis) → Enrich (PostgreSQL) 
→ QuestDB (/write ILP) → Redis Cache → API Query → Frontend
```

---

## QUICK DIAGNOSIS (5 Minutes)

### Run This Script
```powershell
.\diagnose-questdb-flow.ps1
```

### Based on Output:

| Output | Next Step |
|--------|-----------|
| "✓ QuestDB has data: X rows" | Jump to "Wrong Point ID" section |
| "✗ QuestDB is EMPTY: 0 rows" | Check Kafka → Ingestion pipeline |
| "✗ naia-ingestion: STOPPED" | Start: `docker compose up naia-ingestion` |
| "⚠ High Kafka LAG" | Ingestion slow, check logs: `docker logs naia-ingestion` |
| "✗ Redis has 0 entries" | Data not flowing, see QuestDB check |

---

## ROOT CAUSE TEST SEQUENCE

### Test 1: QuestDB Has Data?
```bash
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(*) FROM point_data;"
```
**Expected:** > 0  
**If 0:** → Go to Test 2

---

### Test 2: Kafka Has Messages?
```bash
docker exec kafka kafka-console-consumer.sh --bootstrap-server kafka:9092 \
  --topic naia.datapoints --from-beginning --max-messages 1
```
**Expected:** See JSON message  
**If empty:** → Data source not publishing (check PI, Weather API, etc.)

---

### Test 3: Consumer Processing?
```bash
docker logs --tail=50 naia-ingestion | grep -i "pipeline\|processed"
```
**Expected:** "Pipeline Health: ✓" entries  
**If errors:** → See "Common Errors" section below

---

### Test 4: Points Synced to QuestDB?
```bash
docker exec postgres psql -U naia -d naia \
  -c "SELECT COUNT(point_sequence_id) as with_id, COUNT(*) FILTER (WHERE point_sequence_id IS NULL) as null_id FROM points;"
```
**Expected:** with_id = all points, null_id = 0  
**If null_id > 0:** → Points not enriched, enrichment failed

---

### Test 5: Test API Directly
```bash
# Get a point UUID
POINT_ID=$(docker exec postgres psql -U naia -d naia -t -c "SELECT id FROM points LIMIT 1;" | tr -d ' ')

# Query it
curl -s "http://localhost:5073/api/points/$POINT_ID/history?start=2026-01-10&end=2026-01-13" | jq '.count'
```
**Expected:** count > 0  
**If 0:** → See "Why count=0" matrix below

---

## WHY COUNT=0? (Decision Matrix)

### Step A: Check QuestDB directly
```bash
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(*) FROM point_data WHERE point_id = 12345 LIMIT 1;"
```

| Result | Cause | Fix |
|--------|-------|-----|
| count > 0 | Data exists, point_id wrong | Verify PointSequenceId matches |
| count = 0 | No data for this point_id | Check data was written with correct ID |

### Step B: Check if data exists at all
```bash
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(DISTINCT point_id) FROM point_data;"
```

| Result | Cause | Fix |
|--------|-------|--|
| > 0 | Data in QuestDB but wrong point_id | Fix PointSequenceId mapping |
| 0 | NO DATA in QuestDB | Check Kafka → Pipeline |

### Step C: Check PostgreSQL point lookup
```bash
docker exec postgres psql -U naia -d naia \
  -c "SELECT point_sequence_id FROM points WHERE id = '{uuid}';"
```

| Result | Cause | Fix |
|--------|-------|-----|
| NULL | Point not synced | Reprocess batch or set ID manually |
| Number | ID set | ID might be wrong, compare to QuestDB |

---

## COMMON ERRORS & FIXES

### Error: "Point not yet synchronized to time-series database"
**Cause:** point.PointSequenceId is NULL in PostgreSQL  
**Fix:**
```bash
# Check what's in PostgreSQL
docker exec postgres psql -U naia -d naia \
  -c "SELECT id, name, point_sequence_id FROM points WHERE point_sequence_id IS NULL LIMIT 5;"

# Manually sync (if you know the IDs)
docker exec postgres psql -U naia -d naia \
  -c "UPDATE points SET point_sequence_id = 100 WHERE name = 'KSH_T1_WindSpeed';"

# Or trigger re-enrichment by reprocessing batch
```

---

### Error: "Does not exist" when querying QuestDB
**Cause:** `Server Compatibility Mode=NoTypeLoading` missing from connection string  
**Symptom:** QuestDbTimeSeriesReader can't connect  
**Fix:**
```csharp
// Check: QuestDbTimeSeriesReader.cs line 49
var connString = connStringBuilder.ToString() + ";Server Compatibility Mode=NoTypeLoading";
```
If missing, add this line.

---

### Error: Kafka consumer lag growing
**Cause:** Ingestion worker too slow or crashed  
**Check:**
```bash
docker logs naia-ingestion | grep -i "error\|exception" | tail -10
docker ps | grep naia-ingestion  # Is it running?
```

---

### Error: "Duplicate batch" logged repeatedly
**Cause:** Idempotency store corrupted or data written without offset commit  
**Fix:**
```bash
# Clear idempotency store (one-time recovery)
docker exec redis redis-cli DEL "naia:idempotency:*"

# This forces reprocessing. WARNING: May create duplicates if data already in QuestDB
```

---

## PERFORMANCE EXPECTATIONS

| Metric | Good | Warning | Bad |
|--------|------|---------|-----|
| QuestDB row count | > 1,000 | 0-1,000 | 0 |
| Distinct point_ids | > 10 | 1-10 | 0 |
| Kafka LAG | < 10 | 10-100 | > 100 |
| Pipeline throughput | > 100 pts/sec | 10-100 | < 10 |
| Query response time | < 100ms | 100-500ms | > 500ms |
| Data freshness | < 5 min | 5-60 min | > 60 min |

---

## KEY CONFIGURATION LOCATIONS

| Component | Config File | Key Setting |
|-----------|-------------|------------|
| **Kafka** | appsettings.json | `Kafka.BootstrapServers`, `Kafka.DataPointsTopic` |
| **QuestDB Write** | appsettings.json | `QuestDb.HttpEndpoint` (default: http://localhost:9000) |
| **QuestDB Read** | appsettings.json | `QuestDb.PgWireEndpoint` (default: localhost:8812) |
| **Redis** | appsettings.json | `Redis.ConnectionString` (default: localhost:6379) |
| **PostgreSQL** | appsettings.json | `ConnectionStrings.PostgreSQL` |
| **QuestDB Schema** | init-scripts/questdb/01-init-schema.sql | Table `point_data` |

---

## CRITICAL GOTCHAS

1. **PointSequenceId ≠ Point UUID**
   - Point ID in PostgreSQL: UUID (example: `123e4567-e89b-12d3-a456-426614174000`)
   - PointSequenceId in QuestDB: LONG (example: `12345`)
   - API endpoint takes UUID, converts to LONG for QuestDB query

2. **point_id in QuestDB is NOT the same as UUID**
   - QuestDB stores: `point_id = 12345` (LONG integer)
   - If your Point has `point_sequence_id = NULL`, API will fail with BadRequest

3. **Redis stores JSON, not serialized objects**
   - Key: `naia:cv:12345` (where 12345 is point_sequence_id)
   - Value: JSON string, not binary object
   - Cache update happens AFTER QuestDB write (not before)

4. **Kafka offsets commit ONLY after success**
   - If pipeline crashes mid-process: Batch will be reprocessed
   - If offset commit fails: Batch will be reprocessed (dedup prevents duplicates)
   - Result: At-least-once delivery, exactly-once processing

5. **QuestDB uses HTTP ILP, not direct DB access**
   - Writes: POST http://localhost:9000/write (plain text ILP format)
   - Reads: PostgreSQL Wire Protocol on port 8812
   - Different endpoints!

---

## MONITORING DASHBOARD (Real-Time)

Open 4 terminals:

**Terminal 1: Data arriving**
```bash
watch -n 1 'docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(*) as rows, COUNT(DISTINCT point_id) as points, MAX(timestamp) as latest FROM point_data;"'
```

**Terminal 2: Consumer lag**
```bash
watch -n 5 'docker exec kafka kafka-consumer-groups.sh --bootstrap-server kafka:9092 \
  --group naia-historians --describe | tail -3'
```

**Terminal 3: Pipeline logs**
```bash
docker logs -f naia-ingestion 2>&1 | grep -E "Pipeline|Health|Processed"
```

**Terminal 4: API health**
```bash
watch -n 10 'curl -s http://localhost:5073/api/pipeline/health | jq "{state, isHealthy, pts_per_sec: .metrics.pointsPerSecond}"'
```

---

## ONE-MINUTE HEALTH CHECK

```bash
echo "=== Containers ===" && \
docker ps -a --format "table {{.Names}}\t{{.Status}}" | grep naia && \
echo "" && \
echo "=== QuestDB Data ===" && \
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb -t \
  -c "SELECT COUNT(*) as rows FROM point_data;" && \
echo "" && \
echo "=== Kafka LAG ===" && \
docker exec kafka kafka-consumer-groups.sh --bootstrap-server kafka:9092 \
  --group naia-historians --describe 2>/dev/null | tail -2 && \
echo "" && \
echo "=== Pipeline Health ===" && \
curl -s http://localhost:5073/api/pipeline/health 2>/dev/null | jq '.isHealthy, .metrics.pointsPerSecond' || echo "API not responding"
```

---

## DOCUMENTATION MAP

**Start Here:**
- This file (one-page reference)

**For Debugging:**
- QUESTDB_QUICK_DEBUG_COMMANDS.md (copy-paste commands)

**For Understanding:**
- QUESTDB_DATA_FLOW_INVESTIGATION.md (16-section deep dive)
- QUESTDB_CODE_REFERENCE_MAP.md (code paths and files)

**For Diagnosis:**
- Run: `diagnose-questdb-flow.ps1`

---

## EMERGENCY RECOVERY

### If Everything Broken
```bash
# Stop all services
docker compose down

# Clean state (BE CAREFUL - deletes data)
docker volume rm naia_questdb_data naia_postgres_data naia_redis_data

# Restart
docker compose up

# Wait 2-3 minutes for schema init
# Then start data source (PI, Replay, Weather API, etc.)
```

### If Just QuestDB Broken
```bash
docker restart questdb
# Data in Kafka buffered, will replay when QuestDB comes back
```

### If Just Redis Broken
```bash
docker restart redis
# Idempotency check will fail, but dedup prevention lost
# Risk: Duplicates in QuestDB if batches reprocess
# Mitigation: Clear idempotency store after restart
docker exec redis redis-cli DEL "naia:idempotency:*"
```

---

**Last Updated:** January 12, 2026  
**Version:** NAIA 3.0  
**Status:** Complete

