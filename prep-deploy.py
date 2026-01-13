#!/usr/bin/env python3
import os
import subprocess
import shutil

# Get the binary path
bin_path = r"C:\naia3\src\Naia.Api\bin\Release\net8.0"

# Create a tar file locally
tar_file = r"C:\naia3\naia-api-latest.tar.gz"
print(f"Creating tar file from {bin_path}...")

# Use 7z to create archive (PowerShell friendly)
subprocess.run([
    "powershell", "-Command",
    f"Compress-Archive -Path '{bin_path}\\*' -DestinationPath 'C:\\naia3\\naia-api-latest.zip' -Force"
], check=True)

print("Archive created at C:\\naia3\\naia-api-latest.zip")
print("\nNow run on the server:")
print("cd /tmp && curl -O http://[your-ip]:8000/naia-api-latest.zip && unzip -o naia-api-latest.zip -d /opt/naia/publish/ && sudo systemctl restart naia-api")
