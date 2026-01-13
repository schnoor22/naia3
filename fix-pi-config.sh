#!/bin/bash
# Fix PI Web API config to use localhost and eliminate connection attempts

sudo sed -i 's|https://SDHQPIVWEB02.enxco.com/piwebapi|http://localhost|g' /opt/naia/publish/appsettings.json
sudo sed -i 's|"DataArchive": "SOCC"|"DataArchive": "dummy"|g' /opt/naia/publish/appsettings.json
sudo sed -i 's|"AfServer": "occafsrvr01"|"AfServer": "dummy"|g' /opt/naia/publish/appsettings.json

echo "Updated PI config:"
cat /opt/naia/publish/appsettings.json | grep -A 10 'PIWebApi'
