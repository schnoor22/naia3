# Quick Setup Script for NAIA Web UI
# Run this to install dependencies and start the dev server

Write-Host "🐬 NAIA Web UI Quick Setup" -ForegroundColor Cyan

# Check for Node.js
if (!(Get-Command npm -ErrorAction SilentlyContinue)) {
    Write-Host "❌ Node.js not found. Please install Node.js 18+ from https://nodejs.org" -ForegroundColor Red
    exit 1
}

$nodeVersion = (node --version)
Write-Host "✓ Node.js: $nodeVersion" -ForegroundColor Green

# Navigate to Naia.Web
Push-Location $PSScriptRoot\src\Naia.Web

try {
    # Install dependencies
    if (!(Test-Path "node_modules")) {
        Write-Host "`n📦 Installing dependencies..." -ForegroundColor Yellow
        npm install
    } else {
        Write-Host "✓ Dependencies already installed" -ForegroundColor Green
    }

    # Check for logo file
    if (!(Test-Path "static\logo.png")) {
        Write-Host "`n⚠️  Logo file missing!" -ForegroundColor Yellow
        Write-Host "   Please copy your NAIA logo to: src\Naia.Web\static\logo.png" -ForegroundColor Gray
    }

    Write-Host "`n🚀 Starting development server..." -ForegroundColor Green
    Write-Host ""
    Write-Host "   ┌─────────────────────────────────────────────┐" -ForegroundColor DarkGray
    Write-Host "   │  NAIA Command Center                        │" -ForegroundColor Cyan
    Write-Host "   │                                             │" -ForegroundColor DarkGray
    Write-Host "   │  UI:  http://localhost:5173                 │" -ForegroundColor White
    Write-Host "   │  API: http://localhost:5000 (start separately)│" -ForegroundColor Gray
    Write-Host "   │                                             │" -ForegroundColor DarkGray
    Write-Host "   │  Press Ctrl+C to stop                       │" -ForegroundColor DarkGray
    Write-Host "   └─────────────────────────────────────────────┘" -ForegroundColor DarkGray
    Write-Host ""
    
    npm run dev
}
finally {
    Pop-Location
}
