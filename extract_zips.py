import pyzipper
import os
from pathlib import Path

files_dir = Path(r"C:\naia3\files")
extracted_dir = files_dir / "extracted"

zip_files = [
    ("PND1WTHR_WindDir_Daily_20260113_004715.zip", "pnd1"),
    ("BLW1WTHR_WindSpd_Current_20260113_01269.zip", "blw1")
]

for zip_name, folder_name in zip_files:
    zip_path = files_dir / zip_name
    target_dir = extracted_dir / folder_name
    
    print(f"\nExtracting {zip_name}...")
    target_dir.mkdir(parents=True, exist_ok=True)
    
    try:
        with pyzipper.AESZipFile(zip_path, 'r', compression=pyzipper.ZIP_DEFLATED, allowZip64=True) as zip_ref:
            zip_ref.extractall(target_dir)
            file_count = len(zip_ref.namelist())
            print(f"✓ Extracted {file_count} files to {folder_name}/")
            
            # Show CSV files with "INV" in the name
            inv_files = [f for f in zip_ref.namelist() if 'INV' in f.upper() and f.endswith('.csv')]
            if inv_files:
                print(f"  Found {len(inv_files)} inverter CSV files:")
                for inv_file in inv_files[:10]:  # Show first 10
                    print(f"    - {inv_file}")
                if len(inv_files) > 10:
                    print(f"    ... and {len(inv_files) - 10} more")
    except Exception as e:
        print(f"✗ Error extracting {zip_name}: {e}")

print("\nDone!")
