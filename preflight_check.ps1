# ═══════════════════════════════════════════════════════════════════
#  NAIA Pre-Flight Check - Verify System Readiness
#  Run this before starting the 24-hour test
# ═══════════════════════════════════════════════════════════════════

$ErrorActionPreference = "Continue"

function Write-Check($Message) {
    Write-Host "`n🔍 $Message" -ForegroundColor Cyan
}

function Write-Pass($Message) {
    Write-Host "  ✓ $Message" -ForegroundColor Green
}

function Write-Fail($Message) {
    Write-Host "  ✗ $Message" -ForegroundColor Red
}

function Write-Warn($Message) {
    Write-Host "  ⚠ $Message" -ForegroundColor Yellow
}

$allPass = $true

Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  NAIA Pre-Flight Check" -ForegroundColor Cyan
Write-Host "  Verifying system readiness for 24-hour test" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

# ═══════════════════════════════════════════════════════════════════
# 1. Docker Engine
# ═══════════════════════════════════════════════════════════════════

Write-Check "Docker Engine"

try {
    $dockerVersion = docker --version 2>$null
    if ($dockerVersion) {
        Write-Pass "Docker installed: $dockerVersion"
        
        docker info 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Pass "Docker daemon running"
        } else {
            Write-Fail "Docker daemon not running - Please start Docker Desktop"
            $allPass = $false
        }
    } else {
        Write-Fail "Docker not found - Please install Docker Desktop"
        $allPass = $false
    }
} catch {
    Write-Fail "Docker check failed: $_"
    $allPass = $false
}

# ═══════════════════════════════════════════════════════════════════
# 2. .NET SDK
# ═══════════════════════════════════════════════════════════════════

Write-Check ".NET SDK"

try {
    $dotnetVersion = dotnet --version 2>$null
    if ($dotnetVersion) {
        Write-Pass ".NET SDK installed: v$dotnetVersion"
        
        if ($dotnetVersion -match '^8\.') {
            Write-Pass ".NET 8 detected (compatible)"
        } else {
            Write-Warn ".NET $dotnetVersion detected (may not be compatible - requires .NET 8)"
        }
    } else {
        Write-Fail ".NET SDK not found - Please install .NET 8 SDK"
        $allPass = $false
    }
} catch {
    Write-Fail ".NET check failed: $_"
    $allPass = $false
}

# ═══════════════════════════════════════════════════════════════════
# 3. Docker Containers
# ═══════════════════════════════════════════════════════════════════

Write-Check "Docker Infrastructure"

$requiredContainers = @(
    @{Name="naia-postgres"; Display="PostgreSQL"},
    @{Name="naia-questdb"; Display="QuestDB"},
    @{Name="naia-redis"; Display="Redis"},
    @{Name="naia-kafka"; Display="Kafka"},
    @{Name="naia-zookeeper"; Display="Zookeeper"}
)

$allContainersRunning = $true

foreach ($container in $requiredContainers) {
    $status = docker inspect --format='{{.State.Status}}' $container.Name 2>$null
    
    if ($status -eq "running") {
        $health = docker inspect --format='{{.State.Health.Status}}' $container.Name 2>$null
        
        if ($health -eq "healthy") {
            Write-Pass "$($container.Display) running (healthy)"
        } elseif ($health -eq "starting") {
            Write-Warn "$($container.Display) running (starting...)"
        } elseif ($health) {
            Write-Warn "$($container.Display) running (health: $health)"
        } else {
            Write-Pass "$($container.Display) running"
        }
    } else {
        Write-Fail "$($container.Display) not running"
        $allContainersRunning = $false
        $allPass = $false
    }
}

if (-not $allContainersRunning) {
    Write-Host ""
    Write-Warn "Start infrastructure with: docker-compose up -d"
}

# ═══════════════════════════════════════════════════════════════════
# 4. PostgreSQL Database
# ═══════════════════════════════════════════════════════════════════

Write-Check "PostgreSQL Database"

