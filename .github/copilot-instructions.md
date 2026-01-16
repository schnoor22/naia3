# NAIA v3 - AI Agent Instructions

NAIA is an industrial data historian with Kafka-based ingestion, QuestDB time-series storage, and real-time pattern detection. The system uses Clean Architecture with .NET 8.

## 🚨 CRITICAL: Production Server Paths (37.27.189.86)

**NEVER CONFUSE THESE PATHS - READ THIS FIRST EVERY TIME:**

### ⚠️ FATAL MISTAKE TO AVOID
**NEVER put static files (index.html, _app/, etc.) in `/opt/naia/publish/`!**
The .NET API has `UseStaticFiles()` and `MapFallbackToFile("index.html")` which will serve HTML instead of JSON if these files exist.
If API returns HTML instead of JSON, run: `rm -rf /opt/naia/publish/wwwroot /opt/naia/publish/_app /opt/naia/publish/*.html`
See [DEPLOYMENT_PATHS_CRITICAL.md](DEPLOYMENT_PATHS_CRITICAL.md) for full explanation.

### Web Frontend (Svelte/SvelteKit)
- **Deploy to:** `/opt/naia/build/` ✅ CORRECT
- **Deploy from:** `c:\naia3\src\Naia.Web\build\*` (after `npm run build`)
- **Served by:** Caddy reverse proxy at https://app.naia.run
- **Command:** `scp -r c:\naia3\src\Naia.Web\build\* root@37.27.189.86:/opt/naia/build/`

### API Service (.NET)
- **Deploy to:** `/opt/naia/publish/` ✅ CORRECT
- **Deploy from:** `c:\naia3\publish-api\*` (after `dotnet publish`)
- **Runs from:** `/opt/naia/publish/Naia.Api.dll`
- **Service:** `naia-api.service` (systemd)
- **Config:** `/opt/naia/publish/appsettings.production.json`
- **Command:** `scp -r c:\naia3\publish-api\* root@37.27.189.86:/opt/naia/publish/`
- **Restart:** `ssh root@37.27.189.86 "systemctl restart naia-api"`

### Ingestion Service (.NET)
- **Deploy to:** `/opt/naia/ingestion/` ✅ CORRECT
- **Deploy from:** `c:\naia3\publish-ingestion\*` (after `dotnet publish`)
- **Runs from:** `/opt/naia/ingestion/Naia.Ingestion.dll`
- **Service:** `naia-ingestion.service` (systemd)
- **Command:** `scp -r c:\naia3\publish-ingestion\* root@37.27.189.86:/opt/naia/ingestion/`
- **Restart:** `ssh root@37.27.189.86 "systemctl restart naia-ingestion"`

### Data Files
- **Kelmarsh Wind Data:** `/opt/naia/data/kelmarsh/scada_2019/`
- **Contains:** `Turbine_Data_Kelmarsh_*.csv` files

### WRONG Paths (DO NOT USE)
- ❌ `/opt/naia/web/` - OLD, UNUSED
- ❌ `/opt/naia/api/` - OLD CONFIG LOCATION

## Architecture Essentials

### Three-Database Design
- **PostgreSQL** (`naia` db, port 5432): Point metadata, data sources, pattern results
- **QuestDB** (port 9000 HTTP, 8812 PG wire, 9009 ILP): Time-series data via InfluxDB Line Protocol
- **Redis** (port 6379): Current values cache + Kafka idempotency tracking

### Critical ID Mapping Convention
PostgreSQL stores points with TWO IDs:
- `id` (UUID): EF Core primary key, maps to C# `Point.Id`
- `point_sequence_id` (BIGINT): Sequential ID for QuestDB, maps to C# `Point.PointSequenceId`

QuestDB `point_data` table uses `point_id` (LONG) = PostgreSQL `point_sequence_id`.

**Never** use `GetInt32()` for `point_sequence_id` - it's BIGINT, requires `GetInt64()`.

### Data Flow
```
Connectors → Kafka (naia.datapoints, 12 partitions) 
  → Ingestion Pipeline (manual offset commits)
    → QuestDB (ILP writes) + Redis (current values) + PostgreSQL (idempotency)
```

Kafka partition key = `DataSourceId` ensures ordering per source while enabling parallelism.

## Project Structure

```
src/
├── Naia.Domain/          # Entities (Point, DataSource) - no dependencies
├── Naia.Application/     # Interfaces (IPointRepository, ITimeSeriesService)
├── Naia.Infrastructure/  # Implementations (Kafka, PostgreSQL, QuestDB, Redis)
│   ├── Messaging/        # KafkaDataPointProducer, KafkaDataPointConsumer
│   ├── Persistence/      # PointRepository, PointLookupService (in-memory cache)
│   └── TimeSeries/       # QuestDbService (ILP writer)
├── Naia.Connectors/      # Data source adapters (PI, OPC-UA, CSV)
├── Naia.Ingestion/       # Worker service that consumes Kafka → writes to DBs
├── Naia.Api/             # REST API + SignalR hubs (Program.cs has 2400+ lines)
├── Naia.PatternEngine/   # Behavioral analysis & correlation detection
└── Naia.PatternWorker/   # Separate service for pattern processing
```

## Critical Implementation Patterns

