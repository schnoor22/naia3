#!/usr/bin/env python3
"""Test SignalR with proper headers."""
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

print("Testing with proper SignalR headers...\n")

# SignalR usually sends Content-Type: application/json and empty body for negotiate
stdin, stdout, stderr = client.exec_command(
    "curl -i -X POST "
    "'https://app.naia.run/hubs/patterns/negotiate?negotiateVersion=1' "
    "-H 'Content-Type: application/json' "
    "-H 'User-Agent: Mozilla/5.0' "
    "-d '' 2>&1 | head -30"
)
print(stdout.read().decode('utf-8'))

client.close()