try {
    $dbCheck = docker exec naia-postgres psql -U naia -d naia -c "\dt" 2>$null
    
    if ($dbCheck) {
        Write-Pass "Database 'naia' accessible"
        
        # Check for required tables
        $tables = @("data_sources", "points", "patterns", "pattern_suggestions", "behavioral_clusters")
        $missingTables = @()
        
        foreach ($table in $tables) {
            $tableExists = docker exec naia-postgres psql -U naia -d naia -t -c "SELECT COUNT(*) FROM information_schema.tables WHERE table_name='$table';" 2>$null
            if ($tableExists -match '1') {
                Write-Pass "Table '$table' exists"
            } else {
                Write-Fail "Table '$table' missing"
                $missingTables += $table
                $allPass = $false
            }
        }
        
        if ($missingTables.Count -gt 0) {
            Write-Host ""
            Write-Warn "Missing tables will be created by init scripts on first run"
        }
    } else {
        Write-Fail "Cannot connect to database"
        $allPass = $false
    }
} catch {
    Write-Fail "PostgreSQL check failed: $_"
    $allPass = $false
}

# ═══════════════════════════════════════════════════════════════════
# 5. MLR1 Test Points
# ═══════════════════════════════════════════════════════════════════

Write-Check "MLR1 Test Points"

try {
    $pointCount = docker exec naia-postgres psql -U naia -d naia -t -c "SELECT COUNT(*) FROM points WHERE name LIKE 'MLR1%';" 2>$null
    $count = [int]$pointCount.Trim()
    
    if ($count -eq 4) {
        Write-Pass "MLR1 points configured ($count/4)"
        
        # List points
        $points = docker exec naia-postgres psql -U naia -d naia -t -c "SELECT name FROM points WHERE name LIKE 'MLR1%' ORDER BY name;" 2>$null
        foreach ($point in $points -split "`n") {
            $pointName = $point.Trim()
            if ($pointName) {
                Write-Pass "  - $pointName"
            }
        }
    } elseif ($count -eq 0) {
        Write-Warn "MLR1 points not configured (0/4)"
        Write-Host ""
        Write-Warn "Create points with: .\execute_sql.ps1"
    } else {
        Write-Warn "Partial MLR1 configuration ($count/4 points)"
        $allPass = $false
    }
} catch {
    Write-Fail "Point check failed: $_"
    $allPass = $false
}

# ═══════════════════════════════════════════════════════════════════
# 6. QuestDB
# ═══════════════════════════════════════════════════════════════════

Write-Check "QuestDB"

try {
    $response = Invoke-WebRequest -Uri "http://localhost:9000" -UseBasicParsing -TimeoutSec 5 2>$null
    if ($response.StatusCode -eq 200) {
        Write-Pass "HTTP API accessible (port 9000)"
        Write-Pass "Web Console: http://localhost:9000"
    } else {
        Write-Fail "HTTP API returned status $($response.StatusCode)"
        $allPass = $false
    }
} catch {
    Write-Fail "Cannot reach QuestDB - $_"
    $allPass = $false
}

# ═══════════════════════════════════════════════════════════════════
# 7. Redis
# ═══════════════════════════════════════════════════════════════════

Write-Check "Redis"

try {
    $redisPing = docker exec naia-redis redis-cli ping 2>$null
    if ($redisPing -match 'PONG') {
        Write-Pass "Redis responding to PING"
    } else {
        Write-Fail "Redis not responding"
        $allPass = $false
    }
} catch {
    Write-Fail "Redis check failed: $_"
    $allPass = $false
}

# ═══════════════════════════════════════════════════════════════════
# 8. Kafka
# ═══════════════════════════════════════════════════════════════════

Write-Check "Kafka"

