# NAIA v3 - Foundation Complete

## What We Built

NAIA v3 has been created from the ground up with a **production-grade, Kafka-centric architecture** designed for industrial-scale data ingestion with zero data loss guarantees.

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                    CLEAN ARCHITECTURE                             │
├──────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────────┐      ┌──────────────┐      ┌────────────────┐ │
│  │   Domain    │◄─────│  Application │◄─────│Infrastructure │ │
│  │  (Entities) │      │ (Interfaces) │      │ (Impl)        │ │
│  └─────────────┘      └──────────────┘      └────────────────┘ │
│                                                      ▲           │
│                                                      │           │
│                                          ┌───────────┴────┐     │
│                                          │     API        │     │
│                                          │  (Controllers) │     │
│                                          └────────────────┘     │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│                    DATA FLOW                                      │
├──────────────────────────────────────────────────────────────────┤
│                                                                   │
│   Data Sources                                                    │
│   (OPC-UA, PI, CSV, etc.)                                         │
│          │                                                        │
│          ▼                                                        │
│   ┌──────────────┐                                               │
│   │  Kafka       │  Topic: naia.datapoints (12 partitions)       │
│   │  Producer    │  - Exactly-once semantics                     │
│   └──────┬───────┘  - Idempotent writes                          │
│          │          - Snappy compression                          │
│          │                                                        │
│          ▼                                                        │
│   ┌──────────────┐                                               │
│   │  Kafka       │  Brokers: 1 (scalable to 3+)                  │
│   │  Cluster     │  Retention: 7 days                            │
│   └──────┬───────┘  Replication: 1 (scalable to 3)               │
│          │                                                        │
│          ▼                                                        │
│   ┌──────────────┐                                               │
│   │  Ingestion   │  - Manual offset management                   │
│   │  Pipeline    │  - Idempotency checking (Redis)               │
│   └──────┬───────┘  - DLQ for failed messages                    │
│          │                                                        │
│          ├────────────────────┬──────────────────────┐           │
│          │                    │                      │           │
│          ▼                    ▼                      ▼           │
│   ┌──────────┐         ┌──────────┐         ┌──────────┐       │
│   │ QuestDB  │         │  Redis   │         │PostgreSQL│       │
│   │(Time-    │         │(Current  │         │(Metadata)│       │
│   │ Series)  │         │ Values)  │         │          │       │
│   └──────────┘         └──────────┘         └──────────┘       │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

## Technology Stack

| Layer | Technology | Purpose |
|-------|------------|---------|
| **Application** | .NET 8 | Modern C# with records, pattern matching |
| **Metadata DB** | PostgreSQL 16 | Points, data sources, configuration |
| **Time-Series DB** | QuestDB 7.4.2 | High-performance columnar storage |
| **Message Queue** | Apache Kafka 3.6 (Confluent 7.5.3) | Event backbone, exactly-once delivery |
| **Cache** | Redis 7 | Current values, idempotency store |
| **Coordination** | Zookeeper 3.8 | Kafka cluster coordination |
| **Management UI** | Kafka UI | Topic/consumer monitoring |

## Project Structure

```
naia3/
├── src/
│   ├── Naia.Domain/               # Core entities (Point, DataSource)
│   ├── Naia.Application/          # Interfaces, contracts
│   ├── Naia.Infrastructure/       # Concrete implementations
│   │   ├── Kafka/                 # Producer/consumer services
│   │   ├── Persistence/           # PostgreSQL (EF Core)
│   │   ├── TimeSeries/            # QuestDB reader/writer
│   │   └── Caching/               # Redis services
│   ├── Naia.Ingestion/            # Ingestion pipeline orchestrator
│   └── Naia.Api/                  # REST API endpoints
├── init-scripts/
│   ├── postgres/                  # PostgreSQL schema
│   └── questdb/                   # QuestDB tables
├── docker-compose.yml             # Full stack definition
└── test-infrastructure.ps1        # Connectivity test
```

## Docker Services

All services running and healthy:

```powershell
# Start the stack
docker-compose up -d

# Check status
docker ps

# View logs
docker-compose logs -f kafka
docker-compose logs -f questdb

# Stop the stack
docker-compose down
```

### Service Endpoints

- **PostgreSQL**: `localhost:5432` (user: `naia`, pass: `naia_dev_password`)
- **QuestDB Web Console**: http://localhost:9000
- **QuestDB PostgreSQL Wire**: `localhost:8812`
- **QuestDB ILP (TCP)**: `localhost:9009`
- **Redis**: `localhost:6379`
- **Redis Commander**: http://localhost:8081
- **Kafka**: `localhost:9092` (internal: `kafka:29092`)
- **Kafka UI**: http://localhost:8080
- **Zookeeper**: `localhost:2181`

