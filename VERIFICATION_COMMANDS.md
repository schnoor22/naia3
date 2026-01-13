# Verification Commands - How to Validate All Findings

## 1. Verify ILP is Enabled (Search for Disabled/Commented Code)

### Search 1: Find all QuestDB write calls
```bash
grep -r "WriteAsync" src/ --include="*.cs" | grep -i questdb
```
**Expected:** Should find 1-2 calls, NONE commented out
```
src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs:237:
  await _timeSeriesWriter.WriteAsync(batch, cancellationToken);
```

### Search 2: Check for disabled ILP feature flags
```bash
grep -r "ILP.*enabled\|ILP.*Enabled\|ilp.*disabled" src/ --include="*.cs" -i
grep -r "#if.*ILP\|#if.*QUESTDB" src/ --include="*.cs" -i
grep -r "// await.*WriteAsync\|/\* await.*WriteAsync" src/ --include="*.cs"
```
**Expected:** Zero matches (no feature flags, no commented writes)

### Search 3: Verify single implementation
```bash
grep -r "ITimeSeriesWriter" src/ --include="*.cs" | grep "class\|="
```
**Expected:**
```
QuestDbTimeSeriesWriter.cs:24: public sealed class QuestDbTimeSeriesWriter : ITimeSeriesWriter
DependencyInjection.cs:106: services.AddSingleton<ITimeSeriesWriter, QuestDbTimeSeriesWriter>();
```

---

## 2. Verify QuestDB Connection String

### Search 1: Find HttpEndpoint configuration
```bash
grep -r "HttpEndpoint\|9000" src/ --include="*.json"
```
**Expected:**
```
src/Naia.Api/appsettings.json:22: "HttpEndpoint": "http://localhost:9000",
```

### Search 2: Verify configuration class
```bash
grep -A5 "public.*HttpEndpoint" src/ --include="*.cs"
```
**Expected:**
```
public string HttpEndpoint { get; set; } = "http://localhost:9000";
```

---

## 3. Verify ILP HTTP Protocol (Not REST API)

### Search 1: Find POST endpoint
```bash
grep -r "PostAsync.*write\|/write" src/ --include="*.cs"
```
**Expected:**
```
src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs:92:
  var response = await _httpClient.PostAsync("/write", content, cancellationToken);
```
**Verify:** `/write` is ILP endpoint, NOT `/json` (REST API)

### Search 2: Check for string content type
```bash
grep -r "text/plain" src/ --include="*.cs"
```
**Expected:**
```
src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs:92:
  var content = new StringContent(ilpContent, Encoding.UTF8, "text/plain");
```
**Verify:** `text/plain` confirms ILP (not `application/json`)

### Search 3: Find ILP format generation
```bash
grep -r "point_id=.*value=.*quality=" src/ --include="*.cs"
```
**Expected:**
```
var line = $"{_options.TableName} point_id={point.PointSequenceId}i,value={point.Value}d,quality={qualityInt}i {timestampNanos}";
```

---

## 4. Verify Kafka Consumer Configuration

### Search 1: Find topic subscription
```bash
grep -r "naia.datapoints" src/ --include="*.cs" --include="*.json"
```
**Expected:** Multiple matches for topic configuration

### Search 2: Verify manual offset commits
```bash
grep -r "EnableAutoCommit.*false" src/ --include="*.cs"
```
**Expected:**
```
src/Naia.Infrastructure/Messaging/KafkaDataPointConsumer.cs:54:
  EnableAutoCommit = false,
```

### Search 3: Find CommitAsync implementation
```bash
grep -r "CommitAsync\|_consumer.Commit" src/ --include="*.cs" -A2
```
**Expected:** Manual offset commits via `_consumer.Commit(offsets);`

### Search 4: Verify ReadCommitted isolation
```bash
grep -r "IsolationLevel.*ReadCommitted" src/ --include="*.cs"
```
**Expected:**
```
IsolationLevel = IsolationLevel.ReadCommitted
```

---

## 5. Verify Error Handling

