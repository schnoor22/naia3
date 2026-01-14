#!/bin/bash
set -e

SERVER="root@naia"
REMOTE_WIND_PATH="/opt/naia/data/wind"

echo "[WIND] Uploading wind site CSV data to $SERVER"
echo "====================================================================="

# Create remote directories
echo "[DIR] Creating remote directories..."
ssh $SERVER "mkdir -p $REMOTE_WIND_PATH/{elt1,blx1} && chown -R naia:naia $REMOTE_WIND_PATH && chmod -R u+w $REMOTE_WIND_PATH"

# Upload ELT1
if [ -d "data/wind_processed/elt1" ]; then
    COUNT=$(ls -1 data/wind_processed/elt1/*.csv 2>/dev/null | wc -l)
    if [ "$COUNT" -gt 0 ]; then
        echo ""
        echo "[UP] Uploading ELT1 (El Toro Wind) - $COUNT files..."
        time scp -r data/wind_processed/elt1/*.csv $SERVER:$REMOTE_WIND_PATH/elt1/
        echo "[OK] ELT1 upload complete"
    fi
fi

# Upload BLX1
if [ -d "data/wind_processed/blx1" ]; then
    COUNT=$(ls -1 data/wind_processed/blx1/*.csv 2>/dev/null | wc -l)
    if [ "$COUNT" -gt 0 ]; then
        echo ""
        echo "[UP] Uploading BLX1 (Blixton Wind) - $COUNT files..."
        time scp -r data/wind_processed/blx1/*.csv $SERVER:$REMOTE_WIND_PATH/blx1/
        echo "[OK] BLX1 upload complete"
    fi
fi

# Verify
echo ""
echo "[CHK] Verifying uploaded files..."
ssh $SERVER "du -sh $REMOTE_WIND_PATH/*"

echo ""
echo "====================================================================="
echo "[OK] Wind site data upload complete!"
echo ""
echo "Uploaded: ELT1 and BLX1 wind farm CSV files"
echo "Location: $REMOTE_WIND_PATH/"
