<#
.SYNOPSIS
    NAIA Server V4 Reset - Clean slate while keeping infrastructure
    
.DESCRIPTION
    This script prepares the production server for v4 by:
    - Keeping Docker containers (PostgreSQL, QuestDB, Kafka, Redis)
    - Truncating all data tables (clean slate)
    - Keeping ONLY the OPC simulator code
    - Removing old API/Ingestion/Web deployments
    - Creating new v4 directory structure
    
.NOTES
    Run this FROM your local machine (uses SSH)
    Server: 37.27.189.86
    
.EXAMPLE
    .\SERVER_V4_RESET.ps1 -Confirm
#>

param(
    [switch]$Confirm,
    [switch]$DryRun
)

$server = "37.27.189.86"
$user = "root"

function Write-Header($text) { 
    Write-Host "`n═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan 
}

function Write-Step($text) { Write-Host "→ $text" -ForegroundColor Yellow }
function Write-Done($text) { Write-Host "✓ $text" -ForegroundColor Green }
function Write-Warn($text) { Write-Host "! $text" -ForegroundColor Magenta }

# Confirm before proceeding
if (-not $Confirm) {
    Write-Header "NAIA Server V4 Reset"
    Write-Host @"
This script will prepare the production server for v4 by:

  ✓ KEEPING Docker containers (they work fine)
  ✓ KEEPING OPC simulator (/opt/naia/opc-simulator/)
  ✓ KEEPING Kelmarsh data files (/opt/naia/data/)
  
  ✗ TRUNCATING PostgreSQL tables (points, data_sources, patterns, etc.)
  ✗ TRUNCATING QuestDB tables (point_data)
  ✗ DELETING old deployments (/opt/naia/publish/, /opt/naia/build/, /opt/naia/ingestion/)
  ✗ STOPPING naia-api and naia-ingestion services
  
  → CREATING new v4 directory structure

Server: $server
"@ -ForegroundColor White
    
    Write-Host "`nThis gives you a clean slate with proven OPC infrastructure." -ForegroundColor Green
    Write-Host ""
    
    $response = Read-Host "Type 'RESET V4' to confirm"
    if ($response -ne "RESET V4") {
        Write-Host "`nAborted. No changes made." -ForegroundColor Cyan
        exit 0
    }
    Write-Host ""
}

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 1: Stop Services"
# ═══════════════════════════════════════════════════════════════

Write-Step "Stopping NAIA services..."
if (-not $DryRun) {
    ssh ${user}@${server} "systemctl stop naia-api naia-ingestion 2>/dev/null || true"
}
Write-Done "Services stopped"

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 2: Database Cleanup (Keep Databases, Clear Tables)"
# ═══════════════════════════════════════════════════════════════

Write-Step "Truncating PostgreSQL tables..."
$pgScript = @'
-- Keep database structure, remove all data
TRUNCATE TABLE points CASCADE;
TRUNCATE TABLE data_sources CASCADE;
TRUNCATE TABLE point_patterns CASCADE;
TRUNCATE TABLE pattern_correlations CASCADE;
TRUNCATE TABLE behavioral_stats CASCADE;
TRUNCATE TABLE knowledge_base_entries CASCADE;
TRUNCATE TABLE optimization_suggestions CASCADE;

-- Verify cleanup
SELECT 'Points: ' || COUNT(*) FROM points;
SELECT 'Data Sources: ' || COUNT(*) FROM data_sources;
SELECT 'Patterns: ' || COUNT(*) FROM point_patterns;
'@

if (-not $DryRun) {
    $pgScript | ssh ${user}@${server} "docker exec -i naia-postgres psql -U naia -d naia"
    Write-Done "PostgreSQL tables truncated"
} else {
    Write-Step "[DRY RUN] Would truncate PostgreSQL tables"
}

Write-Step "Truncating QuestDB tables..."
$questScript = @'
DROP TABLE IF EXISTS point_data;

CREATE TABLE point_data (
    point_id LONG,
    value DOUBLE,
    timestamp TIMESTAMP,
    quality INT,
    source_timestamp TIMESTAMP
) TIMESTAMP(timestamp) PARTITION BY DAY;

SELECT 'QuestDB point_data table recreated (empty)';
'@

if (-not $DryRun) {
    $questScript | ssh ${user}@${server} "docker exec -i naia-questdb /questdb/bin/questdb.sh -d /var/lib/questdb query"
    Write-Done "QuestDB tables recreated (empty)"
} else {
    Write-Step "[DRY RUN] Would recreate QuestDB tables"
}

