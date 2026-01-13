#!/bin/bash
# ============================================================================
# NAIA API Deployment Fix - Docker Connection Strings
# ============================================================================
# Run this script on the production server to fix connection strings and
# redeploy the API service.
#
# Usage: bash /tmp/fix-api.sh
# ============================================================================

set -e

echo "╔════════════════════════════════════════════════════════════╗"
echo "║ NAIA API Deployment - Docker Network Fix                  ║"
echo "╚════════════════════════════════════════════════════════════╝"
echo ""

cd /opt/naia

# STEP 1: Update connection strings for Docker networking
echo "[1/4] Updating connection strings..."
echo "  - PostgreSQL: localhost → postgres"
echo "  - QuestDB: localhost → questdb"
echo "  - Redis: localhost → redis"
echo "  - Kafka: localhost → kafka (internal port)"
echo ""

cat > src/Naia.Api/appsettings.json << 'APPSETTINGS'
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
APPSETTINGS

echo "✅ Connection strings updated"
echo ""

# STEP 2: Clean and publish
echo "[2/4] Publishing application..."
dotnet clean Naia.sln -c Release > /dev/null 2>&1 || true
dotnet publish Naia.sln -c Release -o ./publish --no-restore 2>&1 | grep -i "naia.api\|error" | head -5

if [ -f publish/Naia.Api.dll ]; then
    echo "✅ Published successfully"
else
    echo "❌ Publish failed - Naia.Api.dll not found"
    exit 1
fi
echo ""

# STEP 3: Restart service
echo "[3/4] Restarting naia-api service..."
systemctl stop naia-api 2>/dev/null || true
sleep 3
systemctl start naia-api
sleep 5

echo "✅ Service restarted"
echo ""

# STEP 4: Verify
echo "[4/4] Verifying service..."
echo ""

# Check systemd status
if systemctl is-active --quiet naia-api; then
    echo "✅ Service status: ACTIVE"
else
    echo "❌ Service status: INACTIVE"
fi

# Check health endpoint (with timeout)
if timeout 5 curl -s http://localhost:5000/api/health > /tmp/health.json 2>/dev/null; then
    echo "✅ Health endpoint: RESPONDING"
    echo "   Response: $(cat /tmp/health.json | head -c 100)..."
else
    echo "❌ Health endpoint: NOT RESPONDING"
    echo ""
    echo "Recent logs:"
    journalctl -u naia-api -n 30 --no-pager | tail -15
    exit 1
fi

echo ""
echo "╔════════════════════════════════════════════════════════════╗"
echo "║ ✅ Deployment Complete!                                   ║"
echo "╚════════════════════════════════════════════════════════════╝"
echo ""
echo "Next steps:"
echo "  1. Test locally:  curl http://localhost:5000/api/health"
echo "  2. Test remotely: curl https://app.naia.run/api/health"
echo "  3. Monitor logs:  journalctl -u naia-api -f"
echo ""
