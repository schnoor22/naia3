# ═══════════════════════════════════════════════════════════════════
#  NAIA 24-Hour Learning Test - Automated Startup
#  The First Industrial Historian That Learns From You™
# ═══════════════════════════════════════════════════════════════════

param(
    [switch]$SkipBuild = $false,
    [switch]$SkipInfrastructure = $false
)

$ErrorActionPreference = "Stop"
$startTime = Get-Date

function Write-Step {
    param([string]$Message)
    Write-Host "`n$Message" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠ $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

function Test-DockerRunning {
    try {
        docker info | Out-Null
        return $true
    } catch {
        return $false
    }
}

function Wait-ForHealthy {
    param(
        [string]$ContainerName,
        [int]$TimeoutSeconds = 60
    )
    
    $elapsed = 0
    while ($elapsed -lt $TimeoutSeconds) {
        $health = docker inspect --format='{{.State.Health.Status}}' $ContainerName 2>$null
        if ($health -eq "healthy") {
            return $true
        }
        Start-Sleep -Seconds 2
        $elapsed += 2
        Write-Host "." -NoNewline
    }
    return $false
}

# ═══════════════════════════════════════════════════════════════════
# STEP 0: Pre-flight Checks
# ═══════════════════════════════════════════════════════════════════

Write-Step "Pre-flight Checks"

# Check Docker
if (-not (Test-DockerRunning)) {
    Write-Error "Docker is not running. Please start Docker Desktop and try again."
    exit 1
}
Write-Success "Docker is running"

# Check .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Success ".NET SDK $dotnetVersion found"
} catch {
    Write-Error ".NET SDK not found. Please install .NET 8 SDK."
    exit 1
}

# Check project structure
if (-not (Test-Path "Naia.sln")) {
    Write-Error "Not in NAIA project root. Please run from c:\naia3"
    exit 1
}
Write-Success "Project structure verified"

# ═══════════════════════════════════════════════════════════════════
# STEP 1: Start Infrastructure
# ═══════════════════════════════════════════════════════════════════

if (-not $SkipInfrastructure) {
    Write-Step "Starting Docker Infrastructure"
    
    # Stop existing containers
    Write-Host "Stopping existing containers..." -NoNewline
    docker-compose down 2>$null
    Write-Success "Done"
    
    # Start services
    Write-Host "Starting services..."
    docker-compose up -d
    
    # Wait for health checks
    Write-Host "`nWaiting for PostgreSQL..." -NoNewline
    if (Wait-ForHealthy "naia-postgres" 60) {
        Write-Success "PostgreSQL ready"
    } else {
        Write-Error "PostgreSQL failed to start"
        docker logs naia-postgres --tail 20
        exit 1
    }
    
    Write-Host "Waiting for QuestDB..." -NoNewline
    if (Wait-ForHealthy "naia-questdb" 60) {
        Write-Success "QuestDB ready"
    } else {
        Write-Error "QuestDB failed to start"
        docker logs naia-questdb --tail 20
        exit 1
    }
    
    Write-Host "Waiting for Redis..." -NoNewline
    if (Wait-ForHealthy "naia-redis" 30) {
        Write-Success "Redis ready"
    } else {
        Write-Error "Redis failed to start"
        exit 1
    }
    
    Write-Host "Waiting for Kafka..." -NoNewline
    if (Wait-ForHealthy "naia-kafka" 90) {
        Write-Success "Kafka ready"
    } else {
        Write-Error "Kafka failed to start"
        docker logs naia-kafka --tail 20
        exit 1
    }
    
    Write-Success "All infrastructure services running"
    
    # Display status
    Write-Host "`nContainer Status:" -ForegroundColor Yellow
    docker-compose ps
    
} else {
    Write-Warning "Skipping infrastructure startup (--SkipInfrastructure)"
}

# ═══════════════════════════════════════════════════════════════════
# STEP 2: Verify MLR1 Points in PostgreSQL
# ═══════════════════════════════════════════════════════════════════

Write-Step "Verifying MLR1 Points Configuration"

$pointCheck = docker exec naia-postgres psql -U naia -d naia -t -c "SELECT COUNT(*) FROM points WHERE name LIKE 'MLR1%';" 2>$null
$pointCount = [int]$pointCheck.Trim()