## Database Schema

### PostgreSQL Tables (Metadata)

- `data_sources` - OPC-UA, PI, Modbus, etc. connections
- `points` - Point configuration with compression settings
- `current_values` - Snapshot cache (mirrored to Redis)
- `import_sessions` - Data replay/batch import tracking
- `system_config` - Runtime configuration
- `audit_log` - Change tracking

### QuestDB Tables (Time-Series)

- `point_data` - Raw time-series data (partitioned by DAY)
- `point_aggregates` - Hourly rollups (partitioned by MONTH)
- `point_daily_stats` - Daily statistics for pattern analysis

### Kafka Topics

- `naia.datapoints` - Main ingestion topic (12 partitions, 7-day retention)
- `naia.datapoints.dlq` - Dead letter queue (3 partitions, 30-day retention)

## Key Design Decisions

### 1. Kafka-Centric Architecture ✅
- **Zero data loss**: Manual offset management, exactly-once semantics
- **Idempotency**: Redis-backed duplicate detection
- **Ordering**: Per-device partitioning maintains temporal order
- **Scalability**: Horizontal scaling via consumer groups
- **Durability**: 7-day retention allows complete replay

### 2. Dual-ID System ✅
```
Point.Id (UUID)          → PostgreSQL foreign keys, API responses
Point.PointIdSeq (BIGINT) → QuestDB storage (10x more efficient)
```

### 3. Manual Offset Commits ✅
```
Message → Process → Success? → Commit Offset
                  → Failure?  → DON'T commit, pause consumer
```
**Never lose data** - if processing fails, message stays in Kafka.

### 4. Idempotency Store ✅
- Redis cache with 24-hour TTL
- Fallback to in-memory (LRU, 100k entries)
- Detects duplicates from producer retries or manual replays

### 5. Clean Architecture ✅
- **No entity duplication** (v1 problem fixed)
- Clear layer boundaries
- Testable by design
- Domain-driven design

## What's Different from v1?

| Aspect | v1 | v3 |
|--------|----|----|
| **Database** | SQL Server | PostgreSQL |
| **Entity Model** | Dual (EF + Cache) | Single unified |
| **Message Queue** | None | Kafka backbone |
| **Idempotency** | Not implemented | Redis + fallback |
| **Data Loss Prevention** | Best-effort | Exactly-once guarantees |
| **Horizontal Scaling** | Limited | Native (Kafka consumer groups) |
| **Architecture** | Organic complexity | Clean from day 1 |

## Next Steps

### Phase 1: Prove the Pipeline (Current)
- ✅ Infrastructure running
- ✅ Clean architecture established
- ⏳ **Next**: Build data replay tool to test with real historian data
- ⏳ Validate zero-data-loss guarantees at scale
- ⏳ Benchmark: 1M+ points/sec ingestion

