"""
Complete Solar System Setup
Processes PND1 (Pendleton) and BLW1 (Bluewater) solar farm data

Steps:
1. Anonymize CSV files (remove company references)
2. Create SQL registration script for ALL points
3. Upload to server
"""

import os
import shutil
import csv
from pathlib import Path
from datetime import datetime

# Configuration
PND1_SOURCE = r"C:\naia3\data\extracted\PND1"
BLW1_SOURCE = r"C:\naia3\data\extracted\BLW1"
OUTPUT_DIR = r"C:\naia3\data\solar_complete"
SQL_OUTPUT = r"C:\naia3\register_all_solar_points.sql"

# Site configs
SITES = {
    "PND1": {
        "display_name": "Pendleton Solar Farm",
        "output_dir": f"{OUTPUT_DIR}/pendleton",
        "prefix": "PND_",
        "anonymize": True  # Strip company names
    },
    "BLW1": {
        "display_name": "Bluewater Solar Farm",
        "output_dir": f"{OUTPUT_DIR}/bluewater",
        "prefix": "BLW_",
        "anonymize": True
    }
}

def anonymize_filename(filename, site_key):
    """Convert PND1SGEN001_ING_ActivePower_20260113_002851.csv -> PND_SGEN001_ING_ActivePower.csv"""
    # Remove timestamp suffix
    name = filename.replace('.csv', '')
    parts = name.split('_')
    
    # Remove timestamp parts (last 2 items)
    if len(parts) >= 2:
        # Check if last part looks like timestamp (6 digits)
        if parts[-1].isdigit() and len(parts[-1]) in [5, 6]:
            parts = parts[:-1]
        # Check if second-to-last part looks like date (8 digits)
        if parts[-1].isdigit() and len(parts[-1]) == 8:
            parts = parts[:-1]
    
    # Replace site prefix
    cleaned_name = '_'.join(parts)
    cleaned_name = cleaned_name.replace(site_key, SITES[site_key]['prefix'])
    
    return f"{cleaned_name}.csv"

def process_site(site_key, source_dir):
    """Process all CSV files for a site"""
    site_config = SITES[site_key]
    output_dir = site_config['output_dir']
    
    print(f"\n{'='*70}")
    print(f"Processing {site_config['display_name']}")
    print(f"Source: {source_dir}")
    print(f"Output: {output_dir}")
    print(f"{'='*70}")
    
    # Create output directory
    os.makedirs(output_dir, exist_ok=True)
    
    # Get all CSV files
    csv_files = list(Path(source_dir).glob("*.csv"))
    print(f"Found {len(csv_files)} CSV files")
    
    point_list = []
    processed = 0
    
    for csv_file in csv_files:
        try:
            # Generate output filename
            output_name = anonymize_filename(csv_file.name, site_key)
            output_path = Path(output_dir) / output_name
            
            # Copy file (content is already anonymous - just Timestamp,Value,Status)
            shutil.copy2(csv_file, output_path)
            
            # Extract point name (filename without .csv)
            point_name = output_name.replace('.csv', '')
            point_list.append(point_name)
            
            processed += 1
            if processed % 100 == 0:
                print(f"  Processed {processed}/{len(csv_files)}...")
                
        except Exception as e:
            print(f"  ERROR processing {csv_file.name}: {e}")
    
    print(f"✓ Completed: {processed} files processed")
    return point_list

