# NAIA Quick Reference Cheat Sheet

One-page guide for the most common operations.

## Essential Information

| Item | Value |
|------|-------|
| **Production URL** | https://app.naia.run |
| **Server IP** | 37.27.189.86 |
| **API Health Check** | `curl https://app.naia.run/api/health` |
| **SSH Access** | `ssh root@37.27.189.86` |
| **Code Location** | `/home/naia/naia` |
| **Published Binaries** | `/home/naia/naia/publish` |

---

## 🟢 Status Checks (Do This First)

```bash
# Remote check (any machine)
curl https://app.naia.run/api/health

# Complete system check
ssh root@37.27.189.86 "systemctl status naia-api naia-ingestion caddy"

# Docker health
ssh root@37.27.189.86 "docker compose ps"
```

---

## 🔄 Restart Services

```bash
ssh root@37.27.189.86 "systemctl restart naia-api naia-ingestion caddy"
```

---

## 📋 View Logs

```bash
# API logs (follow = real-time)
ssh root@37.27.189.86 "journalctl -u naia-api -f"

# Ingestion logs
ssh root@37.27.189.86 "journalctl -u naia-ingestion -f"

# Last 50 lines
ssh root@37.27.189.86 "journalctl -u naia-api -n 50"

# Search for errors
ssh root@37.27.189.86 "journalctl -u naia-api | grep -i error"
```

---

## 🚀 Deploy New Code

```bash
# From your local machine (with git access)
ssh root@37.27.189.86 << 'ENDSSH'
  cd /home/naia/naia
  git pull origin main
  dotnet publish Naia.sln -c Release -o ./publish
  systemctl restart naia-api naia-ingestion
  curl https://app.naia.run/api/health
ENDSSH
```

**Or as a one-liner:**
```bash
ssh root@37.27.189.86 "cd /home/naia/naia && git pull && dotnet publish Naia.sln -c Release -o ./publish && systemctl restart naia-api naia-ingestion"
```

---

## 🐳 Docker Cheat Sheet

```bash
# SSH to server first
ssh root@37.27.189.86
cd /home/naia/naia

# See all containers
docker compose ps

# View logs for one container
docker compose logs -f postgres
docker compose logs -f questdb
docker compose logs -f kafka

# Stop everything (keeps data)
docker compose stop

# Start everything
docker compose start

# Restart one service
docker compose restart redis

# Full reset (DELETES DATA)
docker compose down -v && docker compose up -d
```

---

## 🗄️ Database Access

```bash
ssh root@37.27.189.86
cd /home/naia/naia

# PostgreSQL (metadata)
docker compose exec postgres psql -U naia -d naia
# Then: SELECT * FROM points; \dt (list tables); \q (quit)

# QuestDB Web Console
# Open browser: http://37.27.189.86:9000

# QuestDB CLI
docker compose exec questdb bash
# Then: java -cp ":/app/questdb.jar" io.questdb.cli

# Redis
docker compose exec redis redis-cli
# Then: KEYS * (list all); GET key_name; FLUSHDB (delete all)

# Kafka Topics
docker compose exec kafka kafka-topics --bootstrap-server localhost:29092 --list
```

---

## 🐛 Quick Troubleshooting

| Problem | Solution |
|---------|----------|
| **API not responding** | `systemctl status naia-api` → `systemctl restart naia-api` |
| **Database offline** | `docker compose ps postgres` → `docker compose start postgres` |
| **"Connection refused"** | Check firewall: `ufw status` |
| **SSL not working** | `systemctl status caddy` → `journalctl -u caddy -n 50` |
| **High memory** | `free -h` → check if `docker stats` shows process using memory → restart or increase limits |
| **Disk full** | `df -h` → `docker system prune -a` → clean logs: `journalctl --vacuum=time:30d` |
| **No data flowing** | Check logs: `journalctl -u naia-ingestion -f` |
| **SSL certificate expired** | Let Caddy auto-renew: `systemctl restart caddy` |

---

## 📊 Health Check Endpoints

```bash
# API Health
curl https://app.naia.run/api/health

# Kafka UI (if exposed)
# http://37.27.189.86:8080

# Redis Commander (if exposed)
# http://37.27.189.86:8081

# QuestDB Console
# http://37.27.189.86:9000
```

---

## 🔐 Important Passwords & Secrets

```bash
# View PostgreSQL password
grep POSTGRES_PASSWORD /home/naia/naia/docker-compose.yml

# View Redis config
grep redis -A10 /home/naia/naia/docker-compose.yml

# SSL certificate location
ls -la /etc/caddy/
```

**⚠️ Never commit passwords to Git!** Use environment files instead.

---

## 📝 Common Development Tasks

### Create Feature Branch
```bash
git checkout -b feature/my-feature
# ... make changes ...
git add .
git commit -m "feat: description"
git push origin feature/my-feature
# Create PR on GitHub
```

### Deploy Your Changes
```bash
# After PR approved and merged
ssh root@37.27.189.86 << 'ENDSSH'
  cd /home/naia/naia
  git pull origin main
  dotnet publish Naia.sln -c Release -o ./publish
  systemctl restart naia-api naia-ingestion
  # Wait 5 seconds
  sleep 5
  curl https://app.naia.run/api/health
ENDSSH
```

