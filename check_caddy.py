#!/usr/bin/env python3
"""Check Caddy configuration."""
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

print("=== Caddy Configuration ===\n")
stdin, stdout, stderr = client.exec_command("cat /etc/caddy/Caddyfile")
print(stdout.read().decode('utf-8'))

print("\n=== Caddy Status ===\n")
stdin, stdout, stderr = client.exec_command("systemctl status caddy --no-pager")
print(stdout.read().decode('utf-8')[:500])

client.close()
