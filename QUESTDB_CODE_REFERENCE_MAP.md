# NAIA Data Flow - Code Reference Map

Visual guide to trace data through the codebase.

---

## COMPLETE FLOW DIAGRAM

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        INGESTION SIDE (Data Arrives)                     │
└─────────────────────────────────────────────────────────────────────────┘

PI System / Weather API / OPC / Replay
        ↓
        ├─→ PIWebApiConnector.cs ─────────┐
        ├─→ WeatherApiConnector.cs ────────┤
        ├─→ OpcSimulatorConnector.cs ──────┼─→ PointValue (current reading)
        ├─→ WindFarmReplayWorker.cs ───────┤
        └─→ CustomConnector.cs ────────────┘
        
        ↓
PIDataIngestionService (PUBLISHER)
        ├─ File: src/Naia.Api/Services/PIDataIngestionService.cs
        ├─ Method: PublishBatchAsync()
        ├─ Creates: DataPointBatch with points
        ├─ Serializes: To JSON
        └─→ Kafka
        
        ↓ Via KafkaDataPointProducer.cs
        
Topic: naia.datapoints
├─ Configured in: src/Naia.Api/appsettings.json
│   "Kafka": {
│     "BootstrapServers": "localhost:9092",
│     "DataPointsTopic": "naia.datapoints",
│     "DataPointsPartitions": 12,
│     "ReplicationFactor": 1
│   }
├─ Messages: JSON-serialized DataPointBatch objects
└─ Retention: Configurable (default: 168 hours)

        ↓ Consumed by Naia.Ingestion service
        
Naia.Ingestion Worker (CONSUMER)
├─ File: src/Naia.Ingestion/Worker.cs
├─ Runs: ExecuteAsync()
├─ Creates: IIngestionPipeline instance
└─→ Pipeline.StartAsync()

        ↓
IngestionPipeline.ProcessLoopAsync()
├─ File: src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs
├─ Runs: While loop, polls every ~1 second
├─ Steps:
│   1. Consumer.ConsumeAsync() ─→ KafkaDataPointConsumer.cs
│   2. Check: context.IsSuccess?
│   3. ProcessBatchAsync(batch) ──┐
│   └─ If successful:
│       ├─ Consumer.CommitAsync() (offset commit)
│       ├─ Metrics.RecordSuccess()
│       └─ PublishMetricsToRedisAsync()

        ↓