### Search 1: Find exception handling for QuestDB writes
```bash
grep -r "catch.*Exception\|LogError\|LogWarning" src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs
```
**Expected:** Proper error logging before throwing

### Search 2: Find retryable error classification
```bash
grep -r "IsTransientError\|RetryableError\|NonRetryableError" src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs -B2 -A2
```
**Expected:** Transient errors (timeout, socket) are retried, others sent to DLQ

### Search 3: Find DLQ logic
```bash
grep -r "SendToDlqAsync\|SendToDLQ" src/ --include="*.cs"
```
**Expected:** Failed messages sent to DLQ with proper logging

---

## 6. Verify Configuration Sections Exist

### Check appsettings has QuestDb section
```bash
cat src/Naia.Api/appsettings.json | jq '.QuestDb'
```
**Expected:**
```json
{
  "HttpEndpoint": "http://localhost:9000",
  "PgWireEndpoint": "localhost:8812",
  "TableName": "point_data",
  "AutoFlushIntervalMs": 1000,
  "AutoFlushRows": 10000
}
```

### Check appsettings has Kafka section
```bash
cat src/Naia.Api/appsettings.json | jq '.Kafka'
```
**Expected:** Contains `BootstrapServers`, `DataPointsTopic`, `ConsumerGroupId`, etc.

---

## 7. Verify Dependency Injection Wiring

### Search 1: QuestDB service registration
```bash
grep -r "AddSingleton.*ITimeSeriesWriter\|AddQuestDb" src/Naia.Infrastructure/DependencyInjection.cs
```
**Expected:**
```
services.AddSingleton<ITimeSeriesWriter, QuestDbTimeSeriesWriter>();
```

### Search 2: Kafka consumer registration
```bash
grep -r "IDataPointConsumer.*KafkaDataPointConsumer" src/Naia.Infrastructure/DependencyInjection.cs
```
**Expected:** Kafka consumer registered as singleton

### Search 3: Pipeline registration
```bash
grep -r "AddSingleton.*IIngestionPipeline" src/Naia.Infrastructure/DependencyInjection.cs
```
**Expected:** Pipeline registered as singleton

---

## 8. Verify Data Flow Integration

### Search 1: Verify pipeline calls write function
```bash
grep -r "_timeSeriesWriter.WriteAsync" src/ --include="*.cs"
```
**Expected:**
```
src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs:237:
  await _timeSeriesWriter.WriteAsync(batch, cancellationToken);
```

### Search 2: Verify consumer is in main loop
```bash
grep -r "_consumer.ConsumeAsync" src/ --include="*.cs"
```
**Expected:** Found in ProcessLoopAsync

### Search 3: Verify offset committed after success
```bash
grep -r "CommitAsync.*context" src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs -B5 -A2
```
**Expected:** Commit only called after successful processing

---

## 9. Runtime Verification Commands

### Check QuestDB is running
```bash
curl -v http://localhost:9000/
```
**Expected:** HTTP 200 response

### Check Kafka is running
```bash
docker exec kafka kafka-broker-api-versions.sh --bootstrap-server localhost:9092
```
**Expected:** Lists API versions

### Check Kafka topic exists
```bash
docker exec kafka kafka-topics.sh --list --bootstrap-server localhost:9092 | grep naia.datapoints
```
**Expected:** Shows `naia.datapoints`

### Check consumer group exists
```bash
docker exec kafka kafka-consumer-groups.sh --list --bootstrap-server localhost:9092 | grep naia-ingestion
```
**Expected:** Shows consumer group

### Check QuestDB table
```bash
docker exec questdb psql -h localhost -p 8812 -U admin qdb -c "\dt point_data"
```
**Expected:** Shows table structure

### Check data in QuestDB
```bash
docker exec questdb psql -h localhost -p 8812 -U admin qdb -c "SELECT COUNT(*) as total, COUNT(DISTINCT point_id) as distinct_points FROM point_data;"
```
**Expected:** Non-zero counts if data has been ingested

---

## 10. Log Verification Commands

