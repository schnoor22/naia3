# Complete System Setup Script
# This will set up all infrastructure and databases needed for testing

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  NAIA Complete System Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Stop and remove existing containers
Write-Host "[1/6] Stopping existing containers..." -ForegroundColor Yellow
docker-compose -f docker-compose.yml down -v 2>&1 | Out-Null
Write-Host "  Done" -ForegroundColor Green
Write-Host ""

# Step 2: Start all infrastructure
Write-Host "[2/6] Starting infrastructure (PostgreSQL, QuestDB, Redis, Kafka)..." -ForegroundColor Yellow
docker-compose -f docker-compose.yml up -d
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Failed to start containers" -ForegroundColor Red
    exit 1
}
Write-Host "  Done" -ForegroundColor Green
Write-Host ""

# Step 3: Wait for services to be ready
Write-Host "[3/6] Waiting for services to initialize..." -ForegroundColor Yellow
Write-Host "  PostgreSQL (15 seconds)..." -ForegroundColor Gray
Start-Sleep -Seconds 15
Write-Host "  QuestDB (5 seconds)..." -ForegroundColor Gray
Start-Sleep -Seconds 5
Write-Host "  Redis (2 seconds)..." -ForegroundColor Gray
Start-Sleep -Seconds 2
Write-Host "  Kafka (5 seconds)..." -ForegroundColor Gray
Start-Sleep -Seconds 5
Write-Host "  Done" -ForegroundColor Green
Write-Host ""

# Step 4: Verify containers are running
Write-Host "[4/6] Verifying containers..." -ForegroundColor Yellow
$containers = docker ps --format "table {{.Names}}\t{{.Status}}" | Select-String "naia"
$containers | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
Write-Host "  Done" -ForegroundColor Green
Write-Host ""

# Step 5: Run database migrations
Write-Host "[5/6] Running EF Core migrations..." -ForegroundColor Yellow
Push-Location src\Naia.Api
$output = dotnet ef database update 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Migration failed" -ForegroundColor Red
    $output | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Pop-Location
    exit 1
}
Write-Host "  Done" -ForegroundColor Green
Pop-Location
Write-Host ""

# Step 6: Verify database
Write-Host "[6/6] Verifying database..." -ForegroundColor Yellow
$dbCheck = docker exec naia-postgres psql -U naia -d naia -c "\dt" 2>&1
if ($dbCheck -match "points|behavioral_fingerprints") {
    Write-Host "  Database tables created successfully" -ForegroundColor Green
} else {
    Write-Host "  WARNING: Could not verify tables" -ForegroundColor Yellow
}
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Services:" -ForegroundColor Cyan
Write-Host "  PostgreSQL:  localhost:5432" -ForegroundColor White
Write-Host "  QuestDB:     localhost:9000 (Web UI)" -ForegroundColor White
Write-Host "  Redis:       localhost:6379" -ForegroundColor White
Write-Host "  Kafka:       localhost:9092" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Start API: cd src\Naia.Api && dotnet run" -ForegroundColor White
Write-Host "  2. Test discovery: POST http://localhost:5052/api/pi/setup-pipeline?filter=*" -ForegroundColor White
Write-Host ""
