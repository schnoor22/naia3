# NAIA Maintenance, Debug & Development Guide

Cheat sheet for day-to-day operations, troubleshooting, and development.

---

## Quick Reference Commands

### Connect to Server
```bash
ssh root@37.27.189.86
```

### View Logs (Real-time)
```bash
# API service logs (follow = tail -f)
journalctl -u naia-api -f

# Ingestion worker logs
journalctl -u naia-ingestion -f

# Caddy reverse proxy logs
journalctl -u caddy -f

# Docker container logs
docker compose logs -f postgres
docker compose logs -f questdb
docker compose logs -f kafka
```

### Service Control
```bash
# Check status
systemctl status naia-api
systemctl status naia-ingestion
systemctl status caddy

# Restart service
systemctl restart naia-api
systemctl restart naia-ingestion
systemctl restart caddy

# Stop service
systemctl stop naia-api

# Start service
systemctl start naia-api

# View service configuration
cat /etc/systemd/system/naia-api.service
```

### Docker Commands
```bash
# See running containers and health
docker compose ps

# View container logs (last 100 lines, follow)
docker compose logs -f --tail=100 postgres

# Check specific container health
docker compose exec postgres pg_isready -U naia -d naia

# Access container shell
docker compose exec postgres bash
docker compose exec questdb bash
docker compose exec redis redis-cli

# Restart all containers
docker compose restart

# Stop all containers (keeps data)
docker compose stop

# Stop and remove containers (keeps volumes/data)
docker compose down

# Stop everything and DELETE data
docker compose down -v
```

### Database Access
```bash
# PostgreSQL
docker compose exec postgres psql -U naia -d naia
# Then run SQL: SELECT * FROM points; \dt

# QuestDB (HTTP API)
curl http://localhost:9000/exec?query=select%20*%20from%20telemetry

# QuestDB (PG Wire - like PostgreSQL)
psql -h localhost -p 8812 -U admin -d qdb
# Then run SQL: SELECT * FROM telemetry; SHOW TABLES;

# Redis
docker compose exec redis redis-cli
# Then run commands: KEYS * (list all keys), GET key_name, FLUSHDB (delete all)
```

### Health Checks
```bash
# API health endpoint
curl https://app.naia.run/api/health

# All services status at once
systemctl status naia-api naia-ingestion caddy

# Docker services health
docker compose ps | grep "unhealthy"

# Check disk space
df -h

# Check memory usage
free -h

# Check CPU load
top -b -n 1 | head -20
```

---

## Common Issues & Solutions

### Problem: API Service Not Running

**Symptoms**: `systemctl status naia-api` shows `failed` or `inactive`

**Solution**:
```bash
# 1. Check logs for error
journalctl -u naia-api -n 50

# 2. Common causes:
# - Port 5000 already in use: lsof -i :5000
# - Missing dependency: dotnet publish again
# - Wrong working directory in service file

# 3. Restart
systemctl restart naia-api

# 4. Check status
systemctl status naia-api
```

### Problem: Database Connection Failing

**Symptoms**: API logs show "connection refused" or "password authentication failed"

**Solution**:
```bash
# 1. Check if PostgreSQL is running
docker compose ps | grep postgres

# 2. If not running, start it
docker compose start postgres

# 3. Verify connection
docker compose exec postgres pg_isready -U naia -d naia

# 4. Check credentials in docker-compose.yml
grep -A3 "POSTGRES_" docker-compose.yml

# 5. If password is wrong, reset:
docker compose down
docker compose up -d
```

### Problem: QuestDB Not Accepting Data

**Symptoms**: Data not appearing in QuestDB, Kafka publishing succeeds

**Solution**:
```bash
# 1. Check QuestDB is running
docker compose ps | grep questdb

# 2. Check ILP port (9009) is listening
docker compose exec questdb curl http://localhost:9000/

# 3. Check QuestDB logs
docker compose logs questdb | tail -50

# 4. Verify data can be written
# Access QuestDB web console: http://37.27.189.86:9000
# Create test table and insert data manually

# 5. If corrupted, reset:
docker compose down
docker volume rm naia-questdb-data
docker compose up -d
```

### Problem: High Memory Usage

**Symptoms**: `free -h` shows high used memory, server becomes slow

