#!/usr/bin/env python3
"""
Clean up local wind CSV files after successful upload to server.

This prevents CSV files from being tracked in git and keeps the repo clean.
Only run this AFTER confirming files are successfully uploaded to the server.

Usage:
    python cleanup_local_wind_files.py
"""

import os
from pathlib import Path
import shutil

def cleanup_wind_files():
    """Remove processed wind files from local machine"""
    
    paths_to_remove = [
        Path("data/wind_processed"),
        Path("data/extracted/ELT"),
        Path("data/extracted/BLX"),
    ]
    
    print("🧹 Cleaning up local wind site files...")
    print("=" * 60)
    
    removed_count = 0
    
    for path in paths_to_remove:
        if path.exists():
            try:
                if path.is_file():
                    path.unlink()
                    print(f"✅ Removed file: {path}")
                else:
                    shutil.rmtree(path)
                    print(f"✅ Removed directory: {path}")
                removed_count += 1
            except Exception as e:
                print(f"❌ Error removing {path}: {e}")
        else:
            print(f"⏭️  Not found: {path}")
    
    print("\n" + "=" * 60)
    print(f"✅ Cleanup complete! Removed {removed_count} items")
    print("\n⚠️  WARNING: Files have been permanently deleted!")
    print("Verify they exist on the server before running git operations.")

if __name__ == "__main__":
    response = input("\n🔍 This will DELETE local wind CSV files. Continue? (yes/no): ").strip().lower()
    if response == "yes":
        cleanup_wind_files()
    else:
        print("❌ Cleanup cancelled")
