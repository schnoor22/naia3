import paramiko
import os
import sys

# SSH to server and deploy
client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect('37.27.189.86', username='ubuntu', key_filename=os.path.expanduser('~/.ssh/id_rsa'), port=22, timeout=10)

# Copy built DLL
sftp = client.open_sftp()

local_bin = r"C:\naia3\src\Naia.Api\bin\Release\net8.0"
remote_publish = "/opt/naia/publish"

# List files to copy
for root, dirs, files in os.walk(local_bin):
    for file in files:
        local_path = os.path.join(root, file)
        rel_path = os.path.relpath(local_path, local_bin)
        remote_path = f"{remote_publish}/{rel_path}".replace('\\', '/')
        
        # Create remote dir if needed
        remote_dir = os.path.dirname(remote_path).replace('\\', '/')
        try:
            sftp.stat(remote_dir)
        except IOError:
            stdin, stdout, stderr = client.exec_command(f"mkdir -p {remote_dir}")
            stdout.read()
        
        print(f"Copying {rel_path}...", end=' ')
        sftp.put(local_path, remote_path)
        print("OK")

sftp.close()

# Restart the API service
print("\nRestarting API service...")
stdin, stdout, stderr = client.exec_command("sudo systemctl restart naia-api")
exit_code = stdout.channel.recv_exit_status()
print(f"Restart exit code: {exit_code}")

# Wait a moment
import time
time.sleep(2)

# Check if it's running
stdin, stdout, stderr = client.exec_command("sudo systemctl status naia-api")
status = stdout.read().decode()
print("\nAPI Status:")
print(status[:500])

# Test the API
print("\nTesting API on IPv4...")
stdin, stdout, stderr = client.exec_command("curl -s http://127.0.0.1:5000/api/health | head -20")
output = stdout.read().decode()
print(output[:500] if output else "No response")

client.close()
print("\nDone!")
