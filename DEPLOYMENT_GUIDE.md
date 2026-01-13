# NAIA Production Deployment Guide - Hetzner CCX23

Complete guide to deploy NAIA on your Hetzner CCX23 server accessible at **app.naia.run**.

---

## 📋 Prerequisites Checklist

- [x] Hetzner CCX23 server provisioned
- [ ] Server root SSH access
- [ ] Domain `app.naia.run` pointing to server IP
- [ ] Code repository ready to transfer

---

## 🚀 Part 1: Initial Server Setup (15 minutes)

### Step 1: Connect to Server

```bash
# From your local machine, SSH as root
ssh root@YOUR_SERVER_IP

# Or if you set up SSH key during provisioning:
ssh -i ~/.ssh/hetzner_key root@YOUR_SERVER_IP
```

### Step 2: System Update & Security

```bash
# Update system packages
apt update && apt upgrade -y

# Create non-root user for running applications
adduser naia
usermod -aG sudo naia

# Set up SSH key for naia user (if not already done)
mkdir -p /home/naia/.ssh
cp /root/.ssh/authorized_keys /home/naia/.ssh/
chown -R naia:naia /home/naia/.ssh
chmod 700 /home/naia/.ssh
chmod 600 /home/naia/.ssh/authorized_keys

# Configure firewall
ufw allow OpenSSH
ufw allow 80/tcp     # HTTP
ufw allow 443/tcp    # HTTPS
ufw enable
```

### Step 3: Install Docker & Docker Compose

```bash
# Install Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sh get-docker.sh

# Add naia user to docker group
usermod -aG docker naia

# Install Docker Compose
apt install docker-compose-plugin -y

# Verify installation
docker --version
docker compose version
```

### Step 4: Install .NET 8 SDK

```bash
# Add Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Install .NET 8 SDK
apt update
apt install -y dotnet-sdk-8.0

# Verify installation
dotnet --version
```

### Step 5: Install Additional Tools

```bash
# Install git, curl, and other essentials
apt install -y git curl wget nano vim htop

# Install Node.js (for the web UI)
curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
apt install -y nodejs

# Verify
node --version
npm --version
```

---

## 📦 Part 2: Transfer Your Code (10 minutes)

### Option A: Using Git (Recommended)

```bash
# Switch to naia user
su - naia

# Create application directory
mkdir -p /opt/naia
cd /opt/naia

# If you have a Git repository:
git clone https://github.com/yourusername/naia.git .

# Or initialize git and push your code
# (Do this from your local machine first)
```

### Option B: Using SCP/RSYNC (From Your Local Machine)

```powershell
# From your Windows machine (PowerShell)
# Install rsync for Windows first if needed

# Using SCP
scp -r C:\naia3\* naia@YOUR_SERVER_IP:/opt/naia/

# Or using RSYNC (better for large transfers)
rsync -avz --progress C:\naia3/ naia@YOUR_SERVER_IP:/opt/naia/
```

### After Transfer

```bash
# On server, set correct ownership
sudo chown -R naia:naia /opt/naia
cd /opt/naia

# Verify files are there
ls -la
```

---

## 🔧 Part 3: Configure for Production (20 minutes)

### Step 1: Create Production docker-compose.yml

```bash
cd /opt/naia

# Create production docker-compose override
nano docker-compose.prod.yml
```

**Content for `docker-compose.prod.yml`:**

