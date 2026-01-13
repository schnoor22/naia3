# NAIA QuestDB Investigation - Documentation Index

**Problem:** Trends page shows `count:0` and empty data arrays  
**Investigation Status:** ✅ COMPLETE  
**Date:** January 12, 2026

---

## 📋 Documents Created (5 Files)

All files are in the root directory: `c:\naia3\`

### 1. **QUESTDB_QUICK_REFERENCE.md** ⭐ START HERE
**Purpose:** One-page quick reference for immediate troubleshooting  
**Best For:** Quick diagnosis in 5 minutes  
**Contains:**
- Quick diagnosis procedure
- Root cause test sequence (5 tests)
- Why count=0 decision matrix
- Common errors and fixes
- Performance expectations
- One-minute health check
- Emergency recovery procedures

**Read Time:** 5 minutes  
**Use When:** You need to fix the issue NOW

---

### 2. **QUESTDB_QUICK_DEBUG_COMMANDS.md**
**Purpose:** Copy-paste commands for immediate testing  
**Best For:** Running commands without thinking  
**Contains:**
- 8 immediate tests (copy-paste ready)
- Deep dive component checks
- Log inspection commands
- Real-time monitoring setup (4-terminal dashboard)
- Root cause matrix (symptom → cause → check)
- Recovery procedures
- Expected normal values

**Read Time:** 10 minutes (reference while testing)  
**Use When:** Testing each component step-by-step

---

### 3. **QUESTDB_DATA_FLOW_INVESTIGATION.md**
**Purpose:** Comprehensive technical investigation document  
**Best For:** Understanding the complete architecture  
**Contains:**
- 16 detailed sections covering:
  - Complete data flow diagram
  - Configuration details (all 4 databases)
  - ILP protocol specification
  - History endpoint walkthrough
  - Point enrichment logic
  - Deduplication mechanism
  - Kafka consumer guarantees
  - QuestDB connection strings
  - Root cause checklist with 8 steps
  - Failure modes and recovery
  - Performance characteristics
  - Summary table of checkpoints
  - Log locations and analysis
  - Commands to check data directly
  - Key insights

**Read Time:** 30 minutes (detailed reading)  
**Use When:** You need deep understanding of how it works

---

### 4. **QUESTDB_CODE_REFERENCE_MAP.md**
**Purpose:** Visual guide to code flow with file paths  
**Best For:** Understanding which file does what  
**Contains:**
- Complete flow diagram with file locations
- Ingestion side code path (5-file journey)
- Query side code path (5-step endpoint)
- Caching layer explanation
- Deduplication layer details
- Error handling and retry logic
- Configuration summary
- Health check endpoints
- Schema definition with comments
- Key dependencies
- Code navigation quick reference table

**Read Time:** 15 minutes  
**Use When:** Tracing data through source code

---

### 5. **diagnose-questdb-flow.ps1** (PowerShell Script)
**Purpose:** Automated diagnostic script  
**Best For:** Running without manual commands  
**Contains:**
- Checks all Docker containers
- Queries QuestDB data
- Checks Kafka consumer status
- Verifies PostgreSQL points
- Checks Redis cache entries
- Tests API health endpoints
- Displays recent logs
- Provides summary and next steps

**Run:** `.\diagnose-questdb-flow.ps1`  
**Runtime:** ~30 seconds  
**Use When:** You want automated diagnosis

---

## 🎯 How to Use These Documents

### Scenario 1: "Data isn't flowing, I don't know what's wrong"
1. Run `diagnose-questdb-flow.ps1` (30 seconds)
2. Look at output → tells you which system is broken
3. Jump to relevant section in QUESTDB_QUICK_REFERENCE.md
4. Execute tests from QUESTDB_QUICK_DEBUG_COMMANDS.md
5. If still confused → Read QUESTDB_DATA_FLOW_INVESTIGATION.md section 1-5

### Scenario 2: "QuestDB has data but API returns 0"
1. Read QUESTDB_QUICK_REFERENCE.md "WHY COUNT=0" matrix
2. Execute Test 4 from QUESTDB_QUICK_DEBUG_COMMANDS.md
3. Check PostgreSQL PointSequenceId alignment
4. If confused → Read QUESTDB_DATA_FLOW_INVESTIGATION.md section 4 (History Endpoint)

### Scenario 3: "I want to understand the complete architecture"
1. Start with QUESTDB_CODE_REFERENCE_MAP.md (visual overview)
2. Read QUESTDB_DATA_FLOW_INVESTIGATION.md sections 1-3 (flow, config, ILP)
3. Read QUESTDB_DATA_FLOW_INVESTIGATION.md section 4 (history endpoint)
4. Refer to source files listed in section 6 (code paths)

### Scenario 4: "I need to monitor this in real-time"
1. QUESTDB_QUICK_REFERENCE.md "MONITORING DASHBOARD" (setup)
2. QUESTDB_QUICK_DEBUG_COMMANDS.md "Real-Time Monitoring Setup"
3. Run the 4 watch commands in 4 terminals

### Scenario 5: "Fixing takes time, I need reference material"
1. Keep QUESTDB_QUICK_REFERENCE.md bookmarked (1 page, everything you need)
2. Keep QUESTDB_QUICK_DEBUG_COMMANDS.md open for copy-paste
3. Refer to QUESTDB_DATA_FLOW_INVESTIGATION.md for specific details

---

## 📊 Document Summary Table

| Document | Read Time | Use For | Key Content |
|----------|-----------|---------|------------|
| Quick Reference | 5 min | Immediate troubleshooting | Tests, matrix, fixes, monitoring |
| Quick Commands | 10 min | Copy-paste testing | Commands, dashboards, metrics |
| Investigation | 30 min | Deep understanding | Architecture, configuration, all details |
| Code Map | 15 min | Source code tracing | File paths, call sequence, dependencies |
| Script | 30 sec | Automated diagnosis | Container check, data presence, API health |

---

## 🔍 Find What You Need

### "Data is in QuestDB but API returns 0"
→ Read: QUESTDB_QUICK_REFERENCE.md "WHY COUNT=0?"  
→ Run: QUESTDB_QUICK_DEBUG_COMMANDS.md Test 4  
→ Deep: QUESTDB_DATA_FLOW_INVESTIGATION.md Section 4 & 5

### "Kafka has messages but nothing in QuestDB"
→ Read: QUESTDB_QUICK_REFERENCE.md "ROOT CAUSE TEST SEQUENCE"  
→ Run: QUESTDB_QUICK_DEBUG_COMMANDS.md Tests 2-3  
→ Deep: QUESTDB_DATA_FLOW_INVESTIGATION.md Section 4 & 12

### "PostgreSQL points have NULL PointSequenceId"
→ Read: QUESTDB_QUICK_REFERENCE.md "COMMON ERRORS"  
→ Run: QUESTDB_QUICK_DEBUG_COMMANDS.md Test 4  
→ Deep: QUESTDB_DATA_FLOW_INVESTIGATION.md Section 5

### "Don't know where to start"
→ Run: diagnose-questdb-flow.ps1  
→ Read: QUESTDB_QUICK_REFERENCE.md (follow suggested next steps)

### "Need to understand code"
→ Read: QUESTDB_CODE_REFERENCE_MAP.md  
→ Then: Source files in src/ directory

### "Setting up monitoring"
→ Read: QUESTDB_QUICK_REFERENCE.md "MONITORING DASHBOARD"  
→ Copy: QUESTDB_QUICK_DEBUG_COMMANDS.md "Real-Time Monitoring Setup"

---

## 🛠️ Quick Copy-Paste Commands

### One-Minute Health Check
```bash
echo "=== QuestDB ===" && \
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb -t \
  -c "SELECT COUNT(*) FROM point_data;" && \
