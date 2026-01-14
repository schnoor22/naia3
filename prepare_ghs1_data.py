#!/usr/bin/env python3
"""
GHS1 (Golden Hill Solar) Data Preparation Script
================================================
Anonymizes DQZ1 BESS/BOP/MET data by:
1. Extracting ZIP files
2. Renaming DQZ1 -> GHS1 in filenames
3. Uploading to server at /opt/naia/data/ghs1/
4. Cleaning up local files

Run from the naia3 directory:
    python prepare_ghs1_data.py
"""

import os
import sys
import shutil
import zipfile
import subprocess
from pathlib import Path

# Configuration
LOCAL_DATA_DIR = Path("c:/naia3/data")
EXTRACT_DIR = LOCAL_DATA_DIR / "extracted" / "GHS1"
SERVER_HOST = "root@37.27.189.86"
SERVER_DATA_DIR = "/opt/naia/data/ghs1"

# Anonymization mappings
RENAME_MAPPINGS = {
    "DQZ1": "GHS1",      # Site prefix
    "dqz1": "ghs1",      # lowercase version
}

# Source ZIP files (find all DQZ1 ZIPs in data dir)
def find_source_zips():
    """Find all DQZ1 ZIP files in the data directory"""
    zips = []
    for f in LOCAL_DATA_DIR.iterdir():
        if f.suffix.lower() == '.zip' and 'DQZ1' in f.name.upper():
            zips.append(f)
    return zips

def anonymize_filename(filename: str) -> str:
    """Replace DQZ1 with GHS1 in filename"""
    result = filename
    for old, new in RENAME_MAPPINGS.items():
        result = result.replace(old, new)
    return result

def extract_and_anonymize():
    """Extract ZIPs and rename files to anonymized names"""
    zips = find_source_zips()
    if not zips:
        print("❌ No DQZ1 ZIP files found in", LOCAL_DATA_DIR)
        print("   Expected files like: DQZ1BESS001_*.zip, DQZ1SMET001_*.zip")
        return False
    
    print(f"📦 Found {len(zips)} ZIP files to process")
    
    # Create extract directory
    EXTRACT_DIR.mkdir(parents=True, exist_ok=True)
    
    total_files = 0
    for zip_path in zips:
        print(f"\n📂 Processing: {zip_path.name}")
        
        # Extract to temp location
        temp_dir = EXTRACT_DIR / "temp"
        temp_dir.mkdir(exist_ok=True)
        
        with zipfile.ZipFile(zip_path, 'r') as zf:
            zf.extractall(temp_dir)
        
        # Move and rename CSV files
        for csv_file in temp_dir.rglob("*.csv"):
            old_name = csv_file.name
            new_name = anonymize_filename(old_name)
            
            dest_path = EXTRACT_DIR / new_name
            shutil.move(str(csv_file), str(dest_path))
            
            if old_name != new_name:
                print(f"   {old_name} → {new_name}")
            total_files += 1
        
        # Clean up temp dir
        shutil.rmtree(temp_dir, ignore_errors=True)
    
    print(f"\n✅ Extracted and anonymized {total_files} CSV files to {EXTRACT_DIR}")
    return True

def upload_to_server():
    """Upload anonymized data to server via rsync/scp"""
    print(f"\n☁️  Uploading to {SERVER_HOST}:{SERVER_DATA_DIR}")
    
    # Create remote directory
    ssh_mkdir = f'ssh {SERVER_HOST} "mkdir -p {SERVER_DATA_DIR}"'
    result = subprocess.run(ssh_mkdir, shell=True)
    if result.returncode != 0:
        print("❌ Failed to create remote directory")
        return False
    
    # Use rsync if available, otherwise scp
    # rsync is preferred for progress and efficiency
    rsync_cmd = f'rsync -avz --progress "{EXTRACT_DIR}/" {SERVER_HOST}:{SERVER_DATA_DIR}/'
    print(f"   Running: {rsync_cmd}")
    
    result = subprocess.run(rsync_cmd, shell=True)
    if result.returncode != 0:
        print("⚠️  rsync failed, trying scp...")
        scp_cmd = f'scp -r "{EXTRACT_DIR}/"* {SERVER_HOST}:{SERVER_DATA_DIR}/'
        result = subprocess.run(scp_cmd, shell=True)
        if result.returncode != 0:
            print("❌ Failed to upload files")
            return False
    
    # Verify upload
    verify_cmd = f'ssh {SERVER_HOST} "ls -la {SERVER_DATA_DIR}/ | head -20 && echo && ls {SERVER_DATA_DIR}/*.csv 2>/dev/null | wc -l"'
    print(f"\n📊 Verifying upload...")
    subprocess.run(verify_cmd, shell=True)
    
    print(f"✅ Upload complete")
    return True

def cleanup_local():
    """Remove local extracted files and optionally source ZIPs"""
    print(f"\n🧹 Cleaning up local files...")
    
    # Remove extracted directory
    if EXTRACT_DIR.exists():
        shutil.rmtree(EXTRACT_DIR)
        print(f"   Removed: {EXTRACT_DIR}")
    
    # Ask about source ZIPs
    zips = find_source_zips()
    if zips:
        print(f"\n   Found {len(zips)} source ZIP files:")
        for z in zips:
            print(f"     - {z.name}")
        
        response = input("\n   Delete source ZIP files? (y/N): ").strip().lower()
        if response == 'y':
            for z in zips:
                z.unlink()
                print(f"   Deleted: {z.name}")
            print("✅ Source ZIPs deleted")
        else:
            print("   Keeping source ZIPs")
    
    print("✅ Cleanup complete")

