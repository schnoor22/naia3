#!/usr/bin/env python3
"""Test SignalR negotiate endpoint."""
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

print("=== Testing SignalR negotiate endpoint ===\n")

# Test locally via API
print("1. Local API test (via localhost:5000):")
stdin, stdout, stderr = client.exec_command(
    "curl -v -X POST 'http://localhost:5000/hubs/patterns/negotiate?negotiateVersion=1' 2>&1 | head -40"
)
print(stdout.read().decode('utf-8'))

print("\n2. Remote API test (via public endpoint):")
stdin, stdout, stderr = client.exec_command(
    "curl -v -X POST 'https://app.naia.run/hubs/patterns/negotiate?negotiateVersion=1' 2>&1 | head -40"
)
print(stdout.read().decode('utf-8'))

client.close()
