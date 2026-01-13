#!/usr/bin/env python3
"""Update Caddyfile to fix SignalR routing."""
import paramiko
from pathlib import Path

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

new_caddyfile = """app.naia.run {
    # All dynamic requests go to ASP.NET first
    reverse_proxy /hubs/* localhost:5000 {
        # WebSocket headers for SignalR
        header_up Host {host}
        header_up Connection {>Connection}
        header_up Upgrade {>Upgrade}
    }

    reverse_proxy /api/* localhost:5000
    reverse_proxy /health localhost:5000
    reverse_proxy /hangfire* localhost:5000

    # Static files for everything else (but not API/hubs)
    @notdynamic {
        not path /api/*
        not path /hubs/*
        not path /health
        not path /hangfire*
    }
    
    root * /opt/naia/build
    try_files @notdynamic {path} /index.html
    file_server @notdynamic

    log {
        output file /var/log/caddy/access.log
    }
}
"""

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(**CONFIG, timeout=10)

sftp = client.open_sftp()

# Backup old caddyfile
try:
    with sftp.open("/etc/caddy/Caddyfile.backup", "w") as f:
        with sftp.open("/etc/caddy/Caddyfile", "r") as src:
            f.write(src.read())
    print("✓ Backed up old Caddyfile")
except:
    pass

# Write new caddyfile
with sftp.open("/etc/caddy/Caddyfile", "w") as f:
    f.write(new_caddyfile)

sftp.close()

# Reload Caddy
print("\nReloading Caddy...")
stdin, stdout, stderr = client.exec_command("caddy reload -c /etc/caddy/Caddyfile")
output = stdout.read().decode('utf-8')
if output:
    print(output)

# Test SignalR
print("\nTesting SignalR via HTTPS...")
stdin, stdout, stderr = client.exec_command(
    'curl -s -X POST "https://app.naia.run/hubs/patterns/negotiate?negotiateVersion=1" '
    '-H "Content-Type: application/json" -k | head -50'
)
result = stdout.read().decode('utf-8')
if "negotiateVersion" in result or "connectionId" in result:
    print("✅ SignalR working!")
    print(result[:200])
else:
    print("Response:", result[:200])

client.close()