def generate_point_registration_sql():
    """Generate SQL to pre-register points with metadata"""
    if not EXTRACT_DIR.exists():
        print("⚠️  Extract directory not found, cannot generate SQL")
        return
    
    # Get unique tag names from CSV filenames
    tags = set()
    for csv_file in EXTRACT_DIR.glob("*.csv"):
        # Extract tag name from filename (before timestamp)
        # Format: GHS1BESS001_1A_E_day_20260113_194530.csv
        name = csv_file.stem
        parts = name.rsplit('_', 2)  # Split from right to remove date_time
        if len(parts) >= 3:
            tag = parts[0]  # Everything before the date
            tags.add(tag)
    
    if not tags:
        print("⚠️  No tags found in CSV files")
        return
    
    sql_file = LOCAL_DATA_DIR / "register_ghs1_points.sql"
    
    with open(sql_file, 'w') as f:
        f.write("-- GHS1 (Golden Hill Solar) Point Registration\n")
        f.write("-- Auto-generated from CSV filenames\n")
        f.write(f"-- Tags found: {len(tags)}\n")
        f.write("--\n")
        f.write("-- Run with: psql -h localhost -U naia -d naia -f register_ghs1_points.sql\n")
        f.write("--\n\n")
        
        f.write("-- Create data source if not exists\n")
        f.write("INSERT INTO data_sources (id, name, connection_string, description, is_active)\n")
        f.write("VALUES (\n")
        f.write("    'a5e5e5e5-5e5e-5e5e-5e5e-5e5e5e5e5e5e'::uuid,\n")
        f.write("    'GHS1_SOLAR_BESS',\n")
        f.write("    'csv-replay://ghs1',\n")
        f.write("    'Golden Hill Solar (Anonymized from DQZ1) - BESS + BOP + MET',\n")
        f.write("    true\n")
        f.write(") ON CONFLICT (id) DO NOTHING;\n\n")
        
        f.write("-- Register points (they will also be auto-registered on first data receipt)\n")
        f.write("-- This pre-registration ensures proper metadata from the start\n\n")
        
        for tag in sorted(tags):
            # Infer metadata from tag name
            unit = infer_unit(tag)
            desc = infer_description(tag)
            
            f.write(f"INSERT INTO points (id, name, data_source_id, description, engineering_units, is_enabled, created_at, updated_at)\n")
            f.write(f"VALUES (\n")
            f.write(f"    gen_random_uuid(),\n")
            f.write(f"    '{tag}',\n")
            f.write(f"    'a5e5e5e5-5e5e-5e5e-5e5e-5e5e5e5e5e5e'::uuid,\n")
            f.write(f"    '{desc}',\n")
            f.write(f"    '{unit}',\n")
            f.write(f"    true,\n")
            f.write(f"    NOW(),\n")
            f.write(f"    NOW()\n")
            f.write(f") ON CONFLICT (name) DO UPDATE SET description = EXCLUDED.description;\n\n")
        
        f.write(f"\n-- Summary: {len(tags)} points registered for GHS1\n")
    
    print(f"✅ Generated SQL: {sql_file}")
    print(f"   Tags to register: {len(tags)}")

def infer_unit(tag_name: str) -> str:
    """Infer engineering units from tag name"""
    tag_upper = tag_name.upper()
    
    # BESS-specific
    if 'SOC' in tag_upper:
        return '%'
    if 'POWER' in tag_upper or '_P_' in tag_upper or 'KW' in tag_upper:
        return 'kW'
    if 'VOLTAGE' in tag_upper or '_V_' in tag_upper or 'VOLT' in tag_upper:
        return 'V'
    if 'CURRENT' in tag_upper or '_I_' in tag_upper or '_A_' in tag_upper:
        return 'A'
    if 'FREQ' in tag_upper:
        return 'Hz'
    if 'TEMP' in tag_upper:
        return '°C'
    if 'ENERGY' in tag_upper or 'KWH' in tag_upper:
        return 'kWh'
    
    # MET station
    if 'GHI' in tag_upper or 'IRRAD' in tag_upper:
        return 'W/m²'
    if 'WSPD' in tag_upper or 'WIND' in tag_upper:
        return 'm/s'
    if 'WDIR' in tag_upper:
        return '°'
    if 'HUMIDITY' in tag_upper or '_RH_' in tag_upper:
        return '%RH'
    if 'PRESSURE' in tag_upper:
        return 'hPa'
    
    return ''

def infer_description(tag_name: str) -> str:
    """Infer description from tag name"""
    tag_upper = tag_name.upper()
    
    if 'BESS' in tag_upper:
        return 'Battery Energy Storage System'
    if 'SMET' in tag_upper or 'MET' in tag_upper:
        return 'Meteorological Station'
    if 'INV' in tag_upper:
        return 'Inverter'
    if 'BOP' in tag_upper:
        return 'Balance of Plant'
    
    return 'GHS1 Solar Site Tag'

def main():
    print("=" * 60)
    print("  GHS1 (Golden Hill Solar) Data Preparation")
    print("  Anonymizing DQZ1 → GHS1")
    print("=" * 60)
    
    # Step 1: Extract and anonymize
    if not extract_and_anonymize():
        sys.exit(1)
    
    # Step 2: Generate point registration SQL
    generate_point_registration_sql()
    
    # Step 3: Upload to server
    if not upload_to_server():
        print("\n⚠️  Upload failed, keeping local files for manual upload")
        sys.exit(1)
    
    # Step 4: Clean up
    cleanup_local()
    
    print("\n" + "=" * 60)
    print("  ✅ GHS1 DATA PREPARATION COMPLETE!")
    print("=" * 60)
    print("\n  Next steps:")
    print("  1. Run the point registration SQL on the server")
    print("  2. Update appsettings to enable GHS1 replay")
    print("  3. Restart the ingestion service")

if __name__ == "__main__":
    main()
