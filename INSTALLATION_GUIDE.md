# NAIA Installation Guide

Complete step-by-step guide to deploy NAIA from zero to production.

## Prerequisites

- **Server**: Hetzner CCX23 or equivalent (4 vCPU, 16GB RAM, 240GB SSD)
- **Domain**: Registered domain (e.g., app.naia.run)
- **DNS Provider**: Cloudflare or similar (for DNS A records)
- **Local Machine**: Git, .NET 8 SDK, Docker Desktop (optional for local testing)
- **Network Access**: SSH to server (port 22)

## Phase 1: Initial Server Setup (5 minutes)

### Step 1.1: Purchase & Connect to Server

```bash
# SSH to your server
ssh root@<YOUR_SERVER_IP>

# Example: ssh root@37.27.189.86
# You'll be prompted for password (from Hetzner email)
```

### Step 1.2: Update System

```bash
apt update && apt upgrade -y
```

### Step 1.3: Install Required Software

**Install Docker**:
```bash
# Download and run Docker install script
curl -fsSL https://get.docker.com -o get-docker.sh
sh get-docker.sh
```

**Install Docker Compose** (usually bundled):
```bash
docker compose version  # Verify it's installed
```

**Install .NET 8 SDK**:
```bash
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --version 8.0

# Add to PATH
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
source ~/.bashrc

dotnet --version  # Should show 8.0.xxx
```

**Install Node.js 20** (for Kafka UI, optional):
```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
apt install -y nodejs
```

**Install Caddy** (SSL/reverse proxy):
```bash
apt install -y caddy
```

### Step 1.4: Configure Firewall

```bash
# Install UFW if not present
apt install -y ufw

# Allow SSH, HTTP, HTTPS
ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw enable

# Verify
ufw status
```

### Step 1.5: Create Application User

```bash
# Create unprivileged user for the app (security best practice)
useradd -m -s /bin/bash naia
usermod -aG docker naia  # Allow naia user to run docker

# Switch to naia user
su - naia
```

## Phase 2: Clone & Prepare Code (5 minutes)

### Step 2.1: Clone Repository

```bash
# As naia user, navigate to home directory
cd ~

# Clone the NAIA repository
git clone https://github.com/<YOUR_USERNAME>/naia.git
cd naia
```

### Step 2.2: Verify Directory Structure

```bash
# You should see:
ls -la

# Expected:
# docker-compose.yml
# src/
#   Naia.Api/
#   Naia.Ingestion/
#   Naia.Infrastructure/
#   Naia.Domain/
#   Naia.Connectors/
#   Naia.Application/
#   Naia.PatternEngine/
# init-scripts/
# Naia.sln
```

## Phase 3: Start Infrastructure (3 minutes)

### Step 3.1: Start Docker Services

```bash
# From /home/naia/naia directory
docker compose up -d

# Verify all containers are running
docker compose ps

# Expected output (all STATUS: Up):
# naia-postgres
# naia-questdb
# naia-redis
# naia-zookeeper
# naia-kafka
# naia-kafka-ui
# naia-redis-commander
```

### Step 3.2: Verify Infrastructure Health

```bash
# Check PostgreSQL
docker compose exec postgres pg_isready -U naia -d naia

# Check QuestDB
curl http://localhost:9000/  # Should return HTML

# Check Redis
docker compose exec redis redis-cli ping  # Should reply PONG

# Check Kafka (wait ~30 seconds after starting)
docker compose logs kafka | tail -20
```

## Phase 4: Build & Publish Application (5-10 minutes)

### Step 4.1: Publish .NET Solution

```bash
# From /home/naia/naia directory
dotnet publish Naia.sln -c Release -o ./publish

# Expected output:
# Build succeeded with X warnings
# Publishing to ./publish
```

### Step 4.2: Verify Published Binaries

```bash
ls -la publish/

# You should see:
# Naia.Api.dll
# Naia.Ingestion.dll
# Naia.Infrastructure.dll
# ... and many other .dlls
```

## Phase 5: Configure systemd Services (5 minutes)

### Step 5.1: Create API Service File

```bash
# Switch to root to create systemd files
sudo bash

# Create API service
cat > /etc/systemd/system/naia-api.service << 'EOF'
[Unit]
Description=NAIA API Service
After=network.target docker-compose@naia.service

[Service]
Type=simple
User=naia
WorkingDirectory=/home/naia/naia
ExecStart=/usr/bin/dotnet /home/naia/naia/publish/Naia.Api.dll
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOF
```

### Step 5.2: Create Ingestion Service File

```bash
cat > /etc/systemd/system/naia-ingestion.service << 'EOF'
[Unit]
Description=NAIA Ingestion Worker
After=network.target docker-compose@naia.service

[Service]
Type=simple
User=naia
WorkingDirectory=/home/naia/naia
ExecStart=/usr/bin/dotnet /home/naia/naia/publish/Naia.Ingestion.dll
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOF
```

### Step 5.3: Enable and Start Services

```bash
systemctl daemon-reload
systemctl enable naia-api naia-ingestion  # Auto-start on reboot
systemctl start naia-api naia-ingestion

# Verify they're running
systemctl status naia-api naia-ingestion

# Expected: both show "active (running)"
```

