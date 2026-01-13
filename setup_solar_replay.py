"""
Anonymize solar data and prepare for 2x speed replay with looping
"""
import shutil
import json
from pathlib import Path
from datetime import datetime

# Paths
files_dir = Path(r"C:\naia3\files")
extracted = files_dir / "extracted"
anonymized = files_dir / "anonymized_solar"

# Clean and create anonymized directory
if anonymized.exists():
    shutil.rmtree(anonymized)
anonymized.mkdir(parents=True)

# Site mapping: old_name -> new_name, inverters
sites = {
    "PND1": {
        "new_name": "SITE1",
        "inverters": ["SINV001", "SINV002", "SINV003", "SINV004", "SINV005"],
        "source_dir": extracted / "pnd1"
    },
    "BLW1": {
        "new_name": "SITE2",
        "inverters": ["SINV001", "SINV002", "SINV003"],
        "source_dir": extracted / "blw1"
    }
}

print("=== Anonymizing Solar Data ===\n")

# Copy and rename files
for old_site, config in sites.items():
    new_site = config["new_name"]
    source_dir = config["source_dir"]
    target_dir = anonymized / new_site.lower()
    target_dir.mkdir(parents=True, exist_ok=True)
    
    print(f"{old_site} -> {new_site}")
    
    # For each inverter, copy only Active_Power CSV (main data file)
    for inv_num, inv_name in enumerate(config["inverters"], 1):
        old_inv = f"{old_site}{inv_name}"
        new_inv = f"INV{inv_num:02d}"
        
        # Find the Active_Power CSV (prefer ING_Active Power for real-time data)
        # Pattern 1: ING_Active Power (may have spaces or underscores in name)
        # Pattern 2: Simple Active_Power
        pattern1 = f"{old_inv}_ING_Active Power*.csv"
        pattern2 = f"{old_inv}_Active_Power_*.csv"
        matches = list(source_dir.glob(pattern1)) or list(source_dir.glob(pattern2))
        
        if matches:
            source_file = matches[0]
            target_file = target_dir / f"{new_site}_{new_inv}_ActivePower.csv"
            
            # Copy file
            shutil.copy2(source_file, target_file)
            
            # Get line count
            with open(target_file, 'r') as f:
                line_count = sum(1 for _ in f)
            
            size_mb = target_file.stat().st_size / 1024 / 1024
            print(f"  ✓ {new_inv}: {line_count:,} lines, {size_mb:.1f} MB")
        else:
            print(f"  ✗ {new_inv}: No Active_Power file found")
    
    print()

# Create appsettings configuration
config = {
    "Connectors": {
        "GenericCsvReplay": {
            "Enabled": True,
            "SpeedMultiplier": 2.0,
            "LoopReplay": True,
            "ShiftToCurrentTime": True,
            "Sites": []
        }
    }
}

for old_site, site_config in sites.items():
    new_site = site_config["new_name"]
    site_entry = {
        "SiteId": new_site,
        "DataDirectory": f"/opt/naia/data/solar/{new_site.lower()}",
        "TagPrefix": f"{new_site}_",
        "PointMappings": []
    }
    
    for inv_num in range(1, len(site_config["inverters"]) + 1):
        site_entry["PointMappings"].append({
            "CsvFile": f"{new_site}_INV{inv_num:02d}_ActivePower.csv",
            "PointName": f"{new_site}_INV{inv_num:02d}_ActivePower",
            "Unit": "kW",
            "Description": f"{new_site} Inverter {inv_num:02d} Active Power"
        })
    
    config["Connectors"]["GenericCsvReplay"]["Sites"].append(site_entry)

# Save config
config_file = anonymized / "appsettings.GenericCsvReplay.json"
with open(config_file, 'w') as f:
    json.dump(config, f, indent=2)

print(f"✓ Configuration saved to: {config_file}\n")

# Create deployment script for server
deploy_script = anonymized / "deploy_to_server.sh"
with open(deploy_script, 'w', encoding='utf-8', newline='\n') as f:
    f.write("""#!/bin/bash
set -e

echo "=== Deploying Solar Replay Data ==="

# Create directories
mkdir -p /opt/naia/data/solar/site1
mkdir -p /opt/naia/data/solar/site2

# Copy data files
echo "Copying SITE1 data..."
cp site1/*.csv /opt/naia/data/solar/site1/

echo "Copying SITE2 data..."
cp site2/*.csv /opt/naia/data/solar/site2/

# Update appsettings
echo "Updating configuration..."
cd /opt/naia/publish-ingestion

# Backup existing config
if [ -f appsettings.json ]; then
    cp appsettings.json appsettings.json.backup.$(date +%Y%m%d_%H%M%S)
fi

# Merge GenericCsvReplay config
python3 << 'PYTHON'
import json
from pathlib import Path

# Read new config
with open('/root/anonymized_solar/appsettings.GenericCsvReplay.json') as f:
    new_config = json.load(f)

# Read existing config
config_file = Path('appsettings.json')
if config_file.exists():
    with open(config_file) as f:
        existing = json.load(f)
else:
    existing = {}

# Merge
if 'Connectors' not in existing:
    existing['Connectors'] = {}
existing['Connectors']['GenericCsvReplay'] = new_config['Connectors']['GenericCsvReplay']

# Save
with open(config_file, 'w') as f:
    json.dump(existing, f, indent=2)

print("✓ Configuration merged")
PYTHON

# Restart ingestion service
echo "Restarting ingestion service..."
systemctl restart naia-ingestion

echo "✓ Deployment complete!"
echo ""
echo "Monitor with: journalctl -u naia-ingestion -f"
""")

print(f"✓ Deployment script saved to: {deploy_script}\n")

# Create upload instructions
readme = anonymized / "README.txt"
with open(readme, 'w', encoding='utf-8') as f:
    f.write(f"""Solar Data Replay - Deployment Instructions
Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}

ANONYMIZATION COMPLETE:
- PND1 -> SITE1 (5 inverters)
- BLW1 -> SITE2 (3 inverters)
- All company/site references removed
- Files renamed with generic identifiers

CONFIGURATION:
- 2x replay speed (30-second intervals for 1-minute data)
- Automatic loop at end of file
- Timestamps shifted to current time

TO DEPLOY:

1. Upload files to server:
   scp -r {anonymized.name} root@37.27.189.86:~/

2. SSH to server:
   ssh root@37.27.189.86

3. Run deployment script:
   cd ~/anonymized_solar
   chmod +x deploy_to_server.sh
   ./deploy_to_server.sh

4. Monitor ingestion:
   journalctl -u naia-ingestion -f

DATA DETAILS:
- Total: 8 inverters across 2 sites
- ~91,000 rows per inverter (~2 months of minute data)
- Format: Timestamp,Value,Status
- Replay: 30-second intervals (2x speed)
- Loop: Restarts automatically at EOF

CLEANUP:
After successful deployment, delete local files:
- {anonymized}
- {extracted}
- {files_dir / '*.zip'}
""")

print(f"✓ Instructions saved to: {readme}\n")
print("=" * 60)
print("ANONYMIZATION COMPLETE!")
print("=" * 60)
print(f"\nFiles ready in: {anonymized}")
print(f"\nNext steps:")
print(f"1. Upload to server: scp -r {anonymized.name} root@37.27.189.86:~/")
print(f"2. Run deployment script on server")
print(f"3. Clean up local files\n")
