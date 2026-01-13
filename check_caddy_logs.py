#!/usr/bin/env python3
"""Check Caddy logs for SignalR negotiate requests."""
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

print("=== Recent Caddy logs ===\n")
stdin, stdout, stderr = client.exec_command("journalctl -u caddy -n 50 --no-pager | grep -A2 -B2 negotiate")
output = stdout.read().decode('utf-8')
if output:
    print(output)
else:
    print("(No negotiate requests found in logs)")

print("\n=== Caddy access logs for negotiate ===\n")
stdin, stdout, stderr = client.exec_command("grep negotiate /var/log/caddy/access.log 2>/dev/null | tail -10")
output = stdout.read().decode('utf-8')
if output:
    print(output)
else:
    print("(Check if Caddy access log exists)")

client.close()
