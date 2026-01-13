# ILP Protocol Implementation - Complete Details

## File: QuestDbTimeSeriesWriter.cs
**Location:** [src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs)

---

## 1. ILP Protocol Format Used

### Official InfluxDB Line Protocol Specification
```
measurement[,tag1=value1,tag2=value2] field1=value1[,field2=value2] [timestamp]
```

### NAIA Implementation
```
point_data point_id=123456i,value=45.67d,quality=1i 1705000000000000000
           ^^^^^ measurement  ^^^^^^^^^^^^^^^^^^^^^^^^^^  ^^^^^^^^^^^ timestamp
                               fields section            (nanoseconds)
```

---

## 2. Exact Field Mappings

### Source Code: [Line 79](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs#L79)

```csharp
var line = $"{_options.TableName} point_id={point.PointSequenceId}i,value={point.Value}d,quality={qualityInt}i {timestampNanos}";
```

### Breakdown:

| Component | ILP Name | Data Type | C# Code | Example |
|-----------|----------|-----------|---------|---------|
| Measurement | `point_data` | String (constant) | `_options.TableName` | `point_data` |
| Field 1 | `point_id` | Integer (Long) | `{point.PointSequenceId}i` | `point_id=123456i` |
| Field 2 | `value` | Double (Float) | `{point.Value}d` | `value=42.5d` |
| Field 3 | `quality` | Integer (Long) | `{qualityInt}i` | `quality=1i` |
| Timestamp | (implicit) | Nanoseconds | `{timestampNanos}` | `1705000000000000000` |

---

## 3. Timestamp Conversion Logic

### Source Code: [Lines 65-69](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs#L65-L69)

```csharp
// Convert to nanoseconds, adding microsecond offset to ensure uniqueness
var baseTimestampNanos = ((DateTimeOffset)point.Timestamp).ToUnixTimeMilliseconds() * 1_000_000;
var timestampNanos = baseTimestampNanos + microsecondOffset;
microsecondOffset += 1000;  // Add 1 microsecond (1000 nanoseconds) per point for uniqueness
```

### Conversion Steps:

1. **Point.Timestamp** = `DateTimeOffset` (e.g., 2024-01-12 10:00:00)
2. **ToUnixTimeMilliseconds()** = Milliseconds since 1970-01-01 (e.g., 1705000000000)
3. **× 1_000_000** = Convert to nanoseconds (e.g., 1705000000000000000)
4. **+ microsecondOffset** = Add 1μs per point for uniqueness (1000 nanoseconds per point)

### Example Sequence:
```
Point 1: 1705000000000000000 ns
Point 2: 1705000000000000000 + 1000 = 1705000000000001000 ns
Point 3: 1705000000000000000 + 2000 = 1705000000000002000 ns
```

**Why?** Multiple points can arrive in the same millisecond. QuestDB uses timestamp as a sorting key, so we add microsecond offsets to ensure deterministic ordering.

---

## 4. Quality Field Mapping

### Source Code: [Lines 72-73](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs#L72-L73)

```csharp
// Quality: 1 for Good, 0 for Bad (LONG column)
var qualityInt = point.Quality == DataQuality.Good ? 1 : 0;
```

### Mapping:
```
DataQuality.Good  → 1 (integer)
DataQuality.Bad   → 0 (integer)
Any other         → 0 (integer)
```

### Usage in QuestDB:
```sql
-- Query only good quality data
SELECT * FROM point_data WHERE quality = 1;

-- Find bad quality points
SELECT * FROM point_data WHERE quality = 0;

-- Statistics
SELECT COUNT(*), AVG(value) FROM point_data WHERE quality = 1;
```

---

## 5. Complete Line Building Example

### Source Code: [Lines 55-87](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs#L55-L87)

```csharp
// Build ILP lines with \n line endings (InfluxDB standard)
var linesList = new List<string>();
long microsecondOffset = 0;

foreach (var point in batch.Points)
{
    // Convert to nanoseconds, adding microsecond offset to ensure uniqueness
    var baseTimestampNanos = ((DateTimeOffset)point.Timestamp).ToUnixTimeMilliseconds() * 1_000_000;
    var timestampNanos = baseTimestampNanos + microsecondOffset;
    microsecondOffset += 1000;
    
    // Quality: 1 for Good, 0 for Bad
    var qualityInt = point.Quality == DataQuality.Good ? 1 : 0;
    
    // Validate value - must be finite
    if (!double.IsFinite(point.Value))
    {
        _logger.LogWarning("Skipping point {PointId} with invalid value: {Value}", 
            point.PointSequenceId, point.Value);
        continue;  // ← SILENTLY SKIP invalid values
    }
    
    // Use type suffixes: i=long, d=double
    var line = $"{_options.TableName} point_id={point.PointSequenceId}i,value={point.Value}d,quality={qualityInt}i {timestampNanos}";
    linesList.Add(line);
}

// Join with newlines
var ilpContent = string.Join("\n", linesList);
if (linesList.Count > 0)
    ilpContent += "\n";  // Trailing newline required
```

