#!/usr/bin/env python3
"""Check QuestDB for recent data."""
import paramiko
from pathlib import Path

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

def check_data():
    try:
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(**CONFIG, timeout=10)
        
        # Check most recent data
        print("=== QuestDB Data Status ===\n")
        
        # Total rows
        stdin, stdout, stderr = client.exec_command(
            "curl -s 'http://localhost:9000/exec?query=SELECT+count(*)FROM+point_data;' 2>/dev/null"
        )
        output = stdout.read().decode('utf-8')
        print("Total rows in point_data:")
        print(output[:300])
        
        # Most recent timestamp
        print("\nMost recent data timestamp:")
        stdin, stdout, stderr = client.exec_command(
            "curl -s 'http://localhost:9000/exec?query=SELECT+MAX(ts)+FROM+point_data;' 2>/dev/null"
        )
        output = stdout.read().decode('utf-8')
        print(output[:300])
        
        # Count of data from last 1 hour
        print("\nData from last 1 hour:")
        stdin, stdout, stderr = client.exec_command(
            "curl -s 'http://localhost:9000/exec?query=SELECT+count(*)FROM+point_data+WHERE+ts+%3E+now()-1h;' 2>/dev/null"
        )
        output = stdout.read().decode('utf-8')
        print(output[:300])
        
        client.close()
    except Exception as e:
        print(f"❌ Failed: {e}")

if __name__ == "__main__":
    check_data()