try {
    # Check if Kafka UI is accessible
    $kafkaUi = Invoke-WebRequest -Uri "http://localhost:8080" -UseBasicParsing -TimeoutSec 5 2>$null
    if ($kafkaUi.StatusCode -eq 200) {
        Write-Pass "Kafka UI accessible (port 8080)"
    } else {
        Write-Warn "Kafka UI not accessible"
    }
    
    # Check if Kafka broker is accessible
    $kafkaCheck = docker exec naia-kafka kafka-topics --bootstrap-server localhost:29092 --list 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Pass "Kafka broker responding"
        
        if ($kafkaCheck -match 'naia.datapoints') {
            Write-Pass "Topic 'naia.datapoints' exists"
        } else {
            Write-Warn "Topic 'naia.datapoints' not yet created (will be auto-created)"
        }
    } else {
        Write-Fail "Kafka broker not responding"
        $allPass = $false
    }
} catch {
    Write-Warn "Kafka check incomplete: $_"
}

# ═══════════════════════════════════════════════════════════════════
# 9. Solution Build Status
# ═══════════════════════════════════════════════════════════════════

Write-Check "Solution Build"

try {
    if (Test-Path "Naia.sln") {
        Write-Pass "Solution file found"
        
        # Check if binaries exist
        if (Test-Path "src/Naia.Api/bin/Debug/net8.0/Naia.Api.dll") {
            Write-Pass "Naia.Api previously built"
        } else {
            Write-Warn "Naia.Api not built - will build on startup"
        }
        
        if (Test-Path "src/Naia.Ingestion/bin/Debug/net8.0/Naia.Ingestion.dll") {
            Write-Pass "Naia.Ingestion previously built"
        } else {
            Write-Warn "Naia.Ingestion not built - will build on startup"
        }
    } else {
        Write-Fail "Solution file not found - Are you in the project root?"
        $allPass = $false
    }
} catch {
    Write-Fail "Build check failed: $_"
    $allPass = $false
}

# ═══════════════════════════════════════════════════════════════════
# 10. Disk Space
# ═══════════════════════════════════════════════════════════════════

Write-Check "Disk Space"

try {
    $drive = Get-PSDrive C
    $freeGB = [Math]::Round($drive.Free / 1GB, 2)
    
    if ($freeGB -gt 10) {
        Write-Pass "Available disk space: ${freeGB}GB"
    } elseif ($freeGB -gt 5) {
        Write-Warn "Low disk space: ${freeGB}GB (recommend 10GB+ for 24h test)"
    } else {
        Write-Fail "Insufficient disk space: ${freeGB}GB (need at least 5GB)"
        $allPass = $false
    }
} catch {
    Write-Warn "Disk space check failed: $_"
}

# ═══════════════════════════════════════════════════════════════════
# Summary
# ═══════════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

if ($allPass) {
    Write-Host "  ✅ PRE-FLIGHT CHECK PASSED" -ForegroundColor Green
    Write-Host "  System is ready for 24-hour test!" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    Write-Host "NEXT STEPS:" -ForegroundColor Yellow
    Write-Host "  1. Review: READY_TO_START.md" -ForegroundColor White
    Write-Host "  2. Start:  .\start_24h_test.ps1" -ForegroundColor White
    Write-Host "  3. Monitor: .\monitor_24h_test.ps1" -ForegroundColor White
    Write-Host ""
} else {
    Write-Host "  ⚠ PRE-FLIGHT CHECK FAILED" -ForegroundColor Red
    Write-Host "  Please resolve issues above before starting test" -ForegroundColor Red
    Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Red
    Write-Host ""
    Write-Host "COMMON FIXES:" -ForegroundColor Yellow
    Write-Host "  - Docker not running:    Start Docker Desktop" -ForegroundColor White
    Write-Host "  - Containers not running: docker-compose up -d" -ForegroundColor White
    Write-Host "  - MLR1 points missing:    .\execute_sql.ps1" -ForegroundColor White
    Write-Host "  - Build required:         dotnet build Naia.sln" -ForegroundColor White
    Write-Host ""
}

Write-Host "For detailed setup instructions, see: START_HISTORIAN_24H_TEST.md" -ForegroundColor Gray
Write-Host ""
