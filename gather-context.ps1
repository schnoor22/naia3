# NAIA AI Context Generator
# Generates a comprehensive markdown file with system state for AI troubleshooting sessions
# Usage: .\gather-context.ps1
# Output: CONTEXT_SNAPSHOT.md (paste this to your AI assistant for instant context)

param(
    [string]$OutputFile = "CONTEXT_SNAPSHOT.md",
    [switch]$IncludeLogs,       # Include recent logs (can be verbose)
    [int]$LogCount = 30         # Number of log entries to include
)

$ErrorActionPreference = "Continue"
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

# Server configuration
$SERVER = "37.27.189.86"
$SERVER_USER = "naia"
$API_URL = "https://app.naia.run"

function Get-RemoteCommand {
    param([string]$Command)
    try {
        # Use SSH with BatchMode and no password prompt - relies on SSH keys being configured
        $result = ssh -o BatchMode=yes -o ConnectTimeout=5 "${SERVER_USER}@${SERVER}" $Command 2>&1
        return $result -join "`n"
    } catch {
        return "SSH command failed: $_"
    }
}

function Get-ApiEndpoint {
    param([string]$Endpoint, [string]$BaseUrl = $API_URL)
    try {
        $response = Invoke-RestMethod -Uri "$BaseUrl$Endpoint" -TimeoutSec 10
        return $response | ConvertTo-Json -Depth 5
    } catch {
        return "API call failed: $($_.Exception.Message)"
    }
}

# Start building the context file
$sb = New-Object System.Text.StringBuilder

[void]$sb.AppendLine("# NAIA Context Snapshot")
[void]$sb.AppendLine("**Generated**: $timestamp")
[void]$sb.AppendLine("**Purpose**: Paste this entire file to your AI assistant for instant project context")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## NAIA Vision")
[void]$sb.AppendLine("**The First Industrial Historian That Learns From You**")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("NAIA represents a generational leap in industrial data management. While legacy systems (PI, Wonderware, Ignition) require manual modeling every single time, NAIA **remembers** how you organized your last 10 sites and suggests structure for site #11.")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## Architecture Overview")
[void]$sb.AppendLine("")
[void]$sb.AppendLine('```')
[void]$sb.AppendLine("USER/BROWSER --> CADDY (app.naia.run:443) --> NAIA.API (:5000)")
[void]$sb.AppendLine("                                                  |")
[void]$sb.AppendLine("                 +------------+------------+------+------+")
[void]$sb.AppendLine("                 |            |            |            |")
[void]$sb.AppendLine("            PostgreSQL    QuestDB       Redis       Kafka")
[void]$sb.AppendLine("              :5432        :9000        :6379       :9092")
[void]$sb.AppendLine("            (Metadata)  (TimeSeries)   (Cache)   (Messages)")
[void]$sb.AppendLine('```')
[void]$sb.AppendLine("")
[void]$sb.AppendLine("**Server**: 37.27.189.86 (Hetzner) | **Domain**: app.naia.run | **SSH**: naia@37.27.189.86")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## Key Paths")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("### Local (Windows)")
[void]$sb.AppendLine("| Path | Purpose |")
[void]$sb.AppendLine("|------|---------|")
[void]$sb.AppendLine("| C:\naia3\src\Naia.Api\ | .NET 8 API |")
[void]$sb.AppendLine("| C:\naia3\src\Naia.Web\ | Svelte 5 UI |")
[void]$sb.AppendLine("| C:\naia3\src\Naia.Web\build.ps1 | UI build script |")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("### Remote (Linux)")
[void]$sb.AppendLine("| Path | Purpose |")
[void]$sb.AppendLine("|------|---------|")
[void]$sb.AppendLine("| /opt/naia/ | Deployed API |")
[void]$sb.AppendLine("| /opt/naia/wwwroot/ | Deployed UI |")
[void]$sb.AppendLine("| /etc/caddy/Caddyfile | Reverse proxy |")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## Key API Endpoints")
[void]$sb.AppendLine("| Endpoint | Purpose |")
[void]$sb.AppendLine("|----------|---------|")
[void]$sb.AppendLine("| /api/health | System health |")
[void]$sb.AppendLine("| /api/version | Build info |")
[void]$sb.AppendLine("| /api/points | Point browser |")
[void]$sb.AppendLine("| /api/suggestions | Pattern suggestions |")
[void]$sb.AppendLine("| /swagger | API docs |")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("")

# Check if we can reach the server
Write-Host "Gathering NAIA system context..." -ForegroundColor Cyan

