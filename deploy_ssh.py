#!/usr/bin/env python3
"""
Deploy appsettings.json to remote server via SSH using paramiko.
"""
import paramiko
import json
import sys
from pathlib import Path

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

def deploy():
    """Deploy the config file."""
    print("╔════════════════════════════════════════════════════════════════╗")
    print("║ NAIA Ingestion Config Deployment                              ║")
    print("╚════════════════════════════════════════════════════════════════╝")
    
    # Read local config
    config_path = Path(r"c:\naia3\publish-ingestion\appsettings.json")
    if not config_path.exists():
        print(f"❌ Config file not found: {config_path}")
        return False
    
    with open(config_path, "r") as f:
        config_content = f.read()
    
    print(f"✓ Loaded config: {len(config_content)} bytes")
    
    # Parse to verify it's valid JSON
    try:
        config_json = json.loads(config_content)
        data_dir = config_json.get("WindFarmReplay", {}).get("DataDirectory", "NOT SET")
        print(f"✓ DataDirectory configured: {data_dir}")
    except json.JSONDecodeError as e:
        print(f"❌ Invalid JSON: {e}")
        return False
    
    # Connect via SSH
    print(f"\n[1/3] Connecting to {CONFIG['hostname']}...")
    try:
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(**CONFIG, timeout=10)
        print("✓ Connected")
    except Exception as e:
        print(f"❌ Connection failed: {e}")
        return False
    
    # Upload config file
    print(f"\n[2/3] Uploading config file...")
    try:
        sftp = client.open_sftp()
        remote_path = "/opt/naia/publish/appsettings.json"
        
        # Write the config file
        with sftp.file(remote_path, "w") as f:
            f.write(config_content)
        
        print(f"✓ Uploaded to {remote_path}")
        sftp.close()
    except Exception as e:
        print(f"❌ Upload failed: {e}")
        client.close()
        return False
    
    # Restart service
    print(f"\n[3/3] Restarting ingestion service...")
    try:
        ssh = client.get_transport().open_session()
        ssh.exec_command("systemctl restart naia-ingestion && sleep 2 && systemctl status naia-ingestion --no-pager")
        
        # Read output
        output = ssh.recv(4096).decode('utf-8', errors='ignore')
        if output:
            print(output[:500])
        
        print("✓ Service restarted")
        ssh.close()
    except Exception as e:
        print(f"⚠️  Could not verify restart: {e}")
    finally:
        client.close()
    
    print("\n✅ Deployment complete!")
    return True

if __name__ == "__main__":
    success = deploy()
    sys.exit(0 if success else 1)
