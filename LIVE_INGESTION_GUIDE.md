# Live Data Ingestion Guide

## Overview

The NAIA system now supports continuous live data ingestion from the PI System. Data is:
1. **Read** from PI every 30 seconds
2. **Synced** to PostgreSQL, QuestDB, and Redis
3. **Accessible** through REST API endpoints

## Quick Start

### 1. Initialize the PI Connector

First, initialize the connection to the PI System:

```bash
curl -X POST http://localhost:5052/api/pi/initialize
```

Expected response:
```json
{
  "status": "connected",
  "connectorType": "AF SDK",
  "dataArchive": "sdhqpisrvr01"
}
```

### 2. Start Live Data Ingestion

Begin continuous data ingestion:

```bash
curl -X POST http://localhost:5052/api/ingestion/start
```

Expected response:
```json
{
  "status": "started",
  "message": "Live data ingestion from PI System initiated. Data will be synced to QuestDB and Redis.",
  "startTime": "2024-01-15T14:30:00Z"
}
```

### 3. Monitor Ingestion Status

Check the ingestion status at any time:

```bash
curl http://localhost:5052/api/ingestion/status
```

Expected response:
```json
{
  "running": true,
  "lastSync": "2024-01-15T14:30:15Z",
  "syncCount": 5,
  "errorCount": 0
}
```

### 4. Query Live Data

#### Get Current Values

Get the latest value for a specific point:

```bash
curl "http://localhost:5052/api/points/{pointId}/current"
```

#### Get Historical Data

Get time-series history for a point:

```bash
curl "http://localhost:5052/api/points/{pointId}/history?start=2024-01-15T00:00:00Z&end=2024-01-15T23:59:59Z&limit=1000"
```

### 5. Stop Ingestion (when needed)

```bash
curl -X POST http://localhost:5052/api/ingestion/stop
```

## How It Works

```
┌──────────────────────────────────────────────────────────────┐
│ PIDataIngestionService (Singleton, HttpClient-based)         │
│  • Runs every 30 seconds                                      │
│  • Calls /api/pi/test-end-to-end endpoint                     │
│  • Tracks sync count and errors                               │
└────────────────┬─────────────────────────────────────────────┘
                 │
                 ▼
┌──────────────────────────────────────────────────────────────┐
│ /api/pi/test-end-to-end Endpoint                             │
│  1. Discovers SINUSOID* points from PI                        │
│  2. Creates/updates Point metadata in PostgreSQL              │
│  3. Reads current values from PI                              │
│  4. Writes to QuestDB time-series database                    │
│  5. Caches in Redis                                           │
└────────────────┬─────────────────────────────────────────────┘
                 │
     ┌───────────┼───────────┬──────────────┐
     ▼           ▼           ▼              ▼
PostgreSQL  QuestDB      Redis         Kafka/Stream
(Metadata)  (TimeSeries) (Cache)       (Events)
```

## API Endpoints

### Ingestion Control
- **POST** `/api/ingestion/start` - Start continuous ingestion
- **POST** `/api/ingestion/stop` - Stop ingestion
- **GET** `/api/ingestion/status` - Get ingestion status

### Data Access
- **GET** `/api/points` - List all points with metadata
- **GET** `/api/points/{id}/current` - Get current value
- **GET** `/api/points/{id}/history` - Get historical data
- **GET** `/api/pi/points` - Discover points from PI
- **GET** `/api/pi/points/{tagName}/current` - Get PI point current value

### System Health
- **GET** `/health` - Check system health
- **GET** `/api/pi/health` - Check PI connector health
- **GET** `/api/pipeline/health` - Check pipeline health

## Configuration

The ingestion service is configured in `Program.cs`:

```csharp
// Add data ingestion service
builder.Services.AddSingleton<PIDataIngestionService>();
builder.Services.AddHttpClient<PIDataIngestionService>();
```

### Ingestion Loop Details

- **Interval**: 30 seconds between syncs (configurable in `PIDataIngestionService.cs`)
- **Timeout**: 1 second per HTTP call (from HttpClient default)
- **Error Handling**: Waits 60 seconds after network errors
- **Logging**: Logs each sync attempt and counts

## Example Workflow

```bash
# 1. Initialize PI connection
curl -X POST http://localhost:5052/api/pi/initialize

# 2. Start ingestion
curl -X POST http://localhost:5052/api/ingestion/start

# 3. Wait for data sync (~30 seconds)
sleep 30

# 4. Check status
curl http://localhost:5052/api/ingestion/status

# 5. Get a point's current value
curl http://localhost:5052/api/points/{pointId}/current

# 6. Get historical data (past hour)
curl "http://localhost:5052/api/points/{pointId}/history?start=$(date -u -d '1 hour ago' +%Y-%m-%dT%H:%M:%SZ)&end=$(date -u +%Y-%m-%dT%H:%M:%SZ)"

# 7. Stop ingestion when done
curl -X POST http://localhost:5052/api/ingestion/stop
```

## Troubleshooting

### "PI connector not initialized"
→ Call `/api/pi/initialize` first

### "No current value" for a point
→ Wait 30+ seconds for first sync
→ Check that point exists in PI System
→ Verify point has a SequenceId in PostgreSQL

### Ingestion not running
→ Check logs for "[Ingestion]" messages
→ Verify API is running on port 5052
→ Check `/api/ingestion/status` endpoint

### High error count
→ Check `/api/pi/health` endpoint
→ Verify PI System connectivity (sdhqpisrvr01)
→ Check network firewall rules

## Performance Notes

- Each sync reads up to 100 points from PI
- Average sync time: < 1 second (depends on PI connectivity)
- QuestDB can handle thousands of writes per second
- Redis caching provides instant current value queries
- Historical queries use QuestDB's optimized time-series format

## Next Steps

1. ✅ Live ingestion is running
2. ✅ Data flows: PI → PostgreSQL → QuestDB → Redis
3. ✅ API provides REST access to current and historical data
4. Next: Set up Kafka/streaming for real-time events
5. Next: Add analytics and aggregations on QuestDB
6. Next: Build web dashboard for visualization
