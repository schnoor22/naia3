# Update appsettings.json to use solar farm auto-discovery instead of explicit mappings

$appsettingsPath = "C:\naia3\publish-ingestion\appsettings.json"

# Read and parse JSON
$config = Get-Content $appsettingsPath -Raw | ConvertFrom-Json

# Replace GenericCsvReplay configuration with auto-discovery version
$config.Connectors.GenericCsvReplay = @{
    Enabled = $true
    AutoDiscoverFiles = $true
    SpeedMultiplier = 2.0
    LoopReplay = $true
    ShiftToCurrentTime = $true
    UseTimeOfDayMatching = $true
    PublishIntervalSeconds = 60
    KafkaTopic = "naia.datapoints"
    Sites = @(
        @{
            SiteId = "aaaaaaaa-1111-4e87-9b82-111111111111"
            SiteName = "Pendleton Solar Farm"
            DataDirectory = "/opt/naia/data/solar/pendleton"
            TagPrefix = "PND_"
        },
        @{
            SiteId = "bbbbbbbb-2222-4e87-9b82-222222222222"
            SiteName = "Bluewater Solar Farm"
            DataDirectory = "/opt/naia/data/solar/bluewater"
            TagPrefix = "BLW_"
        }
    )
}

# Save back to file
$config | ConvertTo-Json -Depth 10 | Set-Content $appsettingsPath -Encoding UTF8

Write-Host "Updated appsettings.json with solar farm auto-discovery configuration"
Write-Host "Pendleton Solar Farm: /opt/naia/data/solar/pendleton"
Write-Host "Bluewater Solar Farm: /opt/naia/data/solar/bluewater"
Write-Host "Time-of-day matching enabled"
Write-Host "Publish interval: 60 seconds"
