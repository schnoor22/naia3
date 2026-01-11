# 🎯 NAIA 24-Hour Test - Executive Summary

**Status:** ✅ **READY TO START**  
**Date:** January 10, 2026  
**Test Duration:** 24 hours  
**Objective:** Demonstrate pattern learning from live PI data

---

## Current System State

### ✅ Infrastructure (All Running)

| Service | Status | Port | Purpose |
|---------|--------|------|---------|
| PostgreSQL | ✓ Healthy | 5432 | Metadata storage (points, patterns, suggestions) |
| QuestDB | ✓ Running | 9000, 8812 | Time-series historian |
| Redis | ✓ Healthy | 6379 | Current values cache + idempotency |
| Kafka | ✓ Healthy | 9092 | Message backbone |
| Zookeeper | ✓ Healthy | 2181 | Kafka coordination |
| Kafka UI | ✓ Running | 8080 | Kafka management dashboard |

### 📊 Test Configuration

**Data Source:** PI System (sdhqpisrvr01)  
**Test Points:** MLR1* tags (4 points)
- `MLR1.Speed` (RPM)
- `MLR1.Power` (kW)
- `MLR1.Temperature` (°C)
- `MLR1.Efficiency` (%)

**Expected Correlations:**
- Speed ↔ Power (strong positive, ~0.85+)
- Power ↔ Temperature (moderate positive, ~0.70+)
- Speed ↔ Efficiency (moderate negative correlation expected)

**Pattern Target:** Motor-Drive equipment pattern

---

## 🚀 Quick Start - 3 Simple Steps

### Step 1: Start the System (5 minutes)

```powershell
# Run automated startup script
cd c:\naia3
.\start_24h_test.ps1
```

**What it does:**
1. ✓ Verifies Docker infrastructure is running
2. ✓ Checks MLR1 points are configured in PostgreSQL
3. ✓ Builds the solution
4. ✓ Starts Naia.Api (Producer + Pattern Engine + Hangfire)
5. ✓ Starts PI data ingestion (PI → Kafka)
6. ✓ Starts Naia.Ingestion (Kafka → QuestDB + Redis)
7. ✓ Opens monitoring dashboards

**Expected output:**
```
═══════════════════════════════════════════════════════════════════
  NAIA 24-Hour Learning Test - STARTED
  The First Industrial Historian That Learns From You™
═══════════════════════════════════════════════════════════════════

DATA FLOW:
  PI System (MLR1*) → Kafka → QuestDB + Redis → Pattern Engine

SERVICES RUNNING:
  ✓ PostgreSQL       (localhost:5432)
  ✓ QuestDB          (localhost:9000, 8812)
  ✓ Redis            (localhost:6379)
  ✓ Kafka            (localhost:9092)
  ✓ Naia.Api         (localhost:5052)
  ✓ Naia.Ingestion   (Background Worker)
```

### Step 2: Monitor Progress (Continuous)

```powershell
# Run live monitoring dashboard
.\monitor_24h_test.ps1
```

**Monitoring Dashboard Shows:**
- Infrastructure status (all containers)
- Data flow metrics (QuestDB records, Redis cache)
- Pattern engine status (suggestions, approvals)
- Behavioral clusters detected
- Hangfire job execution

**Interactive Commands:**
- Press `H` - Open Hangfire dashboard
- Press `Q` - Open QuestDB console
- Press `K` - Open Kafka UI
- Press `S` - Open Swagger API
- Press `L` - View latest suggestions

### Step 3: Approve Pattern Suggestions (As They Appear)

After 30-45 minutes, pattern suggestions will start appearing.

**Check for suggestions:**
```powershell
Invoke-RestMethod "http://localhost:5052/api/suggestions/pending"
```

**Approve a suggestion:**
```powershell
$suggestionId = "YOUR-SUGGESTION-ID-HERE"
Invoke-RestMethod -Uri "http://localhost:5052/api/suggestions/$suggestionId/approve" -Method POST
```

**This teaches NAIA your patterns!** 🧠

---

## 📈 Expected Timeline

