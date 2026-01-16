#!/usr/bin/env python3
"""Import all discovered Brixton Solar points"""
import json
import requests
import sys

# Load discovered nodes from argument or file
if len(sys.argv) > 1:
    discover_file = sys.argv[1]
else:
    discover_file = '/tmp/discover.json'

with open(discover_file, 'r') as f:
    data = json.load(f)

# Extract all ns=2 data nodes from EquipmentGroups
nodes = []
for group in data.get('equipmentGroups', []):
    for node in group.get('nodes', []):
        node_id = node.get('nodeId', '')
        if node_id.startswith('ns=2;'):
            nodes.append({
                'nodeId': node_id,
                'nodePath': node.get('nodePath', ''),
                'browseName': node.get('browseName', ''),
                'displayName': node.get('displayName', ''),
                'dataType': node.get('dataType', 'Double'),
                'description': node.get('description', ''),
                'engineeringUnits': node.get('engineeringUnits', ''),
                'suggestedDescription': node.get('suggestedDescription', ''),
                'suggestedUnits': node.get('suggestedUnits', '')
            })

print(f'Found {len(nodes)} ns=2 nodes to import')

# Build request (importing all ns=2 nodes)
request_body = {
    'nodeIds': [n['nodeId'] for n in nodes],
    'nodes': nodes,
    'pointPrefix': 'bxs1'
}

# Post to import endpoint  
resp = requests.post(
    'http://localhost:5000/api/datasources/77777777-7777-7777-7777-777777777777/discover/import',
    json=request_body,
    timeout=300
)

print(f'Status: {resp.status_code}')
result = resp.json()
print(f"Imported: {result.get('importedCount', 0)}")
print(f"Errors: {result.get('errorCount', 0)}")
if result.get('errors'):
    for e in result['errors'][:10]:
        print(f'  - {e}')
