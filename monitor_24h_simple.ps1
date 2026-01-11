# =======================================================================
#  NAIA 24-Hour Learning Test - Live Monitor Dashboard
#  Real-time metrics from API, QuestDB, PostgreSQL, Redis, Hangfire
# =======================================================================

param([int]$RefreshSeconds = 30)

$startTime = Get-Date
$iteration = 0

function Get-APIMetrics {
    try {
        $response = Invoke-RestMethod "http://localhost:5052/api/ingestion/status" -TimeoutSec 2 -ErrorAction SilentlyContinue
        return $response
    } catch {
        return $null
    }
}

function Get-QuestDBMetrics {
    try {
        $query = "SELECT COUNT(*) as total_records, COUNT(DISTINCT point_name) as unique_points, MAX(timestamp) as latest FROM timeseries WHERE point_name LIKE 'MLR1%'"
        $encoded = [System.Web.HttpUtility]::UrlEncode($query)
        $response = Invoke-RestMethod "http://localhost:9000/exec?query=$encoded" -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response.dataset -and $response.dataset.Count -gt 0) {
            $row = $response.dataset[0]
            return @{
                TotalRecords = $row[0]
                UniquePoints = $row[1]
                LatestTimestamp = $row[2]
            }
        }
    } catch { }
    return $null
}

function Get-PostgreSQLMetrics {
    try {
        $query = "SELECT COUNT(*) FROM patterns WHERE is_active = true; SELECT COUNT(*) FROM behavioral_clusters; SELECT COUNT(*) FROM pattern_suggestions WHERE status = 'pending';"
        $cmd = "docker exec naia-postgres psql -U naia -d naia -t -c `"$query`""
        $result = Invoke-Expression $cmd 2>$null
        $lines = @($result -split "`n" | Where-Object { $_ -match '\d+' })
        if ($lines.Count -ge 3) {
            return @{
                PatternCount = [int]($lines[0])
                ClusterCount = [int]($lines[1])
                SuggestionCount = [int]($lines[2])
            }
        }
    } catch { }
    return $null
}

function Get-HangfireStatus {
    try {
        $response = Invoke-RestMethod "http://localhost:5052/hangfire/api/jobs" -TimeoutSec 2 -ErrorAction SilentlyContinue
        return $response
    } catch { }
    return $null
}

function Get-ProcessStatus {
    $dotnetProcesses = Get-Process dotnet -ErrorAction SilentlyContinue
    return @{
        APIRunning = ($dotnetProcesses | Where-Object { $_.Commandline -match "Naia.Api" }).Count -gt 0
        IngestionRunning = ($dotnetProcesses | Where-Object { $_.Commandline -match "Naia.Ingestion" }).Count -gt 0
    }
}

while ($true) {
    $iteration++
    $now = Get-Date
    $uptime = $now - $startTime
    
    Clear-Host
    
    # Header
    Write-Host "========================================================================" -ForegroundColor Cyan
    Write-Host "  NAIA 24-Hour Learning Test - Live Monitor Dashboard" -ForegroundColor Cyan
    Write-Host "  Uptime: $($uptime.Days)d $($uptime.Hours)h $($uptime.Minutes)m $($uptime.Seconds)s | Refresh: ${RefreshSeconds}s | Iteration: $iteration" -ForegroundColor Cyan
    Write-Host "========================================================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Process Status
    Write-Host "[PROCESS STATUS]" -ForegroundColor Yellow
    $procs = Get-ProcessStatus
    Write-Host "  API Service:                $(if ($procs.APIRunning) { '[OK]' } else { '[STOPPED]' })" -ForegroundColor $(if ($procs.APIRunning) { 'Green' } else { 'Red' })
    Write-Host "  Ingestion Worker:           $(if ($procs.IngestionRunning) { '[OK]' } else { '[STOPPED]' })" -ForegroundColor $(if ($procs.IngestionRunning) { 'Green' } else { 'Red' })
    Write-Host ""
    
    # Data Flow Metrics
    Write-Host "[DATA FLOW - TIME SERIES DATA]" -ForegroundColor Yellow
    $questDb = Get-QuestDBMetrics
    if ($questDb) {
        Write-Host "  Total Records (QuestDB):    $($questDb.TotalRecords)" -ForegroundColor Green
        Write-Host "  Unique Points:              $($questDb.UniquePoints)" -ForegroundColor Green
        Write-Host "  Latest Timestamp:           $($questDb.LatestTimestamp)" -ForegroundColor Green
    } else {
        Write-Host "  Could not connect to QuestDB" -ForegroundColor Red
    }
    Write-Host ""
    
    # Pattern Engine Metrics
    Write-Host "[PATTERN ENGINE - ANALYTICS]" -ForegroundColor Yellow
    $postgres = Get-PostgreSQLMetrics
    if ($postgres) {
        Write-Host "  Active Patterns:            $($postgres.PatternCount)" -ForegroundColor Green
        Write-Host "  Behavioral Clusters:        $($postgres.ClusterCount)" -ForegroundColor Green
        Write-Host "  Pending Suggestions:        $($postgres.SuggestionCount)" -ForegroundColor $(if ($postgres.SuggestionCount -gt 0) { 'Cyan' } else { 'Gray' })
    } else {
        Write-Host "  Could not connect to PostgreSQL" -ForegroundColor Red
    }
    Write-Host ""
    
    # Hangfire Jobs Status
    Write-Host "[HANGFIRE JOB SCHEDULER]" -ForegroundColor Yellow
    Write-Host "  Dashboard:                  http://localhost:5052/hangfire" -ForegroundColor Blue
    Write-Host "  (Open in browser for detailed job execution history)" -ForegroundColor Gray
    Write-Host ""
    
    # Data Ingestion Status
    Write-Host "[API ENDPOINTS]" -ForegroundColor Yellow
    Write-Host "  Start Ingestion:            POST http://localhost:5052/api/ingestion/start" -ForegroundColor Gray
    Write-Host "  Stop Ingestion:             POST http://localhost:5052/api/ingestion/stop" -ForegroundColor Gray
    Write-Host "  Get Metrics:                GET  http://localhost:5052/api/ingestion/status" -ForegroundColor Gray
    Write-Host ""
    
    # Expected Progress
    Write-Host "[24-HOUR TEST EXPECTATIONS]" -ForegroundColor Yellow
    Write-Host "  Phase 1 (0-2h):   Initial data collection, behavioral fingerprints calculated" -ForegroundColor Gray
    Write-Host "  Phase 2 (2-6h):   Correlations detected between MLR1 points" -ForegroundColor Gray
    Write-Host "  Phase 3 (6-12h):  Equipment clusters formed, patterns matched" -ForegroundColor Gray
    Write-Host "  Phase 4 (12-24h): User feedback learning improves pattern confidence" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "Refreshing in $RefreshSeconds seconds... (Press Ctrl+C to stop)" -ForegroundColor Gray
    Write-Host ""
    
    Start-Sleep -Seconds $RefreshSeconds
}