**Solution**:
```bash
# 1. Check what's using memory
docker stats  # See container memory usage

# 2. Check service memory
ps aux | grep dotnet

# 3. Common cause: Redis maxmemory too low
# Edit docker-compose.yml, find redis section:
# Change: --maxmemory 512mb to --maxmemory 2gb

docker compose restart redis

# 4. Or reduce connector polling frequency
# Edit appsettings.json, increase polling interval
# Restart services
systemctl restart naia-ingestion
```

### Problem: Disk Space Running Out

**Symptoms**: `df -h` shows > 90% used

**Solution**:
```bash
# 1. Check what's taking space
du -sh /* | sort -h

# 2. Check logs
du -sh /var/log/journal/

# 3. Clean old Docker logs
docker system prune -a --volumes  # WARNING: Deletes unused containers/images

# 4. Clean journalctl logs (older than 30 days)
journalctl --vacuum=time:30d

# 5. Consider increasing server size (Hetzner resize)
```

### Problem: SSL Certificate Not Auto-Renewing

**Symptoms**: Certificate expires, Caddy shows error

**Solution**:
```bash
# 1. Check Caddy status
systemctl status caddy

# 2. Check Caddy logs
journalctl -u caddy -n 100

# 3. Verify domain resolves correctly
nslookup app.naia.run

# 4. Manually request certificate
caddy reload --config /etc/caddy/Caddyfile

# 5. Check Let's Encrypt rate limits (max 50/week per domain)
# If exceeded, wait or change domain

# 6. Verify certificate
curl -I https://app.naia.run | grep -i cert
```

### Problem: Kafka Topics Not Created

**Symptoms**: Messages published to non-existent topics, data loss

**Solution**:
```bash
# 1. Check Kafka is running
docker compose ps | grep kafka

# 2. Wait for kafka-init to run (takes ~30 seconds)
docker compose logs kafka-init | tail -20

# 3. Check topics exist
docker compose exec kafka kafka-topics --bootstrap-server localhost:29092 --list

# 4. If missing, create manually
docker compose exec kafka kafka-topics \
  --bootstrap-server localhost:29092 \
  --create --if-not-exists \
  --topic naia.datapoints \
  --partitions 12 \
  --replication-factor 1
```

### Problem: Connector Not Polling Data

**Symptoms**: No new data points appearing, Kafka is empty

**Solution**:
```bash
# 1. Check ingestion worker is running
systemctl status naia-ingestion

# 2. Check logs for connector errors
journalctl -u naia-ingestion -n 100 | grep -i "error\|exception"

# 3. Verify connector configuration in appsettings.json
cat /home/naia/naia/appsettings.json | grep -A10 "WeatherApi\|EiaGrid"

# 4. Check API key is valid (if required)
# EIA: https://data.eia.gov/account (sign in and verify API key)
# Open-Meteo: No API key needed (free tier)

# 5. Restart ingestion worker
systemctl restart naia-ingestion

# 6. Monitor logs
journalctl -u naia-ingestion -f
```

---

## Development Workflow

### Local Setup (on your machine)

```bash
# 1. Clone repository
git clone https://github.com/<username>/naia.git
cd naia

# 2. Start local infrastructure
docker compose up -d

# 3. Build solution
dotnet build Naia.sln

# 4. Run API locally
dotnet run -p src/Naia.Api --launch-profile https
# API runs on https://localhost:5001

# 5. In another terminal, run ingestion worker
dotnet run -p src/Naia.Ingestion

# 6. Test API
curl https://localhost:5001/api/health
```

### Make Code Changes

```bash
# 1. Create feature branch
git checkout -b feature/my-feature

# 2. Make changes in VS Code
# Edit files in src/Naia.Api, src/Naia.Connectors, etc.

# 3. Test locally
dotnet build Naia.sln
# Fix any compilation errors

# 4. Restart running services to pick up changes
# Kill dotnet processes (Ctrl+C in terminals)
# Re-run: dotnet run -p src/Naia.Api

# 5. Test functionality
curl https://localhost:5001/api/new-endpoint
```

### Deploy to Production