def generate_sql_registration(all_points):
    """Generate SQL script to register all points"""
    print(f"\n{'='*70}")
    print("Generating SQL Registration Script")
    print(f"{'='*70}")
    
    sql_lines = []
    sql_lines.append("-- Register Solar Farm Data Sources and Points")
    sql_lines.append("-- Generated: " + datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
    sql_lines.append("")
    
    # Create data source for Pendleton
    sql_lines.append("-- Pendleton Solar Farm Data Source")
    sql_lines.append("INSERT INTO data_sources (id, name, source_type, is_enabled, config)")
    sql_lines.append("VALUES (")
    sql_lines.append("  'aaaaaaaa-1111-4e87-9b82-111111111111',")
    sql_lines.append("  'Pendleton Solar Farm',")
    sql_lines.append("  'GenericCsvReplay',")
    sql_lines.append("  true,")
    sql_lines.append("  '{}'::jsonb")
    sql_lines.append(")")
    sql_lines.append("ON CONFLICT (id) DO UPDATE SET")
    sql_lines.append("  name = EXCLUDED.name,")
    sql_lines.append("  source_type = EXCLUDED.source_type,")
    sql_lines.append("  is_enabled = EXCLUDED.is_enabled;")
    sql_lines.append("")
    
    # Create data source for Bluewater
    sql_lines.append("-- Bluewater Solar Farm Data Source")
    sql_lines.append("INSERT INTO data_sources (id, name, source_type, is_enabled, config)")
    sql_lines.append("VALUES (")
    sql_lines.append("  'bbbbbbbb-2222-4e87-9b82-222222222222',")
    sql_lines.append("  'Bluewater Solar Farm',")
    sql_lines.append("  'GenericCsvReplay',")
    sql_lines.append("  true,")
    sql_lines.append("  '{}'::jsonb")
    sql_lines.append(")")
    sql_lines.append("ON CONFLICT (id) DO UPDATE SET")
    sql_lines.append("  name = EXCLUDED.name,")
    sql_lines.append("  source_type = EXCLUDED.source_type,")
    sql_lines.append("  is_enabled = EXCLUDED.is_enabled;")
    sql_lines.append("")
    
    # Get starting sequence ID
    sql_lines.append("-- Get next available point_sequence_id")
    sql_lines.append("DO $$")
    sql_lines.append("DECLARE")
    sql_lines.append("  next_seq_id INTEGER;")
    sql_lines.append("  pnd_count INTEGER := 0;")
    sql_lines.append("  blw_count INTEGER := 0;")
    sql_lines.append("BEGIN")
    sql_lines.append("  -- Get next sequence ID")
    sql_lines.append("  SELECT COALESCE(MAX(point_sequence_id), 99) + 1 INTO next_seq_id FROM points;")
    sql_lines.append("")
    
    # Register Pendleton points
    pnd_points = [p for p in all_points if p.startswith('PND_')]
    blw_points = [p for p in all_points if p.startswith('BLW_')]
    
    sql_lines.append(f"  -- Register {len(pnd_points)} Pendleton Solar Farm points")
    for point_name in pnd_points:
        sql_lines.append(f"  INSERT INTO points (name, point_sequence_id, data_source_id, unit, description)")
        sql_lines.append(f"  VALUES (")
        sql_lines.append(f"    '{point_name}',")
        sql_lines.append(f"    next_seq_id + pnd_count,")
        sql_lines.append(f"    'aaaaaaaa-1111-4e87-9b82-111111111111',")
        sql_lines.append(f"    'kW',")
        sql_lines.append(f"    'Pendleton - {point_name}'")
        sql_lines.append(f"  )")
        sql_lines.append(f"  ON CONFLICT (name) DO UPDATE SET")
        sql_lines.append(f"    point_sequence_id = EXCLUDED.point_sequence_id,")
        sql_lines.append(f"    data_source_id = EXCLUDED.data_source_id;")
        sql_lines.append(f"  pnd_count := pnd_count + 1;")
        sql_lines.append("")
    
    sql_lines.append(f"  -- Register {len(blw_points)} Bluewater Solar Farm points")
    for point_name in blw_points:
        sql_lines.append(f"  INSERT INTO points (name, point_sequence_id, data_source_id, unit, description)")
        sql_lines.append(f"  VALUES (")
        sql_lines.append(f"    '{point_name}',")
        sql_lines.append(f"    next_seq_id + pnd_count + blw_count,")
        sql_lines.append(f"    'bbbbbbbb-2222-4e87-9b82-222222222222',")
        sql_lines.append(f"    'kW',")
        sql_lines.append(f"    'Bluewater - {point_name}'")
        sql_lines.append(f"  )")
        sql_lines.append(f"  ON CONFLICT (name) DO UPDATE SET")
        sql_lines.append(f"    point_sequence_id = EXCLUDED.point_sequence_id,")
        sql_lines.append(f"    data_source_id = EXCLUDED.data_source_id;")
        sql_lines.append(f"  blw_count := blw_count + 1;")
        sql_lines.append("")
    
    sql_lines.append("  RAISE NOTICE 'Registered % Pendleton points and % Bluewater points', pnd_count, blw_count;")
    sql_lines.append("END $$;")
    
    # Write SQL file
    with open(SQL_OUTPUT, 'w') as f:
        f.write('\n'.join(sql_lines))
    
    print(f"✓ SQL script written to: {SQL_OUTPUT}")
    print(f"  - Pendleton points: {len(pnd_points)}")
    print(f"  - Bluewater points: {len(blw_points)}")
    print(f"  - Total points: {len(all_points)}")

def main():
    print("="*70)
    print(" Complete Solar System Setup")
    print("="*70)
    
    all_points = []
    
    # Process PND1
    pnd_points = process_site("PND1", PND1_SOURCE)
    all_points.extend(pnd_points)
    
    # Process BLW1
    blw_points = process_site("BLW1", BLW1_SOURCE)
    all_points.extend(blw_points)
    
    # Generate SQL
    generate_sql_registration(all_points)
    
    print("\n" + "="*70)
    print(" SUMMARY")
    print("="*70)
    print(f"Total CSV files processed: {len(all_points)}")
    print(f"Pendleton Solar Farm: {len([p for p in all_points if p.startswith('PND_')])}")
    print(f"Bluewater Solar Farm: {len([p for p in all_points if p.startswith('BLW_')])}")
    print(f"\nNext steps:")
    print(f"1. Upload data: scp -r {OUTPUT_DIR} root@37.27.189.86:/opt/naia/data/")
    print(f"2. Upload SQL: scp {SQL_OUTPUT} root@37.27.189.86:~/")
    print(f"3. Run SQL: ssh root@37.27.189.86 'cat ~/register_all_solar_points.sql | docker exec -i naia-postgres psql -U naia -d naia'")
    print("="*70)

if __name__ == "__main__":
    main()