# Local Git Status
Write-Host "  Checking local git status..." -ForegroundColor Gray
[void]$sb.AppendLine("## Local Git Status")
if (Test-Path ".git") {
    $branch = git rev-parse --abbrev-ref HEAD 2>&1
    $commit = git rev-parse --short HEAD 2>&1
    $status = git status --porcelain 2>&1
    $changedFiles = ($status | Measure-Object).Count
    [void]$sb.AppendLine("**Branch**: $branch | **Commit**: $commit | **Changed Files**: $changedFiles")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine('```')
    if ($changedFiles -gt 0) { 
        $status | Select-Object -First 15 | ForEach-Object { [void]$sb.AppendLine($_) }
    } else { 
        [void]$sb.AppendLine("Working tree clean") 
    }
    [void]$sb.AppendLine('```')
} else {
    [void]$sb.AppendLine("Not a git repository")
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("")

# Remote Service Status
Write-Host "  Checking remote services..." -ForegroundColor Gray
[void]$sb.AppendLine("## Remote Service Status")
$serviceStatus = Get-RemoteCommand "systemctl is-active naia-api naia-ingestion caddy 2>&1"
[void]$sb.AppendLine('```')
[void]$sb.AppendLine($serviceStatus)
[void]$sb.AppendLine('```')
[void]$sb.AppendLine("")

# API Health Check
Write-Host "  Checking API health..." -ForegroundColor Gray
[void]$sb.AppendLine("## API Health")
$healthStatus = Get-ApiEndpoint "/api/health"
[void]$sb.AppendLine('```json')
[void]$sb.AppendLine($healthStatus)
[void]$sb.AppendLine('```')
[void]$sb.AppendLine("")

# Version Info
Write-Host "  Checking API version..." -ForegroundColor Gray
[void]$sb.AppendLine("## API Version")
$versionInfo = Get-ApiEndpoint "/api/version"
[void]$sb.AppendLine('```json')
[void]$sb.AppendLine($versionInfo)
[void]$sb.AppendLine('```')
[void]$sb.AppendLine("")

# Docker Status
Write-Host "  Checking Docker containers..." -ForegroundColor Gray
[void]$sb.AppendLine("## Docker Containers")
$dockerStatus = Get-RemoteCommand "docker ps --format 'table {{.Names}}\t{{.Status}}' 2>&1"
[void]$sb.AppendLine('```')
[void]$sb.AppendLine($dockerStatus)
[void]$sb.AppendLine('```')
[void]$sb.AppendLine("")

# Recent Logs (optional)
if ($IncludeLogs) {
    Write-Host "  Fetching recent logs..." -ForegroundColor Gray
    [void]$sb.AppendLine("## Recent API Logs (Last $LogCount)")
    $recentLogs = Get-RemoteCommand "journalctl -u naia-api -n $LogCount --no-pager 2>&1"
    [void]$sb.AppendLine('```')
    [void]$sb.AppendLine($recentLogs)
    [void]$sb.AppendLine('```')
    [void]$sb.AppendLine("")
}

# Quick reference section
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## Quick Commands")
[void]$sb.AppendLine("")
[void]$sb.AppendLine('```bash')
[void]$sb.AppendLine("# SSH to server")
[void]$sb.AppendLine("ssh naia@37.27.189.86")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("# Restart API")
[void]$sb.AppendLine("sudo systemctl restart naia-api")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("# View logs")
[void]$sb.AppendLine("sudo journalctl -u naia-api -f")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("# Docker status")
[void]$sb.AppendLine("docker ps")
[void]$sb.AppendLine('```')
[void]$sb.AppendLine("")
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## Reference Links")
[void]$sb.AppendLine("- QuestDB: https://questdb.com/docs/query/rest-api/")
[void]$sb.AppendLine("- Kafka: https://docs.confluent.io/kafka/kafka-apis.html")
[void]$sb.AppendLine("- Redis: https://redis.io/docs/latest/develop/reference/")
[void]$sb.AppendLine("- PostgreSQL: https://www.postgresql.org/docs/current/tutorial-advanced.html")
[void]$sb.AppendLine("- AF SDK: https://docs.aveva.com/bundle/af-sdk/page/html/af-sdk-overview.htm")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("*Generated by gather-context.ps1*")

# Write the output file
$sb.ToString() | Out-File -FilePath $OutputFile -Encoding UTF8

Write-Host ""
Write-Host "Context snapshot saved to: $OutputFile" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "   1. Open $OutputFile" -ForegroundColor Gray
Write-Host "   2. Copy the entire contents" -ForegroundColor Gray
Write-Host "   3. Paste to your AI assistant as context" -ForegroundColor Gray
Write-Host ""
