#!/bin/bash
# NAIA Quick Deployment Script for Hetzner
# Run this on your server after initial setup

set -e

echo "==================================="
echo "NAIA Deployment Script v1.0"
echo "==================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check if running as naia user
if [ "$USER" != "naia" ]; then
    echo -e "${RED}Error: This script must be run as the 'naia' user${NC}"
    echo "Run: su - naia"
    exit 1
fi

# Navigate to app directory
cd /opt/naia || exit 1

echo -e "${GREEN}[1/10] Stopping services...${NC}"
sudo systemctl stop naia-ingestion || true
sudo systemctl stop naia-api || true

echo -e "${GREEN}[2/10] Pulling latest code...${NC}"
if [ -d ".git" ]; then
    git pull
else
    echo -e "${YELLOW}Not a git repository, skipping pull${NC}"
fi

echo -e "${GREEN}[3/10] Starting infrastructure services...${NC}"
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d postgres questdb redis kafka zookeeper
sleep 15

echo -e "${GREEN}[4/10] Restoring NuGet packages...${NC}"
dotnet restore Naia.sln

echo -e "${GREEN}[5/10] Building solution...${NC}"
dotnet build Naia.sln -c Release

echo -e "${GREEN}[6/10] Publishing API...${NC}"
dotnet publish src/Naia.Api/Naia.Api.csproj -c Release -o /opt/naia/publish/api --no-build

echo -e "${GREEN}[7/10] Publishing Ingestion service...${NC}"
dotnet publish src/Naia.Ingestion/Naia.Ingestion.csproj -c Release -o /opt/naia/publish/ingestion --no-build

echo -e "${GREEN}[8/10] Building Web UI...${NC}"
cd src/Naia.Web
npm install --production
npm run build
mkdir -p /opt/naia/publish/api/wwwroot
cp -r dist/* /opt/naia/publish/api/wwwroot/
cd /opt/naia

echo -e "${GREEN}[9/10] Running database migrations...${NC}"
cd /opt/naia/publish/api
ASPNETCORE_ENVIRONMENT=Production dotnet Naia.Api.dll --migrate-db || echo -e "${YELLOW}Migrations skipped or already applied${NC}"
cd /opt/naia

echo -e "${GREEN}[10/10] Starting services...${NC}"
sudo systemctl start naia-api
sudo systemctl start naia-ingestion

# Wait a moment for services to start
sleep 5

echo ""
echo -e "${GREEN}==================================="
echo "Deployment Complete!"
echo "===================================${NC}"
echo ""
echo "Service Status:"
sudo systemctl status naia-api --no-pager | head -5
sudo systemctl status naia-ingestion --no-pager | head -5
echo ""
echo "Docker Containers:"
docker ps --format "table {{.Names}}\t{{.Status}}"
echo ""
echo -e "${GREEN}Access your application at: https://app.naia.run${NC}"
echo ""
echo "To view logs:"
echo "  API:        sudo journalctl -u naia-api -f"
echo "  Ingestion:  sudo journalctl -u naia-ingestion -f"
echo "  Docker:     docker logs -f <container_name>"
echo ""
