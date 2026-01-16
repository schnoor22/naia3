<# 
.SYNOPSIS
    NAIA Nuclear Cleanup Script - Completely removes ALL NAIA-related files from this machine.
    
.DESCRIPTION
    This script performs a deep clean of:
    - All NAIA source code directories
    - Docker containers, images, and volumes
    - AppData caches and configs
    - Environment variables
    - Registry entries
    - OneDrive synced folders
    - VS Code workspace caches
    - .NET build artifacts
    - npm/node_modules caches
    - SSH known_hosts entries
    
.NOTES
    Run as Administrator for full cleanup.
    THIS IS DESTRUCTIVE AND IRREVERSIBLE.
    
.EXAMPLE
    .\NUKE_NAIA.ps1 -Confirm
#>

param(
    [switch]$Confirm,
    [switch]$DryRun,
    [switch]$SkipDocker,
    [switch]$SkipRegistry
)

$ErrorActionPreference = "Continue"

# Colors for output
function Write-Header($text) { Write-Host "`n═══════════════════════════════════════════════════════════════" -ForegroundColor Red; Write-Host "  $text" -ForegroundColor Red; Write-Host "═══════════════════════════════════════════════════════════════`n" -ForegroundColor Red }
function Write-Step($text) { Write-Host "→ $text" -ForegroundColor Yellow }
function Write-Done($text) { Write-Host "✓ $text" -ForegroundColor Green }
function Write-Skip($text) { Write-Host "○ $text (skipped)" -ForegroundColor DarkGray }
function Write-Warn($text) { Write-Host "! $text" -ForegroundColor Magenta }

# Confirm before proceeding
if (-not $Confirm) {
    Write-Header "☢️  NAIA NUCLEAR CLEANUP SCRIPT  ☢️"
    Write-Host @"
This script will PERMANENTLY DELETE all NAIA-related files including:

  • C:\naia3, C:\naia, C:\dev\naia and all subdirectories
  • OneDrive NAIA folders
  • Docker containers, images, and volumes with 'naia' in the name
  • AppData caches (VS Code, npm, .NET)
  • Environment variables containing 'NAIA'
  • Registry entries containing 'naia'
  • SSH known_hosts entries for NAIA server

"@ -ForegroundColor White
    
    Write-Host "THIS CANNOT BE UNDONE!" -ForegroundColor Red -BackgroundColor Yellow
    Write-Host ""
    
    $response = Read-Host "Type 'NUKE NAIA' to confirm"
    if ($response -ne "NUKE NAIA") {
        Write-Host "`nAborted. No changes made." -ForegroundColor Cyan
        exit 0
    }
    Write-Host ""
}

$deletedItems = @()
$failedItems = @()

function Remove-ItemSafely($path, $description) {
    if ($DryRun) {
        Write-Step "[DRY RUN] Would delete: $path"
        return
    }
    
    if (Test-Path $path) {
        try {
            Remove-Item -Path $path -Recurse -Force -ErrorAction Stop
            Write-Done "Deleted: $description ($path)"
            $script:deletedItems += $path
        }
        catch {
            Write-Warn "Failed to delete: $path - $($_.Exception.Message)"
            $script:failedItems += $path
        }
    }
    else {
        Write-Skip "$description not found"
    }
}

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 1: Stopping Processes"
# ═══════════════════════════════════════════════════════════════

Write-Step "Stopping .NET processes..."
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Done "Stopped dotnet processes"

Write-Step "Stopping Node.js processes..."
Get-Process -Name "node" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Done "Stopped node processes"

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 2: Docker Cleanup"
# ═══════════════════════════════════════════════════════════════

if (-not $SkipDocker) {
    Write-Step "Stopping NAIA Docker containers..."
    $containers = docker ps -a --filter "name=naia" --format "{{.Names}}" 2>$null
    if ($containers) {
        docker stop $containers 2>$null
        docker rm -f $containers 2>$null
        Write-Done "Removed containers: $($containers -join ', ')"
    } else {
        Write-Skip "No NAIA containers found"
    }

    Write-Step "Removing NAIA Docker volumes..."
    $volumes = docker volume ls --filter "name=naia" --format "{{.Name}}" 2>$null
    if ($volumes) {
        docker volume rm -f $volumes 2>$null
        Write-Done "Removed volumes: $($volumes -join ', ')"
    } else {
        Write-Skip "No NAIA volumes found"
    }

    Write-Step "Removing NAIA Docker images..."
    $images = docker images --filter "reference=*naia*" --format "{{.Repository}}:{{.Tag}}" 2>$null
    if ($images) {
        docker rmi -f $images 2>$null
        Write-Done "Removed images: $($images -join ', ')"
    } else {
        Write-Skip "No NAIA images found"
    }

    Write-Step "Pruning Docker system..."
    docker system prune -f 2>$null
    Write-Done "Docker system pruned"
} else {
    Write-Skip "Docker cleanup (--SkipDocker)"
}

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 3: Source Code Directories"
# ═══════════════════════════════════════════════════════════════

