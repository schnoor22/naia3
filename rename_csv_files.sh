#!/bin/bash

echo "Renaming Pendleton/Alameda CSV files..."
cd /opt/naia/data/solar/pendleton || exit 1

# Use rename command (perl-based) if available, otherwise use mv
if command -v rename &> /dev/null; then
    rename 's/PND_/ALM1_/' *.csv
    rename 's/_ING_/_/g' *.csv
    rename 's/SGEN/INV/g' *.csv
    rename 's/_GATEWAY_/_GW_/' *.csv
else
    # Fallback to mv in small batches
    for file in PND_*.csv; do
        [ -f "$file" ] || continue
        new=$(echo "$file" | sed 's/PND_/ALM1_/' | sed 's/_ING_/_/g' | sed 's/SGEN/INV/g' | sed 's/_GATEWAY_/_GW_/')
        mv "$file" "$new"
    done
fi

echo "Done with Pendleton. Count: $(ls ALM1_*.csv 2>/dev/null | wc -l)"

echo "Renaming Bluewater/Sunbake CSV files..."
cd /opt/naia/data/solar/bluewater || exit 1

if command -v rename &> /dev/null; then
    rename 's/BLW_/SBK1_/' *.csv
    rename 's/_ING_/_/g' *.csv
    rename 's/SGEN/INV/g' *.csv
    rename 's/_GATEWAY_/_GW_/' *.csv
else
    for file in BLW_*.csv; do
        [ -f "$file" ] || continue
        new=$(echo "$file" | sed 's/BLW_/SBK1_/' | sed 's/_ING_/_/g' | sed 's/SGEN/INV/g' | sed 's/_GATEWAY_/_GW_/')
        mv "$file" "$new"
    done
fi

echo "Done with Bluewater. Count: $(ls SBK1_*.csv 2>/dev/null | wc -l)"
echo "File renaming complete!"
