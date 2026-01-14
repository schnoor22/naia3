#!/bin/bash
# Setup Solar Farms on Server
# Run this on the server after uploading ZIP files

set -e

echo "=========================================="
echo " Solar Farm Setup - Server Side"
echo "=========================================="

# Install unzip if not available
if ! command -v unzip &> /dev/null; then
    echo "Installing unzip..."
    apt-get update -qq && apt-get install -y unzip
fi

# Create directories
echo "Creating directories..."
mkdir -p /opt/naia/data/solar/pendleton
mkdir -p /opt/naia/data/solar/bluewater
mkdir -p /tmp/pnd1_extracted
mkdir -p /tmp/blw1_extracted

# Extract PND1
echo "Extracting Pendleton Solar data (this may take a few minutes)..."
cd ~
unzip -q PND1WTHR_WindDir_Daily_20260113_004715.zip -d /tmp/pnd1_extracted

# Extract BLW1
echo "Extracting Bluewater Solar data..."
unzip -q BLW1WTHR_WindSpd_Current_20260113_01269.zip -d /tmp/blw1_extracted

# Anonymize and copy PND1 files
echo "Processing Pendleton files..."
cd /tmp/pnd1_extracted
for file in *.csv; do
    # Remove timestamp suffix: PND1SGEN001_ING_20260113_002851.csv -> PND_SGEN001_ING.csv
    newname=$(echo "$file" | sed 's/PND1/PND_/' | sed 's/_[0-9]\{8\}_[0-9]\{5,6\}\.csv/.csv/')
    cp "$file" "/opt/naia/data/solar/pendleton/$newname"
done

# Anonymize and copy BLW1 files  
echo "Processing Bluewater files..."
cd /tmp/blw1_extracted
for file in *.csv; do
    newname=$(echo "$file" | sed 's/BLW1/BLW_/' | sed 's/_[0-9]\{8\}_[0-9]\{5,6\}\.csv/.csv/')
    cp "$file" "/opt/naia/data/solar/bluewater/$newname"
done

# Count files
PND_COUNT=$(ls /opt/naia/data/solar/pendleton/*.csv | wc -l)
BLW_COUNT=$(ls /opt/naia/data/solar/bluewater/*.csv | wc -l)

echo "=========================================="
echo " Files processed:"
echo "  Pendleton: $PND_COUNT files"
echo "  Bluewater: $BLW_COUNT files"
echo "=========================================="

# Generate SQL registration
echo "Generating SQL registration..."
cat > /tmp/register_solar_farms.sql << 'EOSQL'
-- Register Solar Farm Data Sources and Points
BEGIN;

-- Pendleton Solar Farm Data Source
INSERT INTO data_sources (id, name, source_type, is_enabled)
VALUES (
  'aaaaaaaa-1111-4e87-9b82-111111111111',
  'Pendleton Solar Farm',
  'GenericCsvReplay',
  true
)
ON CONFLICT (id) DO UPDATE SET
  name = EXCLUDED.name,
  source_type = EXCLUDED.source_type,
  is_enabled = EXCLUDED.is_enabled;

-- Bluewater Solar Farm Data Source
INSERT INTO data_sources (id, name, source_type, is_enabled)
VALUES (
  'bbbbbbbb-2222-4e87-9b82-222222222222',
  'Bluewater Solar Farm',
  'GenericCsvReplay',
  true
)
ON CONFLICT (id) DO UPDATE SET
  name = EXCLUDED.name,
  source_type = EXCLUDED.source_type,
  is_enabled = EXCLUDED.is_enabled;

-- Get next sequence ID
DO $$
DECLARE
  next_seq_id INTEGER;
  point_count INTEGER := 0;
BEGIN
  SELECT COALESCE(MAX(point_sequence_id), 99) + 1 INTO next_seq_id FROM points;
  RAISE NOTICE 'Starting sequence ID: %', next_seq_id;
  
  -- Register Pendleton points
EOSQL

# Add Pendleton points
cd /opt/naia/data/solar/pendleton
for file in *.csv; do
    pointname="${file%.csv}"
    cat >> /tmp/register_solar_farms.sql << EOSQL
  INSERT INTO points (name, point_sequence_id, data_source_id)
  VALUES ('$pointname', next_seq_id + point_count, 'aaaaaaaa-1111-4e87-9b82-111111111111')
  ON CONFLICT (name) DO UPDATE SET point_sequence_id = EXCLUDED.point_sequence_id, data_source_id = EXCLUDED.data_source_id;
  point_count := point_count + 1;
EOSQL
done

# Add Bluewater points
cd /opt/naia/data/solar/bluewater
for file in *.csv; do
    pointname="${file%.csv}"
    cat >> /tmp/register_solar_farms.sql << EOSQL
  INSERT INTO points (name, point_sequence_id, data_source_id)
  VALUES ('$pointname', next_seq_id + point_count, 'bbbbbbbb-2222-4e87-9b82-222222222222')
  ON CONFLICT (name) DO UPDATE SET point_sequence_id = EXCLUDED.point_sequence_id, data_source_id = EXCLUDED.data_source_id;
  point_count := point_count + 1;
EOSQL
done

# Close SQL
cat >> /tmp/register_solar_farms.sql << 'EOSQL'
  
  RAISE NOTICE 'Registered % total points', point_count;
END $$;

COMMIT;
EOSQL

# Execute SQL
echo "Registering points in PostgreSQL..."
cat /tmp/register_solar_farms.sql | docker exec -i naia-postgres psql -U naia -d naia

# Cleanup temp files
echo "Cleaning up..."
rm -rf /tmp/pnd1_extracted /tmp/blw1_extracted

echo "=========================================="
echo " Setup Complete!"
echo "  Total points registered: $(($PND_COUNT + $BLW_COUNT))"
echo "=========================================="
echo ""
echo "Next: Update ingestion config and restart services"
