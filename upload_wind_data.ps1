# PowerShell equivalent of upload_wind_data.sh
# Uploads rebased wind data to server via SCP

param(
    [string]$Host = "37.27.189.86",
    [string]$User = "root",
    [string]$RemoteDataDir = "/opt/naia/data/wind",
    [int]$MaxThreads = 4
)

$ErrorActionPreference = "Stop"

Write-Host "╔════════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  📤 WIND DATA UPLOAD UTILITY (PowerShell)                         ║" -ForegroundColor Cyan
Write-Host "║  Uploads rebased ELT1 & BLX1 data via SCP                         ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

# Verify rebased directories exist
$sites = @(
    @{ name = "elt1"; path = "c:\naia3\data\wind_processed\elt1_rebased" },
    @{ name = "blx1"; path = "c:\naia3\data\wind_processed\blx1_rebased" }
)

foreach ($site in $sites) {
    if (-not (Test-Path $site.path)) {
        Write-Host "ERROR: $($site.name) rebased directory not found: $($site.path)" -ForegroundColor Red
        Write-Host "Run rebase_timestamps.py first!" -ForegroundColor Yellow
        exit 1
    }
    
    $count = @(Get-ChildItem $site.path -Filter "*.csv" -ErrorAction SilentlyContinue).Count
    Write-Host "✓ $($site.name.ToUpper()): $count CSV files ready for upload" -ForegroundColor Green
}

# Create remote directories
Write-Host "`n📂 Creating remote directories..." -ForegroundColor Cyan
foreach ($site in $sites) {
    ssh -n "$User@$Host" "mkdir -p $RemoteDataDir/$($site.name)" 2>$null
    Write-Host "  ✓ Created $RemoteDataDir/$($site.name)" -ForegroundColor Green
}

# Upload each site's data
foreach ($site in $sites) {
    Write-Host "`n⏳ Uploading $($site.name.ToUpper()) data..." -ForegroundColor Cyan
    
    $csvFiles = Get-ChildItem $site.path -Filter "*.csv"
    $total = $csvFiles.Count
    $completed = 0
    
    foreach ($file in $csvFiles) {
        $completed++
        
        # Progress every 100 files
        if ($completed % 100 -eq 0) {
            Write-Host "  Progress: $completed/$total files uploaded" -ForegroundColor Gray
        }
        
        # Upload file
        scp -q "$($file.FullName)" "$User@$Host`:$RemoteDataDir/$($site.name)/"
    }
    
    Write-Host "  ✓ Uploaded $total CSV files for $($site.name.ToUpper())" -ForegroundColor Green
}

# Set permissions on remote
Write-Host "`n🔐 Setting remote permissions..." -ForegroundColor Cyan
ssh -n "$User@$Host" @"
    chown -R naia:naia $RemoteDataDir/
    chmod -R u+rw $RemoteDataDir/
    echo "Remote permissions set"
"@
Write-Host "  ✓ Permissions configured" -ForegroundColor Green

# Verify upload
Write-Host "`n🔍 Verifying upload..." -ForegroundColor Cyan
foreach ($site in $sites) {
    $remoteCount = ssh -n "$User@$Host" "find $RemoteDataDir/$($site.name) -type f -name '*.csv' | wc -l"
    $localCount = @(Get-ChildItem $site.path -Filter "*.csv").Count
    
    if ([int]$remoteCount -eq [int]$localCount) {
        Write-Host "  ✓ $($site.name.ToUpper()): $remoteCount files verified on server" -ForegroundColor Green
    } else {
        Write-Host "  ⚠️  $($site.name.ToUpper()): Mismatch! Local=$localCount Remote=$remoteCount" -ForegroundColor Yellow
    }
}

Write-Host "`n✅ Upload complete!" -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Verify data in /opt/naia/data/wind/ on server" -ForegroundColor Gray
Write-Host "  2. Register ELT1 and BLX1 as data sources in database" -ForegroundColor Gray
Write-Host "  3. Run: python cleanup_local_wind_files.py" -ForegroundColor Gray
