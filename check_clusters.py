#!/usr/bin/env python3
import subprocess

cmd = [
    "ssh", "root@37.27.189.86",
    "docker exec naia-postgres psql -U naia -d naia -t -A -c \"SELECT COUNT(*) FROM behavioral_clusters WHERE cohesion IS NULL AND is_active = true\""
]

result = subprocess.run(cmd, capture_output=True, text=True)
print("Count with NULL cohesion:", result.stdout.strip())

cmd2 = [
    "ssh", "root@37.27.189.86",
    "docker exec naia-postgres psql -U naia -d naia -t -A -c \"SELECT id, cohesion, point_ids FROM behavioral_clusters WHERE is_active = true LIMIT 5\""
]

result2 = subprocess.run(cmd2, capture_output=True, text=True)
print("\nFirst 5 active clusters:")
print(result2.stdout)
