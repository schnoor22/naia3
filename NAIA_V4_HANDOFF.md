# NAIA v4 Handoff Document
> Generated: January 15, 2026 | From: v3 Machine | To: v4 Machine

---

## 🎯 What is NAIA?

**NAIA (Neural Autonomous Industrial Agent)** is an industrial data historian framework that learns from operational data. Think OSIsoft PI, but AI-native and self-managing.

**Core Innovation**: The system is 100% built by Claude. The long-term vision is for Claude to eventually manage, deploy, and evolve NAIA autonomously.

---

## 🏗️ Architecture (The Three-Database Design)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              DATA SOURCES                                    │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐      │
│  │ OPC UA   │  │  PI AF   │  │   CSV    │  │ Weather  │  │  Modbus  │      │
│  │ Servers  │  │  Server  │  │  Files   │  │   APIs   │  │  Devices │      │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘      │
│       │             │             │             │             │             │
│       └─────────────┴──────┬──────┴─────────────┴─────────────┘             │
│                            │                                                 │
│                            ▼                                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     NAIA.CONNECTORS                                  │   │
│  │  Normalize → Validate → Enrich → Produce to Kafka                   │   │
│  └──────────────────────────────┬──────────────────────────────────────┘   │
│                                 │                                           │
│                                 ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                         KAFKA                                        │   │
│  │              Topic: naia.datapoints (12 partitions)                  │   │
│  │              Partition Key: DataSourceId                             │   │
│  └──────────────────────────────┬──────────────────────────────────────┘   │
│                                 │                                           │
│                                 ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    NAIA.INGESTION                                    │   │
│  │  Consume → Dedupe (Redis) → Write (QuestDB) → Cache (Redis)         │   │
│  └──────────────────────────────┬──────────────────────────────────────┘   │
│                                 │                                           │
│                    ┌────────────┼────────────┐                              │
│                    ▼            ▼            ▼                              │
│  ┌──────────────────┐  ┌──────────────┐  ┌──────────────┐                  │
│  │   POSTGRESQL     │  │   QUESTDB    │  │    REDIS     │                  │
│  │   (Metadata)     │  │ (Time-Series)│  │   (Cache)    │                  │
│  │                  │  │              │  │              │                  │
│  │ • Points         │  │ • point_data │  │ • Current    │                  │
│  │ • DataSources    │  │   (billions  │  │   values     │                  │
│  │ • Patterns       │  │    of rows)  │  │ • Idempotency│                  │
│  │ • Correlations   │  │              │  │   keys       │                  │
│  └──────────────────┘  └──────────────┘  └──────────────┘                  │
│         │                    │                  │                           │
│         └────────────────────┼──────────────────┘                           │
│                              ▼                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        NAIA.API                                      │   │
│  │  REST + SignalR + Pattern Engine + Knowledge Base                   │   │
│  └──────────────────────────────┬──────────────────────────────────────┘   │
│                                 │                                           │
│                                 ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        NAIA.WEB                                      │   │
│  │  SvelteKit Dashboard • Real-time Charts • Pattern Visualization     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 🔑 Critical Concepts (MUST UNDERSTAND)

### The Two-ID System
Every point has TWO identifiers:
```
PostgreSQL: id (UUID) ←→ point_sequence_id (BIGINT)
QuestDB:    point_id (LONG) = PostgreSQL's point_sequence_id
```

**WHY?** UUIDs are 128-bit, terrible for time-series indexing. QuestDB uses LONG for efficient partitioning.

**RULE**: When joining PostgreSQL ↔ QuestDB, always use `point_sequence_id`, never `id`.

### The Write Path
```
Connector → Kafka → Ingestion → QuestDB (ILP port 9009) + Redis (current value)
```
**NEVER** write to QuestDB via PostgreSQL wire protocol (8812). It's for reads only.

### The Read Path  
```
API → QuestDB (port 8812 PG wire) → Aggregate → Return JSON
API → Redis (current values only, sub-millisecond)
API → PostgreSQL (metadata, patterns, correlations)
```

---

## 🚨 V3 Mistakes (NEVER REPEAT)

### 1. Path Confusion
```
❌ WRONG: Put index.html in /opt/naia/publish/  (API serves it as HTML instead of JSON!)
✅ RIGHT: API DLLs → /opt/naia/publish/
           Web files → /opt/naia/build/
```

### 2. Missing SaveChanges
```csharp
// ❌ V3 BUG: Points added but never saved
await _pointRepository.AddAsync(point, ct);
// Missing: await _pointRepository.SaveChangesAsync(ct);

// ✅ V4: Always explicit save
await _pointRepository.AddAsync(point, ct);
await _pointRepository.SaveChangesAsync(ct);
```

### 3. NodeId Parsing
```csharp
// ❌ WRONG: Doesn't parse namespace notation
var nodeId = new NodeId("ns=2;s=TAG_NAME", 1);

// ✅ RIGHT: Parses "ns=2;s=..." properly
var nodeId = NodeId.Parse("ns=2;s=TAG_NAME");
```

### 4. Race Conditions
OPC connector connected before PointLookupService loaded cache → "No points found"

**V4 FIX**: Implement proper startup ordering with health checks.

