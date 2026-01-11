# NAIA Web UI - Build and Deploy Script
# This script builds the SvelteKit frontend and copies it to the API's wwwroot folder

param(
    [switch]$Install,  # Run npm install first
    [switch]$Dev,      # Start dev server instead of building
    [switch]$Watch     # Watch for changes and rebuild
)

$ErrorActionPreference = "Stop"
$webDir = $PSScriptRoot
$apiWwwRoot = Join-Path (Split-Path $webDir) "Naia.Api\wwwroot"

Write-Host "NAIA Web UI Build Script" -ForegroundColor Cyan
Write-Host ("=" * 40)

# Navigate to web directory
Push-Location $webDir

try {
    # Install dependencies if requested or node_modules doesn't exist
    if ($Install -or !(Test-Path "node_modules")) {
        Write-Host "`nInstalling dependencies..." -ForegroundColor Yellow
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
    }

    if ($Dev) {
        Write-Host "`nStarting development server..." -ForegroundColor Green
        Write-Host "   UI will be available at: http://localhost:5173" -ForegroundColor Gray
        Write-Host "   API proxy configured to: http://localhost:5052" -ForegroundColor Gray
        Write-Host "`n   Press Ctrl+C to stop`n" -ForegroundColor Gray
        npm run dev
    }
    else {
        # Build for production
        Write-Host "`nBuilding for production..." -ForegroundColor Yellow
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "Build failed" }

        # Ensure wwwroot exists
        if (!(Test-Path $apiWwwRoot)) {
            New-Item -ItemType Directory -Path $apiWwwRoot -Force | Out-Null
        }

        # Clear old files (except README.md)
        Write-Host "`nClearing old files from wwwroot..." -ForegroundColor Yellow
        Get-ChildItem $apiWwwRoot -Exclude "README.md" | Remove-Item -Recurse -Force

        # Copy build output
        Write-Host "Copying build output to API wwwroot..." -ForegroundColor Yellow
        Copy-Item -Path "build\*" -Destination $apiWwwRoot -Recurse -Force

        # Count files
        $fileCount = (Get-ChildItem $apiWwwRoot -Recurse -File).Count
        
        Write-Host "`nBuild complete!" -ForegroundColor Green
        Write-Host "   Output: $apiWwwRoot" -ForegroundColor Gray
        Write-Host "   Files: $fileCount" -ForegroundColor Gray
        Write-Host "`n   Start the API and navigate to http://localhost:5052" -ForegroundColor Cyan
    }
}
finally {
    Pop-Location
}
