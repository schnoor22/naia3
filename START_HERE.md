# � NAIA Complete Documentation Bundle

**Start here.** Choose your path based on what you need to do.

---

## 🎯 I Want To...

### **"Get a quick status check"** (30 seconds)
→ [QUICK_REFERENCE.md - Status Checks](QUICK_REFERENCE.md#-status-checks)
```bash
curl https://app.naia.run/api/health
```

### **"Deploy new code"** (5 minutes)
→ [QUICK_REFERENCE.md - Deploy New Code](QUICK_REFERENCE.md#-deploy-new-code)
```bash
ssh root@37.27.189.86 "cd /home/naia/naia && git pull && dotnet publish Naia.sln -c Release -o ./publish && systemctl restart naia-api naia-ingestion"
```

### **"Fix a problem"** (5-15 minutes)
→ [QUICK_REFERENCE.md - Quick Troubleshooting](QUICK_REFERENCE.md#quick-troubleshooting)
1. Find your error in the table
2. Follow the solution
3. Check logs if still stuck

### **"Understand the system"** (30 minutes)
→ [SYSTEM_OVERVIEW.md](SYSTEM_OVERVIEW.md)
- Architecture diagram
- Component descriptions
- Technology stack
- Data flow explanation

### **"Set up the system from scratch"** (30 minutes)
→ [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md)
- 8 phases with exact commands
- Verification checklist
- Troubleshooting during install

### **"Know all the operations commands"** (Reference)
→ [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md)
- All useful commands organized by task
- Common issues with solutions
- Development workflow
- Backup/recovery procedures

### **"Get a one-page cheat sheet"** (Quick lookup)
→ [QUICK_REFERENCE.md](QUICK_REFERENCE.md)
- Essential info at a glance
- Command reference
- Emergency procedures
- Pro tips

### **"See the whole project journey"** (Overview)
→ [PROJECT_COMPLETION_SUMMARY.md](PROJECT_COMPLETION_SUMMARY.md)
- What was built
- Why each decision was made
- Project status
- Next steps

### **"Find a specific document"** (Navigation)
→ [DOCUMENTATION_INDEX.md](DOCUMENTATION_INDEX.md)
- All documents described
- Task-based navigation
- External resources
---

## 📚 All Documentation Files

| Document | Purpose | Read Time | When to Use |
|----------|---------|-----------|------------|
| **[QUICK_REFERENCE.md](QUICK_REFERENCE.md)** | Cheat sheet | 10 min | Daily operations |
| **[SYSTEM_OVERVIEW.md](SYSTEM_OVERVIEW.md)** | Architecture | 20 min | Understanding the system |
| **[INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md)** | Setup steps | 30 min | Deploying from scratch |
| **[MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md)** | Operations & troubleshooting | 60 min | Reference during work |
| **[DOCUMENTATION_INDEX.md](DOCUMENTATION_INDEX.md)** | Navigation hub | 10 min | Finding documents |
| **[PROJECT_COMPLETION_SUMMARY.md](PROJECT_COMPLETION_SUMMARY.md)** | Project overview | 15 min | Context & decisions |

---

## 🚀 Quick Start (Choose Your Role)

### **I'm a New Team Member**
1. Read [SYSTEM_OVERVIEW.md](SYSTEM_OVERVIEW.md) (20 min)
2. Read [QUICK_REFERENCE.md](QUICK_REFERENCE.md) (10 min)
3. Test: `curl https://app.naia.run/api/health`
4. SSH: `ssh root@37.27.189.86`
5. Keep [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md) handy

### **I'm Deploying the System**
1. Start at [INSTALLATION_GUIDE.md - Phase 1](INSTALLATION_GUIDE.md#phase-1-initial-server-setup-5-minutes)
2. Work through all 8 phases (~30 minutes)
3. Use verification checklist at end
4. Bookmark [QUICK_REFERENCE.md](QUICK_REFERENCE.md) for later

### **I'm Maintaining the System**
1. Bookmark [QUICK_REFERENCE.md](QUICK_REFERENCE.md) (quick commands)
2. Keep [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md) open
3. Use "Quick Troubleshooting" table for common issues
4. Follow "Backup Reminders" weekly

### **I'm Developing Features**
1. Read [SYSTEM_OVERVIEW.md](SYSTEM_OVERVIEW.md) (architecture)
2. Review [MAINTENANCE_DEBUG_GUIDE.md - Development Workflow](MAINTENANCE_DEBUG_GUIDE.md#development-workflow)
3. Clone repo and set up locally
4. Follow git workflow: branch → commit → PR → merge → deploy

---

## ⚡ Most Common Commands

```bash
# Check if system is up
curl https://app.naia.run/api/health

# SSH to server
ssh root@37.27.189.86

# View API logs in real-time
ssh root@37.27.189.86 "journalctl -u naia-api -f"

# Restart services
ssh root@37.27.189.86 "systemctl restart naia-api naia-ingestion caddy"

# Deploy new code
ssh root@37.27.189.86 "cd /home/naia/naia && git pull && dotnet publish Naia.sln -c Release -o ./publish && systemctl restart naia-api naia-ingestion"
```

---

## 🔗 Key Information

| Item | Value |
|------|-------|
| **Production URL** | https://app.naia.run |
| **Server IP** | 37.27.189.86 |
| **SSH** | `ssh root@37.27.189.86` |
| **Health Check** | `curl https://app.naia.run/api/health` |
| **Code Location** | `/home/naia/naia` |

---

## ✅ System Status

**Current Status**: 🟢 LIVE & OPERATIONAL

All services running:
- ✅ API (naia-api.service)
- ✅ Ingestion Worker (naia-ingestion.service)
- ✅ PostgreSQL
- ✅ QuestDB
- ✅ Redis
- ✅ Kafka
- ✅ Caddy

---

## 📖 Next Steps

**Choose what you need:**
- 🆘 **Emergency?** → [QUICK_REFERENCE.md - Emergency Recovery](QUICK_REFERENCE.md#emergency-recovery)
- 📝 **First time?** → [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md)
- 🔧 **Maintenance?** → [MAINTENANCE_DEBUG_GUIDE.md](MAINTENANCE_DEBUG_GUIDE.md)
- 🏗️ **Understand system?** → [SYSTEM_OVERVIEW.md](SYSTEM_OVERVIEW.md)
- 📚 **Find documents?** → [DOCUMENTATION_INDEX.md](DOCUMENTATION_INDEX.md)
- 📋 **Project details?** → [PROJECT_COMPLETION_SUMMARY.md](PROJECT_COMPLETION_SUMMARY.md)

---

**Generated**: January 2026
**System Version**: 3.0 (Production)
**Status**: 🚀 READY FOR OPERATIONS

**Pattern suggestions to view:****
```powershell
# List suggestions
Invoke-RestMethod "http://localhost:5052/api/suggestions/pending" | ConvertTo-Json
```

---

## 📚 Full Documentation

- Complete guide: [START_HISTORIAN_24H_TEST.md](START_HISTORIAN_24H_TEST.md)
- System overview: [READY_TO_START.md](READY_TO_START.md)
- Architecture: [docs/architecture/](docs/architecture/)

---

**The First Industrial Historian That Learns From You™**  
*Let it run for 24 hours and watch NAIA learn your patterns!* 🧠

---

## Quick Reference Commands

```powershell
# Check infrastructure
docker-compose ps

# View QuestDB data
Start-Process "http://localhost:9000"

# View Hangfire jobs
Start-Process "http://localhost:5052/hangfire"

# View Kafka messages
Start-Process "http://localhost:8080"

# Get API docs
Start-Process "http://localhost:5052/swagger"

# Stop everything
# Ctrl+C in each PowerShell window
# Then: docker-compose down
```
