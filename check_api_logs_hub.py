#!/usr/bin/env python3
"""Check API logs for hub-related messages."""
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

print("=== API Logs (last 100 lines) ===\n")
stdin, stdout, stderr = client.exec_command("journalctl -u naia-api -n 100 --no-pager | tail -50")
output = stdout.read().decode('utf-8')
print(output)

client.close()
