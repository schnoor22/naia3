#!/usr/bin/env python3
"""Deploy API to server."""
import paramiko
from pathlib import Path

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

def deploy():
    print("Deploying API to server...")
    
    try:
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(**CONFIG, timeout=10)
        
        # Upload API DLL
        sftp = client.open_sftp()
        api_dll = Path(r"c:\naia3\publish\Naia.Api.dll")
        with sftp.open("/opt/naia/publish/Naia.Api.dll", "w") as f:
            with open(api_dll, "rb") as local:
                f.write(local.read())
        
        sftp.close()
        print("✓ Uploaded Naia.Api.dll")
        
        # Restart API service
        stdin, stdout, stderr = client.exec_command("systemctl restart naia-api && sleep 2 && systemctl status naia-api --no-pager")
        output = stdout.read().decode('utf-8')
        print(output[:300])
        
        client.close()
        print("✅ Deployment complete!")
        
    except Exception as e:
        print(f"❌ Failed: {e}")

if __name__ == "__main__":
    deploy()