| Time | Event | What to Expect |
|------|-------|----------------|
| **0:00** | System starts | Data begins flowing: PI → Kafka → QuestDB |
| **0:05** | First stats | BehavioralAnalysisJob calculates initial statistics |
| **0:15** | Correlations | CorrelationAnalysisJob finds Speed ↔ Power correlation |
| **0:20** | Clustering | ClusterDetectionJob groups MLR1 points into 1 cluster |
| **0:30** | Pattern match | PatternMatchingJob suggests "Motor-Drive" pattern (~75% confidence) |
| **1:00** | Learning | PatternLearningJob processes any approved suggestions |
| **3:00** | Refinement | With more data, correlation strength increases |
| **6:00** | High confidence | Suggestions reach 85%+ confidence with stable correlations |
| **24:00** | Test complete | Review results and analyze pattern library |

---

## 🎓 What NAIA is Learning

### Hour 1-3: Behavioral Fingerprinting
```
MLR1.Speed:        μ=1850.2 RPM  σ=42.1  range=1720-1950  roc=2.3/s
MLR1.Power:        μ=124.8 kW    σ=8.9   range=105-142    roc=1.1/s
MLR1.Temperature:  μ=82.3 °C     σ=3.5   range=75-89      roc=0.4/s
MLR1.Efficiency:   μ=94.5 %      σ=2.1   range=90-98      roc=0.2/s
```

### Hour 3-6: Correlation Discovery
```
MLR1.Speed ↔ MLR1.Power:         r=0.87 (Strong positive)
MLR1.Power ↔ MLR1.Temperature:   r=0.76 (Moderate positive)
MLR1.Speed ↔ MLR1.Temperature:   r=0.68 (Moderate positive)
MLR1.Efficiency ↔ MLR1.Power:    r=0.54 (Weak positive)
```

### Hour 6-12: Pattern Matching
```
Cluster: [MLR1.Speed, MLR1.Power, MLR1.Temperature, MLR1.Efficiency]
Common Prefix: "MLR1"
Point Count: 4

Pattern Match: Motor-Drive
  - Naming Score:      85% (matches motor naming patterns)
  - Correlation Score: 87% (strong speed/power correlation)
  - Range Score:       90% (values within expected motor ranges)
  - Rate Score:        75% (rate-of-change matches motor dynamics)
  → Overall Confidence: 84%

Suggestion Created: "This looks like a Motor-Drive pattern"
```

### Hour 12-24: Learning & Refinement

**User approves suggestion:**
- Pattern confidence increases from 84% → 89%
- Point-to-pattern bindings created
- Future similar clusters get higher confidence scores
- Pattern library grows with feedback

**Next time NAIA sees:**
- Points named "MLR2.*" with similar correlations
- Points with speed/power/temp pattern
- Equipment with motor-like dynamics

**It suggests:**
- "This looks like another Motor-Drive (89% confidence)"
- "Based on MLR1 approval, this is likely similar equipment"

**This is institutional memory!** 🧠📚

---

## 🔍 Verification Checklist

### During Test (Hourly)

```powershell
# Check data is flowing
docker exec naia-postgres psql -U naia -d naia -c "SELECT COUNT(*) FROM behavioral_stats WHERE last_calculated_at > NOW() - INTERVAL '1 hour';"

# Check QuestDB has data
# Open http://localhost:9000 and run:
SELECT COUNT(*) FROM timeseries WHERE point_name LIKE 'MLR1%';

# Check Hangfire jobs are running
# Open http://localhost:5052/hangfire → Recurring Jobs

# Check for suggestions
Invoke-RestMethod "http://localhost:5052/api/suggestions/pending"
```

### After 24 Hours (Final Analysis)

```sql
-- 1. Total data collected
SELECT 
    COUNT(*) as total_records,
    MIN(timestamp) as start_time,
    MAX(timestamp) as end_time,
    COUNT(DISTINCT point_name) as unique_points
FROM timeseries 
WHERE point_name LIKE 'MLR1%';

-- 2. Behavioral statistics
SELECT 
    point_name, 
    mean_value, 
    stddev, 
    min_value, 
    max_value, 
    rate_of_change,
    sample_count
FROM behavioral_stats
WHERE point_name LIKE 'MLR1%';

-- 3. Correlation matrix
SELECT 
    point_a_id::VARCHAR(50) as point_a,
    point_b_id::VARCHAR(50) as point_b,
    correlation_coefficient,
    p_value,
    sample_count
FROM correlation_cache
ORDER BY ABS(correlation_coefficient) DESC;

-- 4. Detected clusters
SELECT 
    id,
    common_prefix,
    point_count,
    point_names,
    detected_at
FROM behavioral_clusters
ORDER BY detected_at DESC;

-- 5. Pattern suggestions
SELECT 
    s.pattern_id,
    p.name as pattern_name,
    s.overall_confidence,
    s.status,
    s.created_at,
    s.reviewed_at
FROM pattern_suggestions s
JOIN patterns p ON s.pattern_id = p.id
ORDER BY s.created_at DESC;

-- 6. Pattern learning results
SELECT 
    name,
    category,
    confidence,
    application_count,
    approval_count,
    rejection_count
FROM patterns
WHERE application_count > 0
ORDER BY confidence DESC;
```

