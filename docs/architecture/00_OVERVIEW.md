# NAIA Architecture Overview

**Last Updated:** January 10, 2026  
**Version:** 3.0  
**Status:** ✅ Current Implementation

---

## 🎯 Vision & Purpose

**NAIA (Network AI Assistant)** is an industrial IoT historian and pattern intelligence platform that transforms raw time-series data from industrial systems (PI System, OPC UA, etc.) into actionable insights through automated pattern recognition and machine learning.

### Core Problem We Solve

Industrial facilities have thousands of sensors generating millions of data points, but:
- **Manual organization is tedious** - Engineers spend weeks tagging and organizing points
- **Knowledge is siloed** - Naming conventions vary, tribal knowledge is lost
- **Patterns are invisible** - Related points aren't connected, anomalies go unnoticed
- **Historical context is missing** - Years of data exists but isn't leveraged for learning

### Our Solution: The Flywheel

NAIA creates a **self-improving intelligence loop**:

1. **Ingest** → Collect data from multiple historians (PI, OPC UA, CSV)
2. **Analyze** → Cluster correlated points, detect behavioral patterns
3. **Suggest** → Propose element hierarchies based on learned patterns
4. **Learn** → User approval strengthens confidence, improves future suggestions
5. **Repeat** → Each cycle makes the system smarter

**The more you use NAIA, the smarter it gets.**

---

## 🏗️ High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         CLIENT APPLICATIONS                              │
│  • Web Browser (SvelteKit UI)                                           │
│  • Excel / Power BI / Tableau (Future export API)                       │
│  • Mobile Apps (Future)                                                 │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │ HTTP REST + SignalR WebSocket
┌────────────────────────────────▼────────────────────────────────────────┐
│                         API LAYER (.NET 8)                              │
│  • 25+ REST Controllers                                                 │
│  • 3 SignalR Hubs (real-time push)                                     │
│  • OpenAPI/Swagger documentation                                        │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
              ┌──────────────────┼──────────────────┐
              │                  │                  │
┌─────────────▼─────┐  ┌────────▼────────┐  ┌─────▼──────────┐
│  APPLICATION      │  │   INGESTION     │  │  PATTERN       │
│  SERVICES         │  │   PIPELINE      │  │  ENGINE        │
│                   │  │                 │  │                │
│ • Element Mgmt    │  │ • PI Connector  │  │ • Clustering   │
│ • Pattern Library │  │ • OPC UA Client │  │ • Correlation  │
│ • Learning System │  │ • Kafka Buffer  │  │ • ML Scoring   │
│ • Caching Layer   │  │ • Data Transform│  │ • Suggestions  │
└─────────┬─────────┘  └────────┬────────┘  └─────┬──────────┘
          │                     │                  │
