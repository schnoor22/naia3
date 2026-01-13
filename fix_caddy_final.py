#!/usr/bin/env python3
"""Fix Caddy config for SignalR - final fix."""
import paramiko
from pathlib import Path

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(**CONFIG, timeout=10)

# Check current config
print("=== Current Caddyfile ===")
stdin, stdout, stderr = client.exec_command("cat /etc/caddy/Caddyfile")
print(stdout.read().decode('utf-8'))

# The issue might be that Caddy's file_server is somehow catching the /hubs path
# Let's use a completely explicit routing approach

caddyfile = '''app.naia.run {
    encode gzip
    
    # Route everything under /hubs to ASP.NET (SignalR)
    @hubs {
        path /hubs /hubs/*
    }
    reverse_proxy @hubs 127.0.0.1:5000 {
        header_up Connection {>Connection}
        header_up Upgrade {>Upgrade}
    }
    
    # Route all API calls
    @api {
        path /api /api/*
    }
    reverse_proxy @api 127.0.0.1:5000
    
    # Route health check
    @health {
        path /health
    }
    reverse_proxy @health 127.0.0.1:5000
    
    # Route hangfire
    @hangfire {
        path /hangfire /hangfire/*
    }
    reverse_proxy @hangfire 127.0.0.1:5000
    
    # Everything else: static files
    root * /opt/naia/build
    try_files {path} /index.html
    file_server
}
'''

print("\n=== Writing new Caddyfile ===")
sftp = client.open_sftp()
with sftp.open("/etc/caddy/Caddyfile", "w") as f:
    f.write(caddyfile)
sftp.close()

print("Restarting Caddy (not just reload)...")
stdin, stdout, stderr = client.exec_command("systemctl restart caddy && sleep 2 && systemctl status caddy --no-pager | head -10")
print(stdout.read().decode('utf-8'))

print("\n=== Testing SignalR negotiate ===")
stdin, stdout, stderr = client.exec_command("curl -s -X POST https://app.naia.run/hubs/patterns/negotiate -k -w 'HTTP: %{http_code}' 2>&1 | tail -3")
print(stdout.read().decode('utf-8'))

client.close()
