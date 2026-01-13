#!/usr/bin/env python3
"""
Deploy via SSH by writing config file remotely.
"""
import subprocess
import base64
import sys
import json

# Read the appsettings.json we want to deploy
with open(r"c:\naia3\publish-ingestion\appsettings.json", "rb") as f:
    content = f.read()

# Encode to base64
encoded = base64.b64encode(content).decode('ascii')

# Build SSH command that will:
# 1. Create a base64 file on remote
# 2. Decode it to the right location
# 3. Restart the service
ssh_cmd = f"""
cat > /tmp/appsettings_b64.txt << 'EOF'
{encoded}
EOF

base64 -d /tmp/appsettings_b64.txt > /opt/naia/publish/appsettings.json
chmod 644 /opt/naia/publish/appsettings.json
systemctl restart naia-ingestion
sleep 2
systemctl status naia-ingestion --no-pager
"""

# Execute via ssh
cmd = ["ssh", "root@app.naia.run", ssh_cmd]
print("Deploying appsettings.json via SSH...")
print(f"File size: {len(content)} bytes")
try:
    result = subprocess.run(cmd, check=True, text=True, capture_output=False)
    print("✅ Deployment complete!")
except Exception as e:
    print(f"❌ Failed: {e}")
    sys.exit(1)
