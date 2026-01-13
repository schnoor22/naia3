import paramiko

# SSH to server
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('naia')

# Read current Caddyfile
sftp = ssh.open_sftp()
with sftp.file('/etc/caddy/Caddyfile', 'r') as f:
    content = f.read().decode('utf-8')

# Replace localhost:5000 with [::1]:5000
new_content = content.replace('localhost:5000', '[::1]:5000')

print("=== ORIGINAL ===")
print(content)
print("\n=== NEW ===")
print(new_content)

# Write back
with sftp.file('/etc/caddy/Caddyfile', 'w') as f:
    f.write(new_content.encode('utf-8'))

# Reload Caddy
stdin, stdout, stderr = ssh.exec_command('caddy reload -c /etc/caddy/Caddyfile')
print("\n=== CADDY RELOAD OUTPUT ===")
print(stdout.read().decode('utf-8'))
print(stderr.read().decode('utf-8'))

sftp.close()
ssh.close()
print("\n✅ Caddyfile updated and Caddy reloaded")