```bash
# 1. Commit changes
git add .
git commit -m "feat: description of changes"

# 2. Push to GitHub
git push origin feature/my-feature

# 3. Create Pull Request
# Go to https://github.com/<username>/naia
# Click "New Pull Request"
# Review changes, request code review from team

# 4. Once approved, merge to main
# Click "Merge Pull Request"

# 5. SSH to server
ssh root@37.27.189.86
cd /home/naia/naia

# 6. Pull latest code
git pull origin main

# 7. Rebuild
dotnet publish Naia.sln -c Release -o ./publish

# 8. Restart services
systemctl restart naia-api naia-ingestion

# 9. Verify
curl https://app.naia.run/api/health
journalctl -u naia-api -n 20
```

### Rollback if Something Breaks

```bash
# 1. See recent commits
git log --oneline -10

# 2. Revert to previous commit
git revert <commit-hash>
# OR
git reset --hard <commit-hash>

# 3. Push to GitHub
git push origin main

# 4. Redeploy (see above)
git pull origin main
dotnet publish Naia.sln -c Release -o ./publish
systemctl restart naia-api naia-ingestion
```

### Add New Connector

```bash
# 1. Create connector directory
mkdir -p src/Naia.Connectors/YourConnector

# 2. Create files:
# - YourConnectorOptions.cs (configuration)
# - YourConnector.cs (implementation)
# - YourIngestionWorker.cs (polling logic)

# 3. Implement interfaces:
# - ICurrentValueConnector
# - IHistoricalDataConnector (optional)
# - IDiscoverableConnector (optional)

# 4. Register in ServiceCollectionExtensions.cs
# services.AddYourConnector(configuration);

# 5. Add to appsettings.json
# {
#   "YourConnector": {
#     "Enabled": true,
#     "PollingIntervalSeconds": 300
#   }
# }

# 6. Add to DataSourceType enum in Domain/Entities/DataSource.cs

# 7. Build and test locally
dotnet build Naia.sln

# 8. Deploy (see workflow above)
```

---

## Monitoring & Metrics

### Check System Health Daily

```bash
# Create a health check script
cat > ~/health-check.sh << 'EOF'
#!/bin/bash
echo "=== NAIA System Health Check ==="
echo ""
echo "Services Status:"
systemctl status naia-api naia-ingestion caddy --no-pager | grep Active

echo ""
echo "Docker Containers:"
docker compose ps --format "{{.Names}}\t{{.Status}}"

echo ""
echo "Disk Usage:"
df -h | grep -E "^/|Size"

echo ""
echo "Memory Usage:"
free -h

echo ""
echo "Recent Errors:"
journalctl -u naia-api -u naia-ingestion -n 20 | grep -i "error\|exception" || echo "No recent errors"

echo ""
echo "API Response Time:"
curl -w "Status: %{http_code}, Time: %{time_total}s\n" -o /dev/null -s https://app.naia.run/api/health

echo ""
echo "SSL Certificate Expiration:"
curl -I https://app.naia.run 2>/dev/null | grep -i cert || echo "Certificate info"
EOF

chmod +x ~/health-check.sh
~/health-check.sh
```

### Set Up Alerting (Optional)

```bash
# Send alert if service goes down
cat > ~/check-service.sh << 'EOF'
#!/bin/bash
RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" https://app.naia.run/api/health)
if [ "$RESPONSE" != "200" ]; then
  echo "ALERT: NAIA API returned $RESPONSE" | mail -s "NAIA Alert" your@email.com
fi
EOF

# Run every 5 minutes via cron
crontab -e
# Add line: */5 * * * * /root/check-service.sh
```

### View Key Metrics

```bash
# Data ingestion rate
docker compose logs kafka | grep "messages received"

# API request count
journalctl -u naia-api | grep "GET\|POST" | wc -l

# Current active connections
netstat -an | grep ESTABLISHED | wc -l

# CPU per service
ps aux | grep dotnet

# Network throughput
iftop  # Install with: apt install iftop
```

---

## Backup & Recovery

### Backup Important Files

```bash
# Backup PostgreSQL database
docker compose exec postgres pg_dump -U naia naia > ~/backup-postgres-$(date +%Y%m%d).sql

# Backup Redis data
docker compose exec redis redis-cli BGSAVE
docker cp naia-redis:/data/dump.rdb ~/backup-redis-$(date +%Y%m%d).rdb

# Backup systemd service files
cp /etc/systemd/system/naia-*.service ~/backup-systemd/

# Backup Caddy configuration
cp /etc/caddy/Caddyfile ~/backup-caddy-$(date +%Y%m%d)

# Backup entire /opt/naia directory
tar -czf ~/backup-naia-$(date +%Y%m%d).tar.gz /home/naia/naia/
```

