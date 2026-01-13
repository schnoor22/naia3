#!/usr/bin/env python3
"""Check API logs."""
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

print("API Service Status:")
stdin, stdout, stderr = client.exec_command("systemctl status naia-api --no-pager")
print(stdout.read().decode('utf-8')[:300])

print("\n\nAPI Logs (last 50 lines):")
stdin, stdout, stderr = client.exec_command("journalctl -u naia-api -n 50 --no-pager")
print(stdout.read().decode('utf-8'))

client.close()
