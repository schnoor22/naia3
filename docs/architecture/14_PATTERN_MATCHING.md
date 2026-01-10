# Pattern Matching & The Flywheel

**Component:** Pattern Intelligence Layer  
**Technology:** .NET 8, Statistical Analysis, ML Scoring  
**Location:** `src/Naia.Application/Services/PatternMatchingService.cs`  
**Status:** ✅ 70% Complete (Statistical complete, ML embeddings planned)

---

## 🎯 Role in NAIA Architecture

The Pattern Matching Service is **the brain of the Flywheel** - NAIA's self-improving intelligence loop. It takes clusters of correlated points and scores them against pattern templates in the library to generate element suggestions.

**Problem:** Engineers manually create asset hierarchies by:
1. Looking at hundreds of point names
2. Guessing which points belong together
3. Creating elements and binding points
4. Repeating for every asset in the plant

**This takes weeks and relies on tribal knowledge.**

**Solution:** The Pattern Matching Service:
- Automatically groups correlated points (clustering)
- Compares groups to known patterns (scoring)
- Generates element suggestions with confidence scores
- **Learns from user approvals** to improve future matches

**In the vision:** This is the core of NAIA's intelligence. Each user approval makes the system smarter. The flywheel spins: Better suggestions → More approvals → Higher confidence → Even better suggestions.

---

## 🏗️ The Flywheel Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                    THE FLYWHEEL CYCLE                            │
└──────────────────────────────────────────────────────────────────┘

1. DATA COLLECTION
   ├─ User imports points from PI/OPC UA/CSV
   ├─ Points stored in PostgreSQL (metadata)
   └─ Time-series data flows to QuestDB
        │
        ▼
2. BEHAVIORAL ANALYSIS (Every 15 minutes)
   ├─ ClusteringEngine groups points by correlation
   │   └─ Pearson correlation: r > 0.85 = same cluster
   ├─ BehavioralAnalysisJob identifies patterns
   │   └─ Steady-state, oscillation, step changes
   └─ Clusters stored in naia.PatternClusters table
        │
        ▼
3. PATTERN MATCHING (This Component ⭐)
   ├─ PatternMatchingService scores each cluster
   │   ├─ Name similarity: 30% (Levenshtein distance)
   │   ├─ Correlation strength: 40% (QuestDB query)
   │   ├─ Range similarity: 20% (operating envelope)
   │   └─ Update rate match: 10% (sampling frequency)
   ├─ Generates SuggestedElement records
   └─ Stored in naia.SuggestedElements table
        │
        ▼
4. HUMAN APPROVAL (UI: /review-suggestions)
   ├─ User reviews suggestions
   ├─ Approves: Creates Element + updates confidence
   ├─ Rejects: Logs feedback, pattern learns
   └─ Approval stored in naia.AuditLog
        │
        ▼
5. LEARNING FEEDBACK
   ├─ Approved patterns: confidence ↑ (0.89 → 0.91)
   ├─ Rejected patterns: weight adjustment
   ├─ Pattern library strengthened
   └─ PatternDefinitions updated with new statistics
        │
        └──────┐
               │ (Cycle repeats)
               ▼
        New data arrives → Clustering → Better matching → Higher confidence
