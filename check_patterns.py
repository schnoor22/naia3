#!/usr/bin/env python3
import subprocess
import json

cmd = [
    "ssh", "root@37.27.189.86",
    "docker exec naia-postgres psql -U naia -d naia -t -A -F'|' -c \"SELECT s.id, p.name, s.overall_confidence, s.status FROM pattern_suggestions s JOIN patterns p ON s.pattern_id = p.id ORDER BY s.created_at DESC\""
]

result = subprocess.run(cmd, capture_output=True, text=True)
print("STDOUT:", result.stdout)
print("STDERR:", result.stderr)
print("Return code:", result.returncode)
