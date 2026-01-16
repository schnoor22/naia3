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

## 📝 Final Notes

This document is the **single source of truth** for onboarding Claude to NAIA.

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
