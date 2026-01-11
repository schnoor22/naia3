# ═══════════════════════════════════════════════════════════════════
#  NAIA 24-Hour Learning Test - Live Monitor
#  Real-time metrics and health dashboard
# ═══════════════════════════════════════════════════════════════════

param(
    [int]$RefreshSeconds = 60
)

$ErrorActionPreference = "SilentlyContinue"

function Get-FormattedNumber {
    param([long]$Number)
    if ($Number -ge 1000000) {
        return "{0:N1}M" -f ($Number / 1000000)
    } elseif ($Number -ge 1000) {
        return "{0:N1}K" -f ($Number / 1000)
    } else {
        return $Number.ToString()
    }
}

function Get-ContainerStatus {
    param([string]$Name)
    
    $status = docker inspect --format='{{.State.Status}}' $Name 2>$null
    $health = docker inspect --format='{{.State.Health.Status}}' $Name 2>$null
    
    if ($status -eq "running") {
        if ($health -eq "healthy") {
            return "✓", "Green"
        } elseif ($health -eq "unhealthy") {
            return "✗", "Red"
        } else {
            return "○", "Yellow"
        }
    } else {
        return "✗", "Red"
    }
}

$startTime = Get-Date
$iteration = 0

while ($true) {
    $iteration++
    $now = Get-Date
    $uptime = $now - $startTime
    
    Clear-Host
    
    # Header
    Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  NAIA 24-Hour Learning Test - Live Monitor" -ForegroundColor Cyan
    Write-Host "  Uptime: $($uptime.Days)d $($uptime.Hours)h $($uptime.Minutes)m $($uptime.Seconds)s | Refresh: ${RefreshSeconds}s | Iteration: $iteration" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    
    # ═══════════════════════════════════════════════════════════════════
    # INFRASTRUCTURE STATUS
    # ═══════════════════════════════════════════════════════════════════
    
    Write-Host "[INFRASTRUCTURE STATUS]" -ForegroundColor Yellow
    Write-Host ""
    
    $containers = @(
        @{Name="naia-postgres"; Display="PostgreSQL"},
        @{Name="naia-questdb"; Display="QuestDB"},
        @{Name="naia-redis"; Display="Redis"},
        @{Name="naia-zookeeper"; Display="Zookeeper"},
        @{Name="naia-kafka"; Display="Kafka"}
    )
    
    foreach ($container in $containers) {
        $status, $color = Get-ContainerStatus $container.Name
        Write-Host ("  {0,-15} {1}" -f $container.Display, $status) -ForegroundColor $color
    }
    
    Write-Host ""
    
    # ═══════════════════════════════════════════════════════════════════
    # DATA FLOW METRICS
    # ═══════════════════════════════════════════════════════════════════
    
    Write-Host "[DATA FLOW METRICS]" -ForegroundColor Yellow
    Write-Host ""
    
    # QuestDB row count
    try {
        $questDbQuery = @"
SELECT 
    COUNT(*) as total_records,
    COUNT(DISTINCT point_name) as unique_points,
    MAX(timestamp) as latest_timestamp
FROM timeseries 
WHERE point_name LIKE 'MLR1%'
"@
        
        $questDbResult = Invoke-RestMethod -Uri "http://localhost:9000/exec?query=$([System.Web.HttpUtility]::UrlEncode($questDbQuery))" -TimeoutSec 5
        
        if ($questDbResult.dataset) {
            $row = $questDbResult.dataset[0]
            $totalRecords = $row[0]
            $uniquePoints = $row[1]
            $latestTimestamp = $row[2]
            
            Write-Host "  QuestDB Records:       $(Get-FormattedNumber $totalRecords)" -ForegroundColor Green
            Write-Host "  Unique Points:         $uniquePoints" -ForegroundColor Green
            Write-Host "  Latest Timestamp:      $latestTimestamp" -ForegroundColor Green
        } else {
            Write-Host "  QuestDB:               No data yet" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  QuestDB:               ✗ Connection failed" -ForegroundColor Red
    }
    
    Write-Host ""
    
    # Redis stats
    try {
        $redisInfo = docker exec naia-redis redis-cli INFO stats 2>$null | Out-String
        if ($redisInfo -match 'total_commands_processed:(\d+)') {
            $commands = [long]$matches[1]
            Write-Host "  Redis Commands:        $(Get-FormattedNumber $commands)" -ForegroundColor Green
        }
        
        $redisKeys = docker exec naia-redis redis-cli DBSIZE 2>$null
        if ($redisKeys -match 'keys=(\d+)') {
            $keyCount = $matches[1]
            Write-Host "  Redis Keys:            $keyCount" -ForegroundColor Green
        }
    } catch {
        Write-Host "  Redis:                 ✗ Connection failed" -ForegroundColor Red
    }
    
    Write-Host ""
    
    # ═══════════════════════════════════════════════════════════════════
    # PATTERN ENGINE STATUS
    # ═══════════════════════════════════════════════════════════════════
    
    Write-Host "[PATTERN ENGINE]" -ForegroundColor Yellow
    Write-Host ""
    
    try {
        $suggestions = Invoke-RestMethod -Uri "http://localhost:5052/api/suggestions/stats" -TimeoutSec 5
        
        Write-Host "  Pending Suggestions:   $($suggestions.pendingCount)" -ForegroundColor $(if ($suggestions.pendingCount -gt 0) { "Cyan" } else { "Gray" })
        Write-Host "  Approved Today:        $($suggestions.approvedToday)" -ForegroundColor Green
        Write-Host "  Rejected Today:        $($suggestions.rejectedToday)" -ForegroundColor Yellow
        Write-Host "  Total Approved:        $($suggestions.totalApproved)" -ForegroundColor Green
        Write-Host "  Total Rejected:        $($suggestions.totalRejected)" -ForegroundColor Yellow
        
        if ($suggestions.totalApproved + $suggestions.totalRejected -gt 0) {
            Write-Host "  Approval Rate:         $($suggestions.approvalRate)%" -ForegroundColor Cyan
        }
    } catch {
        Write-Host "  API:                   ✗ Connection failed (is Naia.Api running?)" -ForegroundColor Red
    }
    
    Write-Host ""
    
    # ═══════════════════════════════════════════════════════════════════
    # BEHAVIORAL CLUSTERS
    # ═══════════════════════════════════════════════════════════════════
    
    Write-Host "[BEHAVIORAL CLUSTERS]" -ForegroundColor Yellow
    Write-Host ""
    
    try {
        $clusterCount = docker exec naia-postgres psql -U naia -d naia -t -c "SELECT COUNT(*) FROM behavioral_clusters;" 2>$null
        $clusterCountTrimmed = [int]$clusterCount.Trim()
        
        if ($clusterCountTrimmed -gt 0) {
            Write-Host "  Total Clusters:        $clusterCountTrimmed" -ForegroundColor Green
            
            $latestCluster = docker exec naia-postgres psql -U naia -d naia -t -c "SELECT common_prefix, point_count, detected_at FROM behavioral_clusters ORDER BY detected_at DESC LIMIT 1;" 2>$null
            Write-Host "  Latest Cluster:        $($latestCluster.Trim())" -ForegroundColor Cyan
        } else {
            Write-Host "  Total Clusters:        0 (need more data)" -ForegroundColor Gray
        }
    } catch {
        Write-Host "  PostgreSQL:            ✗ Connection failed" -ForegroundColor Red
    }
    
    Write-Host ""
    
    # ═══════════════════════════════════════════════════════════════════
    # BEHAVIORAL STATISTICS
    # ═══════════════════════════════════════════════════════════════════
    
    Write-Host "[BEHAVIORAL STATISTICS]" -ForegroundColor Yellow
    Write-Host ""
    
    try {
        $statsCount = docker exec naia-postgres psql -U naia -d naia -t -c "SELECT COUNT(*) FROM behavioral_stats WHERE last_calculated_at > NOW() - INTERVAL '1 hour';" 2>$null
        $statsCountTrimmed = [int]$statsCount.Trim()
        
        if ($statsCountTrimmed -gt 0) {
            Write-Host "  Stats Calculated (1h): $statsCountTrimmed points" -ForegroundColor Green
            
            $latestStat = docker exec naia-postgres psql -U naia -d naia -t -c "SELECT point_name, mean_value, stddev, sample_count FROM behavioral_stats ORDER BY last_calculated_at DESC LIMIT 1;" 2>$null
            if ($latestStat) {
                $parts = $latestStat.Trim() -split '\|'
                if ($parts.Count -ge 4) {
                    Write-Host ("  Latest: {0,-20} μ={1,7} σ={2,7} n={3}" -f $parts[0].Trim(), $parts[1].Trim(), $parts[2].Trim(), $parts[3].Trim()) -ForegroundColor Cyan
                }
            }
        } else {
            Write-Host "  Stats:                 No recent calculations" -ForegroundColor Gray
        }
    } catch {
        Write-Host "  PostgreSQL:            ✗ Connection failed" -ForegroundColor Red
    }
    
    Write-Host ""
    
    # ═══════════════════════════════════════════════════════════════════
    # HANGFIRE JOBS (if accessible)
    # ═══════════════════════════════════════════════════════════════════
    
    Write-Host "[HANGFIRE JOBS]" -ForegroundColor Yellow
    Write-Host ""
    
    try {
        # Try to get Hangfire recurring jobs status
        # Note: Hangfire dashboard doesn't have a JSON API by default, so we show generic message
        $apiHealth = Invoke-WebRequest -Uri "http://localhost:5052/health" -TimeoutSec 5 2>$null
        if ($apiHealth.StatusCode -eq 200) {
            Write-Host "  Dashboard:             http://localhost:5052/hangfire" -ForegroundColor Cyan
            Write-Host "  Status:                ✓ API Running" -ForegroundColor Green
        }
    } catch {
        Write-Host "  Dashboard:             ✗ API not responding" -ForegroundColor Red
    }
    
    Write-Host ""
    
    # ═══════════════════════════════════════════════════════════════════
    # QUICK ACTIONS
    # ═══════════════════════════════════════════════════════════════════
    
    Write-Host "[QUICK ACTIONS]" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  [H] Open Hangfire Dashboard" -ForegroundColor Gray
    Write-Host "  [Q] Open QuestDB Console" -ForegroundColor Gray
    Write-Host "  [K] Open Kafka UI" -ForegroundColor Gray
    Write-Host "  [S] Open Swagger API" -ForegroundColor Gray
    Write-Host "  [L] View Latest Suggestions (JSON)" -ForegroundColor Gray
    Write-Host "  [X] Exit Monitor" -ForegroundColor Gray
    Write-Host ""
    
    # ═══════════════════════════════════════════════════════════════════
    # FOOTER
    # ═══════════════════════════════════════════════════════════════════
    
    $targetEndTime = $startTime.AddHours(24)
    $remaining = $targetEndTime - $now
    
    if ($remaining.TotalHours -gt 0) {
        Write-Host "Time remaining until 24h mark: $($remaining.Days)d $($remaining.Hours)h $($remaining.Minutes)m" -ForegroundColor Cyan
    } else {
        Write-Host "24-hour test completed! Review results and analyze patterns." -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "Next refresh in $RefreshSeconds seconds... (Press key for action)" -ForegroundColor Gray
    
    # Wait with key check
    $startWait = Get-Date
    while (((Get-Date) - $startWait).TotalSeconds -lt $RefreshSeconds) {
        if ([Console]::KeyAvailable) {
            $key = [Console]::ReadKey($true)
            
            switch ($key.KeyChar) {
                'h' { Start-Process "http://localhost:5052/hangfire"; break }
                'H' { Start-Process "http://localhost:5052/hangfire"; break }
                'q' { Start-Process "http://localhost:9000"; break }
                'Q' { Start-Process "http://localhost:9000"; break }
                'k' { Start-Process "http://localhost:8080"; break }
                'K' { Start-Process "http://localhost:8080"; break }
                's' { Start-Process "http://localhost:5052/swagger"; break }
                'S' { Start-Process "http://localhost:5052/swagger"; break }
                'l' {
                    try {
                        $pending = Invoke-RestMethod "http://localhost:5052/api/suggestions/pending"
                        $pending | ConvertTo-Json -Depth 5 | Out-Host
                        Write-Host "`nPress any key to continue..." -ForegroundColor Yellow
                        $null = [Console]::ReadKey($true)
                    } catch {
                        Write-Host "Failed to fetch suggestions: $_" -ForegroundColor Red
                        Start-Sleep -Seconds 3
                    }
                    break
                }
                'L' {
                    try {
                        $pending = Invoke-RestMethod "http://localhost:5052/api/suggestions/pending"
                        $pending | ConvertTo-Json -Depth 5 | Out-Host
                        Write-Host "`nPress any key to continue..." -ForegroundColor Yellow
                        $null = [Console]::ReadKey($true)
                    } catch {
                        Write-Host "Failed to fetch suggestions: $_" -ForegroundColor Red
                        Start-Sleep -Seconds 3
                    }
                    break
                }
                'x' { return }
                'X' { return }
            }
            
            # Break inner loop to refresh immediately after action
            break
        }
        
        Start-Sleep -Milliseconds 100
    }
}