ProcessBatchAsync() - CORE PROCESSING
├─ Location: IngestionPipeline.cs#ProcessBatchAsync
├─
│  STEP 1: Deduplication
│  ├─ File: IngestionPipeline.cs#215
│  ├─ Code: idempotencyStore.CheckAsync(batchId)
│  ├─ Backend: Redis key "naia:idempotency:{batchId}"
│  ├─ TTL: 86400 seconds (24 hours)
│  └─ If duplicate: Return success with 0 points processed
│
│  STEP 2: Point Enrichment (PointSequenceId resolution)
│  ├─ File: IngestionPipeline.cs#EnrichBatchWithPointSequenceIdsAsync
│  ├─ Problem: Some connectors publish with PointSequenceId=0
│  ├─ Solution: Lookup PointName in PostgreSQL via IPointLookupService
│  ├─ Code:
│  │   ```csharp
│  │   var lookup = await _pointLookup.GetByNameAsync(point.PointName);
│  │   if (lookup != null) {
│  │       point.PointSequenceId = lookup.SequenceId;  // Resolved!
│  │   }
│  │   ```
│  └─ CRITICAL: If PointName is null, enrichment skipped
│
│  STEP 3: Write to QuestDB
│  ├─ File: QuestDbTimeSeriesWriter.cs#WriteAsync
│  ├─ Protocol: HTTP ILP (InfluxDB Line Protocol)
│  ├─ Endpoint: http://localhost:9000/write
│  ├─ Format: ILP lines (one per point)
│  │   Example: "point_data point_id=12345i,value=42.5d,quality=1i 1705070400000000000"
│  ├─ Code:
│  │   ```csharp
│  │   var ilpContent = string.Join("\n", linesList);
│  │   var content = new StringContent(ilpContent, Encoding.UTF8, "text/plain");
│  │   var response = await _httpClient.PostAsync("/write", content);
│  │   if (!response.IsSuccessStatusCode) throw new InvalidOperationException(...);
│  │   ```
│  └─ Storage: Written to "point_data" table
│
│  STEP 4: Update Current Values Cache
│  ├─ File: IngestionPipeline.cs#230
│  ├─ Code: currentValueCache.SetManyAsync(latestByPoint)
│  ├─ Backend: Redis
│  ├─ Keys: "naia:cv:{pointSequenceId}"
│  ├─ Value: JSON-serialized CurrentValue
│  ├─ TTL: 3600 seconds (configurable)
│  └─ Purpose: Fast current value reads for dashboard
│
│  STEP 5: Mark As Processed
│  ├─ File: IngestionPipeline.cs#240
│  ├─ Code: idempotencyStore.MarkProcessedAsync(batchId)
│  ├─ Backend: Redis
│  └─ Purpose: Prevent reprocessing if offset commit fails
│
│  STEP 6: Return Success
│  └─ Only if all steps succeed, returns PipelineResult.SuccessResult()
│
└─ Then: ProcessLoopAsync commits Kafka offset ONLY IF ProcessBatchAsync succeeded

        ↓ Data now in:
        ├─ QuestDB: point_data table (persistent storage)
        ├─ Redis: naia:cv:{pointSequenceId} (current values)
        └─ Redis: naia:idempotency:{batchId} (dedup tracking)


┌─────────────────────────────────────────────────────────────────────────┐
│                          QUERY SIDE (Data Leaves)                        │
└─────────────────────────────────────────────────────────────────────────┘

Frontend Request
└─→ GET /api/points/{id:guid}/history?start=&end=&limit=

        ↓
Handler Code
├─ File: src/Naia.Api/Program.cs#292
├─ Signature:
│   ```csharp
│   app.MapGet("/api/points/{id:guid}/history", async (
│       Guid id,
│       IPointRepository pointRepo,
│       ITimeSeriesReader tsReader,
│       DateTime? start = null,
│       DateTime? end = null,
│       int limit = 1000) =>
│   ```
├─
│  STEP 1: Look up Point in PostgreSQL
│  ├─ Code: pointRepo.GetByIdAsync(id)
│  ├─ Table: Points (in naia database)
│  ├─ Returns: Point entity with:
│  │   - id (UUID)
│  │   - name (point name)
│  │   - point_sequence_id (LONG - the key to QuestDB)
│  │
│  └─ CRITICAL CHECK:
│       ```csharp
│       if (point is null) return Results.NotFound();
│       if (point.PointSequenceId is null)
│           return Results.BadRequest("Point not yet synchronized");
│       ```
│
│  STEP 2: Set Query Defaults
│  ├─ Start: end ?? DateTime.UtcNow.AddHours(-1) (last 1 hour)
│  ├─ End: end ?? DateTime.UtcNow (now)
│  └─ Limit: limit ?? 1000 (max rows)
│
│  STEP 3: Query QuestDB via PostgreSQL Wire Protocol
│  ├─ Code: tsReader.ReadRangeAsync(point.PointSequenceId, start, end, limit)
│  ├─ Implementation: QuestDbTimeSeriesReader.cs
│  ├─
│  │  CONNECTION
│  │  ├─ Driver: Npgsql (PostgreSQL .NET driver)
│  │  ├─ Host: localhost (from QuestDb:PgWireEndpoint config)
│  │  ├─ Port: 8812
│  │  ├─ Database: qdb
│  │  ├─ Username: admin
│  │  ├─ Password: quest
│  │  ├─
│  │  └─ CRITICAL SETTING: Server Compatibility Mode=NoTypeLoading
│  │      └─ Reason: QuestDB doesn't have pg_enum, Npgsql tries to introspect
│  │                 Without this setting: "Type does not exist" errors
│  │
│  │  SQL QUERY
│  │  ├─ Code:
│  │  │   ```csharp
│  │  │   var sql = $@"
│  │  │       SELECT timestamp, point_id, value, quality
│  │  │       FROM {_options.TableName}
│  │  │       WHERE point_id = {pointSequenceId}
│  │  │         AND timestamp >= '{startTime:yyyy-MM-ddTHH:mm:ss.ffffffZ}'
│  │  │         AND timestamp <= '{endTime:yyyy-MM-ddTHH:mm:ss.ffffffZ}'
│  │  │       ORDER BY timestamp
│  │  │       LIMIT {limit}";
│  │  │   ```
│  │  └─ Note: String interpolation, not parameterized (QuestDB limitation)
│  │
│  └─ Result: List<DataPoint> with all matching rows
│
│  STEP 4: Transform to DTO
│  ├─ Code:
│  │   ```csharp
│  │   var dataDto = data.Select(d => new {
│  │       timestamp = d.Timestamp.ToString("O"),    // ISO 8601
│  │       value = d.Value,
│  │       quality = d.Quality.ToString()            // "Good" or "Bad"
│  │   }).ToList();
│  │   ```
│  └─ Purpose: JSON-friendly format for frontend
│
│  STEP 5: Return Response
│  └─ Code:
│      ```csharp
│      return Results.Ok(new {
│          pointId = id,
│          sequenceId = point.PointSequenceId,
│          tagName = point.Name,
│          start = startTime,
│          end = endTime,
│          count = data.Count,  ← THIS IS WHAT FRONTEND SEES
│          data = dataDto
│      });
│      ```
│
└─ → JSON Response to Frontend

        ↓