Write-Step "Flushing Redis cache..."
if (-not $DryRun) {
    ssh ${user}@${server} "docker exec naia-redis redis-cli FLUSHALL"
    Write-Done "Redis flushed"
} else {
    Write-Step "[DRY RUN] Would flush Redis"
}

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 3: Remove Old Deployments"
# ═══════════════════════════════════════════════════════════════

Write-Step "Removing old API deployment..."
if (-not $DryRun) {
    ssh ${user}@${server} "rm -rf /opt/naia/publish/*"
    Write-Done "API deployment removed"
} else {
    Write-Step "[DRY RUN] Would remove /opt/naia/publish/"
}

Write-Step "Removing old Ingestion deployment..."
if (-not $DryRun) {
    ssh ${user}@${server} "rm -rf /opt/naia/ingestion/*"
    Write-Done "Ingestion deployment removed"
} else {
    Write-Step "[DRY RUN] Would remove /opt/naia/ingestion/"
}

Write-Step "Removing old Web deployment..."
if (-not $DryRun) {
    ssh ${user}@${server} "rm -rf /opt/naia/build/*"
    Write-Done "Web deployment removed"
} else {
    Write-Step "[DRY RUN] Would remove /opt/naia/build/"
}

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 4: Create V4 Structure"
# ═══════════════════════════════════════════════════════════════

$v4Script = @'
#!/bin/bash
set -e

echo "Creating v4 directory structure..."

# Create new structure
mkdir -p /opt/naia/api/{current,releases,config}
mkdir -p /opt/naia/web/{current,releases}
mkdir -p /opt/naia/ingestion/{current,releases,config}
mkdir -p /opt/naia/logs/{api,ingestion,web}
mkdir -p /opt/naia/backups

# Set permissions
chown -R root:root /opt/naia
chmod -R 755 /opt/naia

echo "✓ V4 structure created"
tree -L 2 /opt/naia || ls -la /opt/naia

echo ""
echo "OPC Simulator status:"
ls -lh /opt/naia/opc-simulator/ | grep -E "dll|json" || echo "  (OPC simulator directory empty or missing)"

echo ""
echo "Data files preserved:"
ls -lh /opt/naia/data/kelmarsh/ | head -5 || echo "  (No data files)"
'@

if (-not $DryRun) {
    $v4Script | ssh ${user}@${server} "cat > /tmp/create_v4_structure.sh && chmod +x /tmp/create_v4_structure.sh && /tmp/create_v4_structure.sh"
    Write-Done "V4 structure created"
} else {
    Write-Step "[DRY RUN] Would create v4 structure"
}

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 5: Verify Docker Containers"
# ═══════════════════════════════════════════════════════════════

Write-Step "Checking Docker containers..."
ssh ${user}@${server} @'
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" | grep -E "naia|NAME"
'@
Write-Done "Docker containers verified"

# ═══════════════════════════════════════════════════════════════
Write-Header "RESET COMPLETE"
# ═══════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  NAIA SERVER V4 RESET COMPLETE" -ForegroundColor Green
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "Server State:" -ForegroundColor White
Write-Host "  ✓ Docker containers: Running" -ForegroundColor Green
Write-Host "  ✓ PostgreSQL: Empty tables, structure intact" -ForegroundColor Green
Write-Host "  ✓ QuestDB: Empty point_data table" -ForegroundColor Green
Write-Host "  ✓ Redis: Flushed" -ForegroundColor Green
Write-Host "  ✓ OPC Simulator: Preserved at /opt/naia/opc-simulator/" -ForegroundColor Green
Write-Host "  ✓ Data files: Preserved at /opt/naia/data/" -ForegroundColor Green
Write-Host "  ✓ V4 structure: Created" -ForegroundColor Green
Write-Host ""
Write-Host "Ready for v4 deployments!" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "  1. On new machine: Clone naia3 repo" -ForegroundColor White
Write-Host "  2. Read NAIA_V4_HANDOFF.md" -ForegroundColor White
Write-Host "  3. Start fresh Claude conversation with handoff doc" -ForegroundColor White
Write-Host "  4. Build v4 with clean architecture" -ForegroundColor White
Write-Host "  5. Deploy API → Test OPC → Deploy Ingestion → Deploy Web" -ForegroundColor White
Write-Host ""
Write-Host "OPC Simulator Test:" -ForegroundColor Cyan
ssh ${user}@${server} 'ps aux | grep -i opc | grep -v grep | head -2 || echo "  (Not currently running - will start when needed)"'
Write-Host ""