### 5. Configuration Sprawl
V3 had: `appsettings.json`, `appsettings.Development.json`, `appsettings.production.json`, `appsettings.ingestion.json`, `appsettings.GenericCsvReplay.json`, `appsettings.CsvReplay.Ingestion.json`

**V4**: Only THREE files:
- `appsettings.json` (defaults)
- `appsettings.Development.json` (local)
- `appsettings.Production.json` (server)

---

## 📁 V4 Server Structure (Clean Slate)

```
/opt/naia/
├── api/
│   ├── current/          → symlink to active release
│   ├── releases/
│   │   └── 2026-01-15/   → timestamped deployments
│   └── config/
│       └── appsettings.Production.json
│
├── web/
│   ├── current/          → symlink to active release
│   └── releases/
│
├── ingestion/
│   ├── current/
│   ├── releases/
│   └── config/
│
├── data/
│   └── kelmarsh/         → CSV data files
│
├── pki/                   → OPC UA certificates
├── logs/                  → Centralized logs
└── backups/               → Database backups
```

**Key Principle**: `current/` is ALWAYS a symlink. Deploy to `releases/`, then atomic symlink swap.

---

## 📁 V4 Local Structure

```
C:\dev\naia\
├── src\
│   ├── Naia.Domain\           → Entities, no dependencies
│   ├── Naia.Application\      → Interfaces, DTOs
│   ├── Naia.Infrastructure\   → PostgreSQL, QuestDB, Redis, Kafka
│   ├── Naia.Connectors\       → OPC UA, PI, CSV, Weather
│   ├── Naia.Api\              → REST + SignalR
│   ├── Naia.Ingestion\        → Kafka consumer worker
│   ├── Naia.PatternEngine\    → Behavioral analysis
│   └── Naia.Web\              → SvelteKit frontend
│
├── tests\
│   ├── Naia.Domain.Tests\
│   ├── Naia.Integration.Tests\  → Full pipeline tests
│   └── Naia.Api.Tests\
│
├── scripts\
│   ├── deploy-api.ps1
│   ├── deploy-web.ps1
│   └── deploy-ingestion.ps1
│
├── docker-compose.yml          → Local Kafka, QuestDB, PostgreSQL, Redis
├── NAIA_V4_HANDOFF.md          → THIS FILE
└── .github\
    └── copilot-instructions.md → Claude context
```

---

## 🚀 V4 Onboarding Sequence

### Step 1: Environment Setup
```powershell
# Set permanent environment variables
[Environment]::SetEnvironmentVariable("NAIA_SERVER", "37.27.189.86", "User")
[Environment]::SetEnvironmentVariable("NAIA_SSH_USER", "root", "User")
[Environment]::SetEnvironmentVariable("NAIA_LOCAL", "C:\dev\naia", "User")
```

### Step 2: First Claude Conversation
```markdown
# Starting NAIA v4

I'm bootstrapping a new development environment for NAIA.
This is an industrial data historian built 100% by Claude.

## Attached: NAIA_V4_HANDOFF.md
[paste this entire document]

## First Task
Before any coding, confirm you understand:
1. The three-database architecture
2. The two-ID system (UUID vs SequenceId)
3. The v3 mistakes to avoid

Then we'll proceed layer by layer.
```

### Step 3: Introduce Code Layers
Order matters:
1. `Naia.Domain` - Pure entities, no dependencies
2. `Naia.Application` - Interfaces (contracts)
3. `ARCHITECTURE.md` - How things connect
4. `Naia.Infrastructure` - Implementations
5. `Naia.Api/Program.cs` - Composition root
6. Specific modules as needed

---

## 🔧 Production Server Details

**Server**: 37.27.189.86 (Hetzner, Ubuntu 22.04, 16GB RAM)
**Domain**: app.naia.run (Caddy reverse proxy with auto HTTPS)

### Services (systemd)
```bash
naia-api.service        → /opt/naia/api/current/Naia.Api.dll
naia-ingestion.service  → /opt/naia/ingestion/current/Naia.Ingestion.dll
```

### Ports
```
5000  - NAIA API (internal, behind Caddy)
443   - HTTPS (Caddy)
5432  - PostgreSQL
9000  - QuestDB HTTP
8812  - QuestDB PostgreSQL wire
9009  - QuestDB ILP (writes)
9092  - Kafka
6379  - Redis
4840  - OPC UA Simulator
```

### Docker Containers
```
naia-postgres   - PostgreSQL 15
naia-questdb    - QuestDB
naia-kafka      - Kafka (KRaft mode, no Zookeeper)
naia-redis      - Redis
```

---

## 📊 Current Data Sources

| ID | Name | Type | Points |
|----|------|------|--------|
| 11111111-... | PI Server | PiAf | 4,265 |
| 22222222-... | Weather API | Weather | 1,751 |
| 33333333-... | Kelmarsh Wind | CsvReplay | 5,549 |
| 44444444-... | Test Source | Manual | 5 |
| 77777777-... | Brixton Solar | OpcUa | 3,880 (not saved - v3 bug) |

---

## 🎯 V4 Priorities (In Order)

