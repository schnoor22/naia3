#!/usr/bin/env pwsh
<#
.SYNOPSIS
    NAIA Data Flow Diagnostic Script
    
.DESCRIPTION
    Comprehensive troubleshooting tool for QuestDB data flow issues.
    Checks all components: Kafka, QuestDB, Redis, PostgreSQL, API
    
.EXAMPLE
    .\diagnose-questdb-flow.ps1
    
#>

$ErrorActionPreference = "Continue"
$WarningPreference = "Continue"

Write-Host @"
╔════════════════════════════════════════════════════════════════════════════╗
║                                                                            ║
║  NAIA QuestDB Data Flow Diagnostic Tool                                    ║
║  Investigating: Why Trends Page Shows count:0 and Empty Data Arrays        ║
║                                                                            ║
║  Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')                          ║
║                                                                            ║
╚════════════════════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

Write-Host ""
Write-Host "CHECKING: Docker Containers & Services" -ForegroundColor Yellow
Write-Host "─" * 80

$containers = @("questdb", "kafka", "postgres", "redis", "naia-api", "naia-ingestion")
$runningCount = 0
$stoppedCount = 0

foreach ($container in $containers) {
    try {
        $status = docker inspect -f '{{.State.Running}}' $container 2>$null
        if ($status -eq "true") {
            Write-Host "  ✓ $container : RUNNING" -ForegroundColor Green
            $runningCount++
        } else {
            Write-Host "  ✗ $container : STOPPED" -ForegroundColor Red
            $stoppedCount++
        }
    } catch {
        Write-Host "  ✗ $container : NOT FOUND" -ForegroundColor Red
        $stoppedCount++
    }
}

Write-Host ""
Write-Host "  Summary: $runningCount running, $stoppedCount stopped" -ForegroundColor Cyan

# ============================================================================

Write-Host ""
Write-Host "CHECKING: QuestDB Data" -ForegroundColor Yellow
Write-Host "─" * 80

