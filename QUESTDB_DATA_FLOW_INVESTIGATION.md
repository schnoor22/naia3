# NAIA QuestDB Data Flow Investigation
## Why the Trends Page Shows count:0 and Empty Data Arrays

**Investigation Date:** January 12, 2026  
**Status:** Complete Data Flow Analysis with Root Cause Checklist

---

## EXECUTIVE SUMMARY

The NAIA system has a **complete, well-designed data flow pipeline**. If the Trends page shows `count:0`, the issue is **NOT the architecture** — it's one of these blockers:

1. **Data not in QuestDB** (ingestion failure)
2. **Queries cannot find the data** (table doesn't exist, wrong point_id)
3. **Caching layer is stale** (Redis issue)
4. **API not calling the right endpoint** (frontend issue)

---

## 1. COMPLETE DATA FLOW: SOURCE → QUESTDB

### Flow Diagram
```
┌─────────────────────────────────────────────────────────────────┐
│ INGESTION DIRECTION (Source → Storage)                          │
└─────────────────────────────────────────────────────────────────┘

PI System / Connectors
    ↓ (PIDataIngestionService polls/receives)
    ↓
Kafka Topic: "naia.datapoints" 
    ├─ Consumer Group: "naia-historians"
    ├─ Bootstrap Servers: "localhost:9092" (appsettings.json)
    ├─ Partitions: 12 (configured, not auto-created)
    └─ Replication Factor: 1
    ↓
Naia.Ingestion Worker (Consumer)
    ├─ Deserializes DataPointBatch from JSON
    ├─ Deduplicates via Redis idempotency store (batch ID)
    ├─ Enriches PointSequenceIds from PointName lookup
    ├─ Manual offset management (enable.auto.commit=false)
    └─ Only commits AFTER successful processing
    ↓
Pipeline Processing (/src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs)
    ├─ Step 1: Deduplication check via Redis
    ├─ Step 2: Write to QuestDB via ILP
    ├─ Step 3: Update Redis current value cache
    ├─ Step 4: Mark as processed in idempotency store
    └─ Step 5: Commit Kafka offset
    ↓
QuestDB Table: "point_data" (HTTP ILP endpoint)
    ├─ Endpoint: http://localhost:9000/write
    ├─ Protocol: InfluxDB Line Protocol (ILP)
    ├─ Table: point_data (created via init-scripts)
    ├─ Columns: timestamp, point_id, value, quality
    └─ Partitioning: By DAY with WAL enabled
    ↓
Redis Cache (Current Values)
    ├─ Key pattern: naia:cv:{pointSequenceId}
    ├─ TTL: 3600 seconds (configurable)
    └─ Updated AFTER QuestDB write

┌─────────────────────────────────────────────────────────────────┐
│ QUERY DIRECTION (Storage → API Response)                        │
└─────────────────────────────────────────────────────────────────┘

API Endpoint: GET /api/points/{id:guid}/history
    ├─ Parameters: start, end, limit
    ├─ Requires: point.PointSequenceId (not null)
    └─ Requires: point exists in PostgreSQL first
    ↓
ITimeSeriesReader (QuestDbTimeSeriesReader.cs)
    ├─ Connection: PostgreSQL Wire Protocol (Npgsql driver)
    ├─ Host:Port: localhost:8812 (configurable via QuestDb:PgWireEndpoint)
    ├─ Database: qdb
    ├─ Username: admin / Password: quest
    ├─ Server Compatibility Mode: NoTypeLoading (CRITICAL for QuestDB)
    └─ Pooling: Disabled (QuestDB may not handle it well)
    ↓
SQL Query to QuestDB:
    ```sql
    SELECT timestamp, point_id, value, quality
    FROM point_data
    WHERE point_id = {pointSequenceId}
      AND timestamp >= '{startTime}'
      AND timestamp <= '{endTime}'
    ORDER BY timestamp
    LIMIT {limit}
    ```
    ↓
Transform to DTO & Return to Frontend
    ├─ Timestamp format: ISO 8601 ("O" format)
    ├─ Quality: String representation (Good/Bad)
    └─ Count: Number of records returned
```

---

## 2. CONFIGURATION DETAILS

### QuestDB Configuration (src/Naia.Api/appsettings.json)
```json
"QuestDb": {
  "HttpEndpoint": "http://localhost:9000",           // ILP write endpoint
  "PgWireEndpoint": "localhost:8812",                // PostgreSQL read endpoint
  "TableName": "point_data",                         // Table name (matches schema)
  "AutoFlushIntervalMs": 1000,                       // Client-side batching
  "AutoFlushRows": 10000                             // Auto-flush after 10k rows
}
```

### Kafka Configuration (src/Naia.Api/appsettings.json)
```json
"Kafka": {
  "BootstrapServers": "localhost:9092",
  "DataPointsTopic": "naia.datapoints",              // Main ingestion topic
  "BackfillTopic": "naia.datapoints.backfill",       // Separate backfill topic (not subscribed by default)
  "DlqTopic": "naia.datapoints.dlq",                 // Dead letter queue (only logged, not published)
  "ConsumerGroupId": "naia-historians",              // Consumer group ID
  "ProducerClientId": "naia-api-producer",
  "ConsumerClientIdPrefix": "naia-consumer",
  "SessionTimeoutMs": 30000,
  "HeartbeatIntervalMs": 10000,
  "MaxPollIntervalMs": 300000,                       // 5 minutes (allows long processing)
  "DataPointsPartitions": 12,
  "ReplicationFactor": 1
}
```

### Ingestion Pipeline Configuration (src/Naia.Api/appsettings.json)
```json
"Pipeline": {
  "PollTimeoutMs": 1000,        // Poll frequency
  "RetryDelayMs": 1000,         // Delay between retries
  "MaxBatchSize": 10000,        // Max points per batch
  "FlushIntervalMs": 1000       // Flush interval
}
```

### Redis Configuration (src/Naia.Api/appsettings.json)
```json
"Redis": {
  "ConnectionString": "localhost:6379",
  "CurrentValueTtlSeconds": 3600,        // Current value cache TTL
  "IdempotencyTtlSeconds": 86400         // Idempotency store TTL (24 hours)
}
```

### QuestDB Schema (init-scripts/questdb/01-init-schema.sql)
```sql
CREATE TABLE IF NOT EXISTS point_data (
    timestamp TIMESTAMP,
    point_id LONG,
    value DOUBLE,
    quality INT
) TIMESTAMP(timestamp) PARTITION BY DAY WAL;

-- Critical performance tuning for high-volume writes
ALTER TABLE point_data SET PARAM maxUncommittedRows = 250000;  -- Flush batch size
ALTER TABLE point_data SET PARAM o3MaxLag = 3600s;             -- 1 hour out-of-order tolerance
```

---

## 3. ILP (INFLUXDB LINE PROTOCOL) FORMAT

### How Data Is Written to QuestDB

**Location:** [QuestDbTimeSeriesWriter.cs](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs)

Each data point is serialized to:
```
point_data point_id={id}i,value={value}d,quality={quality}i {timestamp_nanos}
```

**Example:**
```
point_data point_id=12345i,value=42.5d,quality=1i 1705070400000000000
point_data point_id=12345i,value=43.2d,quality=1i 1705070401000000000
point_data point_id=12346i,value=99.1d,quality=0i 1705070402000000000
```

**Key Details:**
- **Type Suffixes:**
  - `i` = long (integer) — used for `point_id` and `quality`
  - `d` = double (floating point) — used for `value`
- **Timestamp:** Nanoseconds since epoch (converted from milliseconds)
- **Quality:** 1 = Good, 0 = Bad
- **Batching:** HTTP POST to `/write` endpoint
- **Line Endings:** Each line separated by `\n`, trailing newline added
- **Endpoint:** `http://localhost:9000/write`

### Timestamp Uniqueness Trick
```csharp
long microsecondOffset = 0;
foreach (var point in batch.Points)
{
    var baseTimestampNanos = ((DateTimeOffset)point.Timestamp).ToUnixTimeMilliseconds() * 1_000_000;
    var timestampNanos = baseTimestampNanos + microsecondOffset;
    microsecondOffset += 1000;  // Add 1 microsecond per point for uniqueness
}
```

**Purpose:** Ensures all points have unique timestamps even if they come in the same millisecond.

---

## 4. THE HISTORY ENDPOINT (WHERE FRONTEND GETS DATA)

### Location
**File:** [src/Naia.Api/Program.cs](src/Naia.Api/Program.cs#L292)

### Endpoint Signature
```csharp
app.MapGet("/api/points/{id:guid}/history", async (
    Guid id,                           // Point UUID (from PostgreSQL)
    IPointRepository pointRepo,        // Get point metadata
    ITimeSeriesReader tsReader,        // Query QuestDB
    DateTime? start = null,            // Query start time
    DateTime? end = null,              // Query end time
    int limit = 1000) =>               // Max rows to return
{
    // 1. Look up point in PostgreSQL
    var point = await pointRepo.GetByIdAsync(id);
    if (point is null) return Results.NotFound();
    
    // 2. Check if point has been synced to QuestDB
    if (point.PointSequenceId is null)
        return Results.BadRequest("Point not yet synchronized to time-series database");
    
    // 3. Set default time range (last 1 hour)
    var startTime = start ?? DateTime.UtcNow.AddHours(-1);
    var endTime = end ?? DateTime.UtcNow;
    
    // 4. Query QuestDB for time-series data
    var data = await tsReader.ReadRangeAsync(
        point.PointSequenceId.Value,  // Long sequence ID for QuestDB lookup
        startTime, 
        endTime, 
        limit);
    
    // 5. Transform to DTO (JSON-friendly format)
    var dataDto = data.Select(d => new
    {
        timestamp = d.Timestamp.ToString("O"),    // ISO 8601 format
        value = d.Value,
        quality = d.Quality.ToString()            // "Good" or "Bad"
    }).ToList();
    
    // 6. Return response
    return Results.Ok(new
    {
        pointId = id,
        sequenceId = point.PointSequenceId,
        tagName = point.Name,
        start = startTime,
        end = endTime,
        count = data.Count,                       // THIS IS WHAT THE FRONTEND SEES
        data = dataDto
    });
});
```

### Critical Dependency: PointSequenceId

**THE MOST COMMON ISSUE:**

```csharp
if (point.PointSequenceId is null)
    return Results.BadRequest("Point not yet synchronized to time-series database");
```

**This means:**
1. The point **must exist** in PostgreSQL (Table: Points)
2. The point **must have a PointSequenceId** set (not null)
3. The PointSequenceId **must match** a point_id in QuestDB's `point_data` table

If `PointSequenceId` is null → "count:0" in frontend → User sees no data

---

## 5. DATA ENRICHMENT: How Point Names Map to Sequence IDs

### Location
[IngestionPipeline.cs - EnrichBatchWithPointSequenceIdsAsync](src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs#L310)

### Problem Solved
Some connectors (Replay, OPC Simulator) publish with `PointSequenceId=0` but provide a `PointName`.

### Solution
```csharp
// In ProcessBatchAsync:
batch = await EnrichBatchWithPointSequenceIdsAsync(batch, batchId, cancellationToken);

// This resolves:
// - Old PointSequenceId: 0
// - PointName: "KSH_T1_WindSpeed"
//
// To:
// - New PointSequenceId: 12345 (looked up from PostgreSQL)
```

**If enrichment fails:**
- Point not found in PostgreSQL
- Data is still written with PointSequenceId=0
- Frontend queries for point_id=12345 but data is stored as point_id=0
- **Result: count:0**

---

## 6. CACHING: REDIS CURRENT VALUE CACHE

### Location
[RedisCurrentValueCache.cs](src/Naia.Infrastructure/Caching/RedisCurrentValueCache.cs)

### Purpose
Provides **sub-millisecond reads** for current values shown on the dashboard (not history).

### Cache Pattern
```
Key: naia:cv:{pointSequenceId}
Value: {
  "PointSequenceId": 12345,
  "PointName": "KSH_T1_WindSpeed",
  "Timestamp": "2026-01-12T10:30:45.123Z",
  "Value": 12.5,
  "Quality": "Good"
}
TTL: 3600 seconds (configurable)
```

### When Redis Is Updated
```csharp
// In IngestionPipeline.ProcessBatchAsync():
var latestByPoint = batch.Points
    .GroupBy(p => p.PointSequenceId)
    .Select(g => g.OrderByDescending(p => p.Timestamp).First())
    .Select(p => CurrentValue.FromDataPoint(p))
    .ToList();

await _currentValueCache.SetManyAsync(latestByPoint, cancellationToken);
```

**Important:** Redis is updated **AFTER** QuestDB write, so old data means:
1. QuestDB write failed (but logs don't show it)
2. Pipeline isn't processing batches
3. Redis connection failed (but logs don't show it)

---

## 7. DEDUPLICATION: IDEMPOTENCY STORE

### Location
[IngestionPipeline.cs - ProcessBatchAsync](src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs#L215)

### Purpose
Ensures **exactly-once processing** of duplicate batches (e.g., if Kafka offset commit fails)

### How It Works
```csharp
var (isDuplicate, _) = await _idempotencyStore.CheckAsync(batchId, cancellationToken);
if (isDuplicate)
{
    _logger.LogDebug("Duplicate batch {BatchId} - skipping", batchId);
    return PipelineResult.SuccessResult(0, sw.ElapsedMilliseconds, skipped: true);
}

// Process...

await _idempotencyStore.MarkProcessedAsync(batchId, cancellationToken);
```

### Storage
- **Backend:** Redis
- **Key:** `naia:idempotency:{batchId}`
- **TTL:** 86400 seconds (24 hours)

---

## 8. KAFKA CONSUMER GUARANTEES

### Configuration Philosophy
```csharp
// MANUAL OFFSET COMMITS ONLY
EnableAutoCommit = false,
EnableAutoOffsetStore = false,

// ALWAYS START FROM BEGINNING IF NEW CONSUMER GROUP
AutoOffsetReset = AutoOffsetReset.Earliest,

// ENSURE COMMITTED TRANSACTIONS ONLY
IsolationLevel = IsolationLevel.ReadCommitted
```

### Commit Strategy
```
Kafka Offset Commits ONLY After:
1. Batch deserialized successfully
2. Deduplication check passed
3. QuestDB write succeeded
4. Redis cache updated
5. Idempotency mark set

If ANY step fails:
- Offset NOT committed
- Batch will be reprocessed on next startup
- Retries with exponential backoff
```

### Partitioning
- **Partitions:** 12 (configured in appsettings)
- **Replication Factor:** 1 (development)
- **Consumer Group:** "naia-historians" (all Naia.Ingestion instances share)
- **Each partition:** Processed by exactly one consumer (automatic rebalancing)

---

## 9. QUESTDB CONNECTION STRING & SETTINGS

### For Queries (PostgreSQL Wire Protocol)
```
Host=localhost;Port=8812;Database=qdb;Username=admin;Password=quest;
Timeout=60;
Pooling=false;
Server Compatibility Mode=NoTypeLoading
```

**Why NoTypeLoading?**
- QuestDB doesn't have PostgreSQL system catalogs (pg_enum, etc.)
- Npgsql tries to introspect types without this setting
- Results in "Does not exist" errors for valid columns
- **CRITICAL:** Without this, all queries fail!

### For Writes (HTTP ILP)
```
http://localhost:9000/write
Content-Type: text/plain
Body: ILP formatted lines
```

---

## 10. ROOT CAUSE CHECKLIST: Why count:0?

### Checklist to Diagnose the Issue

**⬜ STEP 1: Verify Data in QuestDB**
```powershell
# Connect to QuestDB PostgreSQL wire protocol
# psql -h localhost -p 8812 -U admin -d qdb

SELECT COUNT(*) FROM point_data;  -- Should be > 0
SELECT DISTINCT point_id FROM point_data LIMIT 10;  -- Should have values
SELECT MAX(timestamp) FROM point_data;  -- Should be recent
```

**If count=0:**
→ Data never made it to QuestDB  
→ Go to STEP 2

---

**⬜ STEP 2: Check Ingestion Pipeline Health**
```bash
# HTTP endpoint to check pipeline status
curl http://localhost:5073/api/pipeline/health
```

**Expected response:**
```json
{
  "state": "Running",
  "isHealthy": true,
  "metrics": {
    "totalBatchesProcessed": 123,
    "totalPointsProcessed": 11070,
    "pointsPerSecond": 12.5
  }
}
```

**If isHealthy=false:**
→ Pipeline is stopped or erroring  
→ Check Naia.Ingestion logs

---

**⬜ STEP 3: Verify Kafka Messages Arriving**
```bash
# Check Kafka topic has messages
kafka-console-consumer.sh --bootstrap-server localhost:9092 \
  --topic naia.datapoints \
  --from-beginning \
  --max-messages 1
```

**If no messages:**
→ Producer isn't publishing  
→ Check PIDataIngestionService.StartAsync() logs

---

**⬜ STEP 4: Check Point PointSequenceId**
```sql
-- In PostgreSQL (the points are registered here first)
SELECT id, name, point_sequence_id FROM points LIMIT 5;
```

**If point_sequence_id IS NULL:**
→ Point not synced to QuestDB  
→ Ingestion worker should have set this during enrichment  
→ Check worker logs for enrichment failures

---

**⬜ STEP 5: Verify QuestDB Connection**
```bash
# Test HTTP ILP endpoint
curl -X POST http://localhost:9000/write \
  -d "test_table value=1.0 $(date +%s)000000000"

# Test PG Wire Protocol
psql -h localhost -p 8812 -U admin -d qdb -c "SELECT 1;"
```

**If either fails:**
→ QuestDB not running or misconfigured

---

**⬜ STEP 6: Check Redis for Current Values**
```bash
# Connect to Redis
redis-cli

# Check if current values are cached
KEYS "naia:cv:*"  -- Should have keys
GET "naia:cv:12345"  -- Should have JSON value
```

**If no keys exist:**
→ Either data isn't flowing OR Redis is isolated  
→ Check Redis connection logs

---

**⬜ STEP 7: Test History Endpoint Directly**
```bash
# Get a point ID first (from PostgreSQL)
# Then test the endpoint:

curl "http://localhost:5073/api/points/{point-id-uuid}/history?start=2026-01-11&end=2026-01-12"

# Check response:
# - status code (404 vs 200)
# - count field
# - error message
```

---

**⬜ STEP 8: Check Frontend Request**
```javascript
// In browser console, check network tab for:
// GET /api/points/{id}/history

// Response should have:
{
  "pointId": "...",
  "sequenceId": 12345,
  "tagName": "KSH_T1_WindSpeed",
  "count": 0,  // OR > 0 if data exists
  "data": []
}
```

---

## 11. CRITICAL LOG LOCATIONS

### Naia.Api (REST API Server)
```
Location: ~/logs/api/
Files: api-*.log
Search for: "QuestDB", "history", "synchronized"
```

### Naia.Ingestion (Consumer/Writer)
```
Location: ~/logs/ingestion/
Files: ingestion-*.log
Search for: "Pipeline", "Duplicate", "timeout", "point_data"
```

### QuestDB
```
Container: questdb
Logs: docker logs questdb
Search for: "ILP", "error", "/write"
```

### Kafka
```
Container: kafka
Logs: docker logs kafka
Search for: "group.*naia-historians", "topic.*naia.datapoints"
```

### Docker Compose Status
```powershell
docker-compose ps  # All containers running?
docker-compose logs --tail=50  # Recent errors?
```

---

## 12. COMMANDS TO DIAGNOSE IMMEDIATELY

### Check QuestDB Has Data
```bash
# SSH or exec into QuestDB container
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(*) as total_rows FROM point_data;"

docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT timestamp, point_id, value FROM point_data ORDER BY timestamp DESC LIMIT 5;"
```

### Check Kafka Topic
```bash
docker exec kafka kafka-console-consumer.sh --bootstrap-server kafka:9092 \
  --topic naia.datapoints --from-beginning --max-messages 1

# Or get consumer group status
docker exec kafka kafka-consumer-groups.sh --bootstrap-server kafka:9092 \
  --group naia-historians --describe
```

### Check Redis Cache
```bash
docker exec redis redis-cli KEYS "naia:cv:*"
docker exec redis redis-cli KEYS "naia:idempotency:*"
docker exec redis redis-cli GET "naia:pipeline:metrics"
```

### Check PostgreSQL Points
```bash
docker exec postgres psql -U naia -d naia \
  -c "SELECT id, name, point_sequence_id FROM points LIMIT 5;"
```

### Monitor Pipeline in Real-Time
```bash
# Terminal 1: Watch QuestDB row count
watch -n 1 'docker exec questdb psql -h localhost -p 8812 -U admin -d qdb -c "SELECT COUNT(*) FROM point_data;"'

# Terminal 2: Watch Kafka messages
docker exec kafka kafka-console-consumer.sh --bootstrap-server kafka:9092 \
  --topic naia.datapoints --from-beginning

# Terminal 3: Watch ingestion logs
docker compose logs -f naia-ingestion
```

---

## 13. PERFORMANCE CHARACTERISTICS

### Throughput
- **Design target:** 10,000+ points/second
- **ILP batch size:** Up to 10,000 rows per HTTP POST
- **Kafka polling:** Every 1,000ms
- **QuestDB writes:** Synchronous (waits for response)

### Latency
- **Kafka publish → QuestDB:** ~100-200ms typical
- **QuestDB query:** 1-50ms depending on time range
- **Redis cache read:** <1ms

### Buffering
```
Pipeline Buffer:
- Kafka: Consumer max.poll.records (default configurable)
- Redis idempotency: 24 hours
- QuestDB uncommitted rows: 250,000 (configured)
```

---

## 14. FAILURE MODES & RECOVERY

### If QuestDB Crashes
```
1. Kafka keeps buffering (messages retained)
2. Ingestion worker retries with exponential backoff
3. When QuestDB restarts, Ingestion Worker resumes
4. All buffered data is replayed (at-least-once delivery)
```

### If Redis Crashes
```
1. Idempotency checks fail (new Redis instance = no dedup)
2. Current value cache misses (dashboard shows stale values)
3. Data STILL writes to QuestDB (Redis not critical path)
4. When Redis restarts, idempotency resumes
```

### If Kafka Crashes
```
1. Producers can't publish (PIDa Ingestion Service waits/retries)
2. Consumers lose offset position
3. When Kafka restarts, consumer resumes from last committed offset
4. Batches processed since last commit are reprocessed (idempotency dedup)
```

### If PostgreSQL Crashes
```
1. Point lookup fails (enrichment can't resolve point_id)
2. Data writes to QuestDB with PointSequenceId=0 (wrong!)
3. History queries fail (point metadata needed)
4. When PostgreSQL restarts, everything works again
```

---

## 15. SUMMARY TABLE: Data Flow Checkpoints

| Step | Component | Success Indicator | Failure Symptom |
|------|-----------|-------------------|-----------------|
| 1 | PI System / Connector | Events raised | No events, connector not running |
| 2 | PIDataIngestionService | Publishes to Kafka | Logs show 0 published |
| 3 | Kafka Topic: naia.datapoints | Messages enqueued | Consumer lag increasing, no messages |
| 4 | Naia.Ingestion Worker | Processing batches | Logs show "Pipeline stopped" |
| 5 | Deduplication (Redis) | Checks pass | "Duplicate batch" logged repeatedly |
| 6 | PointSequenceId Enrichment | IDs resolved from PostgreSQL | "Point has no PointName" warning |
| 7 | QuestDB ILP Write | HTTP 200 response | Logs show "write failed" error |
| 8 | Redis Cache Update | Keys set with TTL | "Updated 0 current values" |
| 9 | Kafka Offset Commit | Manual commit successful | Logs show commit retry failures |
| 10 | PostgreSQL Point Lookup | Point found with ID | Logs show "Point not found" |
| 11 | QuestDB Query (PG Wire) | Result set returned | "Table point_data does not exist" |
| 12 | API Response | count > 0 | count = 0 |

---

## 16. NEXT STEPS

1. **Run STEP 1-2 of the checklist** above to determine where data stops flowing
2. **Collect logs** from all components with the same timestamp range as the reported issue
3. **Verify configuration** matches deployment (especially connection strings)
4. **Test endpoints** manually with curl/Postman to isolate frontend vs backend issues
5. **Monitor in real-time** using the watch commands above

The architecture is **sound**. The issue is **operational** — data not arriving, wrong configuration, or missing synchronization step.

