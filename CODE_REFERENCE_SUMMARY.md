# Quick Code Lookup - ILP & Ingestion Flow

## 1. QuestDB ILP Write Function
**File:** [src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs)

| What | Line | Code |
|------|------|------|
| Class definition | L24 | `public sealed class QuestDbTimeSeriesWriter : ITimeSeriesWriter` |
| HTTP endpoint config | L45 | `BaseAddress = new Uri(_options.HttpEndpoint)` |
| ILP format comment | L21-22 | `// table_name,tag1=value1 field1=value1,field2=value2 timestamp_nanos` |
| Build ILP lines | L62-85 | Loop that creates `point_data point_id=...i,value=...d,quality=...i timestamp` format |
| POST to /write | L92 | `var response = await _httpClient.PostAsync("/write", content, cancellationToken);` |
| Error handling | L94-99 | Returns error if response not success |

---

## 2. Kafka Consumer
**File:** [src/Naia.Infrastructure/Messaging/KafkaDataPointConsumer.cs](src/Naia.Infrastructure/Messaging/KafkaDataPointConsumer.cs)

| What | Line | Code |
|------|------|------|
| Class definition | L24 | `public sealed class KafkaDataPointConsumer : IDataPointConsumer` |
| Manual commits | L54 | `EnableAutoCommit = false` |
| Topic subscription | L117 | `_consumer.Subscribe(topics)` where topics = `["naia.datapoints"]` |
| Consume method | L127-179 | `ConsumeAsync()` - gets messages from Kafka |
| Commit method | L181-204 | `CommitAsync()` - manually commits offset after processing |
| Consumer group | L50 | `GroupId = _options.ConsumerGroupId` |

---

## 3. Ingestion Pipeline (The Orchestrator)
**File:** [src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs](src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs)

| What | Line | Code |
|------|------|------|
| Class definition | L25 | `public sealed class IngestionPipeline : IIngestionPipeline` |
| Main process loop | L145-195 | `ProcessLoopAsync()` - continuously consumes & processes |
| Single batch processing | L207-270 | `ProcessBatchAsync()` - orchestrates: dedupe → **QUESTDB WRITE** → redis → commit |
| **ILP Write Call** | **L237** | `await _timeSeriesWriter.WriteAsync(batch, cancellationToken);` |
| Error classification | L259-270 | `IsTransientError()` - determines retry vs DLQ |

---

## 4. Dependency Injection Wiring
**File:** [src/Naia.Infrastructure/DependencyInjection.cs](src/Naia.Infrastructure/DependencyInjection.cs)

| What | Line | Code |
|------|------|------|
| QuestDB writer registration | L106 | `services.AddSingleton<ITimeSeriesWriter, QuestDbTimeSeriesWriter>();` |
| Kafka consumer registration | L154-158 | Singleton registration of KafkaDataPointConsumer |
| QuestDbOptions class | L177-193 | Configuration POCO with `HttpEndpoint`, `PgWireEndpoint` |

---

## 5. Worker Entry Point
**File:** [src/Naia.Ingestion/Worker.cs](src/Naia.Ingestion/Worker.cs)

| What | Line | Code |
|------|------|------|
| Background service | L23 | `public class Worker : BackgroundService` |
| Execution start | L42-60 | Gets pipeline from DI and starts it |
| Pipeline start | L50 | `await pipeline.StartAsync(stoppingToken);` |

---

## 6. Configuration
**File:** [src/Naia.Api/appsettings.json](src/Naia.Api/appsettings.json)

```json
"QuestDb": {
  "HttpEndpoint": "http://localhost:9000",      // ILP WRITE endpoint
  "PgWireEndpoint": "localhost:8812",           // Query endpoint
  "TableName": "point_data",
  "AutoFlushIntervalMs": 1000,
  "AutoFlushRows": 10000
}
```

---

## Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│ NAIA INGESTION FLOW - ILP WRITE PATH                            │
└─────────────────────────────────────────────────────────────────┘

Kafka Topic: naia.datapoints
  │
  ├─ KafkaDataPointConsumer.ConsumeAsync()  [L127]
  │  └─ Receives DataPointBatch from Kafka
  │
  ├─ IngestionPipeline.ProcessLoopAsync()  [L145]
  │  └─ Continuously polls for messages
  │
  ├─ IngestionPipeline.ProcessBatchAsync()  [L207]
  │  ├─ Check if duplicate (Redis)  [L217]
  │  ├─ Enrich with SequenceIds  [L230]
  │  │
  │  ├─► QUESTDB ILP WRITE  [L237]
  │  │   QuestDbTimeSeriesWriter.WriteAsync()
  │  │   └─ POST http://localhost:9000/write
  │  │      ├─ Format: ILP protocol [L79]
  │  │      ├─ point_data point_id=123i,value=45.6d,quality=1i timestamp_nanos
  │  │      └─ HTTP response: 200 OK = success
  │  │
  │  ├─ Update Redis cache  [L239]
  │  └─ Mark as processed (idempotency)  [L249]
  │
  ├─ KafkaDataPointConsumer.CommitAsync()  [L181]
  │  └─ ONLY committed after successful QuestDB write
  │
  ✅ Next message
```

---

## Key Configuration Values

| Component | Config Key | Default | Environment |
|-----------|-----------|---------|-------------|
| QuestDB Write | `QuestDb:HttpEndpoint` | `http://localhost:9000` | HTTP ILP |
| QuestDB Read | `QuestDb:PgWireEndpoint` | `localhost:8812` | PostgreSQL Wire |
| QuestDB Table | `QuestDb:TableName` | `point_data` | Table name |
| Kafka Broker | `Kafka:BootstrapServers` | `localhost:9092` | Broker address |
| Kafka Topic | `KafkaTopic` | `naia.datapoints` | Data points topic |
| Consumer Group | `Kafka:ConsumerGroupId` | `naia-ingestion-group` | Consumer group |

---

## Critical Code Sections

### 1. ILP Line Building (L62-85)
```csharp
var line = $"{_options.TableName} point_id={point.PointSequenceId}i,value={point.Value}d,quality={qualityInt}i {timestampNanos}";
```
This creates the exact ILP format that QuestDB expects.

### 2. HTTP POST to /write Endpoint (L92)
```csharp
var response = await _httpClient.PostAsync("/write", content, cancellationToken);
```
**This is the ILP protocol endpoint**, not REST API.

### 3. Kafka Manual Commit (L189-200)
```csharp
var offsets = new[] {
    new TopicPartitionOffset(
        context.Topic,
        new Partition(context.Partition),
        new Offset(context.Offset + 1))
};
_consumer.Commit(offsets);
```
**Critical:** Only commits AFTER QuestDB write succeeds.

### 4. No Disabled Code
Searched entire codebase for commented-out QuestDB writes: **ZERO FOUND**
- No `//` prefix on the WriteAsync call
- No `#if DEBUG` compilation directives
- No feature flags checking `ILP_ENABLED`
- All code is active in Release builds

---

## Debugging Checklist

- [ ] Verify `QuestDb:HttpEndpoint` = `http://localhost:9000` in appsettings
- [ ] Verify `Kafka:BootstrapServers` = `localhost:9092` in appsettings
- [ ] Check Kafka topic exists: `docker exec kafka kafka-topics.sh --list`
- [ ] Check QuestDB running: `curl http://localhost:9000/`
- [ ] Check logs in Worker: grep for "Writing {Lines} lines to QuestDB"
- [ ] Check Redis for dedup keys: `redis-cli KEYS "naia:batch:*"`
- [ ] Verify point_data table in QuestDB: `SELECT COUNT(*) FROM point_data;`
