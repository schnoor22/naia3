#!/usr/bin/env python3
"""Test with a simpler Caddy config."""
import paramiko
from pathlib import Path

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

# Simplified Caddyfile - just reverse proxy everything
new_caddyfile = """app.naia.run {
    encode gzip
    
    # Reverse proxy everything to the API first
    reverse_proxy localhost:5000 {
        header_up Connection {>Connection}
        header_up Upgrade {>Upgrade}
        header_up X-Forwarded-For {remote_host}
        header_up X-Forwarded-Proto {scheme}
        header_up X-Forwarded-Host {host}
    }
}

:2019
"""

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(**CONFIG, timeout=10)

print("Writing simplified Caddyfile (proxy-all)...")
sftp = client.open_sftp()
with sftp.open("/etc/caddy/Caddyfile", "w") as f:
    f.write(new_caddyfile)
sftp.close()

print("Reloading Caddy...")
stdin, stdout, stderr = client.exec_command("caddy reload --config /etc/caddy/Caddyfile --force 2>&1")
output = stdout.read().decode('utf-8')
if output.strip():
    print(output)

# Wait a moment
import time
time.sleep(2)

print("\nTesting SignalR negotiate...")
stdin, stdout, stderr = client.exec_command(
    "curl -i -X POST 'https://app.naia.run/hubs/patterns/negotiate?negotiateVersion=1' "
    "-H 'Content-Type: application/json' 2>&1 | head -20"
)
output = stdout.read().decode('utf-8')
print(output)

# Check if we got 200 OK
if "HTTP/2 200" in output or "200 OK" in output:
    print("\n✅ SignalR negotiate is now working!")
else:
    print("\n⚠️ Still getting an error - checking API directly...")
    stdin, stdout, stderr = client.exec_command(
        "curl -i -X POST 'http://localhost:5000/hubs/patterns/negotiate?negotiateVersion=1' "
        "-H 'Content-Type: application/json' 2>&1 | head -10"
    )
    print(stdout.read().decode('utf-8'))

client.close()
