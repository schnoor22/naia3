# NAIA Documentation Index

Complete guide to NAIA system documentation and resources.

## 📚 Main Documentation Files

### 1. **SYSTEM_OVERVIEW.md** - What is NAIA?
   - High-level architecture and data flow
   - Component descriptions
   - Technology stack details
   - Cost breakdown
   - Success metrics

   **Read this if you want to understand:**
   - How the system works
   - What each component does
   - Technology choices and why

---

### 2. **INSTALLATION_GUIDE.md** - Deploy from Scratch
   - Step-by-step installation (8 phases)
   - ~30 minutes to production
   - Complete prerequisites checklist
   - Verification procedures
   - Troubleshooting during install

   **Use this if you need to:**
   - Deploy to a new server
   - Understand the installation process
   - Set up all components from zero

---

### 3. **MAINTENANCE_DEBUG_GUIDE.md** - Day-to-Day Operations
   - Quick reference commands for all services
   - Common issues and solutions
   - Development workflow
   - Backup & recovery procedures
   - Emergency procedures
   - Monitoring setup

   **Refer to this for:**
   - Troubleshooting problems
   - Viewing logs and status
   - Making code changes
   - Understanding database access

---

### 4. **QUICK_REFERENCE.md** - Cheat Sheet
   - One-page rapid lookup
   - Essential status checks
   - Common commands
   - Health check endpoints
   - Pro tips and shortcuts
   - Emergency recovery in 5 minutes

   **Use this when you need to:**
   - Quickly check system status
   - Find a command you ran before
   - Troubleshoot in an emergency
   - Copy-paste common operations

---

### 5. **CSV_REPLAY_GUIDE.md** - Historical Data Replay (NEW)
   - Load historical CSV data for training
   - Multi-site support with timezone conversion
   - Privacy via prefix stripping
   - Bad status handling
   - Pattern training workflow

   **Use this if you need to:**
   - Load historical data from CSV files
   - Train pattern recognition across multiple sites
   - Handle timezone conversion
   - Strip privacy-sensitive tag prefixes

---

### 6. **CSV_REPLAY_QUICKSTART.md** - Data Arrival Checklist (NEW)
   - Quick checklist for when data arrives
   - Site-by-site preprocessing steps
   - Configuration examples
   - Deployment and monitoring

   **Use this when:**
   - Real data arrives from sites
   - You need to quickly prepare and load data
   - Setting up multi-site CSV replay

---

## 🎯 Quick Navigation by Task

