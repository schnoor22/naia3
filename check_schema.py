#!/usr/bin/env python3
"""Check QuestDB schema."""
import paramiko
from pathlib import Path

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

def check_schema():
    try:
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(**CONFIG, timeout=10)
        
        # Check table schema
        print("=== QuestDB point_data Schema ===\n")
        stdin, stdout, stderr = client.exec_command(
            "curl -s 'http://localhost:9000/exec?query=SHOW+COLUMNS+FROM+point_data;' 2>/dev/null | head -30"
        )
        output = stdout.read().decode('utf-8')
        print(output)
        
        client.close()
    except Exception as e:
        print(f"❌ Failed: {e}")

if __name__ == "__main__":
    check_schema()