### Restore from Backup

```bash
# Restore PostgreSQL
docker compose exec postgres psql -U naia naia < ~/backup-postgres-20260112.sql

# Restore Redis
docker compose stop redis
docker cp ~/backup-redis-20260112.rdb naia-redis:/data/dump.rdb
docker compose start redis

# Restore codebase
cd /tmp
tar -xzf ~/backup-naia-20260112.tar.gz
cp -r home/naia/naia/* /home/naia/naia/
cd /home/naia/naia
dotnet publish Naia.sln -c Release -o ./publish
systemctl restart naia-api naia-ingestion
```

---

## Quick Troubleshooting Flowchart

```
Service not responding?
├─ Check if running: systemctl status naia-api
│  ├─ Not running? Start it: systemctl start naia-api
│  └─ Failed? Check logs: journalctl -u naia-api -n 50
│
├─ Check if listening: curl localhost:5000/api/health
│  ├─ No response? Check firewall
│  └─ Works locally but not via HTTPS? Check Caddy
│
├─ Check Caddy: systemctl status caddy
│  ├─ Not running? Start it: systemctl start caddy
│  └─ Failed? Check logs: journalctl -u caddy -n 50
│
└─ Check DNS: nslookup app.naia.run
   ├─ Not resolving? Check Cloudflare A record
   └─ Resolves but no response? Server might be down

Data not flowing?
├─ Check Ingestion Worker: systemctl status naia-ingestion
├─ Check PostgreSQL: docker compose exec postgres pg_isready
├─ Check QuestDB: curl http://localhost:9000/
├─ Check Redis: docker compose exec redis redis-cli ping
├─ Check Kafka: docker compose exec kafka kafka-topics --list
└─ Check logs: journalctl -u naia-ingestion -f

Slow performance?
├─ Check memory: free -h
├─ Check disk: df -h
├─ Check CPU: top
├─ Check network: iftop
└─ Check logs for errors: journalctl -u naia-api -n 100
```

---

## Emergency Procedures

### Complete Service Recovery

```bash
# If system is unstable, full restart:
systemctl stop naia-api naia-ingestion caddy
docker compose stop
sleep 5

systemctl start naia-api naia-ingestion caddy
docker compose start
sleep 10

# Monitor
journalctl -u naia-api -f
docker compose logs -f postgres
```

### Data Recovery

```bash
# If database is corrupted:

# 1. Backup current (broken) data
docker compose down
sudo mv /var/lib/docker/volumes/naia-postgres-data /var/lib/docker/volumes/naia-postgres-data.broken

# 2. Restore from SQL backup (if available)
docker compose up -d postgres
sleep 10
docker compose exec postgres psql -U naia naia < ~/backup-postgres-latest.sql

# 3. Restart other services
docker compose up -d
systemctl restart naia-api naia-ingestion

# 4. Verify
curl https://app.naia.run/api/health
```

### Server Reinstall (Last Resort)

```bash
# If server is completely broken:

# 1. Back up /home/naia/naia to your local machine
scp -r root@37.27.189.86:/home/naia/naia ./backup/

# 2. Back up databases
scp root@37.27.189.86:~/backup-postgres-*.sql ./backup/

# 3. Reinstall OS via Hetzner console

# 4. Follow INSTALLATION_GUIDE.md from scratch

# 5. Restore from backups
```

---

## Common Useful Commands

```bash
# Show git commit history
git log --oneline -20

# Show uncommitted changes
git diff

# Show current branch
git branch

# Search logs for errors
journalctl -u naia-api | grep ERROR

# Search logs between timestamps
journalctl -u naia-api --since "2024-01-01 10:00:00"

# Search for specific text in code
grep -r "SearchTerm" src/

# Find files modified in last hour
find . -mmin -60

# Count lines of code
find src -name "*.cs" | xargs wc -l

# Check open ports
netstat -tlnp | grep LISTEN

# Check running processes
ps aux | grep naia

# Check environment variables
env | grep NAIA

# Test DNS
dig app.naia.run
nslookup app.naia.run
host app.naia.run

# Test port connectivity
nc -zv 37.27.189.86 443
telnet 37.27.189.86 443

# Generate SSL certificate info
openssl s_client -connect app.naia.run:443
```

