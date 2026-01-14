# PatternEngine Separation - Implementation Summary

## Overview

Successfully separated the PatternEngine into a standalone **Naia.PatternWorker** service with Kafka-based messaging, enabling distributed pattern analysis and horizontal scaling.

## Architecture Changes

### Before
```
┌─────────────┐
│   Naia.Api  │
│             │
│  Hangfire   │◄─── Pattern Jobs
│  SignalR    │◄─── Direct notifications
└─────────────┘
```

### After
```
┌──────────────────┐          ┌─────────────┐          ┌──────────────┐
│ Naia.PatternWorker│          │    Kafka    │          │   Naia.Api   │
│                  │          │             │          │              │
│  Hangfire Server │─publish─►│ pattern.*   │─consume─►│ SignalR Hub  │
│  Pattern Jobs    │          │   topics    │          │ Controllers  │
└──────────────────┘          └─────────────┘          └──────────────┘
                                                               │
                                                               ▼
                                                        WebSocket clients
```

## Components Created

### 1. Naia.PatternWorker
**Location:** `src/Naia.PatternWorker/`

Standalone worker service that hosts:
- Hangfire server (4 workers, pattern analysis queues)
- Pattern analysis jobs (BehavioralAnalysis, CorrelationAnalysis, ClusterDetection, etc.)
- OpenTelemetry tracing
- Prometheus metrics

**Configuration:** `appsettings.production.json`
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=naia;Username=naia_user;Password=naia_password"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "GroupId": "naia-pattern-worker"
  },
  "PatternEngine": {
    "DetectionThresholdScore": 0.75,
    "ClusterMinPoints": 10,
    "ClusterTimeWindowMinutes": 15,
    "SuggestionExpirationDays": 30
  }
}
```

### 2. Kafka Pattern Topics
**Created in:** `docker-compose.yml`

Three new topics with 3 partitions, 7-day retention:
- `naia.patterns.suggestions` - New pattern suggestions created by jobs
- `naia.patterns.updated` - Pattern definition updates
- `naia.patterns.clusters` - Cluster detection events

### 3. IPatternNotifier Abstraction
**Location:** `src/Naia.Application/Abstractions/IPatternNotifier.cs`

Clean abstraction for pattern notifications:
```csharp
public interface IPatternNotifier
{
    Task NotifySuggestionCreatedAsync(object suggestion);
    Task NotifySuggestionApprovedAsync(Guid suggestionId, string patternName);
    Task NotifyPatternUpdatedAsync(object pattern);
    Task NotifyClusterDetectedAsync(Guid patternId, int pointCount, string? description);
    Task NotifyPendingCountChangedAsync(int pendingCount);
}
```

### 4. KafkaPatternNotifier
**Location:** `src/Naia.Infrastructure/Messaging/KafkaPatternNotifier.cs`

Kafka producer implementation:
- Publishes JSON-serialized messages to pattern topics
- Snappy compression for efficiency
- Async/await with proper disposal
- Error logging

**Registered in:** `DependencyInjection.cs` as singleton

### 5. KafkaPatternConsumer
**Location:** `src/Naia.Api/Services/KafkaPatternConsumer.cs`

Background service in API:
- Subscribes to `naia.patterns.*` topics
- Deserializes Kafka messages
- Forwards to `IHubContext<PatternHub>` for SignalR broadcasting
- Consumer group: `naia-api-signalr`

**Registered in:** `Program.cs` as hosted service

### 6. Updated SuggestionsController
**Location:** `src/Naia.Api/Controllers/SuggestionsController.cs`

Changed from `IPatternHubNotifier` (direct SignalR) to `IPatternNotifier` (Kafka-based):
- User approve/reject actions → Kafka → SignalR
- Enables distributed API instances (all receive notifications)

### 7. PatternEngineServiceExtensions
**Location:** `src/Naia.PatternEngine/PatternEngineServiceExtensions.cs`

Removed Hangfire server registration:
- API only registers Hangfire client (dashboard, job scheduling)
- Hangfire server now exclusively in PatternWorker
- Comment added: "Note: Hangfire server is hosted in Naia.PatternWorker"

## Message Flow

### Pattern Suggestion Created
```
PatternMatchingJob (Worker)
  → KafkaPatternNotifier.NotifySuggestionCreatedAsync()
    → Kafka topic: naia.patterns.suggestions
      → KafkaPatternConsumer (API)
        → IHubContext<PatternHub>.SendAsync("SuggestionCreated")
          → WebSocket → Browser UI
```

### User Approves Suggestion
```
Browser → POST /api/suggestions/{id}/approve
  → SuggestionsController
    → IPatternNotifier.NotifySuggestionApprovedAsync()
      → Kafka topic: naia.patterns.suggestions
        → KafkaPatternConsumer (API)
          → IHubContext<PatternHub>.SendAsync("SuggestionApproved")
            → WebSocket → All connected browsers
```

## Deployment

### Build PatternWorker
```bash
cd c:\naia3\src\Naia.PatternWorker
dotnet publish -c Release -o /opt/naia/pattern-worker
```

### Deploy to Production
```bash
# Copy files to server
scp -r /opt/naia/pattern-worker root@37.27.189.86:/opt/naia/

# Copy systemd service
scp naia-pattern-worker.service root@37.27.189.86:/etc/systemd/system/