### Generated Output Example:
```
point_data point_id=1001i,value=23.5d,quality=1i 1705000000000000000
point_data point_id=1002i,value=45.2d,quality=1i 1705000000000001000
point_data point_id=1003i,value=67.8d,quality=0i 1705000000000002000
```

---

## 6. HTTP POST Operation

### Source Code: [Lines 88-100](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs#L88-L100)

```csharp
_logger.LogDebug("Writing {Lines} lines to QuestDB (batch {BatchId})", 
    batch.Points.Count, batch.BatchId);

// Send via /write endpoint
var content = new StringContent(ilpContent, Encoding.UTF8, "text/plain");
var response = await _httpClient.PostAsync("/write", content, cancellationToken);

if (!response.IsSuccessStatusCode)
{
    var error = await response.Content.ReadAsStringAsync(cancellationToken);
    _logger.LogError("QuestDB write failed. Batch: {BatchId}, Status: {StatusCode}, Error: {Error}", 
        batch.BatchId, response.StatusCode, error);
    throw new InvalidOperationException($"QuestDB write failed: {response.StatusCode} - {error}");
}

_logger.LogDebug("Wrote {Count} points to QuestDB", batch.Count);
```

### HTTP Details:
```
Method:  POST
URL:     http://localhost:9000/write
Headers: Content-Type: text/plain; charset=utf-8
Body:    (ILP formatted lines)

Success:  HTTP 200 OK
Failure:  HTTP 400 Bad Request, 401 Unauthorized, 503 Service Unavailable
```

---

## 7. Type System: ILP Suffixes

### Supported Suffixes in NAIA Code:

| Suffix | Type | C# Example | ILP Example |
|--------|------|-----------|------------|
| `i` | Integer (64-bit long) | `{point.PointSequenceId}i` | `point_id=123456i` |
| `d` | Double (64-bit float) | `{point.Value}d` | `value=42.5d` |
| (none) | String | (not used in NAIA) | `status="active"` |
| (none) | Boolean | (not used in NAIA) | `is_valid=true` |

### Why These Types?
- **point_id: `i`** = Integer because it's a foreign key reference
- **value: `d`** = Double because sensor readings are floating-point numbers
- **quality: `i`** = Integer because it's enum-like (0 or 1)

---

## 8. Error Handling in ILP Write

### Invalid Value Handling