---

## 📊 Success Metrics

### Data Collection
- [ ] ✅ 300,000+ time-series records in QuestDB
- [ ] ✅ All 4 MLR1 points have data
- [ ] ✅ No data gaps > 5 minutes
- [ ] ✅ < 100ms average ingestion latency

### Pattern Discovery
- [ ] ✅ 4+ behavioral statistics calculated (1 per point)
- [ ] ✅ 6+ correlations detected (all point pairs)
- [ ] ✅ 1+ behavioral cluster formed
- [ ] ✅ 1+ pattern suggestion created

### Learning
- [ ] ✅ At least 1 suggestion approved
- [ ] ✅ Pattern confidence increases after approval
- [ ] ✅ Point-to-pattern bindings created
- [ ] ✅ Pattern library has at least 1 learned pattern

### System Health
- [ ] ✅ All Docker containers running for 24h
- [ ] ✅ All Hangfire jobs executing on schedule
- [ ] ✅ 100% job success rate
- [ ] ✅ No critical errors in logs

---

## 🎯 What Makes This Different?

### Traditional Historian (PI, Wonderware, Ignition)
```
Site 1: Engineer manually models equipment → 40 hours
Site 2: Engineer manually models equipment → 40 hours
Site 3: Engineer manually models equipment → 40 hours
...
Site 11: Engineer manually models equipment → 40 hours

Total: 440 hours of repetitive manual work
```

### NAIA (Learning Historian)
```
Site 1: Engineer models equipment → 40 hours
        ↓ NAIA learns patterns
Site 2: Engineer approves suggestions → 8 hours (80% reduction)
        ↓ NAIA learns more
Site 3: Engineer approves suggestions → 4 hours (90% reduction)
        ↓ NAIA learns even more
...
Site 11: Engineer reviews auto-applied patterns → 1 hour (97% reduction)

Total: 40 + (10 × 6) = 100 hours
Savings: 340 hours (77% reduction)
```

**This is compounding efficiency!** 📈

---

## 📚 Resources

- **Full Guide:** [START_HISTORIAN_24H_TEST.md](START_HISTORIAN_24H_TEST.md)
- **Architecture Docs:** [docs/architecture/](docs/architecture/)
- **Pattern Flywheel:** [docs/PATTERN_FLYWHEEL.md](docs/PATTERN_FLYWHEEL.md)
- **Hangfire Dashboard:** http://localhost:5052/hangfire
- **QuestDB Console:** http://localhost:9000
- **Swagger API:** http://localhost:5052/swagger

---

## 🆘 Troubleshooting

**Issue: No data in QuestDB after 10 minutes**
```powershell
# Check PI ingestion status
Invoke-RestMethod "http://localhost:5052/api/ingestion/status"

# Check Kafka messages
# Open http://localhost:8080 → Topics → naia.datapoints

# Check Ingestion worker logs
# Look at the Naia.Ingestion console window
```

**Issue: No pattern suggestions after 1 hour**
```powershell
# Check Hangfire jobs
# Open http://localhost:5052/hangfire → Recurring Jobs
# Verify all jobs showing "Next Execution" times

# Check if enough data exists
# Need at least 2-3 hours of data for meaningful correlations
```

**Issue: Hangfire jobs failing**
```powershell
# Open Hangfire dashboard
# http://localhost:5052/hangfire → Failed Jobs
# Click failed job to see error details
# Check API console window for stack traces
```

---

## 🎉 Ready to Start!

Everything is configured and ready. Just run:

```powershell
.\start_24h_test.ps1
```

Then monitor with:

```powershell
.\monitor_24h_test.ps1
```

**Let NAIA learn from your industrial data for 24 hours!** 🚀

---

*The First Industrial Historian That Learns From You™*  
*NAIA v3.0 - January 10, 2026*
