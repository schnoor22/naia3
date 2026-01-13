#!/usr/bin/env python3
"""Fix Caddy config for SignalR - alternative approach."""
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

# More explicit Caddyfile
caddyfile = '''app.naia.run {
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
    
    # Static files for everything else
    root * /opt/naia/build
    try_files {path} /index.html
    file_server
    
    log {
        output file /var/log/caddy/access.log
    }
}
'''

print("Writing Caddyfile (v2)...")
sftp = client.open_sftp()
with sftp.open("/etc/caddy/Caddyfile", "w") as f:
    f.write(caddyfile)
sftp.close()

print("Reloading Caddy...")
stdin, stdout, stderr = client.exec_command("systemctl reload caddy")
stdout.read()

import time
time.sleep(2)

print("\nTesting SignalR negotiate endpoint...")
stdin, stdout, stderr = client.exec_command(
    "curl -s -X POST 'https://app.naia.run/hubs/patterns/negotiate?negotiateVersion=1' "
    "-H 'Content-Type: text/plain;charset=UTF-8' "
    "-H 'Accept: */*' "
    "-w '\\nHTTP: %{http_code}' -k"
)
result = stdout.read().decode('utf-8')
print(result)

if "200" in result:
    print("\n✅ SignalR is now working!")
else:
    print("\n❌ Still failing, checking Caddy logs...")
    stdin, stdout, stderr = client.exec_command("tail -20 /var/log/caddy/access.log 2>/dev/null || journalctl -u caddy -n 20 --no-pager")
    print(stdout.read().decode('utf-8'))

client.close()
