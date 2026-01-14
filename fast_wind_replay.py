#!/usr/bin/env python3
"""Fast parallel wind site CSV processor using concurrent.futures"""

import os
import sys
import shutil
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed
from collections import defaultdict

class FastWindDataProcessor:
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
            },
            "BLX1": {
                "extracted_folder": "extracted/BLX1",
                "old_prefix": "CHFL",
                "new_prefix": "BLX1",
                "display_name": "Blixton Wind Farm",
            }
        }
        self.stats = {}
        
    def process_all_sites(self):
        """Process all wind sites in parallel"""
        print("[WIND] Fast Wind Site CSV Processing (Parallel)")
        print("=" * 70)
        
        for site_id, config in self.sites.items():
            folder_path = self.data_dir / config["extracted_folder"]
            if not folder_path.exists():
                print(f"[ERR] Folder not found: {folder_path}")
                continue
            
            self.process_site_parallel(site_id, config, folder_path)
        
        self.print_summary()
    
    def copy_file(self, args):
        """Copy single file (for parallel processing)"""
        src, dst, old_prefix, new_prefix = args
        try:
            new_name = src.name.replace(old_prefix, new_prefix)
            dest_file = dst / new_name
            # Skip if already exists
            if not dest_file.exists():
                shutil.copy2(src, dest_file)
            return True
        except Exception as e:
            return False
    
    def process_site_parallel(self, site_id: str, config: dict, folder_path: Path):
        """Process site using parallel file copying"""
        print(f"\n[DIR] Processing {config['display_name']} ({site_id})")
        
        csv_files = list(folder_path.glob("*.csv"))
        total_files = len(csv_files)
        
        if not csv_files:
            print(f"   [ERR] No CSV files found!")
            return
        
        print(f"   [INFO] Found {total_files:,} CSV files")
        
        # Create output directory
        output_site_dir = self.output_dir / site_id.lower()
        output_site_dir.mkdir(parents=True, exist_ok=True)
        
        # Check how many already exist
        existing = len(list(output_site_dir.glob("*.csv")))
        if existing > 0:
            print(f"   [SKIP] {existing:,} files already processed, skipping")
            self.stats[site_id] = {"processed": existing, "total": total_files}
            return
        
        # Prepare copy tasks
        tasks = []
        for csv_file in csv_files:
            tasks.append((csv_file, output_site_dir, config["old_prefix"], config["new_prefix"]))
        
        # Process in parallel
        processed = 0
        failed = 0
        
        with ThreadPoolExecutor(max_workers=16) as executor:
            futures = [executor.submit(self.copy_file, task) for task in tasks]
            
            for i, future in enumerate(as_completed(futures), 1):
                if future.result():
                    processed += 1
                else:
                    failed += 1
                
                if i % 200 == 0:
                    pct = (i / total_files) * 100
                    print(f"   Progress: {i:,}/{total_files:,} ({pct:.0f}%)")
        
        self.stats[site_id] = {"processed": processed, "total": total_files, "failed": failed}
        
        print(f"   [OK] Complete: {processed:,}/{total_files:,} files copied")
        if failed:
            print(f"   [WARN] {failed:,} files failed")
    
    def print_summary(self):
        """Print summary"""
        print("\n" + "=" * 70)
        print("[INFO] Processing Complete")
        print("=" * 70)
        
        total_processed = 0
        for site_id, stats in self.stats.items():
            total_processed += stats["processed"]
            print(f"\n{site_id}")
            print(f"  |- Processed: {stats['processed']:,}/{stats['total']:,} files")
            if "failed" in stats and stats["failed"] > 0:
                print(f"  `- Failed: {stats['failed']:,}")
        
        print(f"\n[OK] Total files processed: {total_processed:,}")
        print("\nNext: bash upload_wind_data.sh")

def main():
    processor = FastWindDataProcessor()
    processor.process_all_sites()

if __name__ == "__main__":
    main()
