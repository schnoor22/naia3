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

## 📝 Final Notes

This document is the **single source of truth** for onboarding Claude to NAIA.

Every time you start a new Claude conversation:
1. Paste this document first
2. State your current focus
3. Introduce code layers progressively

The goal: Claude should be able to deploy, diagnose, and evolve NAIA autonomously.

**This is the future of industrial software.**

---

*Generated by Claude on the v3 machine. Good luck on v4!* 🚀