```yaml
version: '3.8'

services:
  postgres:
    restart: always
    volumes:
      - postgres_data:/var/lib/postgresql/data
    environment:
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-YourStrongPasswordHere123!}
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U naia"]
      interval: 10s
      timeout: 5s
      retries: 5

  questdb:
    restart: always
    volumes:
      - questdb_data:/root/.questdb
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9000/"]
      interval: 30s
      timeout: 10s
      retries: 3

  redis:
    restart: always
    volumes:
      - redis_data:/data
    command: redis-server --appendonly yes --requirepass ${REDIS_PASSWORD:-YourRedisPasswordHere123!}
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 3s
      retries: 3

  kafka:
    restart: always
    volumes:
      - kafka_data:/var/lib/kafka/data
    environment:
      KAFKA_AUTO_CREATE_TOPICS_ENABLE: 'true'
      KAFKA_DELETE_TOPIC_ENABLE: 'true'
      KAFKA_LOG_RETENTION_HOURS: 168

  zookeeper:
    restart: always
    volumes:
      - zookeeper_data:/var/lib/zookeeper

volumes:
  postgres_data:
  questdb_data:
  redis_data:
  kafka_data:
  zookeeper_data:
```

### Step 2: Configure Environment Variables

```bash
# Create .env file for production
nano .env
```

**Content for `.env`:**

```bash
# PostgreSQL
POSTGRES_PASSWORD=ChangeThisToStrongPassword123!
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
POSTGRES_DB=naia
POSTGRES_USER=naia

# Redis
REDIS_PASSWORD=ChangeThisToStrongRedisPassword123!

# Kafka
KAFKA_BOOTSTRAP_SERVERS=localhost:9092

# QuestDB
QUESTDB_HTTP_ENDPOINT=http://localhost:9000
QUESTDB_PG_ENDPOINT=localhost:8812

# Application
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5000
ASPNETCORE_HTTPS_PORT=443

# Logging
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning

# CORS - Allow your domain
CorsOrigins=https://app.naia.run,http://app.naia.run

# Weather API (optional - enable if needed)
WeatherApi__Enabled=false

# EIA Grid (optional - add your API key if enabled)
EiaGrid__Enabled=false
EiaGrid__ApiKey=

# Wind Farm Replay (for demo data)
WindFarmReplay__Enabled=true
```

### Step 3: Update Production appsettings

```bash
# Create production appsettings for API
nano src/Naia.Api/appsettings.Production.json
```

**Content:**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "app.naia.run,*.naia.run",
  
  "ConnectionStrings": {
    "PostgreSql": "Host=localhost;Port=5432;Database=naia;Username=naia;Password=${POSTGRES_PASSWORD};SslMode=Disable;Pooling=true;Minimum Pool Size=5;Maximum Pool Size=20"
  },
  
  "Redis": {
    "ConnectionString": "localhost:6379,password=${REDIS_PASSWORD}"
  },
  
  "Kafka": {
    "BootstrapServers": "localhost:9092"
  },
  
  "QuestDb": {
    "HttpEndpoint": "http://localhost:9000"
  }
}
```

```bash
# Same for Ingestion service
nano src/Naia.Ingestion/appsettings.Production.json
```

**Content:** (Same structure, focused on connectors)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Naia.Connectors": "Information"
    }
  },
  
  "Kafka": {
    "BootstrapServers": "localhost:9092"
  },
  
  "WindFarmReplay": {
    "Enabled": true,
    "DataDirectory": "/opt/naia/data/kelmarsh/scada_2019",
    "SpeedMultiplier": 60.0
  }
}
```

---

## 🌐 Part 4: DNS Configuration (5 minutes)

### Configure Your Domain

1. **Log into your domain registrar** (where you bought naia.run)

2. **Create A record** pointing to your Hetzner server IP:

```
Type: A
Name: app
Value: YOUR_SERVER_IP
TTL: 3600 (or Auto)
```

3. **Verify DNS propagation** (from your local machine):

```bash
# Wait a few minutes, then test
nslookup app.naia.run
ping app.naia.run
```

---

## 🔒 Part 5: SSL & Reverse Proxy with Caddy (15 minutes)

### Option A: Caddy (Recommended - Automatic SSL)

```bash
# Install Caddy
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update
sudo apt install caddy

# Create Caddyfile
sudo nano /etc/caddy/Caddyfile
```

**Caddyfile content:**

