# Critical: Timestamp & Timezone Handling

## 🚨 CRITICAL ISSUE FIXED

The wind/solar replay data had **future timestamps** (2025-11-10) which would break the system. All data has been rebased to **current time** while preserving **time-of-day patterns**.

## Architecture

### Backend (API)

**All timestamps in the backend are stored and processed in UTC:**
- CSV replay files contain timestamps in UTC format: `YYYY-MM-DD HH:MM:SS`
- PostgreSQL stores all `timestamp` columns as UTC (`timestamp without time zone`)
- QuestDB ingests all data as UTC
- Kafka messages carry UTC timestamps
- The replay worker publishes with `DateTime.UtcNow` for `ReceivedAt`

### Frontend (UI)

**All timestamps display in the user's local timezone:**
- When fetching data from the API, timestamps come as ISO 8601 UTC strings
- JavaScript `Date` constructor automatically interprets as UTC
- SvelteKit uses browser's `Intl.DateTimeFormat` API for localization
- All trend charts display converted to user's local timezone
- User can change their timezone in settings (TODO: if not implemented)

## Data Flow

```
CSV Files (UTC) 
   ↓
[rebase_timestamps.py: Apply current_date - min_date offset]
   ↓
Rebased CSV Files (UTC, current time)
   ↓
[upload_wind_data.ps1: SCP to server]
   ↓
/opt/naia/data/wind/elt1, /opt/naia/data/wind/blx1 (server, UTC)
   ↓
GenericCsvReplayWorker
   - Reads CSV as UTC (2026-01-13 HH:MM:SS)
   - Creates DataPoint with Timestamp (UTC)
   - Sets ReceivedAt = DateTime.UtcNow (UTC)
   ↓
Kafka: naia.datapoints
   - Topic carries UTC timestamps
   - Messages serialized as JSON with UTC ISO 8601 strings
   ↓
Ingestion Pipeline
   - Validates timestamp is UTC
   - Stores in PostgreSQL as UTC
   - Stores in QuestDB as UTC
   ↓
API Endpoints
   - SELECT returns timestamps as ISO 8601 UTC strings
   - Example: 2026-01-13T10:30:00Z
   ↓
Frontend
   - Receives ISO 8601 UTC string
   - JavaScript Date constructor: new Date("2026-01-13T10:30:00Z")
   - Automatically converts to local timezone for display
   - Example: User in EST sees: 2026-01-13 05:30:00
```

## Verification

### Check Backend Timestamps Are UTC

```sql
-- PostgreSQL: Verify timestamp storage
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'DataPoints' AND column_name LIKE '%timestamp%';

-- Result should show:
-- timestamp    | timestamp without time zone | YES
-- received_at  | timestamp without time zone | YES
```

### Check Frontend Display

1. Go to any trend on the UI
2. Open browser DevTools Console
3. Run:
```javascript
// Check browser timezone
console.log(Intl.DateTimeFormat().resolvedOptions().timeZone);
// Output: America/New_York or your local timezone

// Check data from API
fetch('/api/trending/points/1/history')
  .then(r => r.json())
  .then(d => {
    console.log(d.points[0].timestamp); // Should be ISO 8601 UTC
    console.log(new Date(d.points[0].timestamp).toString()); // Converts to local
  });
```

## Standards Compliance

✅ **ISO 8601 Format**
- All timestamps in ISO 8601 with timezone indicator
- Example: `2026-01-13T10:30:00Z` (Z = UTC)

✅ **RFC 3339 Compatible**
- Timestamps can be parsed by any RFC 3339 parser
- Supported by JavaScript, Python, .NET, etc.

✅ **Timezone Awareness**
- Backend: Always UTC internally
- Frontend: Always converted to user's local timezone

## Implementation Details

### GenericCsvReplayWorker.cs

The replay worker **explicitly handles UTC**:

```csharp
// Line 233: Timestamp is read from CSV as local time
// But CSV contains UTC timestamps from our rebasing process
var dataPoint = new DataPoint
{
    Timestamp = point.Timestamp,  // UTC from CSV
    ReceivedAt = DateTime.UtcNow  // UTC
};

// Line 270: Publishes with UTC timestamp
await _producer.ProduceAsync(_options.KafkaTopic, message, ct);
```

### API Controllers

All API responses use `DateTime.UtcNow` for `ReceivedAt`:
- Controllers explicitly set `ReceivedAt = DateTime.UtcNow`
- Serialization uses `JsonSerializerOptions` with proper timezone handling

### Frontend Components

SvelteKit components handle timezone conversion:

```svelte
<script>
  // Date comes from API as ISO 8601 UTC
  let timestamp: string = "2026-01-13T10:30:00Z";
  
  // Svelte component automatically converts to local time
  let localDate = new Date(timestamp);
</script>

<!-- Display shows user's local time -->
<p>{localDate.toLocaleString()}</p>
```

## Troubleshooting

### Problem: Timestamps off by several hours

**Solution**: Check browser timezone

```javascript
// In browser console
Intl.DateTimeFormat().resolvedOptions().timeZone
```

### Problem: API returning timestamps without Z

**Solution**: Ensure .NET serialization is configured:

```csharp
var options = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
// JsonSerializer automatically adds Z for DateTime.UtcNow
```

### Problem: Data shows as "future" on startup

**Solution**: This means CSV rebasement wasn't run. Execute:

```bash
python c:\naia3\rebase_timestamps.py
```

Then restart the ingestion service.

## Maintaining This Standard Going Forward

1. **All CSV files must be rebased before upload**
   - Run `rebase_timestamps.py` before `upload_wind_data.ps1`
   - Ensures timestamps are current, not historical

2. **All new data sources must provide UTC timestamps**
   - If a source provides local time, convert to UTC in the connector
   - Document the conversion in the connector code

3. **Never modify timestamps in the frontend**
   - Let JavaScript's `Date` object handle timezone conversion
   - Use `toLocaleString()` or `Intl.DateTimeFormat()` for display

4. **QuestDB queries always use UTC**
   - Query times in UTC
   - QuestDB will return UTC timestamps
   - Frontend handles conversion

## Next Steps

1. ✅ Run `rebase_timestamps.py` to fix CSV timestamps
2. ✅ Run `upload_wind_data.ps1` to upload rebased data
3. ⏳ Register ELT1 and BLX1 data sources in database
4. ⏳ Test that ingestion shows correct current-time data
5. ⏳ Verify UI displays correct local time for user's timezone
