# CSV Site Data Preprocessing Script
# Scans CSV files, extracts tag names, applies transformations, generates config

param(
    [Parameter(Mandatory=$true)]
    [string]$SiteDir,
    
    [Parameter(Mandatory=$true)]
    [string]$SiteId,
    
    [Parameter(Mandatory=$true)]
    [string]$SiteName,
    
    [Parameter(Mandatory=$true)]
    [string]$Timezone,
    
    [Parameter(Mandatory=$false)]
    [int]$StripPrefix = 0,
    
    [Parameter(Mandatory=$false)]
    [string]$OutputDir = "."
)

Write-Host "================================================================" -ForegroundColor Green
Write-Host "  CSV Site Data Preprocessing" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""

# Validate site directory
if (-not (Test-Path $SiteDir)) {
    Write-Host "ERROR: Site directory not found: $SiteDir" -ForegroundColor Red
    exit 1
}

# Scan for CSV files
Write-Host "Scanning for CSV files in: $SiteDir" -ForegroundColor Cyan
$csvFiles = Get-ChildItem -Path $SiteDir -Filter "*.csv" -Recurse
Write-Host "Found $($csvFiles.Count) CSV files" -ForegroundColor Green
Write-Host ""

if ($csvFiles.Count -eq 0) {
    Write-Host "ERROR: No CSV files found in $SiteDir" -ForegroundColor Red
    exit 1
}

# Extract tag names
Write-Host "Extracting tag names from filenames..." -ForegroundColor Cyan
$tags = @()
foreach ($file in $csvFiles) {
    $originalTag = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    
    # Apply prefix stripping
    $processedTag = $originalTag
    if ($StripPrefix -gt 0 -and $originalTag.Length -gt $StripPrefix) {
        $processedTag = $originalTag.Substring($StripPrefix)
        Write-Host "  $originalTag -> $processedTag (stripped $StripPrefix chars)" -ForegroundColor Gray
    }
    
    # Add site prefix
    $finalTag = "${SiteId}_${processedTag}"
    
    $tags += [PSCustomObject]@{
        OriginalTag = $originalTag
        ProcessedTag = $processedTag
        FinalTag = $finalTag
        FilePath = $file.FullName
    }
}

Write-Host ""
Write-Host "Processed $($tags.Count) unique tags" -ForegroundColor Green
Write-Host ""

# Generate appsettings snippet
Write-Host "Generating appsettings configuration..." -ForegroundColor Cyan

$config = @{
    SiteId = $SiteId
    SiteName = $SiteName
    Enabled = $true
    DataDirectory = $SiteDir
    Timezone = $Timezone
    StripPrefixLength = $StripPrefix
    TagPrefix = "${SiteId}_"
    TagNameSource = "Filename"
    BadStatusHandling = "Skip"
    CsvFormat = @{
        Delimiter = ","
        TimestampColumn = "Timestamp"
        ValueColumn = "Value"
        StatusColumn = "Status"
        SkipRowsBeforeHeader = 0
    }
}

# Convert to JSON
$jsonConfig = $config | ConvertTo-Json -Depth 10

# Save to file
$configPath = Join-Path $OutputDir "site-config-${SiteId}.json"
$jsonConfig | Out-File -FilePath $configPath -Encoding UTF8

Write-Host "Configuration saved to: $configPath" -ForegroundColor Green
Write-Host ""

# Generate sample tags list
$tagsPath = Join-Path $OutputDir "site-tags-${SiteId}.txt"
$tags | ForEach-Object { $_.FinalTag } | Out-File -FilePath $tagsPath -Encoding UTF8
Write-Host "Tag list saved to: $tagsPath" -ForegroundColor Green
Write-Host ""

# Show summary
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Summary" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  CSV Files: $($csvFiles.Count)" -ForegroundColor White
Write-Host "  Unique Tags: $($tags.Count)" -ForegroundColor White
Write-Host "  Config File: $configPath" -ForegroundColor White
Write-Host "  Tags File: $tagsPath" -ForegroundColor White
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""

# Show example tags
Write-Host "Example tags (first 10):" -ForegroundColor Cyan
$tags | Select-Object -First 10 | ForEach-Object {
    Write-Host "  $($_.OriginalTag) -> $($_.FinalTag)" -ForegroundColor Gray
}
Write-Host ""

# Show appsettings snippet
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "  Copy this into your appsettings.json:" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host ""
Write-Host $jsonConfig -ForegroundColor White
Write-Host ""
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host ""

Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Review the generated config: $configPath" -ForegroundColor White
Write-Host "  2. Copy the above JSON into your appsettings.json Sites array" -ForegroundColor White
Write-Host "  3. Deploy Naia.Ingestion to start replay" -ForegroundColor White
Write-Host "  4. Monitor logs to verify auto-registration" -ForegroundColor White
Write-Host ""