```
app.naia.run {
    # Automatic HTTPS with Let's Encrypt
    
    # Main application (SPA)
    handle /* {
        reverse_proxy localhost:5000
        
        # SPA fallback
        @notapi {
            not path /api/*
            not path /hangfire/*
            file {
                try_files {path} /index.html
            }
        }
    }
    
    # API endpoints
    handle /api/* {
        reverse_proxy localhost:5000
    }
    
    # Hangfire dashboard
    handle /hangfire/* {
        reverse_proxy localhost:5000
    }
    
    # SignalR websockets
    handle /hubs/* {
        reverse_proxy localhost:5000
    }
    
    # Enable compression
    encode gzip
    
    # Security headers
    header {
        X-Frame-Options "SAMEORIGIN"
        X-Content-Type-Options "nosniff"
        X-XSS-Protection "1; mode=block"
        Strict-Transport-Security "max-age=31536000; includeSubDomains"
    }
    
    # Logs
    log {
        output file /var/log/caddy/naia.log
        format console
    }
}
```

```bash
# Reload Caddy
sudo systemctl reload caddy
sudo systemctl enable caddy
```

### Option B: Nginx (Alternative)

If you prefer Nginx:

```bash
sudo apt install -y nginx certbot python3-certbot-nginx

# Create Nginx config
sudo nano /etc/nginx/sites-available/naia
```

**Content:**

```nginx
server {
    listen 80;
    server_name app.naia.run;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }
}
```

```bash
# Enable site
sudo ln -s /etc/nginx/sites-available/naia /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx

# Get SSL certificate
sudo certbot --nginx -d app.naia.run
```

---

## 🏗️ Part 6: Build & Deploy Application (20 minutes)

### Step 1: Start Infrastructure Services

```bash
cd /opt/naia

# Start only infrastructure (databases, kafka)
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d postgres questdb redis kafka zookeeper

# Wait 30 seconds for services to initialize
sleep 30

# Verify all running
docker ps
```

### Step 2: Build .NET Applications

```bash
cd /opt/naia

# Restore dependencies
dotnet restore Naia.sln

# Build in Release mode
dotnet build Naia.sln -c Release

# Publish API
dotnet publish src/Naia.Api/Naia.Api.csproj -c Release -o /opt/naia/publish/api

# Publish Ingestion service
dotnet publish src/Naia.Ingestion/Naia.Ingestion.csproj -c Release -o /opt/naia/publish/ingestion
```

### Step 3: Build Web UI

```bash
cd /opt/naia/src/Naia.Web

# Install dependencies
npm install

# Build for production
npm run build

# Copy built files to API wwwroot
cp -r dist/* /opt/naia/publish/api/wwwroot/
```

### Step 4: Database Migrations

```bash
cd /opt/naia

# Run EF Core migrations
dotnet ef database update --project src/Naia.Api/Naia.Api.csproj --connection "Host=localhost;Port=5432;Database=naia;Username=naia;Password=YOUR_POSTGRES_PASSWORD"

# Or run from published app
cd /opt/naia/publish/api
ASPNETCORE_ENVIRONMENT=Production dotnet Naia.Api.dll --migrate-db
```

---

## 🎬 Part 7: Run as Systemd Services (10 minutes)

### Create API Service

```bash
sudo nano /etc/systemd/system/naia-api.service
```

**Content:**

```ini
[Unit]
Description=NAIA API Service
After=network.target docker.service
Requires=docker.service

[Service]
Type=notify
User=naia
Group=naia
WorkingDirectory=/opt/naia/publish/api
ExecStart=/usr/bin/dotnet /opt/naia/publish/api/Naia.Api.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=naia-api
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

### Create Ingestion Service

```bash
sudo nano /etc/systemd/system/naia-ingestion.service
```

**Content:**

```ini
[Unit]
Description=NAIA Ingestion Service
After=network.target docker.service naia-api.service
Requires=docker.service

[Service]
Type=notify
User=naia
Group=naia
WorkingDirectory=/opt/naia/publish/ingestion
ExecStart=/usr/bin/dotnet /opt/naia/publish/ingestion/Naia.Ingestion.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=naia-ingestion
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

### Enable and Start Services

```bash
# Reload systemd
sudo systemctl daemon-reload

# Enable services to start on boot
sudo systemctl enable naia-api
sudo systemctl enable naia-ingestion

# Start services
sudo systemctl start naia-api
sudo systemctl start naia-ingestion

# Check status
sudo systemctl status naia-api
sudo systemctl status naia-ingestion

# View logs
sudo journalctl -u naia-api -f
sudo journalctl -u naia-ingestion -f
```

---

## ✅ Part 8: Verify Deployment (5 minutes)

### Check Services

```bash
# Check all Docker containers
docker ps

# Check .NET services
systemctl status naia-api
systemctl status naia-ingestion

# Check reverse proxy
systemctl status caddy  # or nginx
```

### Test Application

```bash
# Test API health
curl http://localhost:5000/api/health

# Test from outside
curl https://app.naia.run/api/health
```

### Access Web UI

1. Open browser: **https://app.naia.run**
2. You should see the NAIA dashboard
3. Check that data is flowing (wind farm replay should be active)

---

## 📊 Part 9: Monitoring & Maintenance

### View Logs

```bash
# API logs
sudo journalctl -u naia-api -f --since "5 minutes ago"

# Ingestion logs
sudo journalctl -u naia-ingestion -f

# Docker logs
docker logs -f kafka
docker logs -f postgres
docker logs -f questdb
docker logs -f redis

# Caddy logs
sudo tail -f /var/log/caddy/naia.log
```

### Monitor Resources

```bash
# System resources
htop

# Docker stats
docker stats

# Disk usage
df -h

# Check specific volumes
docker volume ls
du -sh /var/lib/docker/volumes/*
```

### Database Backups

```bash
# Create backup script
nano /home/naia/backup-naia.sh
```

**Content:**

```bash
#!/bin/bash
BACKUP_DIR=/opt/naia-backups
TIMESTAMP=$(date +%Y%m%d_%H%M%S)

mkdir -p $BACKUP_DIR

# PostgreSQL backup
docker exec postgres pg_dump -U naia naia | gzip > $BACKUP_DIR/postgres_$TIMESTAMP.sql.gz

# QuestDB backup (copy data directory)
tar -czf $BACKUP_DIR/questdb_$TIMESTAMP.tar.gz /var/lib/docker/volumes/naia_questdb_data

# Keep only last 7 days
find $BACKUP_DIR -name "*.gz" -mtime +7 -delete

echo "Backup completed: $TIMESTAMP"
```

```bash
# Make executable
chmod +x /home/naia/backup-naia.sh

# Add to crontab (daily at 2 AM)
crontab -e
```

Add line:
```
0 2 * * * /home/naia/backup-naia.sh >> /var/log/naia-backup.log 2>&1
```

---

## 🔄 Part 10: Updates & Redeployment

### Quick Update Script

```bash
nano /home/naia/deploy-naia.sh
```

**Content:**

```bash
#!/bin/bash
set -e

echo "=== NAIA Deployment Script ==="
cd /opt/naia

# Pull latest code (if using git)
echo "Pulling latest code..."
git pull

# Stop services
echo "Stopping services..."
sudo systemctl stop naia-ingestion
sudo systemctl stop naia-api

# Build
echo "Building applications..."
dotnet build Naia.sln -c Release

# Publish
echo "Publishing API..."
dotnet publish src/Naia.Api/Naia.Api.csproj -c Release -o /opt/naia/publish/api

echo "Publishing Ingestion..."
dotnet publish src/Naia.Ingestion/Naia.Ingestion.csproj -c Release -o /opt/naia/publish/ingestion

# Build Web UI
echo "Building Web UI..."
cd src/Naia.Web
npm install
npm run build
cp -r dist/* /opt/naia/publish/api/wwwroot/

cd /opt/naia

# Start services
echo "Starting services..."
sudo systemctl start naia-api
sudo systemctl start naia-ingestion

echo "=== Deployment complete! ==="
echo "Check status with: systemctl status naia-api naia-ingestion"
```

```bash
chmod +x /home/naia/deploy-naia.sh
```

To update in the future:
```bash
/home/naia/deploy-naia.sh
```

---

## 🔒 Security Hardening (Optional but Recommended)

### SSH Security

```bash
# Disable root SSH login
sudo nano /etc/ssh/sshd_config
```

Set:
```
PermitRootLogin no
PasswordAuthentication no
```

```bash
sudo systemctl restart sshd
```

### Install Fail2Ban

```bash
sudo apt install -y fail2ban
sudo systemctl enable fail2ban
sudo systemctl start fail2ban
```

### Auto Updates

```bash
sudo apt install -y unattended-upgrades
sudo dpkg-reconfigure --priority=low unattended-upgrades
```

---

## 📝 Useful Commands Cheat Sheet

```bash
# Restart everything
sudo systemctl restart naia-api naia-ingestion
docker compose restart

# View all logs at once
sudo journalctl -u naia-api -u naia-ingestion -f

# Check disk space
df -h
docker system df

# Clean Docker
docker system prune -a

# Database console
docker exec -it postgres psql -U naia -d naia

# Redis console
docker exec -it redis redis-cli -a YOUR_REDIS_PASSWORD

# Check Kafka topics
docker exec -it kafka kafka-topics.sh --list --bootstrap-server localhost:9092

# SSL certificate renewal (Caddy does this automatically)
# For Nginx:
sudo certbot renew
```

---

## 🚨 Troubleshooting

### Issue: Can't connect to app.naia.run

**Check:**
1. DNS propagated: `nslookup app.naia.run`
2. Firewall open: `sudo ufw status`
3. Caddy/Nginx running: `systemctl status caddy`
4. App running: `systemctl status naia-api`

### Issue: 502 Bad Gateway

**Check:**
1. API is running: `curl http://localhost:5000/api/health`
2. Check API logs: `sudo journalctl -u naia-api -n 100`
3. Check if port 5000 is listening: `sudo netstat -tlnp | grep 5000`

### Issue: Data not flowing

**Check:**
1. Kafka running: `docker ps | grep kafka`
2. Ingestion service running: `systemctl status naia-ingestion`
3. Check Kafka topics: `docker exec kafka kafka-topics.sh --list --bootstrap-server localhost:9092`

### Issue: Database connection errors

**Check:**
1. PostgreSQL running: `docker ps | grep postgres`
2. Check connection string in appsettings.Production.json
3. Test connection: `docker exec postgres psql -U naia -d naia -c "SELECT 1;"`

---

## ✅ Post-Deployment Checklist

- [ ] All Docker containers running
- [ ] API service running (`systemctl status naia-api`)
- [ ] Ingestion service running (`systemctl status naia-ingestion`)
- [ ] SSL certificate active (https://app.naia.run shows padlock)
- [ ] Web UI loads correctly
- [ ] API health check passes: https://app.naia.run/api/health
- [ ] Data flowing (check charts in UI)
- [ ] Weather connector enabled (optional)
- [ ] EIA Grid connector enabled with API key (optional)
- [ ] Backups configured
- [ ] Monitoring set up

---

## 🎉 Success!

Your NAIA system should now be live at **https://app.naia.run**!

**Next Steps:**
1. Configure any additional data connectors (Weather, EIA Grid)
2. Set up monitoring/alerting (Grafana, Prometheus)
3. Create regular backups
4. Share the URL and demo your system!

---

## 📞 Need Help?

Common logs locations:
- Application: `sudo journalctl -u naia-api -u naia-ingestion`
- Docker: `docker logs <container>`
- Nginx: `/var/log/nginx/`
- Caddy: `/var/log/caddy/`
- System: `/var/log/syslog`