1. **Fix Repository Pattern** - Unit of Work with explicit SaveChanges
2. **Deployment Scripts** - Foolproof, one-command deploy with rollback
3. **Service Startup Ordering** - Health checks, dependencies
4. **Integration Tests** - Full pipeline verification before deploy
5. **Configuration Cleanup** - Three files max
6. **Documentation** - Self-documenting for Claude continuity

---

## 🔐 Secrets (Store Securely)

```
PostgreSQL: naia / [password in 1Password]
Redis: no auth (internal only)
QuestDB: no auth (internal only)
Kafka: no auth (internal only)
SSH: root@37.27.189.86 (key-based)
```

---

## 🎨 Frontend (Naia.Web)

**Framework**: SvelteKit with TypeScript
**Build Output**: Static site (SSG) served by Caddy
**Real-time**: SignalR connection to API

### Key Routes
```
/                    - System overview, database health
/points              - Point browser with search
/sources             - Data source management
/patterns            - Pattern analysis results
/correlations        - Cross-point correlations
/health              - System diagnostics
/coral               - Real-time data visualization
```

### Build & Deploy
```bash
cd src/Naia.Web
npm install
npm run build       # Output: build/ directory
# Deploy: scp build/* root@server:/opt/naia/web/current/
```

### SignalR Topics
- `DataPointUpdate` - Real-time point values
- `PatternDetected` - New pattern found
- `SystemHealth` - Health check updates

---

## 🧠 Pattern Engine

**Purpose**: Autonomous behavioral analysis and optimization suggestions

### What It Does
1. **Behavioral Stats**: Mean, stddev, change rate per point (15min windows)
2. **Correlation Detection**: Pearson coefficient between point pairs
3. **Pattern Recognition**: Anomalies, steady states, oscillations
4. **Optimization Suggestions**: Energy savings, operational improvements

### Tables
```sql
point_patterns          - Detected patterns per point
pattern_correlations    - Cross-point relationships
behavioral_stats        - Statistical metrics
optimization_suggestions - AI-generated recommendations
```

### Trigger
Pattern engine runs every 5 minutes via:
```
POST /api/patterns/jobs/full-pipeline
```

**V4 TODO**: Make this autonomous, no manual trigger needed.

---

## 🚀 Complete Deployment Workflow

### Prerequisites
```powershell
# Set environment variables (one time)
[Environment]::SetEnvironmentVariable("NAIA_SERVER", "37.27.189.86", "User")
[Environment]::SetEnvironmentVariable("NAIA_SSH_USER", "root", "User")
```

### Deploy API (With Rollback Support)
```powershell
# Build
cd C:\dev\naia
dotnet publish src/Naia.Api/Naia.Api.csproj -c Release -o deploy/api

# Deploy
$timestamp = Get-Date -Format "yyyy-MM-dd-HHmm"
scp -r deploy/api/* root@37.27.189.86:/opt/naia/api/releases/$timestamp/

# Atomic switch
ssh root@37.27.189.86 "ln -sfn /opt/naia/api/releases/$timestamp /opt/naia/api/current && systemctl restart naia-api"

# Rollback if needed
ssh root@37.27.189.86 "ln -sfn /opt/naia/api/releases/[previous] /opt/naia/api/current && systemctl restart naia-api"
```

### Deploy Ingestion
```powershell
dotnet publish src/Naia.Ingestion/Naia.Ingestion.csproj -c Release -o deploy/ingestion
$timestamp = Get-Date -Format "yyyy-MM-dd-HHmm"
scp -r deploy/ingestion/* root@37.27.189.86:/opt/naia/ingestion/releases/$timestamp/
ssh root@37.27.189.86 "ln -sfn /opt/naia/ingestion/releases/$timestamp /opt/naia/ingestion/current && systemctl restart naia-ingestion"
```

### Deploy Web
```powershell
cd src/Naia.Web
npm run build
$timestamp = Get-Date -Format "yyyy-MM-dd-HHmm"
scp -r build/* root@37.27.189.86:/opt/naia/web/releases/$timestamp/
ssh root@37.27.189.86 "ln -sfn /opt/naia/web/releases/$timestamp /opt/naia/web/current"
# No restart needed - Caddy serves static files
```

---

## 🐳 Local Development (Docker Compose)

**File**: `docker-compose.yml` (already in v3, keep it)

### Start Infrastructure
```powershell
docker-compose up -d
```

**Services Started**:
- PostgreSQL: `localhost:5432` (naia/naia123)
- QuestDB: `localhost:9000` (HTTP), `localhost:8812` (PG wire)
- Kafka: `localhost:9092`
- Redis: `localhost:6379`

### Initialize Databases
```bash
# PostgreSQL migrations run automatically on first API start
# QuestDB: Manual table creation on first use
```

---

## ⚙️ Server Configuration Files

### Systemd Service: naia-api.service
```ini
[Unit]
Description=NAIA Industrial Historian API
After=network.target docker.service

[Service]
Type=notify
WorkingDirectory=/opt/naia/api/current
ExecStart=/usr/bin/dotnet /opt/naia/api/current/Naia.Api.dll
Restart=always
RestartSec=10
User=root
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="DOTNET_PRINT_TELEMETRY_MESSAGE=false"

[Install]
WantedBy=multi-user.target
```

