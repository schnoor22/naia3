# NAIA v3 Infrastructure Connectivity Test
Write-Host "NAIA v3 - Infrastructure Connectivity Test" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Test PostgreSQL
try {
    $result = docker exec naia-postgres psql -U naia -d naia -tAc "SELECT COUNT(*) FROM points"
    Write-Host "✅ PostgreSQL: Connected (Points table: $result records)" -ForegroundColor Green
} catch {
    Write-Host "❌ PostgreSQL: $_" -ForegroundColor Red
}

# Test Redis
try {
    $result = docker exec naia-redis redis-cli ping
    Write-Host "✅ Redis: Connected ($result)" -ForegroundColor Green
} catch {
    Write-Host "❌ Redis: $_" -ForegroundColor Red
}

# Test Kafka
try {
    $result = docker exec naia-kafka kafka-topics --list --bootstrap-server localhost:29092 2>$null
    $topicCount = ($result | Measure-Object -Line).Lines
    Write-Host "✅ Kafka: Connected ($topicCount topics available)" -ForegroundColor Green
} catch {
    Write-Host "❌ Kafka: $_" -ForegroundColor Red
}

# Test QuestDB
try {
    $result = Invoke-WebRequest -Uri "http://localhost:9000/exec?query=SELECT COUNT(*) FROM point_data" -UseBasicParsing
    Write-Host "✅ QuestDB: Connected (HTTP API responding)" -ForegroundColor Green
} catch {
    Write-Host "❌ QuestDB: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "✨ Infrastructure check complete!" -ForegroundColor Green
