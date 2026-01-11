#!/usr/bin/env pwsh
# Execute SQL script in PostgreSQL container

$scriptPath = "c:\naia3\create_mlr1_points.sql"
$containerName = "naia-postgres"
$containerPath = "/tmp/create_mlr1_points.sql"

# Copy file to container
Write-Host "Copying SQL script to container..."
docker cp $scriptPath "${containerName}:${containerPath}"

# Execute SQL
Write-Host "Executing SQL in PostgreSQL..."
docker exec $containerName psql -U naia -d naia -f $containerPath

Write-Host "Done!"