```

### Why It's Called a Flywheel

Like a physical flywheel that stores energy and spins faster over time, NAIA's intelligence loop **gains momentum**:

- **Week 1:** 50% confidence suggestions, 20% approval rate
- **Week 4:** 70% confidence suggestions, 50% approval rate
- **Month 6:** 90% confidence suggestions, 80% approval rate
- **Year 1:** System automatically organizes 95% of new points

**Each approval adds energy to the flywheel, making it spin faster.**

---

## 📂 Key Components

### 1. PatternMatchingService (`PatternMatchingService.cs`)

**Purpose:** Score clusters against pattern library

```csharp
public class PatternMatchingService
{
    public async Task<List<SuggestedElement>> MatchClustersToPatternsAsync(
        List<PatternCluster> clusters)
    {
        var suggestions = new List<SuggestedElement>();
        
        foreach (var cluster in clusters)
        {
            // Get all pattern definitions from library
            var patterns = await _patternRepo.GetAllPatternsAsync();
            
            // Score cluster against each pattern
            var scores = new List<PatternScore>();
            foreach (var pattern in patterns)
            {
                var score = await CalculatePatternScoreAsync(cluster, pattern);
                if (score.OverallConfidence > 0.60) // 60% threshold
                {
                    scores.Add(score);
                }
            }
            
            // Take highest scoring pattern
            var bestMatch = scores.OrderByDescending(s => s.OverallConfidence).FirstOrDefault();
            if (bestMatch != null)
            {
                var suggestion = CreateSuggestion(cluster, bestMatch.Pattern, bestMatch);
                suggestions.Add(suggestion);
            }
        }
        
        return suggestions;
    }
}
```

---

### 2. Scoring Algorithm (Phase 1: Statistical)

**Current implementation uses 4 weighted factors:**

```csharp
private async Task<PatternScore> CalculatePatternScoreAsync(
    PatternCluster cluster, 
    PatternDefinition pattern)
{
    // 1. Name Similarity (30%) - Levenshtein distance
    var nameSimilarity = CalculateNameSimilarity(cluster.PointNames, pattern.ExpectedNames);
    
    // 2. Correlation Strength (40%) - QuestDB query
    var correlationScore = await CalculateCorrelationScoreAsync(cluster.PointIds);
    
    // 3. Range Similarity (20%) - Operating envelope
    var rangeScore = CalculateRangeScoreAsync(cluster.Ranges, pattern.TypicalRanges);
    
    // 4. Update Rate (10%) - Sampling frequency
    var rateScore = CalculateUpdateRateScoreAsync(cluster.AvgUpdateRate, pattern.ExpectedRate);
    
    // Weighted average
    var overall = (nameSimilarity * 0.30) +
                  (correlationScore * 0.40) +
                  (rangeScore * 0.20) +
                  (rateScore * 0.10);
    
    return new PatternScore
    {
        Pattern = pattern,
        OverallConfidence = overall,
        NameScore = nameSimilarity,
        CorrelationScore = correlationScore,
        RangeScore = rangeScore,
        RateScore = rateScore
    };
}
```

#### Name Similarity (30%)

**Purpose:** How well do point names match expected pattern?

```csharp
Example:
  Cluster points: ["P-401.PV", "P-401.SP", "P-401.OUT"]
  Pattern expects: ["*.PV", "*.SP", "*.OUT"]
  
  Match: 3/3 = 100% × 0.30 = 0.30
```

**Algorithm:** Levenshtein edit distance + wildcard matching

---

#### Correlation Strength (40%)

**Purpose:** How strongly are points related mathematically?

```sql
-- QuestDB query for Pearson correlation
SELECT 
    corr(a.value, b.value) as correlation_coefficient
FROM point_data a
JOIN point_data b ON a.timestamp = b.timestamp
WHERE a.source_address = 'P-401.PV'
  AND b.source_address = 'P-401.SP'
  AND a.timestamp > now() - INTERVAL '7 days';
  
Result: r = 0.92 → Score = 0.92 × 0.40 = 0.368
```

**Interpretation:**
- `r > 0.85`: Strong positive correlation (same equipment)
- `r < -0.85`: Strong negative correlation (inverse relationship)
- `|r| < 0.50`: Weak/no correlation (unrelated)

---

#### Range Similarity (20%)

**Purpose:** Do values operate in expected ranges?

```csharp
Example:
  Cluster: PV range = 50-80 PSI
  Pattern: Typical range = 45-85 PSI
  
  Overlap: 30 / 35 = 85.7% × 0.20 = 0.171
```

**Algorithm:** Jaccard similarity of operating envelopes

---

#### Update Rate (10%)

**Purpose:** Does sampling frequency match expectations?

```csharp
Example:
  Cluster: avg 1 sample/second
  Pattern: expected 1 sample/second
  
  Match: 1.0 - |1-1|/1 = 100% × 0.10 = 0.10