### Systemd Service: naia-ingestion.service
```ini
[Unit]
Description=NAIA Ingestion Worker
After=network.target docker.service naia-api.service

[Service]
Type=exec
WorkingDirectory=/opt/naia/ingestion/current
ExecStart=/usr/bin/dotnet /opt/naia/ingestion/current/Naia.Ingestion.dll
Restart=always
RestartSec=10
User=root
Environment="DOTNET_ENVIRONMENT=Production"

[Install]
WantedBy=multi-user.target
```

### Caddy Configuration (app.naia.run)
```
app.naia.run {
    # API reverse proxy
    handle /api/* {
        reverse_proxy localhost:5000
    }
    
    # SignalR WebSocket
    handle /hubs/* {
        reverse_proxy localhost:5000 {
            header_up Host {host}
            header_up Upgrade {>Upgrade}
            header_up Connection {>Connection}
        }
    }
    
    # Static web files
    handle /* {
        root * /opt/naia/web/current
        try_files {path} /index.html
        file_server
    }
    
    # Security headers
    header {
        X-Frame-Options "SAMEORIGIN"
        X-Content-Type-Options "nosniff"
        Referrer-Policy "strict-origin-when-cross-origin"
    }
}
```

---

## 🔌 OPC UA Simulator Details

**Location**: `/opt/naia/opc-simulator/`
**Endpoint**: `opc.tcp://localhost:4840/NAIA`
**Namespace**: `http://naia.energy/OpcSimulator` (ns=2)

### Brixton Solar Structure
```
Objects (ns=0;i=85)
└── bxs1 (ns=2;s=bxs1) "Brixton Solar"
    └── BUXOM (ns=2;s=BUXOM)
        └── A01 (ns=2;s=A01)
            └── F1A (ns=2;s=F1A)
                └── INV01 (ns=2;s=INV01)
                    └── inv01 (ns=2;s=inv01)
                        ├── F1H-INV01 (ns=2;s=F1H-INV01)
                        │   ├── E_Day (ns=2;s=F1H-INV01.E_Day)
                        │   ├── PAC (ns=2;s=F1H-INV01.PAC)
                        │   └── ... (3,880 total points)
```

### Start OPC Simulator
```bash
cd /opt/naia/opc-simulator
NAIA_SITE_ID=bxs1 NAIA_SITE_NAME="Brixton Solar" NAIA_SITE_TYPE=solar nohup dotnet Naia.OpcSimulator.dll > /var/log/opc-sim.log 2>&1 &
```

---

## 🧪 Testing Commands

### Check API Health
```bash
curl http://localhost:5000/api/health
```

### Query Point Data
```bash
# Recent data
curl 'http://localhost:5000/api/points/123/data?hours=1'

# Current value
curl 'http://localhost:5000/api/points/123/current'
```

### Check Kafka
```bash
docker exec naia-kafka kafka-console-consumer \
  --bootstrap-server localhost:9092 \
  --topic naia.datapoints \
  --max-messages 10
```

### Check QuestDB
```bash
curl 'http://localhost:9000/exec?query=SELECT COUNT(*) FROM point_data'
```

### Check PostgreSQL
```bash
docker exec naia-postgres psql -U naia -d naia -c "SELECT COUNT(*) FROM points"
```

---

## 📊 Performance Expectations

| Metric | Target | Notes |
|--------|--------|-------|
| API Response Time | <100ms | For point queries |
| Kafka Ingestion Rate | 10K/sec | Per partition |
| QuestDB Write Rate | 1M rows/sec | Via ILP |
| Redis Latency | <1ms | Current values |
| Pattern Engine | 5min | Full pipeline |
| Web UI Load Time | <2s | Initial load |

---

## 🚨 Common V3 Issues (With Fixes)

### Issue: API Returns HTML Instead of JSON
**Cause**: Static files in `/opt/naia/api/current/wwwroot/`
**Fix**: Remove `wwwroot/` from API deployment
```bash
rm -rf /opt/naia/api/current/wwwroot
```

### Issue: OPC Points Not Saving
**Cause**: Missing `SaveChangesAsync()` call
**Fix**: Already fixed in v4 code (see commit 3f574cc)

### Issue: OPC Connector Says "No Points Found"
**Cause**: Race condition - connector starts before PointLookupService cache loads
**Fix V4**: Add startup health checks, wait for dependencies

### Issue: Wrong NodeId Namespace
**Cause**: Using `new NodeId("ns=2;s=TAG", 1)` instead of `NodeId.Parse()`
**Fix**: Already fixed in v4 code (see commit 3f574cc)

---

## �️ V4 Claude Conversations: Layer-by-Layer Guide

### Why This Matters
Each layer has **specific pitfalls** that caused v3 bugs. This guide tells you exactly what to say to Claude at each stage, what to watch for, and how to verify understanding.

---

### 🔴 HIGH ATTENTION LAYERS

These layers caused the most v3 bugs. Spend extra time here.

---

### Conversation 1: Domain Layer (EASY)
**Risk Level**: 🟢 Low | **Time**: 15 minutes

**What to say:**
```markdown
# NAIA v4 - Domain Layer

We're starting v4 from scratch. This is the Domain layer - pure C# entities with no dependencies.

## Core Entities:
1. Point - A data point (sensor, calculated value, etc.)
2. DataSource - Where points come from (OPC server, PI, CSV, etc.)
3. Pattern - Detected behavioral patterns
4. Correlation - Relationships between points

## CRITICAL: The Two-ID System
Every Point has TWO IDs:
- `Id` (Guid) - Used in PostgreSQL for EF Core relationships
- `PointSequenceId` (long?) - Assigned by database IDENTITY column, used in QuestDB

This is NOT optional. QuestDB can't efficiently index UUIDs.

## Your Task:
Create the Point entity with:
- Factory method `Point.Create()` that validates
- Private constructor (force factory usage)
- Immutable properties where possible

Show me the code, then explain WHY the two-ID system exists.
```

**Watch for:**
- ✅ Factory method returns `Result<Point, Error>` not just Point
- ✅ `PointSequenceId` is `long?` (nullable until DB assigns it)
- ❌ If Claude uses `int` for SequenceId - PostgreSQL BIGINT is `long`

**Verify understanding:**
> "What happens if I try to query QuestDB using the Point.Id (Guid) instead of PointSequenceId?"

Expected answer: "QuestDB's point_data table uses point_id which maps to PointSequenceId. Using the Guid would find nothing - no rows would match."

---

### Conversation 2: Application Layer (MEDIUM)
**Risk Level**: 🟡 Medium | **Time**: 30 minutes

**What to say:**
```markdown
# NAIA v4 - Application Layer

Now we define interfaces (contracts) that Infrastructure will implement.

## CRITICAL V3 BUG - The SaveChanges Problem:

In v3, we had this bug:
```csharp
await _pointRepository.AddAsync(point, ct);
// Forgot SaveChangesAsync() - point never saved to DB!
```

This is EF Core's Unit of Work pattern - changes aren't committed until SaveChanges.

## Design Options:
1. **Keep SaveChanges explicit** - Handler MUST call it (v3 style, but easy to forget)
2. **Auto-save on Add/Update** - Repository handles it (simple but less control)
3. **Type-state pattern** - Add returns UnsavedPoint, must call Save to get SavedPoint

Which do you recommend for v4 and WHY?

## Then Create:
- IPointRepository interface
- IDataSourceRepository interface  
- ITimeSeriesWriter interface (QuestDB writes)
- IDataPointProducer interface (Kafka produces)

Make sure whatever pattern you choose makes it IMPOSSIBLE to forget saves.
```

**Watch for:**
- ✅ Clear reasoning about SaveChanges strategy
- ✅ Interface methods are async with CancellationToken
- ❌ If `SaveChangesAsync` is on interface but no enforcement mechanism
- ❌ If `GetInt32` used anywhere - SequenceId is BIGINT = `long`

**Push back if needed:**
> "How does your design prevent a developer from calling AddAsync without SaveChangesAsync?"

If Claude says "documentation" or "code review" - reject it. V4 needs compile-time or runtime enforcement.

---

### Conversation 3: Infrastructure - Repositories (🔴 HIGH ATTENTION)
**Risk Level**: 🔴 HIGH | **Time**: 45 minutes

**What to say:**
```markdown
# NAIA v4 - Infrastructure Layer: Repositories

This is where v3 bugs LIVED. Pay close attention.

## THE THREE BUGS:

### Bug 1: SaveChanges
```csharp
// V3 BUG - forgot to call SaveChanges
await _pointRepository.AddAsync(point, ct);
// Point NOT in database!
```

### Bug 2: GetInt32 vs GetInt64
```csharp
// V3 BUG - wrong type for BIGINT column
var sequenceId = reader.GetInt32(1);  // CRASH!
// Correct:
var sequenceId = reader.GetInt64(1);  // point_sequence_id is BIGINT
```

### Bug 3: Race Condition
PointLookupService refreshes cache every 5 minutes.
If OPC connector starts before cache loads → "No points found"

## Your Task:
1. Implement PointRepository with YOUR chosen SaveChanges pattern
2. Show me the EF Core mapping for Point → points table
3. Explain how PointSequenceId gets assigned (database IDENTITY column)
4. How would you prevent the race condition?

## PostgreSQL Schema:
```sql
CREATE TABLE points (
    id UUID PRIMARY KEY,
    point_sequence_id BIGINT GENERATED ALWAYS AS IDENTITY,
    name TEXT NOT NULL,
    data_source_id UUID NOT NULL REFERENCES data_sources(id),
    value_type TEXT NOT NULL,
    engineering_units TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    is_deleted BOOLEAN DEFAULT FALSE
);
```

Note: point_sequence_id is GENERATED ALWAYS - you CAN'T set it, the database assigns it on INSERT.
```

**Watch for:**
- ✅ After SaveChanges, entity needs reload to get SequenceId
- ✅ Uses `GetInt64` for BIGINT columns, never `GetInt32`
- ✅ Proposes startup dependency (health check or explicit wait)
- ❌ Tries to set PointSequenceId in code - database assigns it
- ❌ Uses `int` anywhere for SequenceId

**Critical verification:**
```csharp
// After this code, what is point.PointSequenceId?
var point = Point.Create("TAG_001", PointValueType.Double, dataSourceId);
await _context.Points.AddAsync(point);
await _context.SaveChangesAsync();
Console.WriteLine(point.PointSequenceId);  // What prints?
```

