# NAIA Architecture Documentation

**Last Updated:** January 10, 2026  
**Version:** 3.0  
**Purpose:** Comprehensive documentation of each architectural component

---

## 📖 How to Use This Documentation

This directory contains **individual component documentation** that explains each piece of NAIA's architecture in the context of the overall system vision.

**Start here:** [00_OVERVIEW.md](./00_OVERVIEW.md) - Explains the big picture  
**Then dive into:** Specific components based on your role or interest

---

## 🗺️ Documentation Index

### Getting Started
- **[00_OVERVIEW.md](./00_OVERVIEW.md)** ⭐ **START HERE**
  - Vision and purpose
  - High-level architecture diagram
  - The Flywheel explained
  - Key design principles
  - Current status (what's built vs planned)

---

### Frontend Layer

- **[01_WEB_UI.md](./01_WEB_UI.md)**
  - SvelteKit 2 + Svelte 5 architecture
  - Key routes (`/review-suggestions`, `/framework`, etc.)
  - TanStack Query + SignalR integration
  - Data flow patterns
  - Component structure

---

### API Layer

- **[02_REST_API.md](./02_REST_API.md)** *(Coming Soon)*
  - .NET 8 Web API architecture
  - 25+ controllers overview
  - Minimal APIs vs Controllers
  - OpenAPI/Swagger documentation
  - Authentication & authorization

- **[03_SIGNALR_HUBS.md](./03_SIGNALR_HUBS.md)** *(Coming Soon)*
  - Real-time WebSocket communication
  - DataHub, DiscoveryHub, SmartRelayHub
  - Connection management
  - Push notification patterns

---

### Application Services

- **[04_ELEMENT_MANAGEMENT.md](./04_ELEMENT_MANAGEMENT.md)** *(Coming Soon)*
  - Asset hierarchy organization
  - Templates and attributes
  - Point bindings
  - CRUD operations

- **[05_PATTERN_LIBRARY.md](./05_PATTERN_LIBRARY.md)** *(Coming Soon)*
  - Pattern definition structure
  - Behavioral fingerprints
  - Library management
  - Pattern versioning

- **[06_LEARNING_SYSTEM.md](./06_LEARNING_SYSTEM.md)** *(Coming Soon)*
  - Approval feedback loop
  - Confidence adjustment algorithms
  - Learning metrics
  - Transfer learning (future)

- **[07_CACHING_LAYER.md](./07_CACHING_LAYER.md)** *(Coming Soon)*
  - Redis cache strategy
  - Current value snapshots
  - Cache invalidation
  - Performance optimization

---

### Data Ingestion

- **[08_PI_CONNECTOR.md](./08_PI_CONNECTOR.md)** *(Coming Soon)*
  - AF SDK vs PI Web API
  - Real-time data polling
  - Connection management
  - Health monitoring

- **[09_PI_BACKFILL.md](./09_PI_BACKFILL.md)** ✅ **COMPLETE**
  - Historical data loading
  - 30-day chunking strategy
  - Queue-based processing
  - Checkpoint system for resume
  - API endpoints and testing guide

- **[10_OPC_UA_CLIENT.md](./10_OPC_UA_CLIENT.md)** *(Coming Soon)*
  - OPC UA browsing
  - Node subscriptions
  - Security policies
  - Certificate management

- **[11_KAFKA_PIPELINE.md](./11_KAFKA_PIPELINE.md)** *(Coming Soon)*
  - Event streaming buffer
  - Producer/consumer pattern
  - Topic separation (real-time vs backfill)
  - Error handling and retries

---

### Pattern Intelligence (The Flywheel)

- **[12_CLUSTERING_ENGINE.md](./12_CLUSTERING_ENGINE.md)** *(Coming Soon)*
  - Pearson correlation clustering
  - Behavioral grouping
  - Cluster validation
  - QuestDB correlation queries

- **[13_CORRELATION_ANALYSIS.md](./13_CORRELATION_ANALYSIS.md)** *(Coming Soon)*
  - Statistical correlation
  - Time-series alignment
  - Moving window analysis
  - Correlation caching

- **[14_PATTERN_MATCHING.md](./14_PATTERN_MATCHING.md)** ✅ **COMPLETE**
  - The Flywheel explained
  - Scoring algorithm (4-factor weighted)
  - Pattern library structure
  - End-to-end example
  - ML enhancements (Phase 2)

- **[15_BACKGROUND_JOBS.md](./15_BACKGROUND_JOBS.md)** *(Coming Soon)*
  - Hangfire job scheduling
  - BehavioralAnalysisJob (every 15 min)
  - AggregationJob, RetentionJob
  - Job monitoring

---

### Data Layer

- **[16_POSTGRESQL.md](./16_POSTGRESQL.md)** *(Coming Soon)*
  - Schema design (naia.* tables)
  - Entity Framework Core migrations
  - Key tables and relationships
  - Indexing strategy

- **[17_QUESTDB.md](./17_QUESTDB.md)** *(Coming Soon)*
  - Time-series optimization
  - Partitioning strategy (by day)
  - InfluxDB Line Protocol (ILP)
  - Aggregation and retention
  - PostgreSQL wire protocol queries

- **[18_REDIS.md](./18_REDIS.md)** *(Coming Soon)*
  - Cache patterns
  - Current value snapshots
  - Rate limiting
  - Distributed session state

---

## 🎯 Documentation for Different Roles

### For **Frontend Developers**
1. Read [00_OVERVIEW.md](./00_OVERVIEW.md) for context
2. Focus on [01_WEB_UI.md](./01_WEB_UI.md)
3. Understand [03_SIGNALR_HUBS.md](./03_SIGNALR_HUBS.md) for real-time features
4. Reference [02_REST_API.md](./02_REST_API.md) for API contracts

### For **Backend Developers (.NET)**
1. Read [00_OVERVIEW.md](./00_OVERVIEW.md) for context
2. Study [02_REST_API.md](./02_REST_API.md)
3. Understand [14_PATTERN_MATCHING.md](./14_PATTERN_MATCHING.md) (core logic)
4. Learn [09_PI_BACKFILL.md](./09_PI_BACKFILL.md) and [08_PI_CONNECTOR.md](./08_PI_CONNECTOR.md)

### For **Data Engineers**
1. Read [00_OVERVIEW.md](./00_OVERVIEW.md) for context
2. Focus on [17_QUESTDB.md](./17_QUESTDB.md) and [16_POSTGRESQL.md](./16_POSTGRESQL.md)
3. Study [11_KAFKA_PIPELINE.md](./11_KAFKA_PIPELINE.md)
4. Understand [09_PI_BACKFILL.md](./09_PI_BACKFILL.md) for bulk loading

### For **DevOps / Infrastructure**
1. Read [00_OVERVIEW.md](./00_OVERVIEW.md) for context
2. Focus on [17_QUESTDB.md](./17_QUESTDB.md), [18_REDIS.md](./18_REDIS.md)
3. Study [11_KAFKA_PIPELINE.md](./11_KAFKA_PIPELINE.md)
4. Learn [15_BACKGROUND_JOBS.md](./15_BACKGROUND_JOBS.md) for monitoring

### For **Data Scientists / ML Engineers**
1. Read [00_OVERVIEW.md](./00_OVERVIEW.md) for context
2. Study [14_PATTERN_MATCHING.md](./14_PATTERN_MATCHING.md) (Phase 2 ML plans)
3. Understand [12_CLUSTERING_ENGINE.md](./12_CLUSTERING_ENGINE.md)
4. Learn [13_CORRELATION_ANALYSIS.md](./13_CORRELATION_ANALYSIS.md)

### For **Product Managers / Stakeholders**
1. **Read [00_OVERVIEW.md](./00_OVERVIEW.md)** - This explains the vision
2. Study [14_PATTERN_MATCHING.md](./14_PATTERN_MATCHING.md) - **The Flywheel** is our core differentiator
3. Skim others for feature understanding

---

## 🔄 The Flywheel - Our Core Innovation

```
Data Collection → Clustering → Pattern Matching → User Approval → Learning
       ↑                                                            ↓
       └────────────────────── System Improves ────────────────────┘
```

**Key insight:** NAIA gets smarter with every user interaction. The more approvals, the higher the confidence, the better the suggestions.

**Read this:** [14_PATTERN_MATCHING.md](./14_PATTERN_MATCHING.md) for detailed explanation.

---

## 📊 Documentation Status

| Component              | Status | Priority | Last Updated |
|------------------------|--------|----------|--------------|
| 00_OVERVIEW            | ✅ Complete | Critical | 2026-01-10 |
| 01_WEB_UI              | ✅ Complete | High | 2026-01-10 |
| 09_PI_BACKFILL         | ✅ Complete | High | 2026-01-10 |
| 14_PATTERN_MATCHING    | ✅ Complete | Critical | 2026-01-10 |
| 02_REST_API            | 📋 Planned | High | - |
| 03_SIGNALR_HUBS        | 📋 Planned | Medium | - |
| 08_PI_CONNECTOR        | 📋 Planned | High | - |
| 11_KAFKA_PIPELINE      | 📋 Planned | Medium | - |
| 12_CLUSTERING_ENGINE   | 📋 Planned | High | - |
| 17_QUESTDB             | 📋 Planned | High | - |
| Others                 | 📋 Planned | Low-Med | - |

---

## 🚀 Quick Links

- **[Parent README](../../README.md)** - Main project README
- **[Implementation Summary](../IMPLEMENTATION_SUMMARY.md)** - What's built vs planned
- **[Quick Reference](../QUICK_REFERENCE.md)** - Common commands and endpoints
- **[API Documentation](http://localhost:5282/swagger)** - Live OpenAPI spec

---

## 📝 Contributing to Documentation

When adding new component documentation:

1. **Follow the template structure:**
   - 🎯 Role in NAIA Architecture (the "why")
   - 🏗️ Architecture (the "how")
   - 📂 Key Components (the "what")
   - 🔄 Data Flow Example (the "flow")
   - 📊 Current Status (transparency)
   - 🤝 Integration Points (connections)

2. **Explain in context:**
   - Don't just describe the component
   - Explain how it fits in the overall vision
   - Show how it connects to other components
   - Illustrate with concrete examples

3. **Keep it current:**
   - Update "Last Updated" date
   - Mark planned features clearly (📋)
   - Be honest about status (✅ vs 🚧 vs 📋)

4. **Use diagrams:**
   - ASCII art for simple flows
   - Mermaid for complex diagrams (future)
   - Screenshots for UI components

---

## 🎓 Learning Path

**Week 1:** Understand the vision
- Read [00_OVERVIEW.md](./00_OVERVIEW.md)
- Study [14_PATTERN_MATCHING.md](./14_PATTERN_MATCHING.md) (The Flywheel)
- Browse [01_WEB_UI.md](./01_WEB_UI.md) to see the interface

**Week 2:** Deep dive into your area
- Frontend: Focus on UI, SignalR, API integration
- Backend: Study REST API, services, data flow
- Data: Learn QuestDB, PostgreSQL, Kafka

**Week 3:** Understand the data pipeline
- [08_PI_CONNECTOR.md](./08_PI_CONNECTOR.md) → [11_KAFKA_PIPELINE.md](./11_KAFKA_PIPELINE.md) → [17_QUESTDB.md](./17_QUESTDB.md)
- Test [09_PI_BACKFILL.md](./09_PI_BACKFILL.md) locally

**Week 4:** Explore the intelligence layer
- [12_CLUSTERING_ENGINE.md](./12_CLUSTERING_ENGINE.md)
- [13_CORRELATION_ANALYSIS.md](./13_CORRELATION_ANALYSIS.md)
- [14_PATTERN_MATCHING.md](./14_PATTERN_MATCHING.md)
- [15_BACKGROUND_JOBS.md](./15_BACKGROUND_JOBS.md)

---

## 💬 Questions?

- **Architecture questions:** Open GitHub issue with `[architecture]` tag
- **Implementation questions:** See specific component doc
- **Getting started:** Start with [00_OVERVIEW.md](./00_OVERVIEW.md)

---

**This documentation represents the living architecture of NAIA v3. It will evolve as we build.**
