# NAIA QuestDB Debugging - Quick Commands

## IMMEDIATE TESTS (Copy & Paste)

### Test 1: Does QuestDB Have Any Data?
```bash
# Check row count in point_data table
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(*) as total_rows, COUNT(DISTINCT point_id) as point_ids FROM point_data;"
```

**Expected:** Both numbers > 0
**If 0:** Data is NOT in QuestDB, check Kafka/Pipeline

---

### Test 2: Sample Recent Data from QuestDB
```bash
# Get last 5 data points
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT timestamp, point_id, value, quality FROM point_data ORDER BY timestamp DESC LIMIT 5;"
```

**Expected:** See recent timestamps and values
**If table doesn't exist:** point_data table never created (check init scripts)

---

### Test 3: Check Kafka Has Messages
```bash
# View one message from the topic
docker exec kafka kafka-console-consumer.sh \
  --bootstrap-server kafka:9092 \
  --topic naia.datapoints \
  --from-beginning \
  --max-messages 1
```

**Expected:** JSON DataPointBatch message
**If empty:** Producer isn't publishing (check PIDataIngestionService)

---

### Test 4: Check Kafka Consumer Group Status
```bash
# See what the ingestion worker is doing
docker exec kafka kafka-consumer-groups.sh \
  --bootstrap-server kafka:9092 \
  --group naia-historians \
  --describe
```

**Expected:** LAG column should be 0 or small (not thousands)
**If LAG is high:** Consumer is behind, not processing fast enough

---

### Test 5: Check PostgreSQL Points
```bash
# Count total points and ones with PointSequenceId
docker exec postgres psql -U naia -d naia \
  -c "SELECT 
        COUNT(*) as total_points,
        COUNT(point_sequence_id) as with_sequence_id,
        COUNT(*) FILTER (WHERE point_sequence_id IS NULL) as null_sequence_id
      FROM points;"
```

**Expected:** with_sequence_id should equal total_points
**If null_sequence_id > 0:** Points not synced to QuestDB

---

### Test 6: Check Redis Cache
```bash
# List all current value cache entries
docker exec redis redis-cli KEYS "naia:cv:*" | wc -l

# Get a sample entry
docker exec redis redis-cli KEYS "naia:cv:*" | head -1 | \
  xargs -I {} docker exec redis redis-cli GET {}
```

**Expected:** Should have entries and valid JSON
**If 0 entries:** No data flowing through pipeline

---

### Test 7: Test API History Endpoint
```bash
# First, get a point ID from PostgreSQL
POINT_ID=$(docker exec postgres psql -U naia -d naia -t \
  -c "SELECT id FROM points LIMIT 1;" | tr -d ' ')

# Then query the history endpoint
curl -s "http://localhost:5073/api/points/$POINT_ID/history?start=2026-01-10&end=2026-01-13" | \
  jq '.count, .data | length'
```

**Expected:** count > 0 and data array has items
**If count=0:** Either point_sequence_id is null OR data not in QuestDB

---

### Test 8: Direct QuestDB Query (Like API Does)
```bash
# Query point_data for a specific point_id
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(*) as count FROM point_data WHERE point_id = 1 LIMIT 1000;"

# If that doesn't work, check what point_ids exist
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT DISTINCT point_id FROM point_data LIMIT 5;"
```

**Expected:** count > 0 for valid point_ids
**If 0:** Check if using correct point_id (not point UUID)

---

## DEEP DIVE: Check Each Component

### Kafka - Detailed
```bash
# How many messages in topic?
docker exec kafka kafka-run-class.sh kafka.tools.JmxTool \
  --object-name kafka.server:type=ReplicaManager,name=UnderReplicatedPartitions \
  --attributes Value 2>/dev/null || echo "(Use UI for full stats)"

# Get topic config
docker exec kafka kafka-topics.sh --bootstrap-server kafka:9092 \
  --topic naia.datapoints --describe
```

### QuestDB - Detailed
```bash
# Check table schema
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "\d point_data"

# Check table size
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT pg_size_pretty(pg_total_relation_size('point_data'));"

# Check write rate (rows in last minute)
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(*) FROM point_data 
      WHERE timestamp > now() - INTERVAL '1 minute';"
```

### PostgreSQL - Detailed
```bash
# Check point-to-sequence mapping
docker exec postgres psql -U naia -d naia \
  -c "SELECT id, name, point_sequence_id FROM points LIMIT 10;"

# Check for duplicates in point_sequence_id
docker exec postgres psql -U naia -d naia \
  -c "SELECT point_sequence_id, COUNT(*) FROM points 
      GROUP BY point_sequence_id HAVING COUNT(*) > 1;"
```