Expected answer: "It prints the database-assigned value because EF Core reloads IDENTITY columns after SaveChanges. But ONLY if the entity is still tracked."

---

### Conversation 4: Infrastructure - QuestDB (🔴 HIGH ATTENTION)
**Risk Level**: 🔴 HIGH | **Time**: 45 minutes

**What to say:**
```markdown
# NAIA v4 - QuestDB Integration

QuestDB stores time-series data. This is performance-critical.

## CRITICAL: Write Path vs Read Path

### WRITE (Ingestion → QuestDB):
- Protocol: ILP (InfluxDB Line Protocol)
- Port: 9009 (TCP) or 9000/write (HTTP)
- Format: `point_data,point_id=123 value=45.67 1642012800000000000`
- NO PostgreSQL wire protocol for writes!

### READ (API → QuestDB):
- Protocol: PostgreSQL wire protocol (Npgsql)
- Port: 8812
- Standard SQL: `SELECT * FROM point_data WHERE point_id = 123`

## V3 Bug:
Someone tried to write to QuestDB via port 8812 (PG wire).
It "worked" but was 100x slower and broke batching.

## Schema:
```sql
-- QuestDB table (no CREATE - uses ILP auto-creation)
point_data (
    point_id LONG,      -- Maps to PostgreSQL point_sequence_id
    value DOUBLE,
    timestamp TIMESTAMP
) timestamp(timestamp) PARTITION BY DAY;
```

## Your Task:
1. Implement QuestDbWriter that uses ILP (port 9009)
2. Implement QuestDbReader that uses PG wire (port 8812)  
3. Show me the ILP message format with proper timestamp (nanoseconds)
4. How do you batch writes for performance?

## Important: Point ID Mapping
QuestDB `point_id` = PostgreSQL `point_sequence_id` (NOT the UUID!)
```

**Watch for:**
- ✅ Separates Writer (ILP) from Reader (SQL)
- ✅ Timestamps in nanoseconds for ILP
- ✅ Uses `point_sequence_id` mapping, not UUID
- ❌ Uses port 8812 for writes
- ❌ Timestamps in milliseconds (QuestDB ILP uses nanos)

**Verify:**
> "Show me the exact ILP message for writing value 42.5 for point_id 789 at 2026-01-15T10:30:00Z"

Expected: `point_data,point_id=789 value=42.5 1736936200000000000` (nanoseconds since epoch)

---

### Conversation 5: Infrastructure - Kafka (MEDIUM)
**Risk Level**: 🟡 Medium | **Time**: 30 minutes

**What to say:**
```markdown
# NAIA v4 - Kafka Integration

Kafka is the message bus between Connectors and Ingestion.

## Topic Structure:
- Topic: `naia.datapoints`
- Partitions: 12
- Partition Key: DataSourceId (ensures ordering per source)

## Message Format:
```json
{
  "pointId": "uuid",
  "pointSequenceId": 789,
  "value": 42.5,
  "timestamp": "2026-01-15T10:30:00Z",
  "quality": "Good",
  "dataSourceId": "uuid"
}
```

## Exactly-Once Semantics:
Producer: `enable.idempotence=true`, `acks=all`
Consumer: Manual commit AFTER successful write to QuestDB

## V3 Idempotency:
Used Redis to track processed Kafka offsets.
Key format: `kafka:{topic}:{partition}:{offset}`
TTL: 7 days

## Your Task:
1. Implement KafkaProducer with idempotency
2. Implement KafkaConsumer with manual commit
3. How do you handle consumer rebalancing?
4. What happens if QuestDB write fails AFTER Kafka message consumed?
```

**Watch for:**
- ✅ Manual commit after DB write success
- ✅ Partition key is DataSourceId (for ordering)
- ✅ Handles the "consumed but not written" scenario
- ❌ Auto-commit enabled (loses exactly-once)

---

### Conversation 6: OPC UA Connector (🔴 HIGH ATTENTION)
**Risk Level**: 🔴 HIGH | **Time**: 60 minutes

**What to say:**
```markdown
# NAIA v4 - OPC UA Connector

This connector caused MULTIPLE v3 bugs. Read carefully.

## BUG 1: NodeId Parsing
```csharp
// ❌ WRONG - doesn't parse namespace notation
var nodeId = new NodeId("ns=2;s=TAG_NAME", 1);
// This creates: namespace=1, identifier="ns=2;s=TAG_NAME" (the WHOLE string!)

// ✅ RIGHT - parses the notation
var nodeId = NodeId.Parse("ns=2;s=TAG_NAME");
// This creates: namespace=2, identifier="TAG_NAME"
```

## BUG 2: Missing SaveChanges
When discovering OPC nodes and creating points:
```csharp
foreach (var node in discoveredNodes) {
    var point = Point.Create(node.DisplayName, ...);
    await _pointRepository.AddAsync(point, ct);
}
// ❌ BUG: Forgot SaveChangesAsync - no points saved!
```

## BUG 3: Tree Structure
OPC servers have a hierarchical structure. 
We used `parent.AddChild(node)` to build the tree.
In v3, we forgot some AddChild calls - nodes appeared but had no parent.

## OPC UA Basics:
- Endpoint: `opc.tcp://server:4840/path`
- Namespaces: ns=0 is OPC UA standard, ns=2+ are vendor-specific
- NodeIds: `ns=2;s=TAG_NAME` or `ns=2;i=12345`
- Browse: Start at Objects (ns=0;i=85), recurse through hierarchy

