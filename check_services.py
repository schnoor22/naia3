#!/usr/bin/env python3
"""Check all services and SignalR status."""
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

print("=== REDIS STATUS ===")
stdin, stdout, stderr = client.exec_command("docker ps --filter name=redis --format '{{.Status}}'")
print(stdout.read().decode('utf-8'))

print("\n=== REDIS PING ===")
stdin, stdout, stderr = client.exec_command("docker exec naia-redis redis-cli ping")
print(stdout.read().decode('utf-8'))

print("\n=== API HEALTH CHECK ===")
stdin, stdout, stderr = client.exec_command("curl -s http://localhost:5000/api/health")
print(stdout.read().decode('utf-8'))

print("\n=== SIGNALR HUB TEST ===")
stdin, stdout, stderr = client.exec_command("curl -s -X POST http://localhost:5000/hubs/patterns/negotiate -H 'Content-Type: application/json' -w 'HTTP Status: %{http_code}'")
print(stdout.read().decode('utf-8'))

print("\n=== SIGNALR VIA CADDY ===")
stdin, stdout, stderr = client.exec_command("curl -s -X POST https://app.naia.run/hubs/patterns/negotiate -H 'Content-Type: application/json' -w 'HTTP Status: %{http_code}' -k")
print(stdout.read().decode('utf-8'))

client.close()
