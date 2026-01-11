# =======================================================================
# NAIA Process Cleanup Script
# Gracefully kills zombie processes and frees up ports
# =======================================================================

Write-Host "=== NAIA Process Cleanup ===" -ForegroundColor Cyan
Write-Host ""

# Kill all dotnet processes
Write-Host "Killing dotnet processes..." -ForegroundColor Yellow
Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "OK - Dotnet processes stopped" -ForegroundColor Green

# Kill orphaned node processes
Write-Host "Killing node processes..." -ForegroundColor Yellow
Get-Process node -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "OK - Node processes stopped" -ForegroundColor Green

# Kill escape-node-job if it exists
Write-Host "Killing escape-node-job..." -ForegroundColor Yellow
Get-Process escape-node-job -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "OK - Escape-node-job stopped" -ForegroundColor Green

# Wait for ports to be released
Write-Host "Waiting for ports to be released..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

# Check if port 5052 is now free
Write-Host ""
Write-Host "Checking port availability..." -ForegroundColor Yellow
$port5052 = netstat -ano 2>$null | findstr ":5052"
if ($port5052) {
    Write-Host "WARNING - Port 5052 still in use" -ForegroundColor Red
    Write-Host $port5052
} else {
    Write-Host "OK - Port 5052 is free" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Cleanup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "You can now start the API:" -ForegroundColor Yellow
Write-Host "  cd c:\naia3\src\Naia.Api"
Write-Host "  dotnet run"
Write-Host ""