**Source Code:** [Lines 72-76](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs#L72-L76)

```csharp
if (!double.IsFinite(point.Value))
{
    _logger.LogWarning("Skipping point {PointId} with invalid value: {Value}", 
        point.PointSequenceId, point.Value);
    continue;
}
```

**Invalid values:**
- `double.NaN` (Not a Number)
- `double.PositiveInfinity`
- `double.NegativeInfinity`

**Behavior:** Point is SILENTLY SKIPPED
- No exception thrown
- No return to DLQ
- Only a WARN-level log entry
- Next point in batch continues processing

**Impact:** If a batch has 1000 points and 10 have NaN values:
- 990 points written to QuestDB
- 10 points silently dropped
- Batch marked as success
- No data loss indication to user

---

## 9. HttpClient Configuration

### Source Code: [Lines 40-46](src/Naia.Infrastructure/TimeSeries/QuestDbTimeSeriesWriter.cs#L40-L46)

```csharp
_httpClient = new HttpClient
{
    BaseAddress = new Uri(_options.HttpEndpoint),  // http://localhost:9000
    Timeout = TimeSpan.FromSeconds(30)
};

_logger.LogInformation("QuestDB writer initialized: {Endpoint}", _options.HttpEndpoint);
```

### Configuration:
- **Base URL:** From configuration (default: `http://localhost:9000`)
- **Timeout:** 30 seconds per request
- **Relative URI:** `/write` (becomes `http://localhost:9000/write`)
- **Connection Pooling:** Default HttpClient behavior

### Timeout Behavior:
```csharp
// If write takes > 30 seconds, throws TimeoutException
// In IngestionPipeline, this is caught as RETRYABLE error
```

---

## 10. ILP Specification Compliance

### InfluxDB Line Protocol Rules (from official spec)

| Rule | NAIA Compliance | Code Reference |
|------|-----------------|-----------------|
| Measurement name required | ✅ Yes | `point_data` constant |
| Fields required | ✅ Yes | 3 fields: `point_id`, `value`, `quality` |
| At least one field | ✅ Yes | All 3 required |
| Tags are optional | ✅ Correct | No tags used (simplifies parsing) |
| Timestamp optional | ✅ Provided | Always in nanoseconds |
| Newline separates lines | ✅ Yes | `string.Join("\n", linesList)` |
| Trailing newline | ✅ Yes | `ilpContent += "\n"` if not empty |
| Field type suffixes | ✅ Yes | `i` for int, `d` for double |
| Escape spaces in values | ✅ N/A | No string values used |

---

## 11. Performance Characteristics

### Throughput Analysis

**Source Code:** [Batch processing in ProcessBatchAsync](src/Naia.Infrastructure/Pipeline/IngestionPipeline.cs#L207)

```
Throughput = Points per Second = Batch Size × Batches per Second
```

### Factors:
1. **ILP generation:** O(n) - one line per point
2. **HTTP POST:** Single request per batch (not per point)
3. **QuestDB write:** Depends on table configuration

### Example Calculation:
```
Assume:
- Batch size: 1000 points
- ILP generation: 0.1ms per point = 100ms total
- HTTP POST: 10ms (network round trip)
- QuestDB insert: 100ms
- Total per batch: ~210ms

Throughput: 1000 points / 0.21 seconds = ~4,700 points/second
```

### Optimization:
- Larger batches = better throughput (fewer HTTP requests)
- Network latency matters more than CPU
- Use HTTP/2 persistent connections (built-in to HttpClient)

---

## 12. Data Flow Diagram: ILP Write Path

```
┌─────────────────────────────────────────────────────────────┐
│ DataPointBatch from Kafka                                   │
│ ├─ BatchId: "batch-12345"                                   │
│ ├─ Points: [                                                │
│ │   {PointSequenceId: 1001, Value: 23.5, Quality: Good, ... │
│ │   {PointSequenceId: 1002, Value: 45.2, Quality: Good, ... │
│ │   {PointSequenceId: 1003, Value: NaN, Quality: Good, ...   │
│ │ ]                                                          │
│ └─ Count: 3 points                                          │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
        ┌──────────────────────────────────────────┐
        │ Loop through batch.Points                 │
        └──────────────────────────────────────────┘
                           │
        ┌──────────────────┴──────────────────┐
        ▼                                      ▼
    Point 1001:                          Point 1003:
    - Value: 23.5 (finite) ✅           - Value: NaN ❌
    - Generate ILP line                 - Skip (LogWarning)
    - timestamp: 1705...000000
                                        Point 1002:
        ▼                               - Value: 45.2 (finite) ✅
    ILP Line 1:                         - Generate ILP line
    point_data point_id=1001i,\         - timestamp: 1705...001000
    value=23.5d,quality=1i \
    1705000000000000000                 ▼
                                    ILP Line 2:
                                    point_data point_id=1002i,\
                                    value=45.2d,quality=1i \
                                    1705000000000001000

        ┌────────────────────────────────────────────────┐
        │ Join with newlines                             │
        │ Content:                                       │
        │ "point_data point_id=1001i,value=23.5d,\      │
        │ quality=1i 1705000000000000000\n              │
        │ point_data point_id=1002i,value=45.2d,\       │
        │ quality=1i 1705000000000001000\n"             │
        └────────────────────────────────────────────────┘
                           │
                           ▼
        ┌────────────────────────────────────────────────┐
        │ HTTP POST /write                               │
        │ URL: http://localhost:9000/write               │
        │ Content-Type: text/plain                       │
        │ Body: (ILP content above)                      │
        │ Timeout: 30 seconds                            │
        └────────────────────────────────────────────────┘
                           │
                    ┌──────┴──────┐
                    ▼             ▼
              200 OK          400/503/timeout
              ✅ Success      ❌ Error
              │               │
              │               └─► LogError
              │                   throw
              │                   (caught in
              └─► LogDebug        IngestionPipeline
                  "Wrote 2 points as RetryableError)
                  to QuestDB"
```

---

## 13. Validation Queries for QuestDB

### Verify ILP Protocol Is Working

```sql
-- 1. Check table exists and has data
SELECT COUNT(*) as total_rows FROM point_data;

-- 2. Check column types (should match ILP)
SELECT * FROM point_data LIMIT 1;

-- 3. Verify timestamp precision (nanoseconds)
SELECT ts, typeof(ts) FROM point_data LIMIT 1;

-- 4. Check point_id values (should be > 1000)
SELECT MIN(point_id), MAX(point_id), COUNT(*) FROM point_data;

-- 5. Check quality field
SELECT COUNT(*) as good_quality FROM point_data WHERE quality = 1;
SELECT COUNT(*) as bad_quality FROM point_data WHERE quality = 0;

-- 6. Verify microsecond offsets work
SELECT ts, ROW_NUMBER() OVER (ORDER BY ts) as seq FROM point_data LIMIT 100;
```

---

## Summary Table: ILP Implementation

| Aspect | Value | Notes |
|--------|-------|-------|
| **Protocol** | HTTP ILP | InfluxDB Line Protocol over HTTP |
| **Endpoint** | `http://localhost:9000/write` | Configurable in appsettings.json |
| **Method** | POST | Single batch per request |
| **Measurement** | `point_data` | QuestDB table name |
| **Fields** | 3 (`point_id`, `value`, `quality`) | Integer, Double, Integer types |
| **Timestamp** | Nanoseconds | Unix epoch nanoseconds |
| **Newline Format** | LF (`\n`) | Per ILP specification |
| **Timeout** | 30 seconds | Per HTTP request |
| **Error Handling** | Throw on non-2xx | Caught as retryable in pipeline |
| **Type Suffixes** | `i`, `d` | Integer and Double only |
| **Invalid Values** | Skip silently | NaN, Infinity skipped with warning |