Frontend Display
├─ Trends Page shows: count field
├─ Data Array shown: data field
└─ If count=0 & data=[], user sees "No data available"


┌─────────────────────────────────────────────────────────────────────────┐
│                            CACHING LAYER                                 │
└─────────────────────────────────────────────────────────────────────────┘

Current Value Cache (for Dashboard)
├─ Not for history endpoint
├─ Used for: "Latest reading" on dashboard
├─
├─ Updated in: IngestionPipeline.ProcessBatchAsync() STEP 4
├─
├─ Access pattern:
│   ├─ File: RedisCurrentValueCache.cs
│   ├─ Get: GetAsync(pointSequenceId)
│   │   └─ Code: db.StringGetAsync($"naia:cv:{pointSequenceId}")
│   │   └─ Return: Deserialized CurrentValue object
│   │
│   └─ Set: SetManyAsync(values)
│       └─ Code: db.StringSetAsync(key, json, ttl)
│
├─ Key pattern: "naia:cv:{pointSequenceId}"
├─ Value: JSON string
│   ```json
│   {
│     "PointSequenceId": 12345,
│     "PointName": "KSH_T1_WindSpeed",
│     "Timestamp": "2026-01-12T10:30:45.123Z",
│     "Value": 42.5,
│     "Quality": "Good"
│   }
│   ```
├─ TTL: 3600 seconds (1 hour, configurable in appsettings)
├─ Used by: /api/points/{id}/current (not the history endpoint)
└─ If stale: Old values show until next pipeline batch updates


┌─────────────────────────────────────────────────────────────────────────┐
│                         DEDUPLICATION LAYER                              │
└─────────────────────────────────────────────────────────────────────────┘