if ($pointCount -eq 4) {
    Write-Success "MLR1 points configured ($pointCount points)"
} elseif ($pointCount -eq 0) {
    Write-Warning "MLR1 points not found - creating..."
    
    if (Test-Path "execute_sql.ps1") {
        & .\execute_sql.ps1
        Write-Success "MLR1 points created"
    } else {
        Write-Error "execute_sql.ps1 not found. Please create MLR1 points manually."
        exit 1
    }
} else {
    Write-Warning "Unexpected point count: $pointCount (expected 4)"
}

# Show points
Write-Host "`nConfigured MLR1 Points:" -ForegroundColor Yellow
docker exec naia-postgres psql -U naia -d naia -c "SELECT name, source_address, engineering_units FROM points WHERE name LIKE 'MLR1%' ORDER BY name;"

# ═══════════════════════════════════════════════════════════════════
# STEP 3: Build Solution
# ═══════════════════════════════════════════════════════════════════

if (-not $SkipBuild) {
    Write-Step "Building NAIA Solution"
    
    dotnet build Naia.sln --configuration Release
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }
    
    Write-Success "Build completed successfully"
} else {
    Write-Warning "Skipping build (--SkipBuild)"
}

# ═══════════════════════════════════════════════════════════════════
# STEP 4: Start Naia.Api (Producer + Pattern Engine + Hangfire)
# ═══════════════════════════════════════════════════════════════════

Write-Step "Starting Naia.Api"

# Start API in new window
$apiProcess = Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$PSScriptRoot\src\Naia.Api'; Write-Host 'Starting Naia.Api...' -ForegroundColor Cyan; dotnet run --configuration Release"
) -PassThru

Write-Success "Naia.Api starting in new window (PID: $($apiProcess.Id))"
Write-Host "  - REST API: http://localhost:5000" -ForegroundColor Gray
Write-Host "  - Swagger UI: http://localhost:5000/swagger" -ForegroundColor Gray
Write-Host "  - Hangfire Dashboard: http://localhost:5000/hangfire" -ForegroundColor Gray

# Wait for API to start
Write-Host "`nWaiting for API to start..." -NoNewline
$maxWait = 60
$elapsed = 0
$apiReady = $false

while ($elapsed -lt $maxWait) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5000/health" -TimeoutSec 2 2>$null
        if ($response.StatusCode -eq 200) {
            $apiReady = $true
            break
        }
    } catch {
        # API not ready yet
    }
    
    Start-Sleep -Seconds 2
    $elapsed += 2
    Write-Host "." -NoNewline
}

if ($apiReady) {
    Write-Success "API ready"
} else {
    Write-Warning "API taking longer than expected to start (check API window)"
}

# ═══════════════════════════════════════════════════════════════════
# STEP 5: Start PI Ingestion (PI → Kafka)
# ═══════════════════════════════════════════════════════════════════

if ($apiReady) {
    Write-Step "Starting PI Data Ingestion"
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:5000/api/ingestion/start" -Method POST
        Write-Success "PI ingestion started"
        Write-Host "  Points streaming: $($response.pointsCount)" -ForegroundColor Gray
    } catch {
        Write-Warning "Failed to start PI ingestion: $_"
        Write-Host "  You can start manually: POST http://localhost:5000/api/ingestion/start"
    }
} else {
    Write-Warning "Skipping ingestion start (API not ready)"
}

# ═══════════════════════════════════════════════════════════════════
# STEP 6: Start Naia.Ingestion Worker (Kafka → QuestDB + Redis)
# ═══════════════════════════════════════════════════════════════════

Write-Step "Starting Naia.Ingestion Worker"

$ingestionProcess = Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$PSScriptRoot\src\Naia.Ingestion'; Write-Host 'Starting Naia.Ingestion Worker...' -ForegroundColor Cyan; dotnet run --configuration Release"
) -PassThru

Write-Success "Naia.Ingestion worker starting in new window (PID: $($ingestionProcess.Id))"

# ═══════════════════════════════════════════════════════════════════
# STEP 7: Summary & Next Steps
# ═══════════════════════════════════════════════════════════════════

