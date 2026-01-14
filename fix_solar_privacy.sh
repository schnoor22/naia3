#!/bin/bash
set -e

echo "=========================================="
echo "ANONYMIZING SOLAR FARM DATA"
echo "=========================================="
echo ""

# Step 1: Fix NULL data_source_id points (the 8 old test points)
echo "Step 1: Fixing NULL data_source_id points..."
docker exec -i naia-postgres psql -U naia -d naia << 'SQL'
-- Delete the 8 old test points with NULL data_source_id
DELETE FROM points WHERE data_source_id IS NULL;
SQL
echo "✓ Removed 8 orphaned test points"
echo ""

# Step 2: Update data source names in database
echo "Step 2: Updating data source names..."
docker exec -i naia-postgres psql -U naia -d naia << 'SQL'
-- Rename data sources
UPDATE data_sources 
SET name = 'Alameda Solar Farm' 
WHERE name = 'Pendleton Solar Farm';

UPDATE data_sources 
SET name = 'Sunbake Solar Farm' 
WHERE name = 'Bluewater Solar Farm';

-- Verify
SELECT id, name FROM data_sources WHERE name LIKE '%Solar%';
SQL
echo "✓ Updated data source names"
echo ""

# Step 3: Rename all point names in database
echo "Step 3: Renaming point tags in database..."
docker exec -i naia-postgres psql -U naia -d naia << 'SQL'
-- Rename PND_ to ALM1_
UPDATE points 
SET name = REPLACE(name, 'PND_', 'ALM1_') 
WHERE name LIKE 'PND_%';

-- Rename BLW_ to SBK1_
UPDATE points 
SET name = REPLACE(name, 'BLW_', 'SBK1_') 
WHERE name LIKE 'BLW_%';

-- Also anonymize some common suffixes to be less identifiable
UPDATE points 
SET name = REPLACE(name, '_ING_', '_') 
WHERE name LIKE '%_ING_%';

UPDATE points 
SET name = REPLACE(name, '_GATEWAY_', '_GW_') 
WHERE name LIKE '%_GATEWAY_%';

UPDATE points 
SET name = REPLACE(name, 'SGEN', 'INV') 
WHERE name LIKE '%SGEN%';

UPDATE points 
SET name = REPLACE(name, 'SGENInverters', 'Inverters') 
WHERE name LIKE '%SGENInverters%';

-- Show sample of renamed points
SELECT name FROM points WHERE name LIKE 'ALM1_%' LIMIT 10;
SELECT COUNT(*) as alm1_count FROM points WHERE name LIKE 'ALM1_%';
SELECT COUNT(*) as sbk1_count FROM points WHERE name LIKE 'SBK1_%';
SQL
echo "✓ Renamed all point tags"
echo ""

# Step 4: Rename CSV files on disk
echo "Step 4: Renaming CSV files..."
cd /opt/naia/data/solar/pendleton
echo "  Renaming Pendleton files to ALM1..."
for file in PND_*.csv; do
    if [ -f "$file" ]; then
        # PND_ → ALM1_, _ING_ → _, SGEN → INV, _GATEWAY_ → _GW_
        newname=$(echo "$file" | sed 's/PND_/ALM1_/' | sed 's/_ING_/_/g' | sed 's/SGEN/INV/g' | sed 's/_GATEWAY_/_GW_/g' | sed 's/SGENInverters/Inverters/g')
        mv "$file" "$newname" 2>/dev/null || true
    fi
done
file_count=$(ls ALM1_*.csv 2>/dev/null | wc -l)
echo "  ✓ Renamed $file_count Pendleton/Alameda files"

cd /opt/naia/data/solar/bluewater
echo "  Renaming Bluewater files to SBK1..."
for file in BLW_*.csv; do
    if [ -f "$file" ]; then
        # BLW_ → SBK1_, _ING_ → _, SGEN → INV, _GATEWAY_ → _GW_
        newname=$(echo "$file" | sed 's/BLW_/SBK1_/' | sed 's/_ING_/_/g' | sed 's/SGEN/INV/g' | sed 's/_GATEWAY_/_GW_/g')
        mv "$file" "$newname" 2>/dev/null || true
    fi
done
file_count=$(ls SBK1_*.csv 2>/dev/null | wc -l)
echo "  ✓ Renamed $file_count Bluewater/Sunbake files"
echo ""

# Step 5: Update appsettings.json
echo "Step 5: Updating configuration..."
cat > /opt/naia/publish-ingestion/appsettings.json << 'JSONEOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information",
      "Naia.Connectors": "Debug"
    }
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092"
  },
  "PIWebApi": {
    "Enabled": false
  },
  "WindFarmReplay": {
    "Enabled": true,
    "AutoStart": true,
    "DataDirectory": "/opt/naia/data/kelmarsh/scada_2019",
    "SiteCode": "KSH",
    "SiteName": "Kelmarsh Wind Farm",
    "TurbineCount": 6,
    "SpeedMultiplier": 60.0,
    "DataIntervalMinutes": 10,
    "BatchSize": 1000,
    "LoopReplay": true,
    "SkipNaN": true,
    "DataYears": [ 2019 ],
    "IncludedReadings": [
      "WindSpeed", "Power", "WindDirection", "RotorRPM", "GeneratorRPM",
      "PitchA", "PitchB", "PitchC", "NacelleTemp", "GearOilTemp",
      "GenBearingFrontTemp", "GenBearingRearTemp", "AmbientTemp",
      "GridVoltage", "GridFrequency"
    ],
    "KafkaTopic": "naia.datapoints"
  },
  "Connectors": {
    "GenericCsvReplay": {
      "Enabled": true,
      "AutoDiscoverFiles": true,
      "SpeedMultiplier": 2.0,
      "LoopReplay": true,
      "ShiftToCurrentTime": true,
      "UseTimeOfDayMatching": true,
      "PublishIntervalSeconds": 60,
      "KafkaTopic": "naia.datapoints",
      "Sites": [
        {
          "SiteId": "aaaaaaaa-1111-4e87-9b82-111111111111",
          "SiteName": "Alameda Solar Farm",
          "DataDirectory": "/opt/naia/data/solar/pendleton",
          "TagPrefix": "ALM1_"
        },
        {
          "SiteId": "bbbbbbbb-2222-4e87-9b82-222222222222",
          "SiteName": "Sunbake Solar Farm",
          "DataDirectory": "/opt/naia/data/solar/bluewater",
          "TagPrefix": "SBK1_"
        }
      ]
    }
  }
}
JSONEOF
echo "✓ Updated configuration"
echo ""

# Step 6: Restart services
echo "Step 6: Restarting services..."
systemctl restart naia-api
sleep 3
systemctl restart naia-ingestion
sleep 2
echo "✓ Services restarted"
echo ""

echo "=========================================="
echo "ANONYMIZATION COMPLETE!"
echo "=========================================="
echo ""
echo "Summary:"
docker exec -i naia-postgres psql -U naia -d naia << 'SQL'
SELECT 
    ds.name as data_source,
    COUNT(p.id) as point_count,
    MIN(p.name) as sample_tag
FROM data_sources ds
LEFT JOIN points p ON p.data_source_id = ds.id
WHERE ds.name LIKE '%Solar%'
GROUP BY ds.name
ORDER BY ds.name;
SQL
echo ""
echo "All references to PND1/Pendleton and BLW1/Bluewater removed!"
echo "New prefixes: ALM1_ (Alameda Solar) and SBK1_ (Sunbake Solar)"