Idempotency Store (Redis)
├─ Purpose: Prevent reprocessing of duplicate batches
├─ Scenario:
│   1. Kafka delivers batch A
│   2. Pipeline processes & writes to QuestDB ✓
│   3. API crashes before committing offset
│   4. On restart: Kafka redelivers batch A
│   5. Pipeline checks: "Have I seen this batch before?"
│   6. If yes: Skip processing (return success with 0 points)
│
├─ Implementation: IngestionPipeline.cs
│   ```csharp
│   var (isDuplicate, _) = await _idempotencyStore.CheckAsync(batchId);
│   if (isDuplicate) {
│       _logger.LogDebug("Duplicate batch {BatchId} - skipping", batchId);
│       return PipelineResult.SuccessResult(0, ...);
│   }
│   ```
├─
├─ After processing:
│   ```csharp
│   await _idempotencyStore.MarkProcessedAsync(batchId);
│   ```
│
├─ Backend: Redis
├─ Key: "naia:idempotency:{batchId}"
├─ Value: Timestamp when processed (could be anything)
├─ TTL: 86400 seconds (24 hours)
│   └─ Reason: Batches won't reappear after 24 hours
│
└─ Guarantees: EXACTLY-ONCE processing (not just at-least-once)


┌─────────────────────────────────────────────────────────────────────────┐
│                      ERROR HANDLING & RETRY LOGIC                        │
└─────────────────────────────────────────────────────────────────────────┘

In ProcessBatchAsync (IngestionPipeline.cs)

catch (TimeoutException ex)
├─ Classified as: RETRYABLE
├─ Action: Log warning, don't commit offset
├─ Result: Batch reprocessed on next poll
└─ Reason: Transient network issue

catch (System.Net.Http.HttpRequestException ex)
├─ Classified as: RETRYABLE
├─ Action: Log warning, wait 1 second, retry
├─ Result: Batch reprocessed
└─ Reason: QuestDB temporarily down

catch (Exception ex) where (!IsTransientError)
├─ Classified as: NON-RETRYABLE
├─ Action: Send to DLQ (logged only), commit offset
├─ Result: Batch dropped (but logged)
└─ Reason: Data format error, invalid values, etc.