# Main NAIA directories
Remove-ItemSafely "C:\naia3" "NAIA v3 source"
Remove-ItemSafely "C:\naia" "NAIA v1/v2 source"
Remove-ItemSafely "C:\dev\naia" "NAIA dev directory"
Remove-ItemSafely "C:\naia4" "NAIA v4 source (if exists)"
Remove-ItemSafely "C:\projects\naia" "NAIA projects directory"

# OneDrive paths (common locations)
$oneDrivePath = [Environment]::GetFolderPath("UserProfile") + "\OneDrive"
$oneDriveBusinessPath = [Environment]::GetFolderPath("UserProfile") + "\OneDrive - *"

# Check standard OneDrive
if (Test-Path $oneDrivePath) {
    Get-ChildItem -Path $oneDrivePath -Directory -Recurse -ErrorAction SilentlyContinue | 
        Where-Object { $_.Name -match "naia" } | 
        ForEach-Object { Remove-ItemSafely $_.FullName "OneDrive NAIA folder" }
}

# Check OneDrive for Business
Get-ChildItem -Path ([Environment]::GetFolderPath("UserProfile")) -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "OneDrive*" } |
    ForEach-Object {
        Get-ChildItem -Path $_.FullName -Directory -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "naia" } |
            ForEach-Object { Remove-ItemSafely $_.FullName "OneDrive Business NAIA folder" }
    }

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 4: AppData & Caches"
# ═══════════════════════════════════════════════════════════════

$localAppData = [Environment]::GetFolderPath("LocalApplicationData")
$roamingAppData = [Environment]::GetFolderPath("ApplicationData")

# VS Code workspaces
Remove-ItemSafely "$roamingAppData\Code\User\workspaceStorage\*naia*" "VS Code NAIA workspace cache"

# Search for NAIA in VS Code workspace storage
Get-ChildItem -Path "$roamingAppData\Code\User\workspaceStorage" -Directory -ErrorAction SilentlyContinue |
    ForEach-Object {
        $wsFile = Join-Path $_.FullName "workspace.json"
        if (Test-Path $wsFile) {
            $content = Get-Content $wsFile -Raw -ErrorAction SilentlyContinue
            if ($content -match "naia") {
                Remove-ItemSafely $_.FullName "VS Code workspace storage (NAIA)"
            }
        }
    }

# .NET build caches
Remove-ItemSafely "$localAppData\Microsoft\dotnet" ".NET SDK cache (CAUTION: affects all .NET projects)"
Remove-ItemSafely "$env:USERPROFILE\.nuget\packages\*naia*" "NuGet NAIA packages"

# Node modules global cache
Remove-ItemSafely "$roamingAppData\npm-cache\*naia*" "npm NAIA cache"

# OPC Foundation certificates
Remove-ItemSafely "$localAppData\OPC Foundation" "OPC Foundation certificates"

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 5: Environment Variables"
# ═══════════════════════════════════════════════════════════════

Write-Step "Removing NAIA environment variables..."
$envVars = @("NAIA_SERVER", "NAIA_SSH_USER", "NAIA_LOCAL", "NAIA_DEPLOY", "NAIA_PG_HOST", "NAIA_QUESTDB_HOST", "NAIA_REDIS_HOST", "NAIA_SITE_ID", "NAIA_SITE_NAME", "NAIA_SITE_TYPE", "NAIA_OPC_PORT")