```

**Rationale:** Fast-changing signals (0.1s) vs slow-changing (5min) indicate different equipment types.

---

### 3. Pattern Library Structure

**PatternDefinition Schema:**
```csharp
public class PatternDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; }                    // "Centrifugal Pump"
    public string Description { get; set; }
    public string Category { get; set; }                // "Rotating Equipment"
    
    // Expected point structure
    public List<string> ExpectedPointNames { get; set; } // ["*.PV", "*.SP", "*.OUT"]
    public int MinPointCount { get; set; }              // 3
    public int MaxPointCount { get; set; }              // 15
    
    // Behavioral fingerprint
    public List<RangeDefinition> TypicalRanges { get; set; }
    public double ExpectedUpdateRate { get; set; }       // samples/second
    public List<string> BehaviorTags { get; set; }       // ["steady-state", "PID-controlled"]
    
    // Learning metrics
    public double BaseConfidence { get; set; }           // 0.75 (starts at 75%)
    public int ApprovalCount { get; set; }               // 42 approvals
    public int RejectionCount { get; set; }              // 3 rejections
    public double CurrentConfidence { get; set; }        // 0.92 (improved to 92%)
    
    // ML embeddings (Phase 2 - not yet implemented)
    public float[] NameEmbedding { get; set; }           // VoyageAI vector
    public float[] BehaviorEmbedding { get; set; }       // ONNX autoencoder
}
```

**Example Pattern: Centrifugal Pump**
```json
{
  "name": "Centrifugal Pump",
  "category": "Rotating Equipment",
  "expectedPointNames": [
    "*.PV",          // Process variable (discharge pressure)
    "*.SP",          // Setpoint
    "*.OUT",         // Controller output (speed)
    "*Speed*",       // Motor speed
    "*Current*",     // Motor current
    "*Flow*"         // Optional: flow rate
  ],
  "minPointCount": 3,
  "maxPointCount": 10,
  "typicalRanges": [
    { "pointName": "*.PV", "min": 30, "max": 150, "units": "PSI" },
    { "pointName": "*Speed*", "min": 0, "max": 3600, "units": "RPM" }
  ],
  "expectedUpdateRate": 1.0,
  "behaviorTags": ["steady-state", "PID-controlled"],
  "baseConfidence": 0.85,
  "approvalCount": 127,
  "rejectionCount": 8,
  "currentConfidence": 0.93
}
```

---

## 🔄 End-to-End Example

### Scenario: New Pump Points Imported

**1. Data Ingestion**
```
Engineer imports 12 points from PI System:
  - MLR1_P401_PV        (discharge pressure)
  - MLR1_P401_SP        (pressure setpoint)
  - MLR1_P401_OUT       (controller output)
  - MLR1_P401_SPEED     (motor speed)
  - MLR1_P401_AMPS      (motor current)
  - MLR1_P401_FLOW      (flow rate)
  - MLR1_P401_TEMP      (bearing temp)
  - MLR1_P401_VIB       (vibration)
  - ... 4 more points
  
Time-series data streams to QuestDB for 7 days.
```

**2. Clustering (Background Job)**
```
BehavioralAnalysisJob runs at 10:00 AM:
  └─ Query QuestDB for last 7 days
  └─ Calculate Pearson correlations between all 12 points
  
Results:
  Cluster A (6 points): r > 0.90
    - MLR1_P401_PV, MLR1_P401_SP, MLR1_P401_OUT
    - MLR1_P401_SPEED, MLR1_P401_AMPS, MLR1_P401_FLOW
    
  Cluster B (3 points): r > 0.75
    - MLR1_P401_TEMP, MLR1_P401_VIB, MLR1_P401_STATUS
    
  Outliers (3 points): r < 0.50 (unrelated)