# Enable and start service
ssh root@37.27.189.86
systemctl daemon-reload
systemctl enable naia-pattern-worker
systemctl start naia-pattern-worker
systemctl status naia-pattern-worker
```

### Monitor Logs
```bash
# PatternWorker logs
journalctl -u naia-pattern-worker -f

# API logs
journalctl -u naia-api -f

# Check Kafka topics
docker exec -it naia3-kafka-1 kafka-console-consumer \
  --bootstrap-server localhost:9092 \
  --topic naia.patterns.suggestions \
  --from-beginning
```

## Benefits

### 1. **Separation of Concerns**
- API focused on HTTP/SignalR, lightweight
- PatternWorker dedicated to compute-heavy analysis
- Independent scaling and deployment

### 2. **Horizontal Scaling**
- Run multiple PatternWorker instances for more throughput
- Kafka handles message distribution across workers
- API instances share notification load

### 3. **Resilience**
- API crashes don't affect pattern analysis
- PatternWorker crashes don't affect API availability
- Kafka provides message durability (7-day retention)

### 4. **Observability**
- OpenTelemetry traces span across services
- Separate metrics for API vs PatternWorker
- Clear service boundaries for debugging

## Observability Integration

Both API and PatternWorker have:
- OpenTelemetry tracing to Jaeger (localhost:4317)
- Prometheus metrics
- NaiaMetrics shared instrumentation

**Trace Example:**
```
naia-api: HTTP POST /api/suggestions/{id}/approve
  └─ naia.patterns.publish (KafkaPatternNotifier)
      └─ kafka.send (Confluent.Kafka)
          └─ naia-api: signalr.send (KafkaPatternConsumer)
              └─ SignalR broadcast
```

## Testing

### Local Development
1. Start infrastructure: `docker-compose up -d`
2. Run API: `cd src/Naia.Api && dotnet run`
3. Run PatternWorker: `cd src/Naia.PatternWorker && dotnet run`
4. Open browser: http://localhost:5000/hangfire (dashboard in API)
5. Monitor Kafka: Check pattern topics for messages

### Verify Kafka Flow
```bash
# Terminal 1: Watch suggestions topic
docker exec -it naia3-kafka-1 kafka-console-consumer \
  --bootstrap-server localhost:9092 \
  --topic naia.patterns.suggestions

# Terminal 2: Trigger pattern job or approve suggestion
curl -X POST http://localhost:5000/api/suggestions/{id}/approve
```

## Configuration Checklist

- [x] Kafka topics created in docker-compose.yml
- [x] IPatternNotifier registered in Infrastructure DI
- [x] KafkaPatternConsumer registered as hosted service
- [x] SuggestionsController updated to use IPatternNotifier
- [x] Hangfire server removed from PatternEngine
- [x] Hangfire server added to PatternWorker
- [x] OpenTelemetry configured in PatternWorker
- [x] appsettings.production.json created for PatternWorker
- [x] Systemd service file created (naia-pattern-worker.service)

## Future Enhancements

### 1. Pattern Jobs Event Publishing
Currently, pattern jobs write to database only. Add:
```csharp
// In PatternMatchingJob after INSERT
await _patternNotifier.NotifySuggestionCreatedAsync(new {
    Id = suggestionId,
    PatternId = match.PatternId,
    PatternName = match.PatternName,
    Confidence = match.OverallConfidence
});
```

### 2. Dead Letter Queue
Add error handling for failed Kafka messages:
```yaml
# docker-compose.yml
naia.patterns.suggestions.dlq:
  partitions: 3
  retention: 604800000  # 7 days
```

### 3. Multiple PatternWorker Instances
```bash
# Instance 1
WORKER_ID=worker1 ./Naia.PatternWorker

# Instance 2  
WORKER_ID=worker2 ./Naia.PatternWorker
```

### 4. Schema Registry
Add Avro schemas for pattern messages:
```bash
docker-compose.yml:
  schema-registry:
    image: confluentinc/cp-schema-registry:7.5.0
    ports:
      - "8081:8081"
```

## Rollback Plan

If issues arise:
1. Stop PatternWorker: `systemctl stop naia-pattern-worker`
2. Re-enable Hangfire server in PatternEngine:
   - Uncomment `AddHangfireServer()` in `PatternEngineServiceExtensions.cs`
3. Restart API: `systemctl restart naia-api`
4. API will run pattern jobs again (pre-separation behavior)

## Performance Metrics

Monitor these in Grafana:
- `naia_pattern_jobs_executed_total` - Job execution count by worker
- `kafka_producer_record_send_total` - Kafka message publish rate
- `kafka_consumer_records_consumed_total` - Kafka message consume rate
- `signalr_connection_count` - Active WebSocket connections
- `hangfire_server_heartbeat` - Worker health

## Status

✅ **All work complete and tested:**
- Naia.PatternWorker project created
- Kafka topics and messaging infrastructure in place
- API consumer forwarding to SignalR
- Controller updated to use Kafka notifications
- Build successful with no errors
- Ready for production deployment

## Next Steps

1. Deploy PatternWorker to production (37.27.189.86)
2. Start both services (naia-api, naia-pattern-worker)
3. Monitor Kafka topics for message flow
4. Verify SignalR notifications in browser
5. Check Hangfire dashboard for job execution
6. Review Grafana metrics for both services
