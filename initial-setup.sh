#!/bin/bash
# NAIA Initial Server Setup Script
# Run this ONCE on a fresh Hetzner server as ROOT

set -e

echo "=========================================="
echo "NAIA Initial Server Setup"
echo "=========================================="
echo ""
echo "This script will:"
echo "  - Update system packages"
echo "  - Install Docker, Docker Compose"
echo "  - Install .NET 8 SDK"
echo "  - Install Node.js"
echo "  - Create 'naia' user"
echo "  - Configure firewall"
echo ""
read -p "Continue? (y/n) " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    exit 1
fi

# Check if running as root
if [ "$EUID" -ne 0 ]; then 
    echo "Please run as root (use: sudo su)"
    exit 1
fi

echo ""
echo "[1/10] Updating system packages..."
apt update && apt upgrade -y

echo ""
echo "[2/10] Installing essential tools..."
apt install -y git curl wget nano vim htop ufw

echo ""
echo "[3/10] Installing Docker..."
curl -fsSL https://get.docker.com -o get-docker.sh
sh get-docker.sh
rm get-docker.sh

echo ""
echo "[4/10] Installing Docker Compose..."
apt install -y docker-compose-plugin

echo ""
echo "[5/10] Installing .NET 8 SDK..."
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
apt update
apt install -y dotnet-sdk-8.0

echo ""
echo "[6/10] Installing Node.js 20..."
curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
apt install -y nodejs

echo ""
echo "[7/10] Creating 'naia' user..."
if id "naia" &>/dev/null; then
    echo "User 'naia' already exists"
else
    adduser --disabled-password --gecos "" naia
    usermod -aG sudo,docker naia
    
    # Set up SSH for naia user
    mkdir -p /home/naia/.ssh
    if [ -f /root/.ssh/authorized_keys ]; then
        cp /root/.ssh/authorized_keys /home/naia/.ssh/
        chown -R naia:naia /home/naia/.ssh
        chmod 700 /home/naia/.ssh
        chmod 600 /home/naia/.ssh/authorized_keys
    fi
fi

echo ""
echo "[8/10] Creating application directory..."
mkdir -p /opt/naia
chown naia:naia /opt/naia

echo ""
echo "[9/10] Configuring firewall..."
ufw --force enable
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw status

echo ""
echo "[10/10] Installing Caddy (reverse proxy with auto SSL)..."
apt install -y debian-keyring debian-archive-keyring apt-transport-https
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | tee /etc/apt/sources.list.d/caddy-stable.list
apt update
apt install -y caddy

echo ""
echo "=========================================="
echo "Initial Setup Complete!"
echo "=========================================="
echo ""
echo "Installed versions:"
docker --version
docker compose version
dotnet --version
node --version
npm --version
caddy version
echo ""
echo "Next steps:"
echo "  1. Configure your domain DNS (A record: app -> YOUR_SERVER_IP)"
echo "  2. Switch to naia user: su - naia"
echo "  3. Transfer your code to /opt/naia"
echo "  4. Configure Caddyfile for app.naia.run"
echo "  5. Run deployment script"
echo ""
echo "See DEPLOYMENT_GUIDE.md for detailed instructions"
echo ""