```

**3. Pattern Matching (This Service)**
```
PatternMatchingService processes Cluster A:
  
  Pattern Library contains:
    1. Centrifugal Pump (confidence: 0.93)
    2. Heat Exchanger (confidence: 0.87)
    3. Control Valve (confidence: 0.89)
  
  Scoring Cluster A vs each pattern:
  
  Centrifugal Pump:
    ✓ Name similarity: 6/6 match (*.PV, *.SP, *SPEED*) = 0.30
    ✓ Correlation: r=0.94 = 0.376
    ✓ Range: PV 50-80 PSI matches 45-85 PSI = 0.18
    ✓ Update rate: 1.0 samples/sec matches 1.0 = 0.10
    ───────────────────────────────────────────────
    Overall confidence: 0.956 (95.6%)
  
  Heat Exchanger:
    ✗ Name similarity: 2/6 match = 0.10
    ✓ Correlation: r=0.91 = 0.364
    ✗ Range: Temp expected, pressure found = 0.05
    ✓ Update rate: 1.0 matches = 0.10
    ───────────────────────────────────────────────
    Overall confidence: 0.614 (61.4%)
  
  Best match: Centrifugal Pump (95.6%)
```

**4. Suggestion Creation**
```sql
INSERT INTO naia.SuggestedElements (
  id,
  suggested_name,
  pattern_id,
  confidence_score,
  cluster_id,
  point_ids,
  status,
  created_at
) VALUES (
  'guid-abc123',
  'Pump P-401',                    -- Extracted from "MLR1_P401_*"
  'pattern-guid-pump',
  0.956,
  'cluster-guid-a',
  '{point-guid-1, point-guid-2, ...}',
  'Pending',
  now()
);
```

**5. User Reviews in UI**
```
/review-suggestions page shows:
  
  ┌─────────────────────────────────────────────────┐
  │ 🎯 Suggested Element                            │
  │                                                 │
  │ Name: Pump P-401                                │
  │ Pattern: Centrifugal Pump                       │
  │ Confidence: 95.6% ⭐⭐⭐⭐⭐                       │
  │                                                 │
  │ Points (6):                                     │
  │   ✓ MLR1_P401_PV        (Pressure)              │
  │   ✓ MLR1_P401_SP        (Setpoint)              │
  │   ✓ MLR1_P401_OUT       (Output)                │
  │   ✓ MLR1_P401_SPEED     (Speed)                 │
  │   ✓ MLR1_P401_AMPS      (Current)               │
  │   ✓ MLR1_P401_FLOW      (Flow)                  │
  │                                                 │
  │ [Approve]  [Reject]  [Edit]                    │
  └─────────────────────────────────────────────────┘
```

**6. User Approves**
```
User clicks "Approve":
  
  POST /api/suggestions/guid-abc123/approve
  
  Backend actions:
    1. Create Element in naia.Elements
       └─ Name: "Pump P-401"
       └─ Type: "Centrifugal Pump"
    
    2. Create PointBindings in naia.PointBindings
       └─ Links 6 points to new element
    
    3. Update pattern confidence
       └─ Centrifugal Pump: 0.93 → 0.94 (+0.01)
    
    4. Log approval in naia.AuditLog
       └─ User: john.doe@company.com
       └─ Action: ApprovedSuggestion
       └─ Timestamp: 2026-01-10 10:15:32
    
    5. Delete suggestion from naia.SuggestedElements
       └─ Status: Approved
```

**7. Learning Feedback**
```
PatternDefinition updated:
  {
    "name": "Centrifugal Pump",
    "approvalCount": 127 → 128,      // Incremented
    "currentConfidence": 0.93 → 0.94, // Improved
    "lastApprovedAt": "2026-01-10T10:15:32Z"
  }

Next time a similar cluster appears:
  • Higher confidence (94% instead of 93%)
  • More likely to auto-approve (future: 95%+ threshold)
  • Name extraction improved (learned "MLR1" prefix pattern)