foreach ($var in $envVars) {
    # User level
    $userVal = [Environment]::GetEnvironmentVariable($var, "User")
    if ($userVal) {
        if (-not $DryRun) {
            [Environment]::SetEnvironmentVariable($var, $null, "User")
        }
        Write-Done "Removed user env: $var"
    }
    
    # Machine level (requires admin)
    $machineVal = [Environment]::GetEnvironmentVariable($var, "Machine")
    if ($machineVal) {
        try {
            if (-not $DryRun) {
                [Environment]::SetEnvironmentVariable($var, $null, "Machine")
            }
            Write-Done "Removed machine env: $var"
        }
        catch {
            Write-Warn "Failed to remove machine env: $var (run as admin)"
        }
    }
}

# Also search for any env var containing NAIA
Get-ChildItem env: | Where-Object { $_.Name -match "naia" -or $_.Value -match "naia" } | ForEach-Object {
    Write-Warn "Found env var with NAIA reference: $($_.Name) = $($_.Value)"
}

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 6: Registry Cleanup"
# ═══════════════════════════════════════════════════════════════

if (-not $SkipRegistry) {
    Write-Step "Searching registry for NAIA entries..."
    
    $regPaths = @(
        "HKCU:\Software",
        "HKLM:\Software"
    )
    
    foreach ($regPath in $regPaths) {
        Get-ChildItem -Path $regPath -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "naia" } |
            ForEach-Object {
                Write-Warn "Found registry key: $($_.Name)"
                if (-not $DryRun) {
                    try {
                        Remove-Item -Path $_.PSPath -Recurse -Force
                        Write-Done "Removed registry key: $($_.Name)"
                    }
                    catch {
                        Write-Warn "Failed to remove: $($_.Name)"
                    }
                }
            }
    }
} else {
    Write-Skip "Registry cleanup (--SkipRegistry)"
}

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 7: SSH Known Hosts"
# ═══════════════════════════════════════════════════════════════

$knownHostsFile = "$env:USERPROFILE\.ssh\known_hosts"
if (Test-Path $knownHostsFile) {
    Write-Step "Removing NAIA server from known_hosts..."
    $content = Get-Content $knownHostsFile -ErrorAction SilentlyContinue
    $newContent = $content | Where-Object { $_ -notmatch "37.27.189.86" -and $_ -notmatch "app.naia.run" }
    
    if ($content.Count -ne $newContent.Count) {
        if (-not $DryRun) {
            $newContent | Set-Content $knownHostsFile
        }
        Write-Done "Removed NAIA server entries from known_hosts"
    } else {
        Write-Skip "No NAIA entries in known_hosts"
    }
}

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 8: Git Credentials"
# ═══════════════════════════════════════════════════════════════

Write-Step "Checking for NAIA git credentials..."
$gitCredentialHelper = git config --global credential.helper 2>$null
if ($gitCredentialHelper) {
    Write-Warn "Git credential helper is: $gitCredentialHelper"
    Write-Warn "You may want to manually clear credentials for NAIA repos"
}

# ═══════════════════════════════════════════════════════════════
Write-Header "PHASE 9: Recent Files & Jump Lists"
# ═══════════════════════════════════════════════════════════════

Write-Step "Cleaning Windows recent files..."
$recentPath = "$env:APPDATA\Microsoft\Windows\Recent"
Get-ChildItem -Path $recentPath -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match "naia" } |
    ForEach-Object { Remove-ItemSafely $_.FullName "Recent file: $($_.Name)" }

# ═══════════════════════════════════════════════════════════════
Write-Header "CLEANUP COMPLETE"
# ═══════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  NAIA NUCLEAR CLEANUP COMPLETE" -ForegroundColor Green
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "Items deleted: $($deletedItems.Count)" -ForegroundColor White
Write-Host "Items failed:  $($failedItems.Count)" -ForegroundColor $(if ($failedItems.Count -gt 0) { "Red" } else { "White" })

if ($failedItems.Count -gt 0) {
    Write-Host "`nFailed items:" -ForegroundColor Red
    $failedItems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host "`nTry running as Administrator to remove protected items." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Recommended manual steps:" -ForegroundColor Cyan
Write-Host "  1. Clear browser history for app.naia.run" -ForegroundColor White
Write-Host "  2. Remove any bookmarks to NAIA" -ForegroundColor White
Write-Host "  3. Check cloud storage (Google Drive, Dropbox) for NAIA files" -ForegroundColor White
Write-Host "  4. Empty Recycle Bin" -ForegroundColor White
Write-Host ""
Write-Host "This machine is now NAIA-free. 🧹" -ForegroundColor Green
Write-Host ""