IsTransientError() Check (IngestionPipeline.cs#259)
├─ Returns true if:
│   ├─ ex is TimeoutException
│   ├─ ex is SocketException
│   ├─ ex is HttpRequestException
│   └─ ex.Message contains "timeout" or "connection"
├─
└─ Returns false otherwise (assumed permanent error)


┌─────────────────────────────────────────────────────────────────────────┐
│                       CONFIGURATION SUMMARY                              │
└─────────────────────────────────────────────────────────────────────────┘

src/Naia.Api/appsettings.json
├─ QuestDb section:
│   {
│     "HttpEndpoint": "http://localhost:9000",
│     "PgWireEndpoint": "localhost:8812",
│     "TableName": "point_data",
│     "AutoFlushIntervalMs": 1000,
│     "AutoFlushRows": 10000
│   }
│
├─ Kafka section:
│   {
│     "BootstrapServers": "localhost:9092",
│     "DataPointsTopic": "naia.datapoints",
│     "BackfillTopic": "naia.datapoints.backfill",
│     "DlqTopic": "naia.datapoints.dlq",
│     "ConsumerGroupId": "naia-historians",
│     "DataPointsPartitions": 12,
│     "ReplicationFactor": 1
│   }
│
├─ Redis section:
│   {
│     "ConnectionString": "localhost:6379",
│     "CurrentValueTtlSeconds": 3600,
│     "IdempotencyTtlSeconds": 86400
│   }
│
└─ Pipeline section:
    {
      "PollTimeoutMs": 1000,
      "RetryDelayMs": 1000,
      "MaxBatchSize": 10000,
      "FlushIntervalMs": 1000
    }


┌─────────────────────────────────────────────────────────────────────────┐
│                         HEALTH CHECK ENDPOINTS                           │
└─────────────────────────────────────────────────────────────────────────┘

GET /api/pipeline/health
├─ Location: Program.cs#341
├─ Returns:
│   {
│     "state": "Running",
│     "isHealthy": true,
│     "metrics": {
│       "totalBatchesProcessed": 123,
│       "totalPointsProcessed": 11070,
│       "pointsPerSecond": 12.5,
│       "averageProcessingMs": 45.2
│     },
│     "lastProcessedAt": "2026-01-12T10:30:45Z",
│     "errorMessage": null
│   }
│
└─ Source: IIngestionPipeline.GetHealthAsync()

GET /api/pipeline/metrics
├─ Location: Program.cs#364
├─ Returns:
│   {
│     "isRunning": true,
│     "pointsPerSecond": 12.5,
│     "totalPointsIngested": 11070,
│     "batchesProcessed": 123,
│     "errors": 0,
│     "lastUpdateTime": "2026-01-12T10:30:45Z",
│     "latestDataTimestamp": "2026-01-12T10:30:00Z"
│   }
│
├─ Source: Direct query to QuestDB
├─ Code: Counts rows in point_data, calculates rates
└─ Note: Queries QuestDB directly (not cached)


┌─────────────────────────────────────────────────────────────────────────┐
│                      SCHEMA DEFINITION                                   │
└─────────────────────────────────────────────────────────────────────────┘

File: init-scripts/questdb/01-init-schema.sql

CREATE TABLE IF NOT EXISTS point_data (
    timestamp TIMESTAMP,
    point_id LONG,
    value DOUBLE,
    quality INT
) TIMESTAMP(timestamp) PARTITION BY DAY WAL;

ALTER TABLE point_data SET PARAM maxUncommittedRows = 250000;
ALTER TABLE point_data SET PARAM o3MaxLag = 3600s;

├─ Columns:
│   ├─ timestamp: When the reading was taken (indexed by partition)
│   ├─ point_id: LONG reference to Points.point_sequence_id (NOT UUID!)
│   ├─ value: DOUBLE floating point value
│   └─ quality: INT (1 for Good, 0 for Bad)
│
├─ Partitioning: By DAY
│   └─ Reason: Fast deletion of old data, better query performance
│
├─ WAL: Write-Ahead Log enabled
│   └─ Reason: Durability even on crash
│
├─ maxUncommittedRows: 250,000
│   └─ Reason: Batch size before QuestDB auto-commits
│
└─ o3MaxLag: 3600s (1 hour)
    └─ Reason: Allows late-arriving data within 1 hour


┌─────────────────────────────────────────────────────────────────────────┐
│                        KEY DEPENDENCIES                                  │
└─────────────────────────────────────────────────────────────────────────┘

NuGet Packages Used:
├─ Confluent.Kafka: Kafka client
├─ Npgsql: PostgreSQL/QuestDB driver (PG Wire Protocol)
├─ StackExchange.Redis: Redis client
├─ Entity Framework Core: PostgreSQL ORM
├─ Serilog: Structured logging
└─ System.Text.Json: JSON serialization

Database Requirements:
├─ PostgreSQL 12+: Stores point metadata
├─ Redis 6.0+: Idempotency & caching
├─ QuestDB 7.0+: Time-series storage
└─ Kafka 2.8+: Message backbone

Environment Assumptions:
├─ Docker: All services in containers
├─ Port 5073: API service (HTTP)
├─ Port 5074: Ingestion service (gRPC, internal only)
├─ localhost: All services on same machine (for now)
└─ Network: Services can reach each other

---

## Code Navigation Quick Reference

| Purpose | File | Method |
|---------|------|--------|
| **Publish to Kafka** | PIDataIngestionService.cs | PublishBatchAsync() |
| **Consume from Kafka** | Worker.cs | ExecuteAsync() |
| **Process batch** | IngestionPipeline.cs | ProcessBatchAsync() |
| **Dedup check** | IngestionPipeline.cs | ProcessBatchAsync() #215 |
| **Enrich point IDs** | IngestionPipeline.cs | EnrichBatchWithPointSequenceIdsAsync() |
| **Write to QuestDB** | QuestDbTimeSeriesWriter.cs | WriteAsync() |
| **Update cache** | RedisCurrentValueCache.cs | SetManyAsync() |
| **Query history** | Program.cs | MapGet("/api/points/.../history") |
| **Read from QuestDB** | QuestDbTimeSeriesReader.cs | ReadRangeAsync() |
| **Health check** | IIngestionPipeline | GetHealthAsync() |