## Your Task:
1. Create OpcUaNodeId value object that FORCES Parse (not constructor)
2. Show discovery flow that saves points properly
3. How do you handle the namespace index extraction?
4. What's your strategy for building the tree?
```

**Watch for:**
- ✅ Creates wrapper that forces `Parse()` usage
- ✅ SaveChanges called after batch discovery
- ✅ Properly extracts namespace from notation string
- ❌ Uses `new NodeId(string, ushort)` constructor
- ❌ Hardcodes namespace index

**Critical test:**
```csharp
// What does this return?
var nodeId = new NodeId("ns=2;s=TURB01.Power", 1);
Console.WriteLine($"ns={nodeId.NamespaceIndex}, id={nodeId.Identifier}");
```

Expected: "ns=1, id=ns=2;s=TURB01.Power" - WRONG! That's the bug.
Claude must explain why `NodeId.Parse()` is the only correct approach.

---

### Conversation 7: API Layer (MEDIUM)
**Risk Level**: 🟡 Medium | **Time**: 30 minutes

**What to say:**
```markdown
# NAIA v4 - API Layer

V3's Program.cs was 2,430 lines. This is unacceptable.

## V4 Rules:
- Program.cs: < 50 lines (just startup)
- Each endpoint file: < 200 lines
- One file per feature area (points, datasources, patterns)

## V3 God Object Problem:
```
Program.cs (2,430 lines):
- 200+ endpoint definitions
- Middleware configuration
- Service registration
- SignalR setup
- Pattern engine triggers
- OPC discovery
- CSV replay
- Everything!
```

## Your Task:
1. Show me a 50-line Program.cs that defers to extension methods
2. Create PointEndpoints.cs with CRUD operations
3. How do you organize endpoints by feature?
4. Show SignalR hub registration
```

**Watch for:**
- ✅ Uses extension methods for service registration
- ✅ Endpoint files are organized by feature
- ✅ No business logic in endpoints (handlers do that)
- ❌ Program.cs over 100 lines
- ❌ SQL queries in endpoints

---

### Conversation 8: Ingestion Worker (MEDIUM)
**Risk Level**: 🟡 Medium | **Time**: 30 minutes

**What to say:**
```markdown
# NAIA v4 - Ingestion Worker

This is a standalone worker that consumes Kafka and writes to QuestDB.

## Pipeline:
Kafka → Validate → Transform → QuestDB (ILP) + Redis (current value)

## V3 Issue: Startup Dependencies
Worker started before PointLookupService was ready.
Couldn't resolve PointSequenceId from point names.

