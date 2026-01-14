#!/bin/bash

echo "===== ANONYMIZATION VERIFICATION ====="
echo ""

echo "Data Sources:"
docker exec naia-postgres psql -U naia -d naia -c "SELECT name FROM data_sources WHERE name LIKE '%Solar%';"
echo ""

echo "Point Counts by Prefix:"
docker exec naia-postgres psql -U naia -d naia -c "
SELECT 
    CASE 
        WHEN name LIKE 'ALM1_%' THEN 'ALM1 (Alameda)'
        WHEN name LIKE 'SBK1_%' THEN 'SBK1 (Sunbake)'
        WHEN name LIKE 'KSH_%' THEN 'KSH (Kelmarsh)'
        ELSE 'Other'
    END as prefix,
    COUNT(*) as count
FROM points 
GROUP BY prefix
ORDER BY count DESC;
"
echo ""

echo "Checking for proprietary names (should be 0):"
docker exec naia-postgres psql -U naia -d naia -t -c "SELECT COUNT(*) FROM points WHERE name ~* 'pnd|blw|pendleton|bluewater';"
echo ""

echo "Sample ALM1 tags:"
docker exec naia-postgres psql -U naia -d naia -c "SELECT name FROM points WHERE name LIKE 'ALM1_%' ORDER BY name LIMIT 5;"
echo ""

echo "Sample SBK1 tags:"
docker exec naia-postgres psql -U naia -d naia -c "SELECT name FROM points WHERE name LIKE 'SBK1_%' ORDER BY name LIMIT 5;"
echo ""

echo "CSV File Samples:"
echo "Alameda: $(ls /opt/naia/data/solar/pendleton/ALM1_*.csv 2>/dev/null | head -3 | xargs -n1 basename)"
echo "Sunbake: $(ls /opt/naia/data/solar/bluewater/SBK1_*.csv 2>/dev/null | head -3 | xargs -n1 basename)"