## Phase 6: Configure Reverse Proxy (2 minutes)

### Step 6.1: Create Caddy Configuration

```bash
cat > /etc/caddy/Caddyfile << 'EOF'
app.naia.run {
  reverse_proxy localhost:5000 {
    health_uri /api/health
    health_interval 5s
    health_timeout 3s
    header_up X-Forwarded-For {http.request.remote}
    header_up X-Forwarded-Proto {http.request.scheme}
    header_up X-Forwarded-Host {http.request.host}
  }

  encode gzip

  @websocket {
    header Connection *upgrade*
    header Upgrade websocket
  }

  reverse_proxy @websocket localhost:5000
}
EOF
```

### Step 6.2: Restart Caddy

```bash
systemctl restart caddy

# Verify it's running
systemctl status caddy

# Expected: "active (running)"
```

## Phase 7: Configure DNS (2 minutes)

### Step 7.1: Add A Record in Cloudflare

1. Log into Cloudflare dashboard
2. Select your domain (naia.run)
3. Go to **DNS** section
4. Click **Add record**
5. Fill in:
   - **Type**: A
   - **Name**: `app`
   - **IPv4 address**: `37.27.189.86`
   - **TTL**: Auto
   - **Proxy status**: DNS only (gray cloud)
6. Click **Save**

### Step 7.2: Verify DNS Propagation (wait 1-5 minutes)

```bash
# From your local machine (or server)
nslookup app.naia.run

# Should return:
# Name:   app.naia.run
# Address: 37.27.189.86
```

## Phase 8: Verify Complete Installation

### Step 8.1: Test HTTPS Connectivity

```bash
# From your local machine
curl https://app.naia.run/api/health

# Should return:
# 200 OK with health status JSON
```

### Step 8.2: Check SSL Certificate

```bash
# Verify SSL is valid
curl -I https://app.naia.run

# Should show:
# HTTP/2 200
# ... Certificate information ...
```

### Step 8.3: Verify All Services

```bash
# SSH back to server
ssh root@37.27.189.86

# Check all docker containers
docker compose ps

# Check all systemd services
systemctl status naia-api naia-ingestion caddy

# Check logs for any errors
journalctl -u naia-api -n 20
journalctl -u naia-ingestion -n 20
```

## Installation Verification Checklist

- [ ] Server accessible via SSH
- [ ] Docker running (`docker ps` shows containers)
- [ ] All 7 containers healthy (postgres, questdb, redis, zookeeper, kafka, kafka-ui, redis-commander)
- [ ] .NET published successfully (`ls publish/ | grep Naia`)
- [ ] naia-api systemd service running (`systemctl status naia-api`)
- [ ] naia-ingestion systemd service running (`systemctl status naia-ingestion`)
- [ ] Caddy service running (`systemctl status caddy`)
- [ ] DNS resolves (`nslookup app.naia.run` returns IP)
- [ ] HTTPS works (`curl https://app.naia.run/api/health` returns 200)
- [ ] SSL certificate valid (check with `curl -I`)

## Troubleshooting Installation

**Containers not starting?**
```bash
docker compose logs postgres  # Check specific container logs
docker compose down -v        # Reset volumes and try again
docker compose up -d          # Restart
```

**API service won't start?**
```bash
journalctl -u naia-api -n 50  # Check last 50 lines of logs
systemctl restart naia-api     # Try restarting
```

**DNS not resolving?**
```bash
# Wait a few minutes for DNS propagation
# Check Cloudflare DNS records are correct
# Flush local DNS cache (depends on OS)
# On Windows: ipconfig /flushdns
```

**SSL not working?**
```bash
# Check Caddy logs
journalctl -u caddy -n 50

# Test Caddy configuration
caddy validate --config /etc/caddy/Caddyfile
```

## Next Steps After Installation

1. **Enable Data Connectors**:
   - Edit `appsettings.json` in Naia.Ingestion and Naia.Api
   - Add API keys for EIA (optional)
   - Restart services: `systemctl restart naia-api naia-ingestion`

2. **Create Admin User** (if implemented):
   - Call admin API endpoint to create first user
   - Configure authentication

3. **Deploy Frontend** (if separate):
   - Build frontend assets
   - Deploy to CDN or web server
   - Update API CORS settings

4. **Set Up Monitoring** (optional):
   - Configure log aggregation (ELK, Splunk, etc.)
   - Set up alerting on service failures
   - Monitor disk/memory usage

5. **Backup Configuration**:
   - Back up `/etc/caddy/Caddyfile`
   - Back up systemd service files
   - Document any custom settings

## Installation Time Summary

| Phase | Duration | Task |
|-------|----------|------|
| 1. Server Setup | 5 min | Install Docker, .NET, Caddy |
| 2. Clone Code | 5 min | Git clone repository |
| 3. Infrastructure | 3 min | Docker compose up |
| 4. Build | 5-10 min | dotnet publish |
| 5. Services | 5 min | Create systemd services |
| 6. Reverse Proxy | 2 min | Configure Caddy |
| 7. DNS | 2 min | Add A record + wait for propagation |
| 8. Verify | 5 min | Test connectivity |
| **TOTAL** | **~30 min** | Full production deployment |

