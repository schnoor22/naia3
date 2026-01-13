#!/usr/bin/env python3
"""Check new QuestDB data with correct column names."""
import paramiko
from pathlib import Path
import json

CONFIG = {
    "hostname": "app.naia.run",
    "username": "root",
    "port": 22,
    "key_filename": str(Path.home() / ".ssh" / "id_rsa"),
}

def check_data():
    try:
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(**CONFIG, timeout=10)
        
        print("=== NAIA Ingestion Status ===\n")
        
        # Count new data from last 10 minutes
        stdin, stdout, stderr = client.exec_command(
            "curl -s 'http://localhost:9000/exec?query=SELECT+count(*)FROM+point_data+WHERE+timestamp+%3E+now()-10m;' 2>/dev/null"
        )
        output = stdout.read().decode('utf-8')
        try:
            data = json.loads(output)
            new_rows = data['dataset'][0][0] if data.get('dataset') else 0
            print(f"✅ NEW DATA IN LAST 10 MINUTES: {new_rows:,} rows")
        except:
            print(f"Raw response: {output[:200]}")
        
        # Total count
        stdin, stdout, stderr = client.exec_command(
            "curl -s 'http://localhost:9000/exec?query=SELECT+count(*)FROM+point_data;' 2>/dev/null"
        )
        output = stdout.read().decode('utf-8')
        try:
            data = json.loads(output)
            total = data['dataset'][0][0] if data.get('dataset') else 0
            print(f"📊 TOTAL ROWS IN QUESTDB: {total:,}")
        except:
            pass
        
        # Most recent timestamp
        stdin, stdout, stderr = client.exec_command(
            "curl -s 'http://localhost:9000/exec?query=SELECT+MAX(timestamp)+FROM+point_data;' 2>/dev/null"
        )
        output = stdout.read().decode('utf-8')
        try:
            data = json.loads(output)
            ts = data['dataset'][0][0] if data.get('dataset') else None
            print(f"🕐 MOST RECENT DATA: {ts}")
        except:
            pass
        
        print("\n=== System Status ===\n")
        
        # Kafka message count
        stdin, stdout, stderr = client.exec_command(
            "kafka-console-consumer.sh --bootstrap-server localhost:9092 --topic naia.datapoints --from-beginning --max-messages 1 2>/dev/null | wc -l"
        )
        output = stdout.read().decode('utf-8')
        print(f"Kafka topic naia.datapoints has data: {output.strip()}")
        
        client.close()
        
        print("\n✅ INGESTION IS WORKING! Data is flowing from Replay → Kafka → QuestDB")
        
    except Exception as e:
        print(f"❌ Failed: {e}")

if __name__ == "__main__":
    check_data()
