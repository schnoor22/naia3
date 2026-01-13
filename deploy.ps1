# NAIA Deployment Script
# Run this on the production server to update and restart services

param(
    [Parameter(HelpMessage="API to publish")]
    [ValidateSet("api", "ingestion", "all")]
    [string]$Target = "api"
)

# Configuration
$SourceDir = "/opt/naia"
$PublishDir = "$SourceDir/publish"

Write-Host "╔════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║ NAIA Deployment Script                 ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════╝" -ForegroundColor Cyan

try {
    # Stop services
    Write-Host "`n[1/5] Stopping services..." -ForegroundColor Yellow
    systemctl stop naia-api 2>/dev/null || true
    systemctl stop naia-ingestion 2>/dev/null || true
    sleep 2

    # Navigate to source
    Write-Host "[2/5] Updating source code..." -ForegroundColor Yellow
    cd $SourceDir

    # Clean build
    Write-Host "[3/5] Cleaning and publishing ($Target)..." -ForegroundColor Yellow
    if ($Target -eq "api" -or $Target -eq "all") {
        dotnet publish src/Naia.Api/Naia.Api.csproj -c Release -o $PublishDir
    }
    if ($Target -eq "ingestion" -or $Target -eq "all") {
        dotnet publish src/Naia.Ingestion/Naia.Ingestion.csproj -c Release -o $PublishDir
    }

    # Restart services
    Write-Host "[4/5] Restarting services..." -ForegroundColor Yellow
    systemctl start naia-api
    if ($Target -eq "ingestion" -or $Target -eq "all") {
        systemctl start naia-ingestion
    }
    sleep 3

    # Verify
    Write-Host "[5/5] Verifying health..." -ForegroundColor Yellow
    
    # Check systemd status
    $apiStatus = systemctl is-active naia-api
    Write-Host "API Status: $apiStatus" -ForegroundColor $(if ($apiStatus -eq "active") { "Green" } else { "Red" })
    
    # Check health endpoint
    $health = curl -s http://localhost:5000/api/health 2>/dev/null
    if ($health) {
        Write-Host "API Health: OK" -ForegroundColor Green
        Write-Host "Response: $health" -ForegroundColor Green
    } else {
        Write-Host "API Health: FAILED" -ForegroundColor Red
        Write-Host "Logs:" -ForegroundColor Yellow
        journalctl -u naia-api -n 30 --no-pager
    }

    Write-Host "`n✅ Deployment complete!" -ForegroundColor Green

} catch {
    Write-Host "`n❌ Deployment failed: $_" -ForegroundColor Red
    exit 1
}
