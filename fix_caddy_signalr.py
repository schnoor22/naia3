#!/usr/bin/env python3
"""Update Caddy configuration for better SignalR support."""
import paramiko
from pathlib import Path

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

# New Caddyfile content with improved routing
new_caddyfile = """app.naia.run {
    encode gzip

    route {
        # API endpoints - must come first
        reverse_proxy /api/* localhost:5000
        reverse_proxy /health localhost:5000

        # SignalR WebSocket and all hub-related endpoints
        # This must match before file_server attempts to serve
        reverse_proxy /hubs/* localhost:5000 {
            header_up Connection {>Connection}
            header_up Upgrade {>Upgrade}
            header_up X-Forwarded-For {remote_host}
            header_up X-Forwarded-Proto {scheme}
            header_up X-Forwarded-Host {host}
        }

        # Static files - only for non-API/non-hub paths
        @notapi {
            not path /api/*
            not path /health
            not path /hubs/*
        }
        root * /opt/naia/build
        try_files @notapi {path} /index.html
        file_server @notapi
    }
}

:2019
"""

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(**CONFIG, timeout=10)

# Backup old config
print("Backing up current Caddyfile...")
stdin, stdout, stderr = client.exec_command("cp /etc/caddy/Caddyfile /etc/caddy/Caddyfile.backup")
stdout.read()

# Write new config
print("Writing new Caddyfile...")
sftp = client.open_sftp()
with sftp.open("/etc/caddy/Caddyfile", "w") as f:
    f.write(new_caddyfile)
sftp.close()

# Reload Caddy
print("Reloading Caddy...")
stdin, stdout, stderr = client.exec_command("caddy reload --config /etc/caddy/Caddyfile --force")
output = stdout.read().decode('utf-8')
if output:
    print(output)

# Test the endpoint
print("\nTesting SignalR negotiate endpoint...")
stdin, stdout, stderr = client.exec_command(
    "curl -i -X POST 'https://app.naia.run/hubs/patterns/negotiate?negotiateVersion=1' "
    "-H 'Content-Type: application/json' 2>&1 | head -20"
)
print(stdout.read().decode('utf-8'))

client.close()
print("\n✅ Caddy configuration updated!")
