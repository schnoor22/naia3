#!/usr/bin/env python3
"""Check if CSV files are accessible on the server."""
import paramiko
import sys
from pathlib import Path

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

def check_files():
    print("Checking CSV files on server...")
    
    try:
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(**CONFIG, timeout=10)
        
        # Check if directory exists
        stdin, stdout, stderr = client.exec_command("ls -la /opt/naia/data/kelmarsh/scada_2019/ | head -20")
        output = stdout.read().decode('utf-8')
        print("Files in /opt/naia/data/kelmarsh/scada_2019/:")
        print(output)
        
        # Check recent logs
        print("\n--- Last 30 lines of ingestion logs ---")
        stdin, stdout, stderr = client.exec_command("journalctl -u naia-ingestion -n 30 --no-pager")
        logs = stdout.read().decode('utf-8')
        print(logs)
        
        client.close()
        return True
    except Exception as e:
        print(f"❌ Failed: {e}")
        return False

if __name__ == "__main__":
    check_files()
