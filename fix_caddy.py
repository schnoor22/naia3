#!/usr/bin/env python3
"""Fix Caddy config for SignalR."""
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

# New Caddyfile with proper SignalR handling
caddyfile = '''app.naia.run {
    # SignalR - needs to come FIRST and handle all methods
    @signalr path /hubs/*
    handle @signalr {
        reverse_proxy localhost:5000 {
            header_up Host {host}
            header_up X-Real-IP {remote_host}
            header_up X-Forwarded-For {remote_host}
            header_up X-Forwarded-Proto {scheme}
            # WebSocket support
            header_up Connection {>Connection}
            header_up Upgrade {>Upgrade}
        }
    }
    
    # API endpoints
    handle /api/* {
        reverse_proxy localhost:5000
    }
    
    # Health endpoint
    handle /health {
        reverse_proxy localhost:5000
    }
    
    # Hangfire dashboard
    handle /hangfire* {
        reverse_proxy localhost:5000
    }
    
    # Static files (SvelteKit dashboard) - last
    handle {
        root * /opt/naia/build
        try_files {path} /index.html
        file_server
    }
    
    # Logging
    log {
        output file /var/log/caddy/access.log
        format console
    }
}
'''

print("Writing new Caddyfile...")
sftp = client.open_sftp()
with sftp.open("/etc/caddy/Caddyfile", "w") as f:
    f.write(caddyfile)
sftp.close()
print("✓ Caddyfile updated")

print("\nReloading Caddy...")
stdin, stdout, stderr = client.exec_command("systemctl reload caddy && sleep 2 && systemctl status caddy --no-pager | head -10")
print(stdout.read().decode('utf-8'))

print("\nTesting SignalR via Caddy...")
stdin, stdout, stderr = client.exec_command("curl -s -X POST https://app.naia.run/hubs/patterns/negotiate -H 'Content-Type: application/json' -w '\\nHTTP Status: %{http_code}' -k 2>&1 | tail -5")
print(stdout.read().decode('utf-8'))

client.close()
print("\n✅ Done!")