### Rollback Bad Deployment
```bash
ssh root@37.27.189.86 << 'ENDSSH'
  cd /home/naia/naia
  git log --oneline -5  # See last commits
  git revert <commit-hash>
  git push origin main
  git pull origin main
  dotnet publish Naia.sln -c Release -o ./publish
  systemctl restart naia-api naia-ingestion
ENDSSH
```

### Test Locally Before Pushing
```bash
# On your machine
docker compose up -d          # Start local infrastructure
dotnet build Naia.sln         # Compile
dotnet run -p src/Naia.Api    # Run API (port 5001)
curl https://localhost:5001/api/health  # Test
```

---

## 🔔 Monitoring & Alerts

### Daily Health Check
```bash
ssh root@37.27.189.86 << 'ENDSSH'
  echo "=== NAIA Status ==="
  systemctl status naia-api naia-ingestion caddy --no-pager | grep Active
  docker compose ps --format "{{.Names}}\t{{.Status}}"
  free -h | grep -E "Mem:|total"
  df -h / | tail -1
  echo "=== Recent Errors ==="
  journalctl -u naia-api -n 20 | grep -i error || echo "None"
ENDSSH
```

### Set Auto-Restart on Crash
Already configured! Systemd services have `Restart=always` and `RestartSec=10`

---

## 🚨 Emergency Recovery

### If Everything is Down
```bash
ssh root@37.27.189.86
systemctl stop naia-api naia-ingestion caddy
docker compose stop
sleep 5
systemctl start naia-api naia-ingestion caddy
docker compose start
# Wait 10 seconds
sleep 10
curl https://app.naia.run/api/health
```

### If Database is Corrupted
```bash
ssh root@37.27.189.86 << 'ENDSSH'
  cd /home/naia/naia
  # Backup current (broken) state
  docker compose down
  mv /var/lib/docker/volumes/naia-postgres-data /var/lib/docker/volumes/naia-postgres-data.broken
  # Restart fresh
  docker compose up -d postgres
  sleep 10
  # Restore from backup if available
  # docker compose exec postgres psql -U naia naia < ~/backup-postgres-latest.sql
  # Start other services
  docker compose up -d
  systemctl restart naia-api naia-ingestion
ENDSSH
```

---

## 📚 Full Documentation

| Document | Purpose |
|----------|---------|
| [SYSTEM_OVERVIEW.md](SYSTEM_OVERVIEW.md) | Architecture, components, data flow |
| [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md) | Deploy from zero to production |
| [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md) | Troubleshooting, detailed commands |
| [QUICK_REFERENCE.md](QUICK_REFERENCE.md) | This cheat sheet |

---

## 🆘 When You're Lost

1. **Check status**: `ssh root@37.27.189.86 "systemctl status naia-api naia-ingestion caddy"`
2. **View logs**: `ssh root@37.27.189.86 "journalctl -u naia-api -n 50"`
3. **Search this guide**: Ctrl+F in [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md)
4. **Check Docker**: `ssh root@37.27.189.86 "docker compose ps"`
5. **Restart everything**: Follow "Emergency Recovery" section above

---

## 📞 Support Resources

- **GitHub Issues**: Create issue in repository for bugs/features
- **Git Blame**: `git blame file.cs` to see who wrote each line
- **Git Log**: `git log -p src/file.cs` to see file change history
- **Stack Overflow**: Search ".NET" or "Docker" issues
- **Caddy Docs**: https://caddyserver.com/docs
- **QuestDB Docs**: https://questdb.io/docs
- **Kafka Docs**: https://kafka.apache.org/documentation

---

## 💾 Backup Reminders

**Weekly:**
```bash
docker compose exec postgres pg_dump -U naia naia > ~/backup-$(date +%Y%m%d).sql
```

**Before Major Changes:**
```bash
tar -czf ~/backup-naia-$(date +%Y%m%d-%H%M%S).tar.gz /home/naia/naia/
```

**Store backups:**
- Locally on your machine
- External drive (if available)
- Cloud storage (AWS S3, Google Drive, etc.)

---

## ⚡ Pro Tips

1. **Use SSH config** to avoid typing IP every time:
   ```bash
   # ~/.ssh/config
   Host naia
     HostName 37.27.189.86
     User root
   ```
   Then: `ssh naia` instead of `ssh root@37.27.189.86`

2. **Create bash aliases** for common commands:
   ```bash
   alias naia-logs="ssh naia 'journalctl -u naia-api -f'"
   alias naia-status="ssh naia 'systemctl status naia-api naia-ingestion caddy'"
   alias naia-restart="ssh naia 'systemctl restart naia-api naia-ingestion'"
   ```

3. **Monitor in parallel**:
   ```bash
   # Terminal 1
   ssh naia "journalctl -u naia-api -f"
   # Terminal 2
   ssh naia "docker compose logs -f postgres"
   # Terminal 3
   watch -n 2 "curl -s https://app.naia.run/api/health"
   ```

4. **Use git aliases**:
   ```bash
   git config --global alias.st status
   git config --global alias.co checkout
   git config --global alias.br branch
   git config --global alias.ci commit
   ```

5. **Schedule auto-backups** (cron):
   ```bash
   # Run daily at 2 AM
   0 2 * * * ssh root@37.27.189.86 "docker compose exec postgres pg_dump -U naia naia > ~/backup-\$(date +\%Y\%m\%d).sql"
   ```

