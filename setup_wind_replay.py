#!/usr/bin/env python3
"""
Process wind site CSV data (ELT1 and BLX1) and prepare for replay
Handles anonymization of EX5R -> ELT1 and CHFL -> BLX1 wind farm data
"""

import os
import sys
import shutil
from pathlib import Path
from collections import defaultdict

class WindDataProcessor:
    def __init__(self):
        self.data_dir = Path("data")
        self.output_dir = Path("data/wind_processed")
        self.output_dir.mkdir(parents=True, exist_ok=True)
        self.sites = {
            "ELT1": {
                "extracted_folder": "extracted/ELT1",
                "old_prefix": "EX5R",
                "new_prefix": "ELT1",
                "display_name": "El Toro Wind Farm",
                "source_name": "el_toro_wind",
            },
            "BLX1": {
                "extracted_folder": "extracted/BLX1",
                "old_prefix": "CHFL",
                "new_prefix": "BLX1",
                "display_name": "Blixton Wind Farm",
                "source_name": "blixton_wind",
            }
        }
        self.stats = defaultdict(dict)
        
    def process_all_sites(self):
        """Main processing workflow"""
        print("[WIND] Wind Site CSV Processing")
        print("=" * 70)
        
        for site_id, config in self.sites.items():
            folder_path = self.data_dir / config["extracted_folder"]
            if not folder_path.exists():
                print(f"[ERR] Folder not found: {folder_path}")
                continue
            
            self.process_site(site_id, config, folder_path)
        
        self.print_summary()
        self.generate_upload_script()
        self.generate_cleanup_script()
    
    def process_site(self, site_id: str, config: dict, folder_path: Path):
        """Process a single wind site"""
        print(f"\n[DIR] Processing {config['display_name']} ({site_id})")
        print(f"   Source folder: {folder_path}")
        
        csv_files = sorted(folder_path.glob("*.csv"))
        total_files = len(csv_files)
        
        if not csv_files:
            print(f"   [ERR] No CSV files found!")
            return
        
        print(f"   [INFO] Found {total_files:,} CSV files")
        
        # Create output directory
        output_site_dir = self.output_dir / site_id.lower()
        output_site_dir.mkdir(parents=True, exist_ok=True)
        
        # Process files in batches
        batch_size = 100
        processed = 0
        skipped = 0
        
        for i, csv_file in enumerate(csv_files, 1):
            new_name = self._anonymize_filename(csv_file.name, config["old_prefix"], config["new_prefix"])
            output_path = output_site_dir / new_name
            
            try:
                shutil.copy2(csv_file, output_path)
                processed += 1
                
                if i % batch_size == 0:
                    pct = (i / total_files) * 100
                    print(f"   Progress: {i:,}/{total_files:,} ({pct:.0f}%)")
            except Exception as e:
                print(f"   [WARN] Error processing {csv_file.name}: {e}")
                skipped += 1
        
        self.stats[site_id] = {
            "processed": processed,
            "skipped": skipped,
            "total": total_files,
            "output_dir": str(output_site_dir)
        }
        
        print(f"   [OK] Complete: {processed:,}/{total_files:,} files processed")
        if skipped:
            print(f"   [WARN] {skipped:,} files skipped due to errors")
    
    def _anonymize_filename(self, filename: str, old_prefix: str, new_prefix: str) -> str:
        """Replace old site prefix with new anonymized prefix"""
        return filename.replace(old_prefix, new_prefix)
    
    def print_summary(self):
        """Print processing summary"""
        print("\n" + "=" * 70)
        print("[INFO] Processing Summary")
        print("=" * 70)
        
        total_processed = 0
        for site_id, stats in self.stats.items():
            total_processed += stats["processed"]
            print(f"\n{site_id} - {self.sites[site_id]['display_name']}")
            print(f"  |- Processed: {stats['processed']:,} files")
            print(f"  |- Skipped: {stats['skipped']:,} files")
            print(f"  `- Output: {stats['output_dir']}")
        
        print(f"\n[OK] Total files processed: {total_processed:,}")
    
    def generate_upload_script(self):
        """Create upload script for wind site data"""
        script = """#!/bin/bash
# Upload wind site data to server
# Usage: bash upload_wind_data.sh

set -e

SERVER="root@naia"
REMOTE_WIND_PATH="/opt/naia/data/wind"

echo "[WIND] Uploading wind site CSV data..."
echo "====================================================================="

# Create remote directories
echo "[DIR] Creating remote directories..."
ssh $SERVER "mkdir -p $REMOTE_WIND_PATH/{elt1,blx1} && chown -R naia:naia $REMOTE_WIND_PATH && chmod -R u+w $REMOTE_WIND_PATH"

# Upload ELT1 (El Toro Wind)
if [ -d "data/wind_processed/elt1" ] && [ "$(ls -A data/wind_processed/elt1)" ]; then
    echo ""
    COUNT=$(ls data/wind_processed/elt1/*.csv 2>/dev/null | wc -l)
    echo "[UP] Uploading ELT1 (El Toro Wind) - $COUNT files..."
    scp -r data/wind_processed/elt1/*.csv $SERVER:$REMOTE_WIND_PATH/elt1/ || echo "[ERR] Upload failed"
    echo "[OK] ELT1 upload complete"
else
    echo "[SKIP] ELT1 data not available"
fi

# Upload BLX1 (Blixton Wind)
if [ -d "data/wind_processed/blx1" ] && [ "$(ls -A data/wind_processed/blx1)" ]; then
    echo ""
    COUNT=$(ls data/wind_processed/blx1/*.csv 2>/dev/null | wc -l)
    echo "[UP] Uploading BLX1 (Blixton Wind) - $COUNT files..."
    scp -r data/wind_processed/blx1/*.csv $SERVER:$REMOTE_WIND_PATH/blx1/ || echo "[ERR] Upload failed"
    echo "[OK] BLX1 upload complete"
else
    echo "[SKIP] BLX1 data not available"
fi

# Fix final permissions
echo ""
echo "[SEC] Setting file permissions..."
ssh $SERVER "chown -R naia:naia $REMOTE_WIND_PATH && chmod -R u+w $REMOTE_WIND_PATH && echo '[OK] Permissions set'"

echo ""
echo "====================================================================="
echo "[OK] Wind site data upload complete!"
"""
        script_path = Path("upload_wind_data.sh")
        with open(script_path, 'w') as f:
            f.write(script)
        os.chmod(str(script_path), 0o755)
        print(f"\n[FILE] Created: {script_path}")
    
    def generate_cleanup_script(self):
        """Create cleanup script to remove local files after upload"""
        script = """#!/usr/bin/env python3
import shutil
from pathlib import Path

paths_to_remove = [
    Path("data/wind_processed"),
    Path("data/extracted/ELT1"),
    Path("data/extracted/BLX1"),
]

print("[CLEAN] Cleaning up local wind site files...")
print("=" * 60)

total_size = 0
total_files = 0

for path in paths_to_remove:
    if path.exists():
        if path.is_file():
            size = path.stat().st_size
            total_size += size
            total_files += 1
        else:
            for f in path.rglob("*"):
                if f.is_file():
                    total_size += f.stat().st_size
                    total_files += 1

print(f"Will delete: {total_files:,} files")
print(f"Total size: {total_size / (1024**3):.2f} GB")
print()

response = input("[WARN] Proceed with deletion? (type 'yes' to confirm): ").strip().lower()

if response != "yes":
    print("[ERR] Cleanup cancelled")
    exit(1)

print()
for path in paths_to_remove:
    if path.exists():
        try:
            if path.is_file():
                path.unlink()
                print(f"[OK] Deleted file: {path}")
            else:
                shutil.rmtree(path)
                print(f"[OK] Deleted directory: {path}")
        except Exception as e:
            print(f"[ERR] Error: {e}")

print()
print("=" * 60)
print("[OK] Cleanup complete!")
"""
        cleanup_path = Path("cleanup_wind_files.py")
        with open(cleanup_path, 'w') as f:
            f.write(script)
        print(f"[FILE] Created: {cleanup_path}")

def main():
    processor = WindDataProcessor()
    processor.process_all_sites()
    
    print("\n" + "=" * 70)
    print("[OK] Wind site processing complete!")
    print("\nNext steps:")
    print("1. Run upload script: bash upload_wind_data.sh")
    print("2. Verify on server: ssh root@naia 'du -sh /opt/naia/data/wind/*'")
    print("3. Clean up locally: python cleanup_wind_files.py")
    print()

if __name__ == "__main__":
    main()
