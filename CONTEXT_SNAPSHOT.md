# NAIA Context Snapshot
**Generated**: 2026-01-12 23:00:59
**Purpose**: Paste this entire file to your AI assistant for instant project context

---

## NAIA Vision
**The First Industrial Historian That Learns From You**

NAIA represents a generational leap in industrial data management. While legacy systems (PI, Wonderware, Ignition) require manual modeling every single time, NAIA **remembers** how you organized your last 10 sites and suggests structure for site #11.

---

## Architecture Overview

```
USER/BROWSER --> CADDY (app.naia.run:443) --> NAIA.API (:5000)
                                                  |
                 +------------+------------+------+------+
                 |            |            |            |
            PostgreSQL    QuestDB       Redis       Kafka
              :5432        :9000        :6379       :9092
            (Metadata)  (TimeSeries)   (Cache)   (Messages)
```

**Server**: 37.27.189.86 (Hetzner) | **Domain**: app.naia.run | **SSH**: naia@37.27.189.86

---

## Key Paths

### Local (Windows)
| Path | Purpose |
|------|---------|
| C:\naia3\src\Naia.Api\ | .NET 8 API |
| C:\naia3\src\Naia.Web\ | Svelte 5 UI |
| C:\naia3\src\Naia.Web\build.ps1 | UI build script |

### Remote (Linux)
| Path | Purpose |
|------|---------|
| /opt/naia/ | Deployed API |
| /opt/naia/wwwroot/ | Deployed UI |
| /etc/caddy/Caddyfile | Reverse proxy |

---

## Key API Endpoints
| Endpoint | Purpose |
|----------|---------|
| /api/health | System health |
| /api/version | Build info |
| /api/points | Point browser |
| /api/suggestions | Pattern suggestions |
| /swagger | API docs |

---

## Local Git Status
**Branch**: main | **Commit**: 3ab225f | **Changed Files**: 67

```
 M DOCUMENTATION_INDEX.md
 M init-scripts/postgres/01-init-schema.sql
 M src/Naia.Api/Program.cs
 M src/Naia.Api/obj/Debug/net8.0/Naia.Api.AssemblyInfo.cs
 M src/Naia.Api/obj/Debug/net8.0/Naia.Api.AssemblyInfoInputs.cache
 M src/Naia.Application/Abstractions/IPointLookupService.cs
 M src/Naia.Application/obj/Debug/net8.0/Naia.Application.AssemblyInfo.cs
 M src/Naia.Application/obj/Debug/net8.0/Naia.Application.AssemblyInfoInputs.cache
 M src/Naia.Connectors/ServiceCollectionExtensions.cs
 M src/Naia.Connectors/obj/Debug/net8.0/Naia.Connectors.AssemblyInfo.cs
 M src/Naia.Connectors/obj/Debug/net8.0/Naia.Connectors.AssemblyInfoInputs.cache
 M src/Naia.Domain/obj/Debug/net8.0/Naia.Domain.AssemblyInfo.cs
 M src/Naia.Domain/obj/Debug/net8.0/Naia.Domain.AssemblyInfoInputs.cache
 M src/Naia.Infrastructure/Persistence/PointLookupService.cs
 M src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs
```

---

## Remote Service Status
```
Warning: Permanently added '37.27.189.86' (ED25519) to the list of known hosts.
naia@37.27.189.86: Permission denied (publickey,password).
```

## API Health
```json
"\u003c!doctype html\u003e\n\u003chtml lang=\"en\" class=\"dark\"\u003e\n\t\u003chead\u003e\n\t\t\u003cmeta charset=\"utf-8\" /\u003e\n\t\t\u003clink rel=\"icon\" href=\"/favicon.png\" /\u003e\n\t\t\u003cmeta name=\"viewport\" content=\"width=device-width, initial-scale=1\" /\u003e\n\t\t\u003cmeta name=\"description\" content=\"NAIA Industrial AI Framework - The First Historian That Learns From You\" /\u003e\n\t\t\u003clink rel=\"preconnect\" href=\"https://fonts.googleapis.com\"\u003e\n\t\t\u003clink rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin\u003e\n\t\t\u003clink href=\"https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700\u0026family=JetBrains+Mono:wght@400;500\u0026display=swap\" rel=\"stylesheet\"\u003e\n\t\t\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/entry/start.4Wm8VO20.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/BeFGrokg.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/CMoxswSl.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/DnZeFeAf.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/entry/app.BBlpOiXl.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/Dyrg4-m5.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/DBwLsdZZ.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/BJTlkxqz.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/CLB420GD.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/BHReAqjr.js\"\u003e\n\t\u003c/head\u003e\n\t\u003cbody data-sveltekit-preload-data=\"hover\" class=\"bg-gray-50 dark:bg-gray-950 text-gray-900 dark:text-gray-100\"\u003e\n\t\t\u003cdiv style=\"display: contents\"\u003e\n\t\t\t\u003cscript\u003e\n\t\t\t\t{\n\t\t\t\t\t__sveltekit_n7k6fp = {\n\t\t\t\t\t\tbase: \"\"\n\t\t\t\t\t};\n\n\t\t\t\t\tconst element = document.currentScript.parentElement;\n\n\t\t\t\t\tPromise.all([\n\t\t\t\t\t\timport(\"/_app/immutable/entry/start.4Wm8VO20.js\"),\n\t\t\t\t\t\timport(\"/_app/immutable/entry/app.BBlpOiXl.js\")\n\t\t\t\t\t]).then(([kit, app]) =\u003e {\n\t\t\t\t\t\tkit.start(app, element);\n\t\t\t\t\t});\n\t\t\t\t}\n\t\t\t\u003c/script\u003e\n\t\t\u003c/div\u003e\n\t\u003c/body\u003e\n\u003c/html\u003e\n"
```

