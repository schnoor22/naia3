#!/bin/bash
# Deploy UI to production
# This script updates the UI on the server at app.naia.run

set -e

echo "======================================"
echo "NAIA UI Deployment Script"
echo "======================================"

SERVER="root@37.27.189.86"
REPO_PATH="/opt/naia"
UI_SOURCE="$REPO_PATH/src/Naia.Web"
UI_DEPLOY="/opt/naia/build"  # Caddy serves from here

echo ""
echo "Step 1: Pull latest code from GitHub..."
ssh $SERVER "cd $REPO_PATH && git pull"

echo ""
echo "Step 2: Install npm dependencies..."
ssh $SERVER "cd $UI_SOURCE && npm install"

echo ""
echo "Step 3: Build UI..."
ssh $SERVER "cd $UI_SOURCE && npm run build"

echo ""
echo "Step 4: Deploy to Caddy static file location..."
ssh $SERVER "rm -rf $UI_DEPLOY && cp -r $UI_SOURCE/build $UI_DEPLOY"

echo ""
echo "Step 5: Restart Caddy..."
ssh $SERVER "sudo systemctl restart caddy"

echo ""
echo "✓ UI deployed successfully!"
echo "  - Source: $UI_SOURCE"
echo "  - Deploy: $UI_DEPLOY (served by Caddy)"
echo "  - URL: https://app.naia.run"
