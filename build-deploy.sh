#!/bin/bash
# This script will be executed on the remote server to build and deploy the API

cd /opt/naia

# Git pull the latest code with the Program.cs changes
git pull origin main

# Build the API
cd /opt/naia
dotnet build -c Release src/Naia.Api/Naia.Api.csproj

# Copy to publish directory
mkdir -p /opt/naia/publish
cp -r src/Naia.Api/bin/Release/net8.0/* /opt/naia/publish/

# Restart the service
sudo systemctl restart naia-api

# Wait for startup
sleep 3

# Test the API on IPv4
echo "Testing API on IPv4..."
curl -s http://127.0.0.1:5000/api/health | jq . | head -20

echo "Done!"