### PointLookupService (In-Memory Cache)
Lives in `Naia.Infrastructure/Persistence/PointLookupService.cs`. Refreshes every 5 minutes from PostgreSQL. Provides O(1) lookups by:
- SequenceId (for QuestDB queries)
- Name (for connector tag resolution)
- Id (GUID, for EF Core joins)
- DataSourceId (for bulk operations)

Used throughout ingestion and pattern engine to avoid DB roundtrips.

### Kafka Idempotency
Producer uses `enable.idempotence=true` + `acks=all`. Consumer combines:
1. Manual offset management (`EnableAutoCommit=false`)
2. Redis-based duplicate detection (`IIdempotencyStore.CheckAsync`)
3. Commit only after successful DB write

Key format: `kafka:{topic}:{partition}:{offset}` with 7-day TTL in Redis.

### QuestDB Writes
Use ILP (InfluxDB Line Protocol) over HTTP (port 9000 `/exec` endpoint) or TCP (port 9009).
Format: `point_data,point_id={seq_id} value={val} {timestamp_nanos}`

Never use PostgreSQL wire protocol (port 8812) for writes - it's for reads only.

## Common Development Tasks

### Building & Deployment
```bash
# Local build
dotnet build Naia.sln -c Debug

# Publish for production
dotnet publish Naia.sln -c Release -o ./publish

# Start Docker stack (Kafka, QuestDB, PostgreSQL, Redis)
docker compose up -d
```

### Service Management (Production)
Services run as systemd units on Ubuntu server (app.naia.run):
- `naia-api.service` - REST API + SignalR
- `naia-ingestion.service` - Kafka consumer
- `naia-pattern-worker.service` - Pattern detection

```bash
ssh root@37.27.189.86 "systemctl restart naia-api naia-ingestion"
ssh root@37.27.189.86 "journalctl -u naia-ingestion -f"  # Live logs
```

Caddy reverse proxy handles HTTPS termination at port 443.

### CSV Replay (Bulk Import)
Use `preprocess-site-data.ps1` to transform site CSVs:
1. Converts local timestamps → UTC (requires timezone param)
2. Strips privacy-sensitive tag prefixes
3. Creates `GenericCsvConnector` compatible format

Then run API with `GenericCsvReplay` connector enabled. See `CSV_REPLAY_QUICKSTART.md`.

### Adding New Data Points
Points auto-create on first data arrival if connector configured with `AutoCreatePoints=true`. Otherwise use `/api/points` POST endpoint with:
```json
{
  "name": "TAG_NAME",
  "dataSourceId": "uuid",
  "valueType": "Double",  // or Int32, Boolean, String
  "engineeringUnits": "MW"
}
```

PostgreSQL assigns `point_sequence_id` via IDENTITY column. PointLookupService picks it up on next refresh.

## Configuration Patterns

### appsettings Hierarchy
- `appsettings.json` - defaults
- `appsettings.production.json` - remote DB connections
- `appsettings.ingestion.json` - worker-specific (no web UI)
- `appsettings.GenericCsvReplay.json` - CSV import mode

Connection strings override per environment. Kafka bootstrap servers default to `localhost:9092` but production uses `37.27.189.86:9092`.

### Important Settings
```json
{
  "Kafka": {
    "DataPointsPartitions": 12,
    "ConsumerGroupId": "naia-historians",
    "MaxPollIntervalMs": 300000  // 5 min before consumer kicked
  },
  "QuestDB": {
    "HttpUrl": "http://localhost:9000",
    "BatchSize": 1000,
    "FlushIntervalMs": 5000
  }
}
```

## Debugging Tips

### Check Data Flow
```bash
# 1. Verify Kafka topic has messages
ssh root@37.27.189.86 "docker exec -it naia-kafka kafka-console-consumer --bootstrap-server localhost:9092 --topic naia.datapoints --max-messages 5"

# 2. Check ingestion service is consuming
ssh root@37.27.189.86 "journalctl -u naia-ingestion -n 100 | grep 'Processed batch'"

# 3. Query QuestDB for recent data
curl 'http://localhost:9000/exec?query=SELECT * FROM point_data LIMIT 10'

# 4. Check Redis current values
redis-cli GET "current:point:123"  # 123 = point_sequence_id
```

### Common Issues
- **No data in QuestDB**: Check ingestion logs for idempotency key collisions or QuestDB connection errors
- **Missing PointSequenceId**: Point created but not yet assigned ID - refresh PointLookupService cache or wait 5min
- **Kafka consumer lag**: Check `docker stats` for resource limits, increase `MaxPollIntervalMs` if processing is slow

## Pattern Engine Notes
Runs every 5 minutes via `PatternJobsController.TriggerFullPipeline()`. Analyzes:
- Behavioral stats (mean, stddev, change rate per point)
- Cross-point correlations (Pearson coefficient)
- Generates optimization suggestions stored in PostgreSQL

Uses PointLookupService heavily to resolve point names from historical data queries.

## Key Files for Reference
- [ARCHITECTURE.md](ARCHITECTURE.md) - Type mappings, ID conventions, schema reference
- [COMMANDS.md](COMMANDS.md) - SSH shortcuts, Docker commands
- [CSV_REPLAY_QUICKSTART.md](CSV_REPLAY_QUICKSTART.md) - Bulk data import workflow
- [QUICK_REFERENCE.md](QUICK_REFERENCE.md) - One-page operations cheatsheet
- [src/Naia.Infrastructure/DependencyInjection.cs](src/Naia.Infrastructure/DependencyInjection.cs) - Service registration patterns