## Your Task:
1. Implement the ingestion pipeline
2. How do you wait for dependencies (PointLookupService)?
3. Show batch processing (don't write one message at a time)
4. What's the error handling if QuestDB is down?
```

**Watch for:**
- ✅ Startup health check for dependencies
- ✅ Batch processing (configurable batch size)
- ✅ Retry logic for QuestDB failures
- ✅ Uses PointSequenceId, not UUID

---

### Conversation 9: Web Frontend (EASY)
**Risk Level**: 🟢 Low | **Time**: 20 minutes

**What to say:**
```markdown
# NAIA v4 - Web Frontend

SvelteKit static site with SignalR real-time updates.

## Pages:
- / (Dashboard) - System health, recent activity
- /points - Browse and search points
- /sources - Data source management
- /patterns - Pattern analysis results

## SignalR Topics:
- DataPointUpdate - Real-time values
- PatternDetected - New patterns

## Build:
npm run build → static files → Caddy serves

## Your Task:
Show me the SignalR connection setup in Svelte.
How do you handle reconnection when WebSocket drops?
```

**Watch for:**
- ✅ Reconnection logic for SignalR
- ✅ Svelte stores for real-time data
- ✅ SSG (static site generation) configuration

---

### Conversation 10: Pattern Engine (MEDIUM)
**Risk Level**: 🟡 Medium | **Time**: 30 minutes

**What to say:**
```markdown
# NAIA v4 - Pattern Engine

Autonomous behavioral analysis. Runs every 5 minutes.

## What It Detects:
1. Behavioral stats (mean, stddev, change rate)
2. Correlations between points (Pearson coefficient)
3. Anomalies (values outside normal range)
4. Optimization opportunities

## Pipeline:
Query QuestDB (15min window) → Calculate stats → Detect patterns → Store in PostgreSQL

## V4 Goal:
Make it truly autonomous - not triggered by API call.
Background service that runs on schedule.

## Your Task:
1. Show the pattern detection algorithm
2. How do you handle 10K+ points efficiently?
3. Where do correlations get stored?
4. How does the UI get notified of new patterns?
```

**Watch for:**
- ✅ Batches point processing (not one at a time)
- ✅ Uses QuestDB aggregation functions
- ✅ Pushes notifications via SignalR
- ❌ Loads all point data into memory

---

### Conversation 11: Deployment (🔴 HIGH ATTENTION)  
**Risk Level**: 🔴 HIGH | **Time**: 45 minutes

**What to say:**
```markdown
# NAIA v4 - Deployment

V3 had path confusion that caused major bugs. Never again.

## V3 BUG:
Static files in /opt/naia/publish/ caused API to return HTML instead of JSON.
ASP.NET's UseStaticFiles() and MapFallbackToFile() intercepted API requests.

## V4 Structure:
```
/opt/naia/v4/
├── api/
│   ├── current -> releases/2026-01-15-1430/  (SYMLINK)
│   └── releases/2026-01-15-1430/
│       ├── Naia.Api.dll
│       ├── appsettings.json
│       └── (NO wwwroot!)
├── web/
│   ├── current -> releases/2026-01-15-1430/
│   └── releases/2026-01-15-1430/
│       ├── index.html
│       ├── _app/
│       └── (static files ONLY here)
└── ingestion/
    └── current -> releases/...
```

## Atomic Deployment:
1. Upload to releases/timestamp/
2. Symlink current → new release
3. Restart service
4. Health check
5. If unhealthy → rollback symlink

## Your Task:
1. Create deploy-api.ps1 script
2. Include health check verification
3. Include automatic rollback if health fails
4. Never put static files in API directory
```

**Watch for:**
- ✅ Symlink-based atomic deploys
- ✅ Health check after deploy
- ✅ Automatic rollback on failure
- ❌ Any static files in API directory
- ❌ Manual symlink commands (script it!)

---

## 🎯 Attention Priority Summary

| Layer | Risk | Key Issues | Time |
|-------|------|------------|------|
| Domain | 🟢 | Two-ID system, factory methods | 15 min |
| Application | 🟡 | SaveChanges pattern choice | 30 min |
| **Repositories** | 🔴 | SaveChanges, GetInt64, race conditions | 45 min |
| **QuestDB** | 🔴 | Write port, timestamps, point_id mapping | 45 min |
| Kafka | 🟡 | Exactly-once, manual commit | 30 min |
| **OPC UA** | 🔴 | NodeId.Parse, SaveChanges, tree building | 60 min |
| API | 🟡 | God object prevention, file size limits | 30 min |
| Ingestion | 🟡 | Startup dependencies, batching | 30 min |
| Web | 🟢 | SignalR reconnection | 20 min |
| Pattern Engine | 🟡 | Efficiency with 10K points | 30 min |
| **Deployment** | 🔴 | Path separation, atomic deploy, rollback | 45 min |

**Total**: ~7 hours of focused conversations

---

## 🚨 Red Flags to Watch For

If Claude says any of these, STOP and correct:

1. **"We'll document the SaveChanges requirement"** - No! Code must enforce it.
2. **"Using `new NodeId(notation, namespaceIndex)`"** - WRONG! Must use Parse().
3. **"point_sequence_id is an int"** - WRONG! It's BIGINT = long.
4. **"Write to QuestDB on port 8812"** - WRONG! Use ILP on 9009.
5. **"Timestamps in milliseconds"** - WRONG! QuestDB ILP uses nanoseconds.
6. **"Let services start in any order"** - NO! Need dependency ordering.
7. **"Put index.html next to the API DLL"** - CATASTROPHIC!

---

## ✅ Success Criteria Per Layer

Each layer is "done" when Claude can:

1. **Domain**: Explain why two IDs exist without prompting
2. **Application**: Justify chosen SaveChanges pattern with tradeoffs
3. **Repositories**: Show exact sequence from Add → SaveChanges → reload SequenceId
4. **QuestDB**: Write correct ILP message with nanosecond timestamp
5. **Kafka**: Explain what happens if QuestDB fails after consume
6. **OPC UA**: Parse "ns=2;s=TAG" and show namespace=2, identifier=TAG
7. **API**: Show Program.cs under 50 lines
8. **Ingestion**: Explain startup dependency mechanism
9. **Web**: Show SignalR reconnection code
10. **Patterns**: Explain batch processing strategy
11. **Deployment**: Show rollback scenario script

---



### Every New Claude Conversation:
1. **Paste this entire document first**
2. State your current focus (e.g., "implementing OPC connector")
3. Introduce code layers progressively (Domain → Application → Infrastructure)
4. Ask Claude to confirm understanding before proceeding

### V4 Development Order:
1. ✅ Domain entities (pure C#, no dependencies)
2. ✅ Application interfaces (contracts)
3. ✅ Infrastructure (repositories with explicit SaveChanges)
4. ✅ API (OPC connector + basic REST endpoints)
5. ✅ Ingestion worker (Kafka → QuestDB pipeline)
6. ✅ Web UI (real-time visualization)
7. ✅ Pattern Engine (autonomous analysis)

### Goal
Claude should be able to:
- Deploy NAIA to production
- Diagnose issues from logs
- Implement new features
- Optimize performance
- Evolve the architecture

**This is the future of industrial software: AI-native, self-managing, continuously learning.**

---

*Generated by Claude on the v3 machine. Everything you need is here. Good luck on v4!* 🚀
