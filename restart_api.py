#!/usr/bin/env python3
"""Restart API and check status."""
import paramiko
from pathlib import Path
import time

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(**CONFIG, timeout=10)

print("=== Checking API status ===")
stdin, stdout, stderr = client.exec_command("systemctl status naia-api --no-pager | head -10")
print(stdout.read().decode('utf-8'))

print("\n=== Restarting API ===")
stdin, stdout, stderr = client.exec_command("systemctl restart naia-api")
stdout.read()
time.sleep(3)

print("=== New status ===")
stdin, stdout, stderr = client.exec_command("systemctl status naia-api --no-pager | head -15")
print(stdout.read().decode('utf-8'))

print("\n=== Testing API health ===")
stdin, stdout, stderr = client.exec_command("curl -s http://localhost:5000/api/health")
result = stdout.read().decode('utf-8')
print(result)

if "healthy" in result:
    print("\n✅ API is running!")
    
    print("\n=== Testing SignalR ===")
    stdin, stdout, stderr = client.exec_command("curl -s -X POST 'http://localhost:5000/hubs/patterns/negotiate' -w 'HTTP: %{http_code}'")
    print(stdout.read().decode('utf-8'))
    
    print("\n=== Testing via Caddy ===")
    stdin, stdout, stderr = client.exec_command("curl -s -X POST 'https://app.naia.run/hubs/patterns/negotiate' -k -w '\\nHTTP: %{http_code}'")
    print(stdout.read().decode('utf-8'))
else:
    print("\n❌ API failed to start. Checking logs...")
    stdin, stdout, stderr = client.exec_command("journalctl -u naia-api -n 30 --no-pager")
    print(stdout.read().decode('utf-8'))

client.close()
