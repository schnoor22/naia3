#!/usr/bin/env python3
"""Fix Caddyfile for SignalR."""
import paramiko
from pathlib import Path
import time

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

def run():
    try:
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(**CONFIG, timeout=30, allow_agent=False, look_for_keys=False)
        
        new_caddy = """app.naia.run {
    # Dynamic routes first (highest priority)
    reverse_proxy /hubs/* localhost:5000 {
        header_up Host {host}
        header_up Connection {>Connection}
        header_up Upgrade {>Upgrade}
    }

    reverse_proxy /api/* localhost:5000
    reverse_proxy /health localhost:5000
    reverse_proxy /hangfire* localhost:5000

    # Static files - exclude all dynamic paths
    @notdynamic {
        not path /api/*
        not path /hubs/*
        not path /health
        not path /hangfire*
    }
    
    root * /opt/naia/build
    try_files @notdynamic {path} /index.html
    file_server @notdynamic

    log {
        output file /var/log/caddy/access.log
    }
}
"""
        
        # Write new Caddyfile
        sftp = client.open_sftp()
        with sftp.open("/etc/caddy/Caddyfile", "w") as f:
            f.write(new_caddy)
        sftp.close()
        print("✓ Updated Caddyfile")
        
        # Reload Caddy
        stdin, stdout, stderr = client.exec_command("caddy reload -c /etc/caddy/Caddyfile 2>&1")
        time.sleep(1)
        out = stdout.read().decode('utf-8', errors='ignore')
        if out:
            print(f"Caddy reload output: {out[:100]}")
        
        # Test
        print("\nTesting SignalR...")
        stdin, stdout, stderr = client.exec_command(
            'curl -s -X POST "https://app.naia.run/hubs/patterns/negotiate?negotiateVersion=1" '
            '-k 2>&1 | grep -o "connectionId\\|error\\|405\\|200" | head -1'
        )
        result = stdout.read().decode('utf-8').strip()
        if result:
            print(f"Result: {result}")
            if "connectionId" in result or "200" in result:
                print("✅ SignalR should now work!")
            elif "405" in result:
                print("❌ Still getting 405 - may need different fix")
        
        client.close()
        
    except Exception as e:
        print(f"Error: {e}")
        import traceback
        traceback.print_exc()

if __name__ == "__main__":
    run()