### Check Ingestion worker logs for ILP writes
```bash
docker logs naia-ingestion | grep -i "writing.*lines\|wrote.*points\|questdb"
```
**Expected:** Messages like "Writing 100 lines to QuestDB"

### Check for QuestDB write errors
```bash
docker logs naia-ingestion | grep -i "questdb.*error\|questdb.*failed"
```
**Expected:** No errors (or only transient connection errors)

### Check for Kafka consumer initialization
```bash
docker logs naia-ingestion | grep -i "kafka.*initialized\|consumer.*initialized"
```
**Expected:** Shows consumer connected to topics

### Check for deduplication logs
```bash
docker logs naia-ingestion | grep -i "duplicate\|deduplicate"
```
**Expected:** Few duplicates (normal operation)

### Check pipeline health
```bash
docker logs naia-ingestion | grep -i "pipeline.*health\|processed"
```
**Expected:** Regular health check messages

---

## 11. Full Verification Script

```bash
#!/bin/bash
echo "=== NAIA ILP Verification Script ==="
echo ""

echo "1. Check ILP Protocol (should find /write endpoint):"
grep -r "PostAsync.*write" src/ --include="*.cs" | wc -l
echo "   Expected: >= 1"
echo ""

echo "2. Check for disabled code (should find 0):"
grep -r "// await.*WriteAsync\|/\* WriteAsync\|#if.*ILP" src/ --include="*.cs" | wc -l
echo "   Expected: 0"
echo ""

echo "3. Check Kafka topic in config:"
grep -r "naia.datapoints" src/ --include="*.json" | head -1
echo ""

echo "4. Check QuestDB endpoint:"
grep -r "HttpEndpoint" src/ --include="*.json"
echo ""

echo "5. Check manual offset commits:"
grep -r "EnableAutoCommit = false" src/ --include="*.cs" | wc -l
echo "   Expected: >= 1"
echo ""

echo "6. Runtime checks:"
echo "   QuestDB: $(curl -s -o /dev/null -w '%{http_code}' http://localhost:9000/)"
echo "   Kafka: $(docker exec kafka kafka-broker-api-versions.sh --bootstrap-server localhost:9092 2>/dev/null | head -1)"
echo "   Topics: $(docker exec kafka kafka-topics.sh --list --bootstrap-server localhost:9092 2>/dev/null | grep naia.datapoints)"
echo ""

echo "7. Data in QuestDB:"
docker exec questdb psql -h localhost -p 8812 -U admin qdb -c "SELECT COUNT(*) FROM point_data;" 2>/dev/null || echo "   (QuestDB not available)"
echo ""

echo "=== Verification Complete ==="
```

---

## 12. Expected Results Summary

| Check | Expected | Actual |
|-------|----------|--------|
| ILP endpoint found | `/write` | `[ ]` |
| Disabled code found | 0 matches | `[ ]` |
| Kafka topic found | `naia.datapoints` | `[ ]` |
| QuestDB endpoint | `http://localhost:9000` | `[ ]` |
| Manual commits | found | `[ ]` |
| HTTP 200 from QuestDB | Yes | `[ ]` |
| point_data table | Exists | `[ ]` |
| Data count | > 0 | `[ ]` |
| Consumer group | `naia-ingestion*` | `[ ]` |

---

## Quick Validation One-Liner

```bash
# This one command checks the most critical aspects
echo "ILP Enabled:" && grep -c "PostAsync.*write" src/ -r --include="*.cs" && \
echo "Disabled Code:" && grep -c "// await.*WriteAsync" src/ -r --include="*.cs" && \
echo "Kafka Topic:" && grep -c "naia.datapoints" src/ -r --include="*.json" && \
echo "QuestDB Endpoint:" && grep "http://localhost:9000" src/ -r --include="*.json" && \
echo "Manual Commits:" && grep -c "EnableAutoCommit = false" src/ -r --include="*.cs"
```

**Expected output:**
```
ILP Enabled:
1
Disabled Code:
0
Kafka Topic:
3
QuestDB Endpoint:
"HttpEndpoint": "http://localhost:9000",
Manual Commits:
1
```