┌─────────▼─────────────────────▼──────────────────▼──────────┐
│                      DATA PERSISTENCE                        │
│  • PostgreSQL (metadata, config, patterns)                   │
│  • QuestDB (time-series: 100M+ points/day)                  │
│  • Redis (cache, snapshots, rate limits)                    │
└──────────────────────────────────────────────────────────────┘
```

---

## 📦 Component Breakdown

### Frontend Layer
- **[Web UI](./01_WEB_UI.md)** - SvelteKit 2 + Svelte 5 responsive interface

### API Layer
- **[REST API](./02_REST_API.md)** - .NET 8 Web API with 25+ controllers
- **[SignalR Hubs](./03_SIGNALR_HUBS.md)** - Real-time WebSocket communication

### Application Services
- **[Element Management](./04_ELEMENT_MANAGEMENT.md)** - Asset hierarchy and organization
- **[Pattern Library](./05_PATTERN_LIBRARY.md)** - Template definitions and matching
- **[Learning System](./06_LEARNING_SYSTEM.md)** - The Flywheel intelligence loop
- **[Caching Layer](./07_CACHING_LAYER.md)** - Performance optimization

### Data Ingestion
- **[PI System Connector](./08_PI_CONNECTOR.md)** - AF SDK and Web API integration
- **[PI Backfill System](./09_PI_BACKFILL.md)** - Historical data loading
- **[OPC UA Client](./10_OPC_UA_CLIENT.md)** - Industrial protocol support
- **[Kafka Pipeline](./11_KAFKA_PIPELINE.md)** - Event streaming buffer

### Pattern Intelligence
- **[Clustering Engine](./12_CLUSTERING_ENGINE.md)** - Behavioral grouping
- **[Correlation Analysis](./13_CORRELATION_ANALYSIS.md)** - Point relationship detection
- **[Pattern Matching](./14_PATTERN_MATCHING.md)** - Scoring and suggestions
- **[Background Jobs](./15_BACKGROUND_JOBS.md)** - Hangfire scheduled tasks

### Data Layer
- **[PostgreSQL Database](./16_POSTGRESQL.md)** - Metadata and configuration
- **[QuestDB Time-Series](./17_QUESTDB.md)** - High-performance historian
- **[Redis Cache](./18_REDIS.md)** - Distributed caching

---

## 🔄 Data Flow Example

**Scenario:** A new pump is installed in a refinery

1. **Discovery** (OPC UA Client)
   - Engineer browses OPC UA server
   - Discovers 15 new points: temperatures, pressures, flows
   - Points imported into NAIA

2. **Ingestion** (Kafka → QuestDB)
   - Real-time values streamed through Kafka
   - QuestDB stores time-series data
   - Redis caches current snapshots

3. **Analysis** (Clustering Engine)
   - Background job runs every 15 minutes
   - Groups 15 points by correlation (temp + pressure move together)
   - Detects steady-state behavior, normal operating range

4. **Matching** (Pattern Matching Service)
   - Compares cluster to "Centrifugal Pump" pattern in library
   - Scores: Name similarity 85%, Correlation 92%, Range 78%
   - Overall confidence: 89%

5. **Suggestion** (Learning System)
   - Creates suggested element: "Pump P-401"
   - Proposes attributes: Type=Centrifugal, Service=Cooling Water
   - Presents to user in Review Suggestions UI

6. **Learning** (Approval → Flywheel)
   - User approves suggestion
   - Element created with bound points
   - Pattern confidence increases (89% → 91%)
   - Future pump suggestions become more accurate

---

## 🎯 Key Design Principles

### 1. **Event-Driven Architecture**
- Kafka decouples ingestion from processing
- SignalR pushes updates to UI in real-time
- Background jobs run independently

### 2. **Time-Series Optimization**
- QuestDB chosen for 10x faster writes than InfluxDB
- Columnar storage for compression
- Time-partitioned for efficient retention

### 3. **Caching Strategy**
- Redis for hot data (current values, session state)
- PostgreSQL for cold data (metadata, configuration)
- Layered cache prevents DB hammering

### 4. **Machine Learning Pipeline**
- Phase 1: Statistical clustering (COMPLETE)
- Phase 2: Vector embeddings (PLANNED)
- Phase 3: Deep learning (FUTURE)

### 5. **Scalability**
- Horizontal: Multiple API instances behind load balancer
- Vertical: QuestDB handles 1M+ writes/sec
- Queue-based backfill prevents overload

---

## 📊 Current Status

### ✅ Fully Implemented
- Web UI with 6 major routes
- 25 REST API controllers
- PI System connector (AF SDK + Web API)
- Backfill system with chunking
- OPC UA client with browsing
- Kafka ingestion pipeline
- QuestDB time-series storage
- Clustering and correlation analysis
- Pattern matching with scoring
- Review suggestions workflow
- Background jobs (Hangfire)

### 🚧 In Progress
- End-to-end flywheel testing (blocked on operational data)
- Vector embeddings for semantic search
- AI-powered chat assistant (Coral AI)

### 📋 Planned
- Persona-based access control
- Mobile app (React Native)
- Export API for Excel/Power BI
- Anomaly detection ML models
- Predictive maintenance alerts

---

## 🚀 Next Steps

1. **Deploy to production** with real PI System connection
2. **Collect operational data** (30 days minimum)
3. **Validate flywheel** end-to-end with real patterns
4. **Tune ML models** based on production feedback
5. **Scale horizontally** as data volume grows

---

## 📚 Related Documentation

- **[Quick Reference](../QUICK_REFERENCE.md)** - Common commands and endpoints
- **[Implementation Summary](../IMPLEMENTATION_SUMMARY.md)** - What's built vs planned
- **[API Documentation](http://localhost:5282/swagger)** - Live OpenAPI spec
- **[Component Docs](./01_WEB_UI.md)** - Detailed component architecture

---

**Read this first, then dive into individual component docs for implementation details.**
