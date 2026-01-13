#!/usr/bin/env python3
"""
Deploy ingestion service to remote server via SCP and restart it.
"""
import subprocess
import sys
import os

# Configuration
SOURCE_DIR = r"c:\naia3\publish-ingestion"
REMOTE_USER = "root"
REMOTE_HOST = "app.naia.run"
REMOTE_DEST = "/opt/naia/publish"
SSH_KEY = os.path.expanduser("~/.ssh/id_rsa")  # Will try ~/.ssh/id_rsa

def run_command(cmd, description=""):
    """Run a command and return success status."""
    if description:
        print(f"\n📦 {description}...")
    print(f"   Command: {' '.join(cmd)}")
    try:
        result = subprocess.run(cmd, check=True, capture_output=True, text=True)
        if result.stdout:
            print(f"   Output: {result.stdout[:200]}")
        return True
    except subprocess.CalledProcessError as e:
        print(f"   ❌ Failed: {e.stderr[:200] if e.stderr else str(e)}")
        return False
    except FileNotFoundError:
        print(f"   ❌ Command not found")
        return False

def main():
    print("╔════════════════════════════════════════════════════════════════╗")
    print("║ NAIA Ingestion Service Deployment                             ║")
    print("╚════════════════════════════════════════════════════════════════╝")
    
    # Verify source directory exists
    if not os.path.isdir(SOURCE_DIR):
        print(f"❌ Source directory not found: {SOURCE_DIR}")
        sys.exit(1)
    
    file_count = len(os.listdir(SOURCE_DIR))
    print(f"✓ Source directory found: {SOURCE_DIR} ({file_count} files)")
    
    # Try to deploy using scp
    print(f"\n[1/3] Copying files to {REMOTE_HOST}...")
    scp_cmd = ["scp", "-r", SOURCE_DIR + "/*", f"{REMOTE_USER}@{REMOTE_HOST}:{REMOTE_DEST}/"]
    if not run_command(scp_cmd, "Copying files with scp"):
        print("Note: scp failed. Trying with pscp (PuTTY tool)...")
        pscp_cmd = ["pscp", "-r", SOURCE_DIR, f"{REMOTE_USER}@{REMOTE_HOST}:{REMOTE_DEST}"]
        if not run_command(pscp_cmd, "Copying files with pscp"):
            print("❌ Could not copy files using scp or pscp")
            print("   Ensure scp or pscp is in PATH, or manually run:")
            print(f"   scp -r {SOURCE_DIR}/* {REMOTE_USER}@{REMOTE_HOST}:{REMOTE_DEST}/")
            sys.exit(1)
    
    # Restart ingestion service via SSH
    print(f"\n[2/3] Restarting ingestion service...")
    ssh_cmd = ["ssh", f"{REMOTE_USER}@{REMOTE_HOST}", "systemctl restart naia-ingestion && sleep 2 && systemctl status naia-ingestion --no-pager"]
    if not run_command(ssh_cmd, "Restarting service"):
        print("Warning: Could not verify restart via SSH")
    
    # Verify via API
    print(f"\n[3/3] Verifying deployment...")
    verify_cmd = ["curl", "-s", "https://app.naia.run/api/ingestion/status"]
    if run_command(verify_cmd, "Checking ingestion status"):
        print("✓ Deployment likely successful - check the status above")
    
    print("\n✅ Deployment complete!")
    print("\nNext steps:")
    print("  - Monitor: journalctl -u naia-ingestion -f")
    print("  - Status:  curl https://app.naia.run/api/ingestion/status")

if __name__ == "__main__":
    main()
