# NAIA v3 Architecture

> **Single Source of Truth** for schema names, ID mappings, and data flow

---

## 🗄️ Database Architecture

### PostgreSQL (Metadata Store)

| Database | Port | Purpose |
|----------|------|---------|
| naia | 5432 | Point metadata, data sources, patterns, suggestions |

**Key Tables:**
- `points` - Point/tag configuration with `point_sequence_id` (BIGINT)
- `data_sources` - Connector configurations
- `behavioral_stats` - Pattern engine behavioral data
- `correlation_results` - Detected correlations
- `suggestions` - AI-generated optimization suggestions

### QuestDB (Time-Series Store)

| Protocol | Port | Purpose |
|----------|------|---------|
| ILP (InfluxDB Line Protocol) | 9009 | High-speed data writes |
| PostgreSQL Wire | 8812 | SQL reads for analysis |
| HTTP API | 9000 | Admin and REST queries |

**Key Table:**
- `point_data` - Time-series data with `point_id` (LONG)

---

## 🔑 ID Mapping Convention

> **CRITICAL**: Consistent naming prevents data flow breaks

### Point Identifiers

| Context | Column Name | Type | Description |
|---------|-------------|------|-------------|
| PostgreSQL | `id` | UUID | Primary key for relational queries |
| PostgreSQL | `point_sequence_id` | BIGINT | Sequential ID for QuestDB |
| QuestDB | `point_id` | LONG | Same as PostgreSQL `point_sequence_id` |
| Code (Entity) | `Id` | Guid | Maps to PostgreSQL `id` |
| Code (Entity) | `PointSequenceId` | long? | Maps to PostgreSQL `point_sequence_id` |

### Column Name Standard

```
EF Core Property      PostgreSQL Column      Notes
─────────────────────────────────────────────────────
PointSequenceId   →   point_sequence_id   ✅ CORRECT
                      point_id_seq        ❌ OLD (migrated)
```

### Data Flow

```
Point Created in PostgreSQL
       │
       ▼
   point_sequence_id assigned (BIGINT IDENTITY)
       │
       ▼
PointLookupService caches by SequenceId AND Name
       │
       ▼
Ingestion resolves PointName → SequenceId via cache
       │
       ▼
QuestDB receives data with point_id = SequenceId
       │
       ▼
API queries QuestDB using point_id for history
```

---

## 📊 Type Mappings

### PostgreSQL → C# Type Mapping

| PostgreSQL Type | C# Type | ADO.NET Reader Method |
|-----------------|---------|----------------------|
| UUID | Guid | `GetGuid()` |
| BIGINT | long | `GetInt64()` ⚠️ NOT GetInt32! |
| INTEGER | int | `GetInt32()` |
| DOUBLE PRECISION | double | `GetDouble()` |
| TEXT | string | `GetString()` |
| BOOLEAN | bool | `GetBoolean()` |
| TIMESTAMPTZ | DateTime | `GetDateTime()` |

### QuestDB → C# Type Mapping

| QuestDB Type | C# Type | Notes |
|--------------|---------|-------|
| LONG | long | Point ID storage |
| DOUBLE | double | Sensor values |
| TIMESTAMP | DateTime | Nanosecond precision |
| SYMBOL | string | Categorical data |

---

## 🔧 Service Architecture

### Port Allocations

| Service | Port | Protocol |
|---------|------|----------|
| Naia.Api | 5000 | HTTP/HTTPS |
| Naia.Api (dev) | 5052 | HTTP |
| PostgreSQL | 5432 | PostgreSQL wire |
| QuestDB ILP | 9009 | InfluxDB Line Protocol |
| QuestDB SQL | 8812 | PostgreSQL wire |
| QuestDB HTTP | 9000 | REST API |
| Redis | 6379 | Redis protocol |
| Kafka | 9092 | Kafka protocol |
| Caddy | 80, 443 | HTTP/HTTPS reverse proxy |

### Cache Layers