### Redis - Detailed
```bash
# List all keys
docker exec redis redis-cli KEYS "*"

# Check idempotency store
docker exec redis redis-cli KEYS "naia:idempotency:*" | head -5

# Check pipeline metrics
docker exec redis redis-cli GET "naia:pipeline:metrics"

# Check database size
docker exec redis redis-cli INFO stats | grep used_memory
```

---

## LOGS TO CHECK

### Naia.Ingestion (Most Important)
```bash
# Watch in real-time
docker logs -f naia-ingestion

# Last 50 lines
docker logs --tail=50 naia-ingestion

# Search for errors
docker logs naia-ingestion 2>&1 | grep -i "error\|fail\|warn" | tail -20
```

### QuestDB
```bash
docker logs --tail=50 questdb | grep -i "error\|fail\|warn"
```

### Kafka
```bash
docker logs --tail=50 kafka | grep -i "naia-historians\|error\|fail"
```

---

## MONITORING: Real-Time Dashboard

### Terminal 1: Watch Data Arriving in QuestDB
```bash
watch -n 1 'docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(*) FROM point_data; SELECT MAX(timestamp) FROM point_data;"'
```

### Terminal 2: Watch Kafka Consumer Lag
```bash
watch -n 5 'docker exec kafka kafka-consumer-groups.sh --bootstrap-server kafka:9092 \
  --group naia-historians --describe | tail -5'
```

### Terminal 3: Watch Ingestion Logs
```bash
docker logs -f naia-ingestion 2>&1 | grep -E "Pipeline|Processed|Health|Error"
```

### Terminal 4: Watch API Responses
```bash
watch -n 10 'curl -s http://localhost:5073/api/pipeline/health | jq "{state, isHealthy, metrics: .metrics | {totalPointsProcessed, pointsPerSecond}}"'
```

---

## ROOT CAUSE MATRIX

| Symptom | Most Likely Cause | Check First |
|---------|-------------------|------------|
| QuestDB count=0 | No data flowing | Kafka topic message count |
| QuestDB has data, API returns 0 | Wrong point_id or null PointSequenceId | PostgreSQL points table |
| High Kafka LAG | Consumer slow | Naia.Ingestion logs for errors |
| Redis empty | Data not flowing | QuestDB table count |
| API endpoint 404 | Point doesn't exist in PostgreSQL | "SELECT ... FROM points WHERE id='...'" |
| Point has null PointSequenceId | Enrichment failed | Naia.Ingestion logs "Point has no PointName" |
| QuestDB connection error | Wrong host/port or NoTypeLoading missing | psql test with Server Compatibility Mode |
| Duplicate batch logged repeatedly | Idempotency store corrupted | Delete Redis naia:idempotency:* keys |

---

## RECOVERY PROCEDURES

### If No Data in QuestDB
1. Check Kafka has messages: `kafka-console-consumer` test above
2. If Kafka empty: Start a data source (PI, Weather API, Replay Worker)
3. If Kafka full: Restart `naia-ingestion` container

### If Points Have Null PointSequenceId
1. Check PostgreSQL: `SELECT * FROM points WHERE point_sequence_id IS NULL LIMIT 5;`
2. Manually set PointSequenceId (if you know the IDs):
   ```sql
   UPDATE points SET point_sequence_id = 100 WHERE name = 'KSH_T1_WindSpeed';
   ```
3. Restart pipeline to reprocess (will enrich on next batch)

### If Old Data Still Showing
1. Clear Redis cache: `docker exec redis redis-cli FLUSHDB`
2. This forces API to re-query QuestDB (not use cached current values)

### If Pipeline Won't Start
1. Check containers running: `docker compose ps`
2. Check logs: `docker compose logs naia-ingestion`
3. Verify configuration: Check appsettings.json files match environment
4. Restart: `docker compose down && docker compose up`

---

## EXPECTED NORMAL VALUES

| Metric | Healthy | Warning | Critical |
|--------|---------|---------|----------|
| QuestDB row count | > 1000 | 0-1000 | 0 |
| Distinct point_ids | > 10 | 1-10 | 0 |
| Kafka LAG | < 10 | 10-100 | > 100 |
| Redis current value entries | > 10 | 1-10 | 0 |
| API response time | < 100ms | 100-500ms | > 500ms |
| Pipeline throughput | > 100 pts/s | 10-100 pts/s | < 10 pts/s |
| Last data timestamp | < 5 min old | 5-60 min old | > 60 min old |

---

## CONTACTS FOR HELP

- **QuestDB Issues:** Check `/logs/questdb/` and `docker logs questdb`
- **Kafka Issues:** Check Kafka UI at `http://localhost:8080`
- **API Issues:** Check `/logs/api/` and `docker logs naia-api`
- **Ingestion Issues:** Check `/logs/ingestion/` and `docker logs naia-ingestion`

See `QUESTDB_DATA_FLOW_INVESTIGATION.md` for complete architecture details.