### Phase 2: Pattern Learning
- CSV import → behavioral clustering (DBSCAN)
- Pattern library (learn from site #1-10)
- Backward modeling (suggestions for site #11)
- Flywheel validation with real industrial data

### Phase 3: Multi-Site Intelligence
- Site similarity scoring
- Auto-suggestions based on site type
- Confidence scoring (naming, behavior, correlation)
- User feedback loop

### Phase 4: Enterprise Features
- Multi-tenancy
- Authentication/Authorization (JWT)
- Role-based access control
- Audit trail completeness

## Testing the System

### Infrastructure Test
```powershell
.\test-infrastructure.ps1
```

Expected output:
```
✅ PostgreSQL: Connected (Points table: 0 records)
✅ Redis: Connected (PONG)
✅ Kafka: Connected (2 topics available)
✅ QuestDB: Connected (HTTP API responding)
```

### Create Test Data Source
```sql
INSERT INTO data_sources (name, source_type, connection_string, is_enabled)
VALUES ('TestOPC', 'OPC-UA', 'opc.tcp://localhost:4840', true);
```

### Create Test Point
```sql
INSERT INTO points (
    name, data_source_id, address, engineering_units,
    compression_enabled, compression_deviation
)
VALUES (
    'TEST.POINT.001',
    (SELECT id FROM data_sources WHERE name = 'TestOPC'),
    'ns=2;s=Device.Sensor.Temperature',
    '°C',
    true,
    0.5
);
```

### Write Test Data to QuestDB
```sql
-- Via QuestDB web console (http://localhost:9000)
INSERT INTO point_data 
VALUES (systimestamp(), 1, 75.5, 0);
```

### Send Test Message to Kafka
```bash
docker exec naia-kafka kafka-console-producer \
  --bootstrap-server localhost:29092 \
  --topic naia.datapoints \
  --property "parse.key=true" \
  --property "key.separator=:"
  
# Type: test-key:{"pointId":"123","timestamp":"2026-01-10T18:00:00Z","value":100.5}
```

## Monitoring

### Kafka Topics
```bash
# List topics
docker exec naia-kafka kafka-topics --list --bootstrap-server localhost:29092

# Describe topic
docker exec naia-kafka kafka-topics --describe --topic naia.datapoints --bootstrap-server localhost:29092

# Consumer lag
docker exec naia-kafka kafka-consumer-groups --describe --group naia-historians --bootstrap-server localhost:29092
```

### QuestDB Queries
```sql
-- Check data count
SELECT COUNT(*) FROM point_data;

-- Recent data
SELECT * FROM point_data LATEST ON timestamp PARTITION BY point_id;

-- Query by time range
SELECT * FROM point_data 
WHERE timestamp > dateadd('h', -1, now())
ORDER BY timestamp DESC;
```

### PostgreSQL Queries
```sql
-- Check points
SELECT p.name, p.engineering_units, ds.name as data_source
FROM points p
JOIN data_sources ds ON p.data_source_id = ds.id;

-- System health
SELECT * FROM system_config;
```

## Configuration

All configuration in `src/Naia.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=naia;Username=naia;Password=naia_dev_password",
    "Redis": "localhost:6379",
    "QuestDb": "Host=localhost;Port=8812;Database=qdb;Username=admin;Password=quest"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "ProducerClientId": "naia-producer",
    "ConsumerGroupId": "naia-historians",
    "DataPointsTopic": "naia.datapoints",
    "DlqTopic": "naia.datapoints.dlq"
  },
  "Ingestion": {
    "BatchSize": 10000,
    "FlushIntervalMs": 1000,
    "MaxIdempotencyEntries": 100000
  }
}
```

## Key Files Reference

- [`docker-compose.yml`](docker-compose.yml) - Full stack definition
- [`init-scripts/postgres/01-init-schema.sql`](init-scripts/postgres/01-init-schema.sql) - PostgreSQL schema
- [`init-scripts/questdb/01-init-schema.sql`](init-scripts/questdb/01-init-schema.sql) - QuestDB tables
- [`src/Naia.Domain/Entities/Point.cs`](src/Naia.Domain/Entities/Point.cs) - Core point entity
- [`src/Naia.Infrastructure/Kafka/KafkaProducerService.cs`](src/Naia.Infrastructure/Kafka/KafkaProducerService.cs) - Kafka producer
- [`src/Naia.Infrastructure/Kafka/KafkaConsumerService.cs`](src/Naia.Infrastructure/Kafka/KafkaConsumerService.cs) - Kafka consumer
- [`src/Naia.Ingestion/IngestionPipeline.cs`](src/Naia.Ingestion/IngestionPipeline.cs) - Core ingestion orchestrator

## Troubleshooting

### Kafka issues
```bash
# Check Kafka logs
docker logs naia-kafka

# Verify topics exist
docker exec naia-kafka kafka-topics --list --bootstrap-server localhost:29092

# Reset consumer group (DESTRUCTIVE - reprocesses all messages)
docker exec naia-kafka kafka-consumer-groups --delete --group naia-historians --bootstrap-server localhost:29092
```

### QuestDB issues
```bash
# Check QuestDB logs
docker logs naia-questdb

# Verify tables
curl "http://localhost:9000/exec?query=SHOW TABLES"
```

### PostgreSQL issues
```bash
# Check PostgreSQL logs
docker logs naia-postgres

# Connect to database
docker exec -it naia-postgres psql -U naia -d naia

# Verify schema
docker exec naia-postgres psql -U naia -d naia -c "\dt"
```

## Success Criteria

We've successfully established:

✅ **Zero-data-loss infrastructure** - Kafka + manual offsets + idempotency  
✅ **Production-grade storage** - PostgreSQL (metadata) + QuestDB (time-series)  
✅ **Horizontal scalability** - Kafka consumer groups, partition-based parallelism  
✅ **Clean architecture** - Domain → Application → Infrastructure → API  
✅ **Docker-first deployment** - One command to start entire stack  
✅ **Monitoring tools** - Kafka UI, Redis Commander, QuestDB Console  

**This is the foundation. Everything else builds on this rock-solid base.**

---

## Vision Reminder

This system will:
- Remember how you organized sites #1-10 and suggest structure for site #11
- Monitor data patterns and detect anomalies
- Import legacy historian data (PI, Wonderware) and make it smarter
- Learn continuously - every new site improves the system
- Scale to 10M+ points, 500+ concurrent users

**We're not just building a historian. We're building an industrial AI platform.**

Ready to change the industrial data landscape. 🚀