## API Version
```json
"\u003c!doctype html\u003e\n\u003chtml lang=\"en\" class=\"dark\"\u003e\n\t\u003chead\u003e\n\t\t\u003cmeta charset=\"utf-8\" /\u003e\n\t\t\u003clink rel=\"icon\" href=\"/favicon.png\" /\u003e\n\t\t\u003cmeta name=\"viewport\" content=\"width=device-width, initial-scale=1\" /\u003e\n\t\t\u003cmeta name=\"description\" content=\"NAIA Industrial AI Framework - The First Historian That Learns From You\" /\u003e\n\t\t\u003clink rel=\"preconnect\" href=\"https://fonts.googleapis.com\"\u003e\n\t\t\u003clink rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin\u003e\n\t\t\u003clink href=\"https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700\u0026family=JetBrains+Mono:wght@400;500\u0026display=swap\" rel=\"stylesheet\"\u003e\n\t\t\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/entry/start.4Wm8VO20.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/BeFGrokg.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/CMoxswSl.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/DnZeFeAf.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/entry/app.BBlpOiXl.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/Dyrg4-m5.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/DBwLsdZZ.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/BJTlkxqz.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/CLB420GD.js\"\u003e\n\t\t\u003clink rel=\"modulepreload\" href=\"/_app/immutable/chunks/BHReAqjr.js\"\u003e\n\t\u003c/head\u003e\n\t\u003cbody data-sveltekit-preload-data=\"hover\" class=\"bg-gray-50 dark:bg-gray-950 text-gray-900 dark:text-gray-100\"\u003e\n\t\t\u003cdiv style=\"display: contents\"\u003e\n\t\t\t\u003cscript\u003e\n\t\t\t\t{\n\t\t\t\t\t__sveltekit_n7k6fp = {\n\t\t\t\t\t\tbase: \"\"\n\t\t\t\t\t};\n\n\t\t\t\t\tconst element = document.currentScript.parentElement;\n\n\t\t\t\t\tPromise.all([\n\t\t\t\t\t\timport(\"/_app/immutable/entry/start.4Wm8VO20.js\"),\n\t\t\t\t\t\timport(\"/_app/immutable/entry/app.BBlpOiXl.js\")\n\t\t\t\t\t]).then(([kit, app]) =\u003e {\n\t\t\t\t\t\tkit.start(app, element);\n\t\t\t\t\t});\n\t\t\t\t}\n\t\t\t\u003c/script\u003e\n\t\t\u003c/div\u003e\n\t\u003c/body\u003e\n\u003c/html\u003e\n"
```

## Docker Containers
```
Warning: Permanently added '37.27.189.86' (ED25519) to the list of known hosts.
naia@37.27.189.86: Permission denied (publickey,password).
```

---

## Quick Commands

```bash
# SSH to server
ssh naia@37.27.189.86

# Restart API
sudo systemctl restart naia-api

# View logs
sudo journalctl -u naia-api -f

# Docker status
docker ps
```

---

## Reference Links
- QuestDB: https://questdb.com/docs/query/rest-api/
- Kafka: https://docs.confluent.io/kafka/kafka-apis.html
- Redis: https://redis.io/docs/latest/develop/reference/
- PostgreSQL: https://www.postgresql.org/docs/current/tutorial-advanced.html
- AF SDK: https://docs.aveva.com/bundle/af-sdk/page/html/af-sdk-overview.htm

---
*Generated by gather-context.ps1*

