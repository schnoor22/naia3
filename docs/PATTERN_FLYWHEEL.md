# NAIA v3 Pattern Flywheel Implementation

## Overview

The Pattern Flywheel is an event-driven system that continuously learns from industrial data streams to automatically detect, classify, and bind equipment points to known patterns. It replaces the batch-job approach of NAIA v1 (Hangfire) with a real-time Kafka-based architecture.

## Architecture

```
┌──────────────────┐    ┌───────────────────────┐    ┌─────────────────────┐
│  naia.datapoints │───▶│ BehavioralAggregator  │───▶│ naia.points.behavior│
│  (Raw data)      │    │ (Stats + Fingerprints)│    │ (Behavioral events) │
└──────────────────┘    └───────────────────────┘    └──────────┬──────────┘
                                                                 │
                                                                 ▼
┌──────────────────────┐    ┌─────────────────────┐    ┌────────────────────┐
│ naia.patterns.updated│◀──│ CorrelationProcessor│◀───│naia.correlations   │
│ (Pattern learning)   │    │ (Pairwise corr calc)│    │.updated            │
└──────────────────────┘    └─────────────────────┘    └────────┬───────────┘
         ▲                                                       │
         │                                                       ▼
┌────────┴───────────┐    ┌─────────────────────┐    ┌────────────────────┐
│ PatternLearnerWorker│◀──│ClusterDetectionWorker│◀──│naia.clusters.created│
│ (Confidence updates)│    │ (Louvain/DBSCAN)   │    │ (Detected clusters)│
└────────────────────┘    └─────────────────────┘    └────────┬───────────┘
         ▲                                                     │
         │                                                     ▼
┌────────┴──────────┐    ┌─────────────────────┐    ┌────────────────────┐
│naia.patterns      │◀───│PatternMatcherWorker │◀───│naia.suggestions    │
│.feedback          │    │ (Multi-factor match)│    │.created            │
│ (User decisions)  │    └─────────────────────┘    │ (Match suggestions)│
└───────────────────┘                               └────────────────────┘
```

## Components

### 1. BehavioralAggregator (`Workers/BehavioralAggregator.cs`)
- **Subscribes to**: `naia.datapoints`
- **Publishes to**: `naia.points.behavior`
- **Function**: Maintains sliding-window statistics for each point using Welford's online algorithm
- **Behavior Metrics**:
  - Mean, StdDev, Min, Max
  - Update rate (Hz)
  - Change frequency
  - Good quality ratio
- **Caching**: Redis for behavioral fingerprints with 48h TTL

### 2. CorrelationProcessor (`Workers/CorrelationProcessor.cs`)
- **Subscribes to**: `naia.points.behavior`
- **Publishes to**: `naia.correlations.updated`
- **Function**: Calculates pairwise correlations between points with similar behaviors
- **Algorithm**: Uses QuestDB ASOF JOIN for time-aligned correlation calculation
- **Optimization**: Groups points by update rate and value range to reduce O(n²) pairs
- **Caching**: Redis for correlation values with 24h TTL

### 3. ClusterDetectionWorker (`Workers/ClusterDetectionWorker.cs`)
- **Subscribes to**: `naia.correlations.updated`
- **Publishes to**: `naia.clusters.created`
- **Function**: Detects behavioral clusters of correlated points
- **Algorithms**:
  - **Louvain**: Community detection via modularity optimization
  - **DBSCAN**: Density-based clustering with correlation distance
- **Parameters**: MinClusterSize (3), MaxClusterSize (50), MinCohesion (0.50)

### 4. PatternMatcherWorker (`Workers/PatternMatcherWorker.cs`)
- **Subscribes to**: `naia.clusters.created`
- **Publishes to**: `naia.suggestions.created`
- **Function**: Matches clusters against known patterns using multi-factor scoring
- **Scoring Weights**:
  - 30% Naming similarity (regex matching)
  - 40% Correlation patterns
  - 20% Value range similarity
  - 10% Update rate similarity
- **Minimum confidence**: 50% to create suggestion

