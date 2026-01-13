#!/bin/bash
# Deploy NAIA API with corrected connection strings

set -e

echo "╔════════════════════════════════════════╗"
echo "║ NAIA API Deployment (Docker Fix)       ║"
echo "╚════════════════════════════════════════╝"

cd /opt/naia

# Update connection strings
echo "[1/4] Updating connection strings..."
cat > src/Naia.Api/appsettings.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning",
      "Naia": "Debug"
    }
  },
  "AllowedHosts": "*",
  
  "DefaultPointFilter": "*BESS*",
  "DefaultMaxPoints": 100,
  
  "ConnectionStrings": {
    "PostgreSql": "Host=postgres;Port=5432;Database=naia;Username=naia;Password=naia_dev_password;SslMode=Disable;Pooling=false"
  },
  
  "QuestDb": {
    "HttpEndpoint": "http://questdb:9000",
    "PgWireEndpoint": "questdb:8812",
    "TableName": "point_data",
    "AutoFlushIntervalMs": 1000,
    "AutoFlushRows": 10000
  },
  
  "Redis": {
    "ConnectionString": "redis:6379",
    "CurrentValueTtlSeconds": 3600,
    "IdempotencyTtlSeconds": 86400
  },
  
  "Kafka": {
    "BootstrapServers": "kafka:29092",
    "DataPointsTopic": "naia.datapoints",
    "BackfillTopic": "naia.datapoints.backfill",
    "DlqTopic": "naia.datapoints.dlq",
    "ConsumerGroupId": "naia-historians",
    "ProducerClientId": "naia-api-producer",
    "ConsumerClientIdPrefix": "naia-consumer",
    "SessionTimeoutMs": 30000,
    "HeartbeatIntervalMs": 10000,
    "MaxPollIntervalMs": 300000,
    "DataPointsPartitions": 12,
    "ReplicationFactor": 1
  },
  
  "Pipeline": {
    "PollTimeoutMs": 1000,
    "RetryDelayMs": 1000,
    "MaxRetries": 3,
    "DeadLetterQueueTtlMs": 2592000000
  },
  
  "BatchIngest": {
    "BatchSizeRows": 10000,
    "BatchTimeoutMs": 5000,
    "MaxConcurrentBatches": 10
  },
  
  "PatternEngine": {
    "Enabled": true,
    "CheckIntervalMs": 300000,
    "MaxDegreeOfParallelism": 4,
    "CorrelationMinSimilarity": 0.70
  },
  
  "RateLimiting": {
    "Enabled": true,
    "RequestsPerSecond": 100,
    "BurstSize": 200
  }
}
EOF

# Publish
echo "[2/4] Publishing application..."
dotnet publish Naia.sln -c Release -o ./publish --no-restore 2>&1 | grep -E "Naia.Api|error" || true

# Stop service
echo "[3/4] Restarting service..."
systemctl stop naia-api 2>/dev/null || true
sleep 2

systemctl start naia-api
sleep 5

# Verify
echo "[4/4] Verifying..."
if curl -s http://localhost:5000/api/health > /dev/null 2>&1; then
    echo "✅ API is responding!"
    curl -s http://localhost:5000/api/health | head -c 200
    echo ""
else
    echo "⚠️  API not responding yet. Checking logs..."
    journalctl -u naia-api -n 50 --no-pager | tail -20
fi
