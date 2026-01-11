# Test PI Web API endpoints to see what's available
$baseUrl = "https://SDHQPIVWEB02.enxco.com/piwebapi"

# Bypass SSL validation
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

Write-Host "=== Testing PI Web API Endpoints ===" -ForegroundColor Cyan
Write-Host ""

# Test 1: Root endpoint
Write-Host "[1] Testing root endpoint..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/" -Method Get -UseDefaultCredentials
    Write-Host "✓ Root endpoint works" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 2 | Write-Host
} catch {
    Write-Host "✗ Root endpoint failed: $_" -ForegroundColor Red
}

Write-Host ""

# Test 2: /points endpoint
Write-Host "[2] Testing /points endpoint..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/points" -Method Get -UseDefaultCredentials
    Write-Host "✓ /points endpoint works, returned $($response.Items.Count) items" -ForegroundColor Green
    $response.Items | Select-Object -First 5 | ConvertTo-Json | Write-Host
} catch {
    Write-Host "✗ /points endpoint failed: $_" -ForegroundColor Red
}

Write-Host ""

# Test 3: /dataservers endpoint
Write-Host "[3] Testing /dataservers endpoint..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/dataservers" -Method Get -UseDefaultCredentials
    Write-Host "✓ /dataservers endpoint works" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 3 | Write-Host
} catch {
    Write-Host "✗ /dataservers endpoint failed: $_" -ForegroundColor Red
}

Write-Host ""

# Test 4: Try to find sdhqpisrvr01
Write-Host "[4] Testing specific data server (sdhqpisrvr01)..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/dataservers?name=sdhqpisrvr01" -Method Get -UseDefaultCredentials
    Write-Host "✓ Found data server" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 3 | Write-Host
} catch {
    Write-Host "✗ Query failed: $_" -ForegroundColor Red
}

Write-Host ""

# Test 5: /assetservers (AF)
Write-Host "[5] Testing /assetservers (Asset Framework)..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/assetservers" -Method Get -UseDefaultCredentials
    Write-Host "OK - /assetservers endpoint works" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 3 | Write-Host
}
catch {
    Write-Host "FAILED - /assetservers endpoint error: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan
