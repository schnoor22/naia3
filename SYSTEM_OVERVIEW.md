# NAIA System Overview

## What is NAIA?

NAIA (Nature and Industrial Analytics) is a real-time data ingestion and pattern matching platform for grid stability analysis. It collects data from multiple sources (utility grids, weather, simulated devices), stores it in time-series and relational databases, and provides APIs for analysis and visualization.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      USER / BROWSER                         │
└──────────────────────────┬──────────────────────────────────┘
                           │
                    HTTPS (SSL via Let's Encrypt)
                           │
        ┌──────────────────▼──────────────────┐
        │   CADDY (Reverse Proxy)             │
        │   app.naia.run:443 → localhost:5000 │
        │   Automatic SSL, WebSocket support  │
        └──────────────────┬──────────────────┘
                           │
        ┌──────────────────▼──────────────────┐
        │   NAIA.API (.NET 8)                 │
        │   localhost:5000                    │
        │   - REST API endpoints              │
        │   - WebSocket connections           │
        │   - Current value queries           │
        └──────────────────┬──────────────────┘
                           │
        ┌──────────────────┴──────────────────┐
        │                                     │
   ┌────▼─────┐  ┌─────────┐  ┌───────────┐ │
   │PostgreSQL│  │ QuestDB │  │   Redis   │ │
   │Metadata  │  │Time-Ser.│  │Cache/ILP  │ │
   │Points    │  │Data     │  │Idempotent │ │
   └──────────┘  └────┬────┘  └──────┬────┘ │
                      │               │      │
                      └───────┬───────┘      │
                              │              │
        ┌─────────────────────▼──────────────┤
        │  NAIA.INGESTION (Background)       │
        │  - Polls connectors (5-15 min)     │
        │  - Publishes to Kafka topics       │
        │  - Updates Redis cache             │
        └──────────────────┬──────────────────┘
                           │
        ┌──────────────────▼──────────────────┐
        │   KAFKA (Message Backbone)         │
        │   - naia.datapoints                │
        │   - naia.datapoints.dlq            │
        │   - Zookeeper for coordination     │
        └─────────────────────────────────────┘
                           │
        ┌──────────────────▼──────────────────┐
        │   DATA CONNECTORS                  │
        │   - WeatherApiConnector            │
        │   - EiaGridApiConnector            │
        │   - OpcSimulatorConnector          │
        │   - PIConnector (future)           │
        └─────────────────────────────────────┘
```

## Key Components

### Frontend Services
- **Caddy**: Reverse proxy with automatic SSL certificates from Let's Encrypt
  - Domain: app.naia.run
  - Auto-redirects HTTP → HTTPS
  - Handles WebSocket upgrades
  - Port: 80 (HTTP), 443 (HTTPS)

### Backend Services
- **Naia.Api**: .NET 8 REST API
  - Runs via systemd (naia-api.service)
  - Port: 5000 (localhost only, via Caddy)
  - Handles user requests, current values, historical queries

- **Naia.Ingestion**: .NET 8 background worker
  - Runs via systemd (naia-ingestion.service)
  - Polls configured data connectors
  - Publishes to Kafka
  - Updates Redis cache

### Data Storage
- **PostgreSQL 16**: Relational metadata
  - User: naia
  - Password: (check .env file)
  - Database: naia
  - Tables: Points, DataSources, etc.

- **QuestDB 7.4.2**: Time-series database
  - HTTP: port 9000 (web console + API)
  - PG Wire: port 8812 (PostgreSQL protocol)
  - ILP: port 9009 (Influx Line Protocol)
  - Optimized for fast time-series queries

- **Redis 7**: Cache + idempotency
  - Port: 6379
  - Max memory: 512MB (LRU eviction)
  - Stores: Current values, deduplication keys

### Message Queue
- **Kafka 7.5.3**: Decoupled data pipeline
  - External: port 9092
  - Internal: port 29092
  - Topics: naia.datapoints (12 partitions), naia.datapoints.dlq (3 partitions)
  - Retention: 7 days default

- **Zookeeper 7.5.3**: Kafka coordination
  - Port: 2181

### Management UIs
- **Kafka UI**: http://37.27.189.86:8080
  - View topics, messages, consumer groups
  
- **Redis Commander**: http://37.27.189.86:8081
  - Inspect Redis cache

## Data Connectors

### Active Connectors

**WeatherApiConnector** (Free - Open-Meteo)
- Polls every 5 minutes
- Variables: temperature, humidity, wind speed, precipitation, etc.
- Point naming: `WEATHER_{LAT}_{LON}_{VARIABLE}`
- No API key required (unlimited free tier)

**EiaGridApiConnector** (Free - EIA.gov)
- Polls every 15 minutes
- US electricity grid operators: CAISO, ERCOT, MISO, NYISO, PJM
- Point naming: `GRID_{FRIENDLY_NAME}`
- Requires free API key from data.eia.gov

**OpcSimulatorConnector** (Built-in simulation)
- Generates synthetic time-series data
- Useful for testing and demo

### Future Connectors
- **PI System Connector**: For OSIsoft PI historians
- **MQTT Connector**: For IoT devices
- **REST Generic Connector**: Template for any REST API

## Technology Stack

| Layer | Technology | Version | Purpose |
|-------|-----------|---------|---------|
| Runtime | .NET | 8.0.416 | Backend runtime |
| Language | C# | 12 | Application code |
| API Framework | ASP.NET Core | 8.0 | REST + WebSocket |
| Relational DB | PostgreSQL | 16 | Metadata storage |
| Time-Series DB | QuestDB | 7.4.2 | High-speed time-series |
| Cache | Redis | 7 | Current values + idempotency |
| Messaging | Apache Kafka | 7.5.3 | Data pipeline backbone |
| Coordination | Zookeeper | 7.5.3 | Kafka coordination |
| Reverse Proxy | Caddy | 2.10.2 | SSL + HTTP/2 + WebSocket |
| Logging | Serilog | Latest | Structured logging |
| Container | Docker | 29.1.4 | Infrastructure |
| Orchestration | Docker Compose | 3.8 | Multi-container setup |
| Process Manager | Systemd | Linux | Service management |
| DNS | Cloudflare | - | Domain + DNS records |

## Deployment Environment

**Server**: Hetzner CCX23
- CPU: 4 vCores
- RAM: 16 GB
- Storage: 240 GB NVMe SSD
- IP: 37.27.189.86
- OS: Linux (Ubuntu 22.04+)

**Domain**: app.naia.run
- Managed via Cloudflare
- A record: app → 37.27.189.86
- SSL: Automatic (Let's Encrypt via Caddy)

**Firewall**:
- Port 22: SSH (restricted)
- Port 80: HTTP (redirects to 443)
- Port 443: HTTPS (main entry point)
- Ports 5432, 6379, 8812, 9009, 9092, 2181: Local network only

## Development Languages

- **Backend**: C# (.NET 8)
- **Frontend**: (Separate repo, deployed separately)
- **Infrastructure**: Docker Compose (YAML)
- **Configuration**: JSON (appsettings.json)
- **Deployment**: Bash scripts + systemd

## Data Flow Example

1. **Ingestion Worker** wakes up every 5 minutes
2. Calls **WeatherApiConnector.GetCurrentValues()**
3. Open-Meteo API returns JSON: `{temperature: 12.5, humidity: 65, ...}`
4. Worker creates **DataPoint** objects with timestamps
5. Publishes batch to **Kafka topic** (naia.datapoints)
6. **QuestDB** subscriber consumes and writes to time-series
7. **Redis** cache updated with latest values
8. **REST API** serves data on request
9. **WebSocket** pushes real-time updates to clients

## Cost Breakdown

| Item | Monthly Cost | Notes |
|------|--------------|-------|
| Hetzner CCX23 | $26 | Server + bandwidth |
| Cloudflare | Free (basic) | DNS + WAF (optional pro) |
| Data APIs | Free | Open-Meteo (unlimited), EIA (free tier) |
| **Total** | **$26** | Minimal for full production |

## Key Constraints & Considerations

- **Kafka topics**: 12 partitions (naia.datapoints) for parallelism
- **Redis maxmemory**: 512MB with LRU eviction (adjust if needed)
- **QuestDB retention**: Not explicitly set (runs until disk full)
- **API polling**: 5-15 minute intervals (configurable per connector)
- **SSL certificates**: Auto-renewed by Caddy (no manual intervention)
- **Logging**: Serilog structured logs to console (optionally file)

## Success Metrics

✅ **Operational**:
- All systemd services running and healthy
- Docker containers up and passing health checks
- Caddy serving HTTPS with valid SSL cert
- DNS resolves app.naia.run → 37.27.189.86

✅ **Data Flow**:
- Connectors polling successfully
- Data flowing through Kafka
- QuestDB storing time-series data
- Redis cache populated

✅ **API Availability**:
- `curl https://app.naia.run/api/health` returns 200
- REST endpoints responding
- WebSocket connections accepted

## Next Steps (Optional Enhancements)

1. Enable EIA connector with API key
2. Implement PI System connector for historian integration
3. Set up data retention policies in QuestDB
4. Configure alerting (Slack/email) on data gaps
5. Add grafana dashboards for monitoring
6. Implement rate limiting on API
7. Add authentication/authorization to API
8. Set up automated backups of PostgreSQL