echo "=== Kafka LAG ===" && \
docker exec kafka kafka-consumer-groups.sh --bootstrap-server kafka:9092 \
  --group naia-historians --describe 2>/dev/null | tail -2
```

### Check for Data
```bash
docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(*) as total, COUNT(DISTINCT point_id) as points FROM point_data;"
```

### Test API
```bash
POINT_ID=$(docker exec postgres psql -U naia -d naia -t \
  -c "SELECT id FROM points LIMIT 1;" | tr -d ' ')
curl -s "http://localhost:5073/api/points/$POINT_ID/history?start=2026-01-10&end=2026-01-13" | jq '.count'
```

### Watch Data Arriving
```bash
watch -n 1 'docker exec questdb psql -h localhost -p 8812 -U admin -d qdb \
  -c "SELECT COUNT(*) as rows, MAX(timestamp) as latest FROM point_data;"'
```

---

## 📚 Source Code Reference

**Key Files for Data Flow:**
- [src/Naia.Api/Services/PIDataIngestionService.cs](src/Naia.Api/Services/PIDataIngestionService.cs) - Publish to Kafka
- [src/Naia.Ingestion/Worker.cs](src/Naia.Ingestion/Worker.cs) - Consume from Kafka
- [src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs](src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs) - Core processing
- [src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs) - Write to QuestDB
- [src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesReader.cs](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesReader.cs) - Read from QuestDB
- [src/Naia.Api/Program.cs](src/Naia.Api/Program.cs) - API endpoints (line 292 for history)

**Configuration:**
- [src/Naia.Api/appsettings.json](src/Naia.Api/appsettings.json) - API configuration
- [src/Naia.Ingestion/appsettings.json](src/Naia.Ingestion/appsettings.json) - Ingestion configuration
- [init-scripts/questdb/01-init-schema.sql](init-scripts/questdb/01-init-schema.sql) - QuestDB schema

**Related Documentation:**
- [QUESTDB_ISSUE_SUMMARY.md](QUESTDB_ISSUE_SUMMARY.md) - Executive summary

---

## ❓ FAQ

### Q: Which file should I read first?
**A:** QUESTDB_QUICK_REFERENCE.md (1 page, 5 minutes, solves most issues)

### Q: I don't have time to read everything
**A:** 
1. Run `diagnose-questdb-flow.ps1` 
2. Read "COMMON ERRORS & FIXES" in QUESTDB_QUICK_REFERENCE.md
3. Copy-paste relevant test from QUESTDB_QUICK_DEBUG_COMMANDS.md

### Q: Where do I find commands to run?
**A:** QUESTDB_QUICK_DEBUG_COMMANDS.md (all copy-paste ready)

### Q: How do I understand the code?
**A:** QUESTDB_CODE_REFERENCE_MAP.md (explains every component with file paths)

### Q: What about detailed architecture?
**A:** QUESTDB_DATA_FLOW_INVESTIGATION.md (16 comprehensive sections)

### Q: I need monitoring/metrics
**A:** QUESTDB_QUICK_REFERENCE.md "MONITORING DASHBOARD" section

### Q: I need recovery procedures
**A:** QUESTDB_QUICK_REFERENCE.md "EMERGENCY RECOVERY" or QUESTDB_DATA_FLOW_INVESTIGATION.md section 14

---

## ✅ What I Analyzed

### Code Files Reviewed (25+ files)
- ✓ Program.cs (API startup and endpoints)
- ✓ Worker.cs (Ingestion consumer loop)
- ✓ PIDataIngestionService.cs (Kafka producer)
- ✓ IngestionPipeline.cs (Core processing, dedup, enrichment, writes)
- ✓ QuestDbTimeSeriesWriter.cs (ILP protocol implementation)
- ✓ QuestDbTimeSeriesReader.cs (PostgreSQL wire protocol queries)
- ✓ RedisCurrentValueCache.cs (Caching layer)
- ✓ KafkaDataPointConsumer.cs (Kafka configuration)
- ✓ All configuration files (appsettings.json)
- ✓ All initialization scripts (SQL schema)

### Data Flow Traced
- ✓ Source → Kafka topic (naia.datapoints)
- ✓ Kafka → Naia.Ingestion Worker (consumer)
- ✓ Worker → Deduplication (Redis idempotency)
- ✓ Dedup → Point enrichment (PostgreSQL lookup)
- ✓ Enrichment → QuestDB write (HTTP ILP)
- ✓ Write → Redis cache update
- ✓ Cache → API query endpoint
- ✓ API → Frontend response

### Root Causes Identified
- ✓ PointSequenceId NULL checks
- ✓ ILP protocol specification
- ✓ PostgreSQL wire protocol with Server Compatibility Mode
- ✓ Kafka offset management (manual commits)
- ✓ Redis deduplication and caching
- ✓ Error handling and retry logic
- ✓ Configuration requirements

---

## 🚀 Next Steps (In Order)

1. **Run Diagnosis** (30 seconds)
   ```powershell
   .\diagnose-questdb-flow.ps1
   ```

2. **Read Quick Reference** (5 minutes)
   - File: QUESTDB_QUICK_REFERENCE.md
   - Focus on your specific issue

3. **Run Relevant Tests** (5-10 minutes)
   - File: QUESTDB_QUICK_DEBUG_COMMANDS.md
   - Tests 1-5 for most common issues

4. **Deep Dive if Needed** (15-30 minutes)
   - File: QUESTDB_DATA_FLOW_INVESTIGATION.md
   - Reference source files with file paths

5. **Monitor/Debug** (ongoing)
   - Use QUESTDB_QUICK_REFERENCE.md "MONITORING DASHBOARD"
   - Or Terminal commands from QUESTDB_QUICK_DEBUG_COMMANDS.md

---

**Investigation Complete** ✅

All files ready for immediate use. Start with QUESTDB_QUICK_REFERENCE.md

