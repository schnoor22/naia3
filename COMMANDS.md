# NAIA SSH Commands Cheatsheet

Quick reference for all server operations. Server: `naia@37.27.189.86`

---

## 🔌 Connect to Server

```bash
# Standard SSH connection
ssh naia@37.27.189.86

# With port forwarding (access services locally)
ssh -L 5000:localhost:5000 -L 9000:localhost:9000 naia@37.27.189.86
```

---

## 🔄 Service Management

### API Service (naia-api)
```bash
# Check status
sudo systemctl status naia-api

# Start/Stop/Restart
sudo systemctl start naia-api
sudo systemctl stop naia-api
sudo systemctl restart naia-api

# View live logs
sudo journalctl -u naia-api -f

# View last 100 log entries
sudo journalctl -u naia-api -n 100 --no-pager
```

### Ingestion Service (naia-ingestion)
```bash
# Check status
sudo systemctl status naia-ingestion

# Start/Stop/Restart
sudo systemctl start naia-ingestion
sudo systemctl stop naia-ingestion
sudo systemctl restart naia-ingestion

# View live logs
sudo journalctl -u naia-ingestion -f
```

### Caddy (Reverse Proxy)
```bash
# Check status
sudo systemctl status caddy

# Restart after config changes
sudo systemctl restart caddy

# Validate Caddyfile
caddy validate --config /etc/caddy/Caddyfile

# View Caddy logs
sudo journalctl -u caddy -f
```

---

## 🐳 Docker Commands

### Container Status
```bash
# List all running containers
docker ps

# List all containers (including stopped)
docker ps -a

# Container resource usage
docker stats --no-stream
```

### Individual Services
```bash
# PostgreSQL
docker logs naia-postgres --tail 50
docker exec -it naia-postgres psql -U naia -d naia

# QuestDB
docker logs naia-questdb --tail 50
curl "http://localhost:9000/exec?query=SELECT%20count()%20FROM%20datapoints"

# Redis
docker logs naia-redis --tail 50
docker exec -it naia-redis redis-cli PING

# Kafka
docker logs naia-kafka --tail 50
docker exec -it naia-kafka kafka-topics --list --bootstrap-server localhost:9092
```

### Restart All Infrastructure
```bash
cd /opt/naia
docker compose restart
```

---

## 📦 Deployment

### Quick Redeploy (from Windows)
```powershell
# 1. Build the API (Windows)
cd C:\naia3
dotnet publish src/Naia.Api -c Release -o publish

# 2. Build the UI (Windows)
cd C:\naia3\src\Naia.Web
.\build.ps1

# 3. Copy to server (Windows)
scp -r publish/* naia@37.27.189.86:/opt/naia/

# 4. Restart services (SSH)
ssh naia@37.27.189.86 "sudo systemctl restart naia-api"
```

### Full Deployment Script
```bash
# On server, after files are copied
cd /opt/naia
sudo systemctl stop naia-api
sudo systemctl stop naia-ingestion
# Files should already be copied here
sudo systemctl start naia-api
sudo systemctl start naia-ingestion
```

### UI Only Redeploy
```powershell
# Windows - build UI
cd C:\naia3\src\Naia.Web
.\build.ps1

# Copy wwwroot to server
scp -r C:\naia3\src\Naia.Api\wwwroot\* naia@37.27.189.86:/opt/naia/wwwroot/

# Restart API to pick up new static files (may not be needed)
ssh naia@37.27.189.86 "sudo systemctl restart naia-api"
```

---

## 🔍 Diagnostics

### API Health Check
```bash
# From server
curl http://localhost:5000/api/health | jq

# From anywhere
curl https://app.naia.run/api/health | jq
```

### Version Check
```bash
curl https://app.naia.run/api/version | jq
```

### Database Checks
```bash
# PostgreSQL - check tables
docker exec -it naia-postgres psql -U naia -d naia -c "\dt"

# PostgreSQL - check points count
docker exec -it naia-postgres psql -U naia -d naia -c "SELECT COUNT(*) FROM points;"

# QuestDB - check datapoints
curl "http://localhost:9000/exec?query=SELECT%20count()%20FROM%20datapoints"

# Redis - check keys
docker exec -it naia-redis redis-cli DBSIZE
```

### Network/Ports
```bash
# Check what's listening
sudo ss -tlnp

# Check specific port
sudo lsof -i :5000
```

### Disk Space
```bash
df -h /opt /var
du -sh /opt/naia/*
```

---

## 🔧 Configuration Files

| File | Purpose |
|------|---------|
| `/opt/naia/appsettings.json` | API configuration |
| `/opt/naia/appsettings.Production.json` | Production overrides |
| `/etc/caddy/Caddyfile` | Reverse proxy config |
| `/etc/systemd/system/naia-api.service` | API systemd unit |
| `/etc/systemd/system/naia-ingestion.service` | Ingestion systemd unit |

### Edit Caddyfile
```bash
sudo nano /etc/caddy/Caddyfile
sudo systemctl restart caddy
```

### Edit API Settings
```bash
sudo nano /opt/naia/appsettings.Production.json
sudo systemctl restart naia-api
```

---

## 🆘 Troubleshooting

### API Won't Start
```bash
# Check for port conflicts
sudo lsof -i :5000

# Check logs for errors
sudo journalctl -u naia-api -n 50 --no-pager

# Try running manually to see errors
cd /opt/naia
dotnet Naia.Api.dll
```

### Database Connection Failed
```bash
# Check Docker containers are running
docker ps

# Restart all infrastructure
cd /opt/naia
docker compose restart

# Check PostgreSQL specifically
docker logs naia-postgres --tail 20
```

### SSL/HTTPS Issues
```bash
# Check Caddy status
sudo systemctl status caddy

# Check Caddy logs
sudo journalctl -u caddy -n 50

# Verify certificate
curl -vI https://app.naia.run 2>&1 | grep -i "ssl\|certificate"
```

### SignalR/WebSocket Issues
```bash
# Test WebSocket endpoint
curl -i -N -H "Connection: Upgrade" -H "Upgrade: websocket" \
     -H "Sec-WebSocket-Key: SGVsbG8sIHdvcmxkIQ==" \
     -H "Sec-WebSocket-Version: 13" \
     https://app.naia.run/patternHub
```

---

## 📊 Monitoring

### Watch Logs
```bash
# API logs (live)
sudo journalctl -u naia-api -f

# All NAIA services
sudo journalctl -u naia-api -u naia-ingestion -f

# Grep for errors
sudo journalctl -u naia-api | grep -i error | tail -20
```

### Quick Health Dashboard
```bash
# One-liner status check
echo "=== Services ===" && systemctl is-active naia-api naia-ingestion caddy && \
echo "=== Docker ===" && docker ps --format "{{.Names}}: {{.Status}}" && \
echo "=== API ===" && curl -s http://localhost:5000/api/health | jq -r '.status'
```

---

*Last updated: January 2026*