### 5. PatternLearnerWorker (`Workers/PatternLearnerWorker.cs`)
- **Subscribes to**: `naia.patterns.feedback`
- **Publishes to**: `naia.patterns.updated`
- **Function**: Updates pattern confidence based on user feedback
- **Actions**:
  - **Approved**: +5% confidence, binds points to pattern
  - **Rejected**: -3% confidence
  - **Deferred**: No change (logged for analysis)
- **Bounds**: Confidence floor 30%, ceiling 100%
- **Decay**: 0.5% confidence loss per day without activity

## Database Schema

### PostgreSQL Tables
- `patterns`: Pattern definitions with confidence scores
- `pattern_roles`: Expected point roles within patterns
- `pattern_suggestions`: AI-generated match suggestions
- `pattern_feedback_log`: User feedback history
- `point_pattern_bindings`: Links points to patterns
- `behavioral_clusters`: Detected point groupings
- `correlation_cache`: Calculated correlations

### Pre-seeded Patterns
1. **HVAC Air Handling Unit** (75% confidence)
   - Supply/Return/Mixed Air Temperature
   - Outside Air Damper
   - Supply Fan Status

2. **Chiller** (70% confidence)
   - CHW Supply/Return Temperature
   - CHW Flow
   - Condenser Water Temperature
   - Status

3. **VAV Box** (70% confidence)
   - Damper Position
   - Air Flow
   - Zone Temperature/Setpoint
   - Reheat Valve

4. **Pump** (65% confidence)
   - Status, Speed
   - Discharge Pressure
   - Flow Rate

5. **Boiler** (65% confidence)
   - HW Supply/Return Temperature
   - Status, Firing Rate

## Configuration

```json
{
  "PatternFlywheel": {
    "Enabled": true,
    "Kafka": {
      "BootstrapServers": "localhost:9092",
      "DataPointsTopic": "naia.datapoints",
      "PointsBehaviorTopic": "naia.points.behavior"
    },
    "BehavioralAggregator": {
      "MinSamplesForBehavior": 50,
      "WindowSizeHours": 24,
      "PublishIntervalSeconds": 60
    },
    "CorrelationProcessor": {
      "MinCorrelationThreshold": 0.60,
      "CorrelationWindowHours": 168
    },
    "ClusterDetection": {
      "Algorithm": "Louvain",
      "MinClusterSize": 3,
      "MinCohesion": 0.50
    },
    "PatternMatching": {
      "MinConfidenceForSuggestion": 0.50,
      "NamingWeight": 0.30,
      "CorrelationWeight": 0.40,
      "RangeWeight": 0.20,
      "RateWeight": 0.10
    },
    "PatternLearning": {
      "ConfidenceIncreasePerApproval": 0.05,
      "ConfidenceDecreasePerRejection": 0.03
    }
  }
}
```

## Usage

### Register Services
```csharp
// In Program.cs or Startup.cs
services.AddPatternEngine(configuration);
```

### Kafka Topics Required
```bash
naia.datapoints              # Input: raw data points
naia.points.behavior         # Behavioral fingerprints
naia.correlations.updated    # Correlation pairs
naia.clusters.created        # Detected clusters
naia.suggestions.created     # Pattern match suggestions
naia.patterns.feedback       # User feedback
naia.patterns.updated        # Pattern confidence updates
```

## Flywheel Effect

The system creates a positive feedback loop:

1. **Data flows in** → Behaviors calculated
2. **Behaviors analyzed** → Correlations discovered
3. **Correlations cluster** → Equipment groups detected
4. **Clusters matched** → Pattern suggestions created
5. **Users respond** → Pattern confidence updated
6. **Confidence improves** → Better future matches
7. **Better matches** → More user trust → More approvals
8. **Repeat** → System gets smarter with each cycle

## Next Steps

1. **Integrate with API**: Add endpoints for viewing/responding to suggestions
2. **Add UI components**: Suggestion cards, approval workflows
3. **Enable cross-tenant learning**: Anonymous pattern sharing
4. **Add pattern derivation**: Create new patterns from user modifications
5. **Implement confidence decay job**: Scheduled decay for stale patterns
