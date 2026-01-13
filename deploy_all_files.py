#!/usr/bin/env python3
"""Deploy all API files to server."""
import paramiko
import os
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

sftp = client.open_sftp()

# Upload all files from publish directory
publish_dir = Path(r"c:\naia3\publish")
print(f"Uploading files from {publish_dir}...")

count = 0
for file in publish_dir.glob("*"):
    if file.is_file():
        try:
            with sftp.open(f"/opt/naia/publish/{file.name}", "w") as f:
                with open(file, "rb") as local:
                    f.write(local.read())
            count += 1
            if count % 10 == 0:
                print(f"  ✓ {count} files uploaded...")
        except Exception as e:
            print(f"  ⚠️  Failed to upload {file.name}: {e}")

print(f"✓ Total {count} files uploaded")
sftp.close()

# Restart API
print("\nRestarting API...")
stdin, stdout, stderr = client.exec_command("systemctl restart naia-api && sleep 2 && curl -s http://localhost:5000/api/health | head -100")
output = stdout.read().decode('utf-8', errors='ignore')
if output:
    print(output[:300])

client.close()
print("✅ Done!")
