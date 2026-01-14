#!/usr/bin/env python3
"""
Rebase all CSV replay data to current time while preserving intra-day patterns.
Essential for preventing future-dated data from breaking the system.

Algorithm:
1. Detect the date range in each file
2. Calculate offset: (TODAY - original_date_start)
3. Apply offset to every timestamp
4. Preserve original time-of-day patterns
5. Ensure all timestamps are in UTC
"""

import os
import sys
from datetime import datetime, timedelta
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import csv
import tempfile
import shutil

def get_today_utc():
    """Get today's date at midnight UTC"""
    return datetime.utcnow().date()

def rebase_timestamp(original_ts_str: str, day_offset: timedelta) -> str:
    """
    Rebase a single timestamp by applying day offset.
    
    Preserves time-of-day. Assumes input is in UTC.
    Examples:
        2025-11-10 16:00:00 with offset +64 days -> 2026-01-13 16:00:00
    """
    try:
        # Parse the original timestamp
        dt = datetime.strptime(original_ts_str.strip(), '%Y-%m-%d %H:%M:%S')
        
        # Apply the day offset (preserves time-of-day)
        rebased_dt = dt + day_offset
        
        # Return as UTC string
        return rebased_dt.strftime('%Y-%m-%d %H:%M:%S')
    except Exception as e:
        print(f"ERROR parsing timestamp '{original_ts_str}': {e}")
        return None

def detect_date_range(file_path: str) -> tuple:
    """
    Scan CSV file to find min and max dates.
    Returns (min_date, max_date, record_count)
    """
    min_date = None
    max_date = None
    count = 0
    
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                if 'Timestamp' not in row:
                    continue
                
                try:
                    dt = datetime.strptime(row['Timestamp'].strip(), '%Y-%m-%d %H:%M:%S')
                    ts_date = dt.date()
                    
                    if min_date is None or ts_date < min_date:
                        min_date = ts_date
                    if max_date is None or ts_date > max_date:
                        max_date = ts_date
                    
                    count += 1
                except:
                    pass
    except Exception as e:
        print(f"ERROR scanning {file_path}: {e}")
        return None, None, 0
    
    return min_date, max_date, count

def rebase_csv_file(input_path: str, output_path: str, day_offset: timedelta) -> bool:
    """
    Rebase all timestamps in a CSV file and write to output.
    Preserves CSV structure and all other columns.
    """
    try:
        temp_output = output_path + '.tmp'
        
        with open(input_path, 'r', encoding='utf-8') as infile, \
             open(temp_output, 'w', encoding='utf-8', newline='') as outfile:
            
            reader = csv.DictReader(infile)
            
            if reader.fieldnames is None:
                print(f"ERROR: Could not read CSV headers from {input_path}")
                return False
            
            writer = csv.DictWriter(outfile, fieldnames=reader.fieldnames)
            writer.writeheader()
            
            rows_written = 0
            for row in reader:
                if 'Timestamp' in row:
                    rebased_ts = rebase_timestamp(row['Timestamp'], day_offset)
                    if rebased_ts:
                        row['Timestamp'] = rebased_ts
                        rows_written += 1
                
                writer.writerow(row)
        
        # Atomic rename
        if os.path.exists(output_path):
            os.remove(output_path)
        shutil.move(temp_output, output_path)
        
        return rows_written > 0
    
    except Exception as e:
        print(f"ERROR processing {input_path}: {e}")
        if os.path.exists(temp_output):
            os.remove(temp_output)
        return False

def rebase_directory(input_dir: str, output_dir: str, max_workers: int = 16) -> dict:
    """
    Rebase all CSV files in a directory.
    Uses parallel processing for speed.
    """
    os.makedirs(output_dir, exist_ok=True)
    
    csv_files = list(Path(input_dir).glob('*.csv'))
    if not csv_files:
        print(f"No CSV files found in {input_dir}")
        return {'total': 0, 'success': 0, 'failed': 0}
    
    print(f"\n🔍 Scanning {len(csv_files)} files to detect date ranges...")
    
    # Scan all files to find the overall min date
    min_date_overall = None
    max_date_overall = None
    total_records = 0
    file_info = {}
    
    for idx, csv_file in enumerate(csv_files, 1):
        if idx % 100 == 0:
            print(f"   Scanned {idx}/{len(csv_files)} files...")
        
        min_dt, max_dt, count = detect_date_range(str(csv_file))
        if min_dt:
            file_info[csv_file.name] = (min_dt, max_dt, count)
            total_records += count
            
            if min_date_overall is None or min_dt < min_date_overall:
                min_date_overall = min_dt
            if max_date_overall is None or max_dt > max_date_overall:
                max_date_overall = max_dt
    
    if not file_info:
        print("ERROR: Could not detect dates in any CSV files")
        return {'total': len(csv_files), 'success': 0, 'failed': len(csv_files)}
    
    today = get_today_utc()
    day_offset = today - min_date_overall
    
    print(f"\n📊 Summary:")
    print(f"   Original date range: {min_date_overall} to {max_date_overall}")
    print(f"   Total records to rebase: {total_records:,}")
    print(f"   Day offset to apply: {day_offset.days} days")
    print(f"   New date range will be: {today} to {max_date_overall + day_offset}")
    print(f"\n⏳ Rebasing {len(csv_files)} files...")
    
    # Process files in parallel
    def process_file(csv_file):
        output_file = os.path.join(output_dir, csv_file.name)
        success = rebase_csv_file(str(csv_file), output_file, day_offset)
        return csv_file.name, success
    
    results = {'total': len(csv_files), 'success': 0, 'failed': 0}
    processed = 0
    
    with ThreadPoolExecutor(max_workers=max_workers) as executor:
        for name, success in executor.map(process_file, csv_files):
            processed += 1
            if success:
                results['success'] += 1
            else:
                results['failed'] += 1
            
            if processed % 200 == 0:
                print(f"   Processed {processed}/{len(csv_files)} files...")
    
    print(f"\n✅ Rebasing complete!")
    print(f"   Success: {results['success']}/{results['total']}")
    print(f"   Failed: {results['failed']}/{results['total']}")
    
    return results

def main():
    sites = {
        'elt1': 'c:\\naia3\\data\\wind_processed\\elt1',
        'blx1': 'c:\\naia3\\data\\wind_processed\\blx1',
    }
    
    print("╔════════════════════════════════════════════════════════════════╗")
    print("║  🕐 TIMESTAMP REBASEMENT UTILITY                              ║")
    print("║  Moves all CSV data to current time while preserving patterns  ║")
    print("╚════════════════════════════════════════════════════════════════╝")
    
    all_results = {}
    
    for site_name, input_path in sites.items():
        if not os.path.exists(input_path):
            print(f"\n⚠️  Directory not found: {input_path}")
            continue
        
        print(f"\n{'='*70}")
        print(f"Processing {site_name.upper()} data")
        print(f"{'='*70}")
        
        # Create output directory in same location with _rebased suffix
        output_path = input_path + '_rebased'
        
        results = rebase_directory(input_path, output_path)
        all_results[site_name] = (input_path, output_path, results)
    
    # Summary
    print(f"\n{'='*70}")
    print("FINAL SUMMARY")
    print(f"{'='*70}")
    
    for site_name, (in_path, out_path, results) in all_results.items():
        print(f"\n{site_name.upper()}:")
        print(f"  Input:  {in_path}")
        print(f"  Output: {out_path}")
        print(f"  Status: {results['success']}/{results['total']} files successfully rebased")
        
        if results['failed'] > 0:
            print(f"  ⚠️  {results['failed']} files failed!")

if __name__ == '__main__':
    main()
