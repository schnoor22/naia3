#!/usr/bin/env python3
import json
import sys

# Read existing config
with open('/opt/naia/publish/appsettings.json', 'r') as f:
    config = json.load(f)

# Add WindFarmReplay configuration
config['WindFarmReplay'] = {
    "Enabled": True,
    "AutoStart": True,
    "DataDirectory": "/opt/naia/data/kelmarsh/scada_2019",
    "SiteCode": "KSH",
    "SiteName": "Kelmarsh Wind Farm",
    "TurbineCount": 6,
    "SpeedMultiplier": 60.0,
    "DataIntervalMinutes": 10,
    "BatchSize": 1000,
    "LoopReplay": True,
    "SkipNaN": True,
    "DataYears": [2019],
    "IncludedReadings": [
        "WindSpeed",
        "Power",
        "WindDirection",
        "RotorRPM",
        "GeneratorRPM",
        "PitchA",
        "PitchB",
        "PitchC",
        "NacelleTemp",
        "GearOilTemp",
        "GenBearingFrontTemp",
        "GenBearingRearTemp",
        "AmbientTemp",
        "GridVoltage",
        "GridFrequency"
    ],
    "KafkaTopic": "naia.datapoints"
}

# Write updated config
with open('/opt/naia/publish/appsettings.json', 'w') as f:
    json.dump(config, f, indent=2)

print("WindFarmReplay configuration added successfully!")
