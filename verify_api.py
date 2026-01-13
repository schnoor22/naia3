#!/usr/bin/env python3
"""Verify API deployment and check endpoint."""
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

# Check API file timestamp
stdin, stdout, stderr = client.exec_command("ls -la /opt/naia/publish/Naia.Api.dll")
print("API DLL timestamp:")
print(stdout.read().decode('utf-8'))

# Force restart
print("\nRestarting API service...")
stdin, stdout, stderr = client.exec_command("systemctl restart naia-api && sleep 3 && curl -s http://localhost:5000/api/ingestion/status | head -50")
print(stdout.read().decode('utf-8'))

client.close()