try {
    $rowCount = docker exec questdb psql -h localhost -p 8812 -U admin -d qdb -t -c "SELECT COUNT(*) FROM point_data;" 2>$null
    $rowCount = [int]$rowCount.Trim()
    
    if ($rowCount -gt 0) {
        Write-Host "  ✓ QuestDB has data: $rowCount rows in point_data table" -ForegroundColor Green
        
        # Get latest timestamp
        $latestTimestamp = docker exec questdb psql -h localhost -p 8812 -U admin -d qdb -t -c "SELECT MAX(timestamp) FROM point_data;" 2>$null
        Write-Host "    Latest timestamp: $latestTimestamp" -ForegroundColor Cyan
        
        # Get point count
        $pointCount = docker exec questdb psql -h localhost -p 8812 -U admin -d qdb -t -c "SELECT COUNT(DISTINCT point_id) FROM point_data;" 2>$null
        Write-Host "    Distinct point IDs: $pointCount" -ForegroundColor Cyan
        
        # Sample data
        Write-Host ""
        Write-Host "    Sample data (last 3 points):" -ForegroundColor Cyan
        docker exec questdb psql -h localhost -p 8812 -U admin -d qdb -c "SELECT timestamp, point_id, value, quality FROM point_data ORDER BY timestamp DESC LIMIT 3;" 2>$null | Select-Object -Skip 2
    } else {
        Write-Host "  ✗ QuestDB is EMPTY: 0 rows in point_data table" -ForegroundColor Red
        Write-Host "    → Data is NOT flowing to QuestDB" -ForegroundColor Yellow
        Write-Host "    → Check: Kafka topic, Ingestion Worker, QuestDB ILP endpoint" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ✗ FAILED to query QuestDB: $_" -ForegroundColor Red
    Write-Host "    → QuestDB may not be running or connection failed" -ForegroundColor Yellow
}

# ============================================================================

Write-Host ""
Write-Host "CHECKING: Kafka Topic (naia.datapoints)" -ForegroundColor Yellow
Write-Host "─" * 80

try {
    # Get consumer group status
    $groupStatus = docker exec kafka kafka-consumer-groups.sh --bootstrap-server kafka:9092 --group naia-historians --describe 2>$null | Select-Object -Skip 1
    
    if ($groupStatus) {
        Write-Host "  ✓ Kafka consumer group 'naia-historians' exists" -ForegroundColor Green
        
        $lines = @($groupStatus) -split "`n"
        $totalLag = 0
        foreach ($line in $lines) {
            if ($line -match 'naia\.datapoints') {
                $parts = $line -split '\s+' | Where-Object { $_ }
                if ($parts.Count -ge 5) {
                    $partition = $parts[1]
                    $currentOffset = $parts[2]
                    $logEndOffset = $parts[3]
                    $lag = $parts[4]
                    
                    Write-Host "    Partition $partition : offset=$currentOffset, end=$logEndOffset, lag=$lag" -ForegroundColor Cyan
                    $totalLag += [int]$lag
                }
            }
        }
        
        if ($totalLag -gt 100) {
            Write-Host "    ⚠ WARNING: High lag ($totalLag) - messages not being consumed fast enough" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  ⚠ Kafka consumer group 'naia-historians' not found" -ForegroundColor Yellow
        Write-Host "    → Consumer may not have started yet" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ✗ FAILED to check Kafka: $_" -ForegroundColor Red
}

# ============================================================================

Write-Host ""
Write-Host "CHECKING: PostgreSQL Points" -ForegroundColor Yellow
Write-Host "─" * 80

try {
    $pointCount = docker exec postgres psql -U naia -d naia -t -c "SELECT COUNT(*) FROM points;" 2>$null
    $pointCount = [int]$pointCount.Trim()
    
    Write-Host "  ✓ PostgreSQL points table has: $pointCount points" -ForegroundColor Green
    
    if ($pointCount -gt 0) {
        # Check for null PointSequenceId
        $nullCount = docker exec postgres psql -U naia -d naia -t -c "SELECT COUNT(*) FROM points WHERE point_sequence_id IS NULL;" 2>$null
        $nullCount = [int]$nullCount.Trim()
        
        if ($nullCount -gt 0) {
            Write-Host "  ⚠ WARNING: $nullCount points have NULL point_sequence_id" -ForegroundColor Yellow
            Write-Host "    → These points won't return data from QuestDB" -ForegroundColor Yellow
            Write-Host "    → Check enrichment logs in Naia.Ingestion" -ForegroundColor Yellow
        } else {
            Write-Host "  ✓ All points have point_sequence_id set" -ForegroundColor Green
        }
        
        # Sample points
        Write-Host ""
        Write-Host "    Sample points:" -ForegroundColor Cyan
        docker exec postgres psql -U naia -d naia -c "SELECT id, name, point_sequence_id FROM points LIMIT 3;" 2>$null | Select-Object -Skip 2
    }
} catch {
    Write-Host "  ✗ FAILED to query PostgreSQL: $_" -ForegroundColor Red
}

# ============================================================================

Write-Host ""
Write-Host "CHECKING: Redis Cache" -ForegroundColor Yellow
Write-Host "─" * 80

try {
    $cvCount = docker exec redis redis-cli KEYS "naia:cv:*" 2>$null | Measure-Object -Line | Select-Object -ExpandProperty Lines
    
    if ($cvCount -gt 0) {
        Write-Host "  ✓ Redis has $cvCount current value cache entries" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ WARNING: Redis has 0 current value cache entries" -ForegroundColor Yellow
        Write-Host "    → Data may not be flowing or Redis is isolated" -ForegroundColor Yellow
    }
    
    $idempCount = docker exec redis redis-cli KEYS "naia:idempotency:*" 2>$null | Measure-Object -Line | Select-Object -ExpandProperty Lines
    Write-Host "  ✓ Redis has $idempCount idempotency entries" -ForegroundColor Green
} catch {
    Write-Host "  ✗ FAILED to check Redis: $_" -ForegroundColor Red
}

# ============================================================================

Write-Host ""
Write-Host "CHECKING: API Endpoints" -ForegroundColor Yellow
Write-Host "─" * 80

try {
    # Test pipeline health endpoint
    $health = Invoke-RestMethod -Uri "http://localhost:5073/api/pipeline/health" -ErrorAction Stop 2>$null
    
    if ($health.isHealthy) {
        Write-Host "  ✓ Pipeline is HEALTHY" -ForegroundColor Green
        Write-Host "    State: $($health.state)" -ForegroundColor Cyan
        Write-Host "    Points processed: $($health.metrics.totalPointsProcessed)" -ForegroundColor Cyan
        Write-Host "    Points/sec: $($health.metrics.pointsPerSecond)" -ForegroundColor Cyan
    } else {
        Write-Host "  ✗ Pipeline is NOT HEALTHY: $($health.errorMessage)" -ForegroundColor Red
    }
} catch {
    Write-Host "  ✗ FAILED to check API health: $_" -ForegroundColor Red
    Write-Host "    → API may not be running on port 5073" -ForegroundColor Yellow
}

# ============================================================================

Write-Host ""
Write-Host "CHECKING: Naia.Ingestion Worker Logs" -ForegroundColor Yellow
Write-Host "─" * 80

try {
    $recentLogs = docker logs --tail=20 naia-ingestion 2>$null
    
    if ($recentLogs) {
        $hasError = $recentLogs -match "error|fail|exception" -i
        if ($hasError) {
            Write-Host "  ⚠ Recent errors found in logs:" -ForegroundColor Yellow
            $recentLogs -split "`n" | Where-Object { $_ -match "error|fail|exception" -i } | ForEach-Object {
                Write-Host "    $_" -ForegroundColor Red
            }
        } else {
            Write-Host "  ✓ No recent errors in Naia.Ingestion logs" -ForegroundColor Green
            # Show last few lines
            Write-Host "    Last few log lines:" -ForegroundColor Cyan
            $recentLogs -split "`n" | Select-Object -Last 3 | ForEach-Object {
                if ($_) { Write-Host "    $_" }
            }
        }
    }
} catch {
    Write-Host "  ⚠ Could not retrieve logs: $_" -ForegroundColor Yellow
}

# ============================================================================

Write-Host ""
Write-Host "SUMMARY & NEXT STEPS" -ForegroundColor Yellow
Write-Host "─" * 80

Write-Host @"

Based on the diagnostics above, use this checklist:

1. If QuestDB is EMPTY (0 rows):
   → Data is NOT flowing from Kafka
   → Check: Is Naia.Ingestion worker running? (docker logs naia-ingestion)
   → Check: Are messages in Kafka? (kafka-console-consumer on naia.datapoints)
   → Check: Is QuestDB accepting ILP writes? (curl -X POST http://localhost:9000/write)

2. If QuestDB has data BUT API returns count:0:
   → Data is in QuestDB but API can't find it
   → Check: Does the point have a point_sequence_id? (PostgreSQL points table)
   → Check: Is the point_sequence_id in QuestDB? (compare IDs between databases)
   → Check: Is API querying with correct parameters?

3. If PostgreSQL points have NULL point_sequence_id:
   → Points exist but haven't been synchronized to QuestDB yet
   → Check: Did the enrichment step in IngestionPipeline work?
   → Check: Do the points exist in PostgreSQL with matching names?

4. If Redis has no cache entries:
   → Either no data is flowing OR Redis is isolated from the pipeline
   → Check: Can Redis be reached from the containers? (docker network inspect)
   → Check: Are there connection errors in logs?

5. For more detailed diagnostics:
   → See: QUESTDB_DATA_FLOW_INVESTIGATION.md (sections 11-15)
   → Run: This script again after checking each component
   → Monitor: Use the 'watch' commands in section 12 of the investigation doc

"@ -ForegroundColor Cyan

Write-Host ""
Write-Host "Diagnostic complete!" -ForegroundColor Green
Write-Host ""