$elapsed = ((Get-Date) - $startTime).TotalSeconds

Write-Host "`n"
Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  NAIA 24-Hour Learning Test - STARTED" -ForegroundColor Green
Write-Host "  The First Industrial Historian That Learns From You™" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "Startup completed in $([Math]::Round($elapsed, 1)) seconds" -ForegroundColor Cyan
Write-Host ""
Write-Host "DATA FLOW:" -ForegroundColor Yellow
Write-Host "  PI System (MLR1*) → Kafka → QuestDB + Redis → Pattern Engine" -ForegroundColor Gray
Write-Host ""
Write-Host "SERVICES RUNNING:" -ForegroundColor Yellow
Write-Host "  ✓ PostgreSQL       (localhost:5432)" -ForegroundColor Green
Write-Host "  ✓ QuestDB          (localhost:9000, 8812)" -ForegroundColor Green
Write-Host "  ✓ Redis            (localhost:6379)" -ForegroundColor Green
Write-Host "  ✓ Kafka            (localhost:9092)" -ForegroundColor Green
Write-Host "  ✓ Naia.Api         (localhost:5000)" -ForegroundColor Green
Write-Host "  ✓ Naia.Ingestion   (Background Worker)" -ForegroundColor Green
Write-Host ""
Write-Host "MONITORING DASHBOARDS:" -ForegroundColor Yellow
Write-Host "  - Swagger API:        http://localhost:5000/swagger" -ForegroundColor Cyan
Write-Host "  - Hangfire Jobs:      http://localhost:5000/hangfire" -ForegroundColor Cyan
Write-Host "  - QuestDB Console:    http://localhost:9000" -ForegroundColor Cyan
Write-Host "  - Kafka UI:           http://localhost:8080" -ForegroundColor Cyan
Write-Host ""
Write-Host "PATTERN ENGINE SCHEDULE:" -ForegroundColor Yellow
Write-Host "  - Behavioral Analysis:  Every 5 minutes" -ForegroundColor Gray
Write-Host "  - Correlation Analysis: Every 15 minutes" -ForegroundColor Gray
Write-Host "  - Cluster Detection:    Every 15 minutes (+5m offset)" -ForegroundColor Gray
Write-Host "  - Pattern Matching:     Every 15 minutes (+10m offset)" -ForegroundColor Gray
Write-Host "  - Pattern Learning:     Every hour" -ForegroundColor Gray
Write-Host "  - Maintenance:          Daily at 3 AM" -ForegroundColor Gray
Write-Host ""
Write-Host "NEXT STEPS:" -ForegroundColor Yellow
Write-Host "  1. Open Hangfire dashboard to monitor jobs" -ForegroundColor White
Write-Host "  2. Wait 30-45 minutes for first pattern suggestions" -ForegroundColor White
Write-Host "  3. Check suggestions: GET http://localhost:5000/api/suggestions/pending" -ForegroundColor White
Write-Host "  4. Approve suggestions to teach NAIA your patterns" -ForegroundColor White
Write-Host ""
Write-Host "VERIFICATION COMMANDS:" -ForegroundColor Yellow
Write-Host '  docker-compose ps                    # Check infrastructure' -ForegroundColor Gray
Write-Host '  docker logs naia-kafka -f            # Watch Kafka logs' -ForegroundColor Gray
Write-Host '  # QuestDB: SELECT * FROM timeseries  # Check data in QuestDB' -ForegroundColor Gray
Write-Host ""
Write-Host "Let the system run for 24 hours to see pattern learning in action!" -ForegroundColor Cyan
Write-Host "Full guide: START_HISTORIAN_24H_TEST.md" -ForegroundColor Gray
Write-Host ""

# Open key dashboards
Write-Host "Opening dashboards..." -ForegroundColor Cyan
Start-Sleep -Seconds 3

Start-Process "http://localhost:5000/hangfire"
Start-Sleep -Seconds 1
Start-Process "http://localhost:9000"
Start-Sleep -Seconds 1
Start-Process "http://localhost:8080"

Write-Host ""
Write-Host "Press any key to exit this script (services will continue running)..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