### "The system is down!"
1. Read: [QUICK_REFERENCE.md - Emergency Recovery](QUICK_REFERENCE.md#emergency-recovery)
2. Or detailed: [MAINTENANCE_DEBUG_GUIDE.md - Emergency Procedures](MAINTENANCE_DEBUG_GUIDE.md#emergency-procedures)
3. Command to run:
   ```bash
   ssh root@37.27.189.86 "systemctl status naia-api naia-ingestion caddy"
   ```

### "I need to deploy new code"
1. Read: [MAINTENANCE_DEBUG_GUIDE.md - Deploy to Production](MAINTENANCE_DEBUG_GUIDE.md#deploy-to-production)
2. Or quick version: [QUICK_REFERENCE.md - Deploy New Code](QUICK_REFERENCE.md#-deploy-new-code)
3. Command:
   ```bash
   ssh root@37.27.189.86 "cd /home/naia/naia && git pull && dotnet publish Naia.sln -c Release -o ./publish && systemctl restart naia-api naia-ingestion"
   ```

### "I see an error in the logs"
1. Read: [MAINTENANCE_DEBUG_GUIDE.md - Common Issues & Solutions](MAINTENANCE_DEBUG_GUIDE.md#common-issues--solutions)
2. Find your error in the table
3. Follow the solution steps

### "How do I set up the system for the first time?"
1. Start: [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md)
2. Follow 8 phases, ~30 minutes total
3. Use checklist at end to verify

### "What is this system and how does it work?"
1. Read: [SYSTEM_OVERVIEW.md](SYSTEM_OVERVIEW.md)
2. Follow the architecture diagram
3. Understand component purposes

### "I want to add a new data connector"
1. Read: [MAINTENANCE_DEBUG_GUIDE.md - Add New Connector](MAINTENANCE_DEBUG_GUIDE.md#add-new-connector)
2. Create files following the pattern
3. Register in ServiceCollectionExtensions
4. Deploy

### "I have historical CSV data to load"
1. Read: [CSV_REPLAY_QUICKSTART.md](CSV_REPLAY_QUICKSTART.md) - Quick checklist
2. Or detailed: [CSV_REPLAY_GUIDE.md](CSV_REPLAY_GUIDE.md) - Complete guide
3. Run preprocessing: `.\preprocess-site-data.ps1`
4. Deploy and monitor

### "I want to train patterns across multiple sites"
1. Load data: [CSV_REPLAY_GUIDE.md - Pattern Training Workflow](CSV_REPLAY_GUIDE.md#pattern-training-workflow)
2. Manually tag first site equipment types
3. Let pattern engine analyze
4. Approve suggestions for other sites

### "The server is running slow"
1. Check: [QUICK_REFERENCE.md - Quick Troubleshooting](QUICK_REFERENCE.md#quick-troubleshooting)
2. Find "High memory" or "Slow performance" solution
3. Or detailed guide: [MAINTENANCE_DEBUG_GUIDE.md - Problem: High Memory Usage](MAINTENANCE_DEBUG_GUIDE.md#problem-high-memory-usage)

### "I need to back up the database"
1. Read: [MAINTENANCE_DEBUG_GUIDE.md - Backup & Recovery](MAINTENANCE_DEBUG_GUIDE.md#backup--recovery)
2. Or quick version: [QUICK_REFERENCE.md - Backup Reminders](QUICK_REFERENCE.md#-backup-reminders)

---

## 🔗 External Resources

### Official Documentation
- **.NET 8**: https://learn.microsoft.com/en-us/dotnet/
- **Docker**: https://docs.docker.com/
- **PostgreSQL**: https://www.postgresql.org/docs/
- **QuestDB**: https://questdb.io/docs/
- **Apache Kafka**: https://kafka.apache.org/documentation/
- **Caddy**: https://caddyserver.com/docs/
- **Cloudflare**: https://developers.cloudflare.com/dns/

### API Documentation
- **Open-Meteo Weather API**: https://open-meteo.com/en/docs (free, no key)
- **EIA Energy Data API**: https://data.eia.gov/docs/ (free API key required)

### Tools
- **GitHub Desktop**: https://desktop.github.com/ (easier Git GUI)
- **VS Code Remote SSH**: https://code.visualstudio.com/docs/remote/ssh
- **PuTTY SSH Client**: https://www.putty.org/ (if not using OpenSSH)

---

## 📊 Command Cheat Sheet Summary

### Status & Health
```bash
# Check everything
curl https://app.naia.run/api/health

# System status
ssh root@37.27.189.86 "systemctl status naia-api naia-ingestion caddy"

# Docker status
ssh root@37.27.189.86 "docker compose ps"
```

### Logs
```bash
# Real-time API logs
ssh root@37.27.189.86 "journalctl -u naia-api -f"

# Last 50 lines
ssh root@37.27.189.86 "journalctl -u naia-api -n 50"

# Search for errors
ssh root@37.27.189.86 "journalctl -u naia-api | grep ERROR"
```

### Restart
```bash
# Restart all services
ssh root@37.27.189.86 "systemctl restart naia-api naia-ingestion caddy"

# Restart just API
ssh root@37.27.189.86 "systemctl restart naia-api"
```

### Deploy
```bash
# Full deployment
ssh root@37.27.189.86 "cd /home/naia/naia && git pull && dotnet publish Naia.sln -c Release -o ./publish && systemctl restart naia-api naia-ingestion"
```

### Database Access
```bash
# PostgreSQL
ssh root@37.27.189.86 "cd /home/naia/naia && docker compose exec postgres psql -U naia -d naia"

# Redis
ssh root@37.27.189.86 "cd /home/naia/naia && docker compose exec redis redis-cli"

# QuestDB Web
# Open: http://37.27.189.86:9000
```

---

## 📋 Common Tasks & Time Estimates

| Task | Time | Document |
|------|------|----------|
| **Check system status** | 30 sec | [QUICK_REFERENCE.md](QUICK_REFERENCE.md#-status-checks) |
| **Deploy code** | 3-5 min | [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md#deploy-to-production) |
| **Restart services** | 30 sec | [QUICK_REFERENCE.md](QUICK_REFERENCE.md#-restart-services) |
| **View API logs** | 1 min | [QUICK_REFERENCE.md](QUICK_REFERENCE.md#-view-logs) |
| **Fix connectivity** | 5-10 min | [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md#problem-api-service-not-running) |
| **Restore database** | 10-15 min | [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md#restore-from-backup) |
| **Full system recovery** | 15 min | [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md#emergency-procedures) |
| **Fresh installation** | 30 min | [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md) |

---

## 🎓 Learning Path

### For New Team Members
1. **Day 1**: Read [SYSTEM_OVERVIEW.md](SYSTEM_OVERVIEW.md) - understand the architecture
2. **Day 2**: Read [QUICK_REFERENCE.md](QUICK_REFERENCE.md) - learn common commands
3. **Day 3**: Follow [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md) locally - practice setup
4. **Week 1**: Keep [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md) handy for reference
5. **Week 2+**: Contribute features, use guides as needed

### For New Deployments
1. Start at [INSTALLATION_GUIDE.md - Phase 1](INSTALLATION_GUIDE.md#phase-1-initial-server-setup-5-minutes)
2. Work through all 8 phases
3. Use checklist at end to verify
4. Keep [QUICK_REFERENCE.md](QUICK_REFERENCE.md) open for commands

### For Troubleshooting
1. Check [QUICK_REFERENCE.md - Quick Troubleshooting](QUICK_REFERENCE.md#quick-troubleshooting)
2. If not found, search [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md)
3. If still stuck, check Docker/systemd logs
4. Refer to [MAINTENANCE_DEBUG_GUIDE.md - Troubleshooting Flowchart](MAINTENANCE_DEBUG_GUIDE.md#quick-troubleshooting-flowchart)

---

## 🔐 Important Information

### Server Details
- **IP**: 37.27.189.86
- **Domain**: app.naia.run
- **Type**: Hetzner CCX23 (4 vCPU, 16GB RAM, 240GB SSD)
- **OS**: Linux (Ubuntu 22.04+)
- **Cost**: $26/month

### Key Ports
- **80**: HTTP (redirects to 443)
- **443**: HTTPS (Caddy)
- **5000**: API (localhost only)
- **5432**: PostgreSQL (localhost only)
- **6379**: Redis (localhost only)
- **8812**: QuestDB PG Wire (localhost only)
- **9000**: QuestDB Web (localhost only)
- **9009**: QuestDB ILP (localhost only)
- **9092**: Kafka (localhost only)
- **2181**: Zookeeper (localhost only)
- **8080**: Kafka UI (localhost only)
- **8081**: Redis Commander (localhost only)

### Access Methods
```bash
# SSH to server
ssh root@37.27.189.86

# Git repository
https://github.com/<username>/naia.git

# Production URL
https://app.naia.run

# Health check
curl https://app.naia.run/api/health
```

---

## 🆘 If You're Stuck

### Level 1: Quick Check (1 minute)
```bash
# Is the API responding?
curl https://app.naia.run/api/health
```

### Level 2: System Status (2 minutes)
```bash
# Are services running?
ssh root@37.27.189.86 "systemctl status naia-api naia-ingestion caddy"
```

### Level 3: Check Logs (5 minutes)
```bash
# What's the error?
ssh root@37.27.189.86 "journalctl -u naia-api -n 50"
```

### Level 4: Search Documentation (5-10 minutes)
- Search [QUICK_REFERENCE.md](QUICK_REFERENCE.md) with Ctrl+F
- Look in [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md) for your error
- Check [Troubleshooting Flowchart](MAINTENANCE_DEBUG_GUIDE.md#quick-troubleshooting-flowchart)

### Level 5: Emergency Recovery (15 minutes)
- Follow [Emergency Procedures](MAINTENANCE_DEBUG_GUIDE.md#emergency-procedures)
- Or [QUICK_REFERENCE.md - Emergency Recovery](QUICK_REFERENCE.md#emergency-recovery)

### Level 6: Full Reset (30 minutes)
- Follow [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md) from scratch

---

## 📞 Getting Help

1. **Check Documentation**: Start with [QUICK_REFERENCE.md](QUICK_REFERENCE.md)
2. **Search Logs**: `journalctl -u naia-api | grep -i "error\|warning"`
3. **Use Troubleshooting Guide**: [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md)
4. **Check GitHub Issues**: In the naia repository
5. **Ask Team**: Slack/Email team members who've deployed before

---

## 📝 File Locations Reference

| File | Purpose | Location |
|------|---------|----------|
| **docker-compose.yml** | Infrastructure definition | `/home/naia/naia/docker-compose.yml` |
| **appsettings.json** | API configuration | `/home/naia/naia/src/Naia.Api/appsettings.json` |
| **Caddyfile** | Reverse proxy config | `/etc/caddy/Caddyfile` |
| **naia-api.service** | API systemd service | `/etc/systemd/system/naia-api.service` |
| **naia-ingestion.service** | Ingestion systemd service | `/etc/systemd/system/naia-ingestion.service` |
| **Published binaries** | Compiled .NET dlls | `/home/naia/naia/publish/` |
| **PostgreSQL data** | Database files | `/var/lib/docker/volumes/naia-postgres-data/` |
| **QuestDB data** | Time-series data | `/var/lib/docker/volumes/naia-questdb-data/` |
| **Redis data** | Cache data | `/var/lib/docker/volumes/naia-redis-data/` |
| **Documentation** | These guides | `/home/naia/naia/*.md` |

---

## ✅ Success Criteria

Your NAIA system is working correctly if:

- ✅ `curl https://app.naia.run/api/health` returns 200 with JSON
- ✅ `systemctl status naia-api` shows "active (running)"
- ✅ `systemctl status naia-ingestion` shows "active (running)"
- ✅ `docker compose ps` shows all containers "Up"
- ✅ No errors in `journalctl -u naia-api -n 20`
- ✅ Data flowing through Kafka (check with Kafka UI at port 8080)
- ✅ SSL certificate valid (check with `curl -I https://app.naia.run`)

---

## 🚀 Next Steps

1. **Bookmark this index** - You'll refer to it often
2. **Read SYSTEM_OVERVIEW.md** - Understand the architecture
3. **Save QUICK_REFERENCE.md** - Quick lookups
4. **Keep MAINTENANCE_DEBUG_GUIDE.md** - For detailed help
5. **Test a deployment** - Follow INSTALLATION_GUIDE.md on a test server

---

**Last Updated**: January 2026
**NAIA Version**: v3 (Production)
**Server**: Hetzner CCX23 @ 37.27.189.86