```
┌─────────────────────────────────────────────────┐
│                 Redis (6379)                    │
│  ├─ Current Values (per point_sequence_id)     │
│  └─ Idempotency Keys (deduplication)           │
└─────────────────────────────────────────────────┘
                        │
┌─────────────────────────────────────────────────┐
│           PointLookupService (In-Memory)        │
│  ├─ _bySequenceId: Dictionary<long, Point>     │
│  ├─ _byId: Dictionary<Guid, Point>             │
│  ├─ _byName: Dictionary<string, Point>         │
│  └─ _byDataSource: Dictionary<Guid, List>      │
│  Refreshes every 5 minutes                      │
└─────────────────────────────────────────────────┘
```

---

## 🔄 Data Ingestion Pipeline

### Message Flow

```
Connector (PI/Replay/Weather/EIA)
    │
    ▼ Kafka Message (topic: naia.datapoints)
    │
    ├─ PointSequenceId = 0 (needs lookup)
    │   └─ PointName provided for resolution
    │
    ▼ IngestionPipeline.EnrichPointsAsync()
    │
    ├─ Resolves PointName → PointSequenceId via PointLookupService
    │
    ▼ QuestDB ILP Write
    │
    └─ point_data,point_id={SequenceId}i value={Value} {Timestamp}
```

### Enrichment Logic

```csharp
// If SequenceId is 0, resolve from PointName
if (point.PointSequenceId == 0 && !string.IsNullOrEmpty(point.PointName))
{
    var lookup = await _pointLookup.GetByNameAsync(point.PointName);
    if (lookup != null && lookup.HasSequenceId)
    {
        point.PointSequenceId = lookup.SequenceId;
    }
}
```

---

## 🧠 Pattern Engine Jobs

### Job Pipeline (Hangfire)

| Job | Schedule | Function |
|-----|----------|----------|
| BehavioralAnalysisJob | Hourly | Calculate point statistics |
| CorrelationAnalysisJob | Hourly | Find correlated point pairs |
| ClusterDetectionJob | Daily | Group related points |
| PatternMatchingJob | Hourly | Match known patterns |
| PatternLearningJob | Daily | Generate suggestions |

### SQL Column References

```sql
-- Pattern Engine uses behavioral_stats table
SELECT point_id, point_id_seq, point_name, ...
FROM behavioral_stats

-- Main points table
SELECT id, point_sequence_id, name, ...
FROM points
```

---

## ⚠️ Common Issues & Fixes

### Issue: Empty Data Returns

**Symptom:** API returns `{ data: [], count: 0 }`

**Root Causes:**
1. `point_sequence_id` is NULL after point creation
2. PointLookupService filters out points without SequenceId
3. Ingestion can't resolve PointName → SequenceId

**Fix:**
1. Run migration: `04-fix-column-name.sql`
2. Deploy updated PointLookupService (caches all points)
3. Restart services to refresh cache

### Issue: Pattern Engine Type Errors

**Symptom:** `InvalidCastException` on GetInt32 for BIGINT

**Fix:** Use `GetInt64()` for all `point_sequence_id`/`point_id_seq` columns

### Issue: Schema Name Mismatch

**Symptom:** Queries fail with "column not found"

**Fix:** Standardize on `point_sequence_id` (EF Core naming)

---

## 📁 Key Files

| File | Purpose |
|------|---------|
| `src/Naia.Infrastructure/Persistence/NaiaDbContext.cs` | EF Core schema definition |
| `src/Naia.Infrastructure/Persistence/PointLookupService.cs` | Point cache service |
| `src/Naia.Domain/Entities/Point.cs` | Point entity with ID properties |
| `init-scripts/postgres/01-init-schema.sql` | Initial PostgreSQL schema |
| `init-scripts/postgres/04-fix-column-name.sql` | Migration for column rename |

---

## 🚀 Deployment Checklist

- [ ] Run `04-fix-column-name.sql` on production PostgreSQL
- [ ] Deploy API with updated PointLookupService
- [ ] Restart naia-api.service
- [ ] Verify cache logs show points "with SequenceId" and "pending"
- [ ] Test data flow: Trends page should show historical data