```

---

## 🎯 Phase 2: Machine Learning (Planned)

### Current Limitations of Statistical Approach

1. **Name matching is brittle**
   - "Pump P-401" vs "P401 Pump" vs "401-P" all mean the same
   - Levenshtein distance doesn't understand semantics
   
2. **Can't handle new patterns**
   - Only matches patterns in library
   - Requires manual pattern definition
   
3. **No transfer learning**
   - Each facility starts from zero
   - Can't leverage learnings across sites

### Planned ML Enhancements

**1. Vector Embeddings (Q2 2026)**
```
VoyageAI API for text embeddings:
  "Centrifugal Pump P-401 Discharge Pressure"
  └─> [0.234, -0.891, 0.512, ...] (1024-dim vector)
  
Cosine similarity replaces Levenshtein:
  cos_sim(embedding_a, embedding_b) = 0.94
  
Benefits:
  ✓ Semantic understanding ("pump" ≈ "centrifugal")
  ✓ Multi-language support (Spanish, Portuguese)
  ✓ Handles typos and abbreviations
```

**2. Behavioral Autoencoders (Q3 2026)**
```
ONNX Runtime + LSTM autoencoder:
  Time-series → [0.123, 0.456, ...] (128-dim vector)
  
Learns behavioral patterns:
  • Steady-state vs oscillating
  • PID-controlled vs on-off
  • Normal operating region vs anomalous
  
Benefits:
  ✓ Unsupervised learning (no labels needed)
  ✓ Detects novel patterns
  ✓ Anomaly detection
```

**3. Transfer Learning (Q4 2026)**
```
Pretrained model on 100 facilities:
  • 50,000 approved patterns
  • 10M point time-series samples
  • Industry-specific knowledge (oil & gas, pharma, etc.)
  
New facility starts at 80% confidence instead of 50%.
```

---

## 📊 Current Status

### ✅ Implemented (Phase 1: Statistical)
- Name similarity scoring (Levenshtein)
- Correlation scoring (Pearson via QuestDB)
- Range similarity (Jaccard)
- Update rate matching
- Weighted scoring algorithm
- Pattern library CRUD
- Suggestion generation
- Approval workflow with learning feedback

### 🧪 Partially Tested
- Algorithm logic verified in isolation
- **Blocked:** Need operational data (30+ days) for end-to-end testing
- Manual test clusters show 85-95% accuracy

### 📋 Planned (Phase 2: ML)
- VoyageAI text embeddings
- ONNX behavioral autoencoders
- Vector database (Qdrant or Pinecone)
- Transfer learning models

---

## 🤝 Integration Points

### With ClusteringEngine
- **Input:** `List<PatternCluster>` from behavioral analysis
- **Trigger:** BehavioralAnalysisJob calls MatchClustersToPatternsAsync()
- **Frequency:** Every 15 minutes

### With Pattern Library
- **Source:** naia.PatternDefinitions table
- **CRUD:** PatternsController exposes REST API
- **UI:** /patterns route for browsing/editing

### With Review Suggestions UI
- **Output:** naia.SuggestedElements table
- **API:** GET /api/suggestions returns pending suggestions
- **Approval:** POST /api/suggestions/{id}/approve

### With Learning System
- **Feedback:** Approval/rejection updates pattern confidence
- **Storage:** naia.AuditLog tracks all decisions
- **Analytics:** Dashboard shows approval rate, confidence trends

---

## 🚀 Key Metrics

### Performance Targets
- **Scoring Speed:** < 100ms per cluster
- **Accuracy:** 85%+ correct suggestions
- **False Positive Rate:** < 15%
- **User Approval Rate:** 60%+ (80%+ after 6 months)

### Current Metrics (Estimated - Pending Real Data)
- **Scoring Speed:** ~50ms per cluster ✓
- **Accuracy:** 85-95% (on test data)
- **User Approval Rate:** Unknown (no production data yet)

---

**The Flywheel is the soul of NAIA. Every approval makes it spin faster.**

---

**Next:** [Background Jobs Documentation](./15_BACKGROUND_JOBS.md)
