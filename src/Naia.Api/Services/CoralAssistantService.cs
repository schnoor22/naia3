using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Naia.Infrastructure.Telemetry;
using Npgsql;
using StackExchange.Redis;

namespace Naia.Api.Services;

/// <summary>
/// Coral - The nurturing AI guide to NAIA's data ocean.
/// She helps engineers navigate their data needs with warmth and precision.
/// </summary>
public class CoralAssistantService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CoralAssistantService> _logger;
    private readonly string _questDbConnectionString;
    private readonly string _postgresConnectionString;

    private const string CoralSystemPrompt = @"You are Coral, the intelligent and nurturing AI guide for NAIA - an Industrial Historian and time-series data platform. 

Your personality:
- You are warm, helpful, and patient - like a knowledgeable mentor
- You understand that engineers are often under pressure and need accurate information quickly
- You speak clearly and concisely, avoiding unnecessary jargon
- You use ocean/coral metaphors naturally when appropriate (data flows, diving into details, navigating)
- You celebrate discoveries and guide users toward insights

Your knowledge:
- NAIA is an Industrial Historian that collects time-series data from wind turbines, solar installations, and other industrial equipment
- Data flows: CSV files → Kafka → QuestDB (time-series) → UI via SignalR
- PostgreSQL stores metadata (points, equipment, patterns)
- QuestDB stores time-series measurements (point_data table with timestamp, point_id, value, quality)
- Redis caches frequently accessed data
- Points have patterns (naming conventions) that help organize data semantically

Your capabilities (use tools when needed):
- Query QuestDB for time-series data trends and statistics
- Query PostgreSQL for point metadata, equipment info, patterns
- Check system health and ingestion status
- Help users find points and understand their data
- Generate reports and summaries

Guidelines:
- When asked about data, actually query the database to provide accurate numbers
- Format numbers nicely (e.g., 1,234.56 instead of 1234.5678)
- When showing timestamps, use human-readable formats
- If you don't know something, say so honestly and suggest where they might find it
- Keep responses focused and actionable";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public CoralAssistantService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CoralAssistantService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _questDbConnectionString = configuration.GetConnectionString("QuestDb") ?? "host=localhost;port=8812;database=qdb;username=admin;password=quest";
        _postgresConnectionString = configuration.GetConnectionString("DefaultConnection") ?? "Host=localhost;Database=naia;Username=postgres;Password=postgres";
    }

    public async IAsyncEnumerable<CoralStreamEvent> ChatAsync(
        string userMessage,
        CoralContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var span = NaiaMetrics.StartSpan("coral.chat");
        
        var apiKey = _configuration["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            NaiaMetrics.CoralConversations.WithLabels("error").Inc();
            yield return new CoralStreamEvent { Text = "I'm sorry, I'm not fully configured yet. Please ask your administrator to set up my connection." };
            yield break;
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var messages = new List<object>
        {
            new { role = "user", content = userMessage }
        };

        var tools = GetCoralTools();
        var maxIterations = 8;
        var iteration = 0;

        while (iteration++ < maxIterations)
        {
            var request = new
            {
                model = "claude-sonnet-4-20250514",
                max_tokens = 4096,
                system = CoralSystemPrompt,
                messages = messages,
                tools = tools,
                stream = true
            };

            var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            httpRequest.Content = content;
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            var (response, connectError) = await TrySendRequestAsync(client, httpRequest, cancellationToken);
            if (connectError != null)
            {
                NaiaMetrics.CoralConversations.WithLabels("error").Inc();
                yield return new CoralStreamEvent { Text = "I'm having trouble connecting right now. Let me try to help another way." };
                yield break;
            }
            if (response == null)
            {
                NaiaMetrics.CoralConversations.WithLabels("error").Inc();
                yield return new CoralStreamEvent { Text = "I encountered an unexpected issue. Please try again." };
                yield break;
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Claude API error: {Status} - {Error}", response.StatusCode, error);
                NaiaMetrics.CoralConversations.WithLabels("error").Inc();
                yield return new CoralStreamEvent { Text = "I encountered an issue processing your request. Could you try rephrasing?" };
                yield break;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            var currentText = new StringBuilder();
            var toolUses = new List<ToolUseInfo>();
            var stopReason = "";

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                    continue;

                var data = line.Substring(6);
                if (data == "[DONE]") break;

                var eventResult = TryParseStreamEvent(data, toolUses);
                if (eventResult == null) continue;

                if (eventResult.Text != null)
                {
                    currentText.Append(eventResult.Text);
                    yield return new CoralStreamEvent { Text = eventResult.Text };
                }
                if (eventResult.StopReason != null)
                {
                    stopReason = eventResult.StopReason;
                }
            }

            // If tool use, execute tools and continue
            if (stopReason == "tool_use" && toolUses.Count > 0)
            {
                // Add assistant message
                var contentBlocks = new List<object>();
                if (currentText.Length > 0)
                {
                    contentBlocks.Add(new { type = "text", text = currentText.ToString() });
                }
                foreach (var tool in toolUses)
                {
                    contentBlocks.Add(new { type = "tool_use", id = tool.Id, name = tool.Name, input = tool.Input });
                    yield return new CoralStreamEvent { Tool = new ToolStatus { Name = GetToolDisplayName(tool.Name), Status = "running" } };
                }
                messages.Add(new { role = "assistant", content = contentBlocks });

                // Execute tools and add results
                var toolResults = new List<object>();
                foreach (var tool in toolUses)
                {
                    var result = await ExecuteToolAsync(tool.Name, tool.Input, cancellationToken);
                    toolResults.Add(new { type = "tool_result", tool_use_id = tool.Id, content = result });
                    yield return new CoralStreamEvent { Tool = new ToolStatus { Name = GetToolDisplayName(tool.Name), Status = "complete" } };
                }
                messages.Add(new { role = "user", content = toolResults });
            }
            else
            {
                // Finished - track successful conversation
                stopwatch.Stop();
                NaiaMetrics.CoralResponseLatency.WithLabels("chat").Observe(stopwatch.Elapsed.TotalSeconds);
                NaiaMetrics.CoralConversations.WithLabels("success").Inc();
                break;
            }
        }
    }

    private async Task<(HttpResponseMessage? Response, Exception? Error)> TrySendRequestAsync(
        HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return (response, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Claude API");
            return (null, ex);
        }
    }

    private class StreamEventResult
    {
        public string? Text { get; set; }
        public string? StopReason { get; set; }
    }

    private StreamEventResult? TryParseStreamEvent(string data, List<ToolUseInfo> toolUses)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            var result = new StreamEventResult();

            switch (type)
            {
                case "content_block_start":
                    var block = root.GetProperty("content_block");
                    if (block.GetProperty("type").GetString() == "tool_use")
                    {
                        toolUses.Add(new ToolUseInfo
                        {
                            Id = block.GetProperty("id").GetString() ?? "",
                            Name = block.GetProperty("name").GetString() ?? "",
                            InputJson = ""
                        });
                    }
                    break;

                case "content_block_delta":
                    var delta = root.GetProperty("delta");
                    var deltaType = delta.GetProperty("type").GetString();
                    if (deltaType == "text_delta")
                    {
                        result.Text = delta.GetProperty("text").GetString();
                    }
                    else if (deltaType == "input_json_delta" && toolUses.Count > 0)
                    {
                        var jsonPart = delta.GetProperty("partial_json").GetString();
                        toolUses[^1].InputJson += jsonPart;
                    }
                    break;

                case "content_block_stop":
                    if (toolUses.Count > 0 && !string.IsNullOrEmpty(toolUses[^1].InputJson))
                    {
                        try
                        {
                            toolUses[^1].Input = JsonSerializer.Deserialize<JsonElement>(toolUses[^1].InputJson);
                        }
                        catch
                        {
                            toolUses[^1].Input = JsonDocument.Parse("{}").RootElement;
                        }
                    }
                    break;

                case "message_delta":
                    if (root.TryGetProperty("delta", out var msgDelta) && 
                        msgDelta.TryGetProperty("stop_reason", out var stopProp))
                    {
                        result.StopReason = stopProp.GetString();
                    }
                    break;
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private class ToolUseInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string InputJson { get; set; } = "";
        public JsonElement Input { get; set; }
    }

    private string GetToolDisplayName(string toolName) => toolName switch
    {
        "query_timeseries" => "Querying time-series data",
        "query_metadata" => "Searching metadata",
        "search_points" => "Finding points",
        "get_system_status" => "Checking system status",
        "get_equipment_summary" => "Loading equipment info",
        "get_recent_values" => "Fetching recent values",
        "get_data_statistics" => "Calculating statistics",
        "check_ingestion" => "Checking ingestion status",
        _ => toolName
    };

    private object[] GetCoralTools() => new object[]
    {
        new {
            name = "query_timeseries",
            description = "Query QuestDB for time-series data. Use for trends, values, aggregations over time.",
            input_schema = new {
                type = "object",
                properties = new {
                    sql = new { type = "string", description = "SQL query for QuestDB. Tables: point_data (timestamp, point_id, value, quality), point_aggregates, point_daily_stats" }
                },
                required = new[] { "sql" }
            }
        },
        new {
            name = "query_metadata",
            description = "Query PostgreSQL for metadata about points, equipment, patterns, sources.",
            input_schema = new {
                type = "object",
                properties = new {
                    sql = new { type = "string", description = "SQL query for PostgreSQL. Tables: points, equipment, data_sources, patterns, etc." }
                },
                required = new[] { "sql" }
            }
        },
        new {
            name = "search_points",
            description = "Search for points by name, description, or pattern. Returns matching points with metadata.",
            input_schema = new {
                type = "object",
                properties = new {
                    query = new { type = "string", description = "Search term for point name, description, or pattern" },
                    limit = new { type = "integer", description = "Max results (default 20)" }
                },
                required = new[] { "query" }
            }
        },
        new {
            name = "get_system_status",
            description = "Get current system health status including database connections, ingestion state, and service health.",
            input_schema = new {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            }
        },
        new {
            name = "get_equipment_summary",
            description = "Get summary of equipment/assets including point counts, recent activity.",
            input_schema = new {
                type = "object",
                properties = new {
                    equipment_id = new { type = "integer", description = "Optional specific equipment ID" }
                },
                required = Array.Empty<string>()
            }
        },
        new {
            name = "get_recent_values",
            description = "Get the most recent values for specific points.",
            input_schema = new {
                type = "object",
                properties = new {
                    point_ids = new { type = "array", items = new { type = "integer" }, description = "Array of point IDs to get values for" },
                    point_names = new { type = "array", items = new { type = "string" }, description = "Array of point names to search for and get values" }
                },
                required = Array.Empty<string>()
            }
        },
        new {
            name = "get_data_statistics",
            description = "Calculate statistics (min, max, avg, count) for points over a time range.",
            input_schema = new {
                type = "object",
                properties = new {
                    point_id = new { type = "integer", description = "Point ID to analyze" },
                    hours = new { type = "integer", description = "Hours of history to analyze (default 24)" }
                },
                required = new[] { "point_id" }
            }
        },
        new {
            name = "check_ingestion",
            description = "Check the current data ingestion status, including what's being processed and any issues.",
            input_schema = new {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            }
        }
    };

    private async Task<string> ExecuteToolAsync(string toolName, JsonElement input, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                "query_timeseries" => await QueryQuestDbAsync(input.GetProperty("sql").GetString() ?? "", ct),
                "query_metadata" => await QueryPostgresAsync(input.GetProperty("sql").GetString() ?? "", ct),
                "search_points" => await SearchPointsAsync(input, ct),
                "get_system_status" => await GetSystemStatusAsync(ct),
                "get_equipment_summary" => await GetEquipmentSummaryAsync(input, ct),
                "get_recent_values" => await GetRecentValuesAsync(input, ct),
                "get_data_statistics" => await GetDataStatisticsAsync(input, ct),
                "check_ingestion" => await CheckIngestionAsync(ct),
                _ => $"Unknown tool: {toolName}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {Tool}", toolName);
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> QueryQuestDbAsync(string sql, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "No SQL provided";

        // Safety check
        var upperSql = sql.ToUpperInvariant();
        if (upperSql.Contains("DROP") || upperSql.Contains("DELETE") || upperSql.Contains("TRUNCATE") || upperSql.Contains("INSERT") || upperSql.Contains("UPDATE"))
            return "For safety, I can only run SELECT queries on time-series data.";

        try
        {
            await using var conn = new NpgsqlConnection(_questDbConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.CommandTimeout = 30;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var results = new List<Dictionary<string, object?>>();
            var rowCount = 0;

            while (await reader.ReadAsync(ct) && rowCount < 100)
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    if (value is DateTime dt)
                        value = dt.ToString("yyyy-MM-dd HH:mm:ss");
                    row[reader.GetName(i)] = value;
                }
                results.Add(row);
                rowCount++;
            }

            return JsonSerializer.Serialize(new { rows = results, count = rowCount }, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Query error: {ex.Message}";
        }
    }

    private async Task<string> QueryPostgresAsync(string sql, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "No SQL provided";

        var upperSql = sql.ToUpperInvariant();
        if (upperSql.Contains("DROP") || upperSql.Contains("DELETE") || upperSql.Contains("TRUNCATE") || upperSql.Contains("INSERT") || upperSql.Contains("UPDATE"))
            return "For safety, I can only run SELECT queries on metadata.";

        try
        {
            await using var conn = new NpgsqlConnection(_postgresConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.CommandTimeout = 30;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var results = new List<Dictionary<string, object?>>();
            var rowCount = 0;

            while (await reader.ReadAsync(ct) && rowCount < 100)
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[reader.GetName(i)] = value;
                }
                results.Add(row);
                rowCount++;
            }

            return JsonSerializer.Serialize(new { rows = results, count = rowCount }, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Query error: {ex.Message}";
        }
    }

    private async Task<string> SearchPointsAsync(JsonElement input, CancellationToken ct)
    {
        var query = input.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
        var limit = input.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;

        var sql = @"SELECT p.id, p.name, p.description, p.unit, p.point_type, e.name as equipment_name
                    FROM points p
                    LEFT JOIN equipment e ON p.equipment_id = e.id
                    WHERE p.name ILIKE @query OR p.description ILIKE @query
                    ORDER BY p.name
                    LIMIT @limit";

        try
        {
            await using var conn = new NpgsqlConnection(_postgresConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("query", $"%{query}%");
            cmd.Parameters.AddWithValue("limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var results = new List<object>();

            while (await reader.ReadAsync(ct))
            {
                results.Add(new
                {
                    id = reader.GetInt32(0),
                    name = reader.GetString(1),
                    description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    unit = reader.IsDBNull(3) ? null : reader.GetString(3),
                    type = reader.IsDBNull(4) ? null : reader.GetString(4),
                    equipment = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }

            return JsonSerializer.Serialize(new { points = results, found = results.Count }, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Search error: {ex.Message}";
        }
    }

    private async Task<string> GetSystemStatusAsync(CancellationToken ct)
    {
        var status = new Dictionary<string, object>();

        // Check PostgreSQL
        try
        {
            await using var pgConn = new NpgsqlConnection(_postgresConnectionString);
            await pgConn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM points", pgConn);
            var count = await cmd.ExecuteScalarAsync(ct);
            status["postgresql"] = new { healthy = true, pointCount = count };
        }
        catch (Exception ex)
        {
            status["postgresql"] = new { healthy = false, error = ex.Message };
        }

        // Check QuestDB
        try
        {
            await using var qConn = new NpgsqlConnection(_questDbConnectionString);
            await qConn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT count() FROM point_data WHERE timestamp > dateadd('h', -1, now())", qConn);
            var count = await cmd.ExecuteScalarAsync(ct);
            status["questdb"] = new { healthy = true, recentRecords = count };
        }
        catch (Exception ex)
        {
            status["questdb"] = new { healthy = false, error = ex.Message };
        }

        return JsonSerializer.Serialize(status, JsonOptions);
    }

    private async Task<string> GetEquipmentSummaryAsync(JsonElement input, CancellationToken ct)
    {
        var equipmentId = input.TryGetProperty("equipment_id", out var eid) ? (int?)eid.GetInt32() : null;

        var sql = equipmentId.HasValue
            ? "SELECT e.id, e.name, e.description, COUNT(p.id) as point_count FROM equipment e LEFT JOIN points p ON p.equipment_id = e.id WHERE e.id = @id GROUP BY e.id"
            : "SELECT e.id, e.name, e.description, COUNT(p.id) as point_count FROM equipment e LEFT JOIN points p ON p.equipment_id = e.id GROUP BY e.id ORDER BY e.name LIMIT 50";

        try
        {
            await using var conn = new NpgsqlConnection(_postgresConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (equipmentId.HasValue)
                cmd.Parameters.AddWithValue("id", equipmentId.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var results = new List<object>();

            while (await reader.ReadAsync(ct))
            {
                results.Add(new
                {
                    id = reader.GetInt32(0),
                    name = reader.GetString(1),
                    description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    pointCount = reader.GetInt64(3)
                });
            }

            return JsonSerializer.Serialize(new { equipment = results }, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> GetRecentValuesAsync(JsonElement input, CancellationToken ct)
    {
        var pointIds = new List<long>();

        if (input.TryGetProperty("point_ids", out var ids))
        {
            foreach (var id in ids.EnumerateArray())
                pointIds.Add(id.GetInt64());
        }

        if (input.TryGetProperty("point_names", out var names))
        {
            await using var conn = new NpgsqlConnection(_postgresConnectionString);
            await conn.OpenAsync(ct);
            foreach (var name in names.EnumerateArray())
            {
                var sql = "SELECT id FROM points WHERE name ILIKE @name LIMIT 5";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("name", $"%{name.GetString()}%");
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    pointIds.Add(reader.GetInt64(0));
            }
        }

        if (pointIds.Count == 0)
            return "No points specified or found";

        var results = new List<object>();
        await using var qConn = new NpgsqlConnection(_questDbConnectionString);
        await qConn.OpenAsync(ct);

        foreach (var pointId in pointIds.Take(20))
        {
            var sql = $"SELECT timestamp, value, quality FROM point_data WHERE point_id = {pointId} ORDER BY timestamp DESC LIMIT 1";
            try
            {
                await using var cmd = new NpgsqlCommand(sql, qConn);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    results.Add(new
                    {
                        pointId = pointId,
                        timestamp = reader.GetDateTime(0).ToString("yyyy-MM-dd HH:mm:ss"),
                        value = reader.GetDouble(1),
                        quality = reader.GetInt32(2)
                    });
                }
            }
            catch { }
        }

        return JsonSerializer.Serialize(new { values = results }, JsonOptions);
    }

    private async Task<string> GetDataStatisticsAsync(JsonElement input, CancellationToken ct)
    {
        var pointId = input.GetProperty("point_id").GetInt64();
        var hours = input.TryGetProperty("hours", out var h) ? h.GetInt32() : 24;

        var sql = $@"SELECT 
            min(value) as min_value,
            max(value) as max_value,
            avg(value) as avg_value,
            count() as count,
            min(timestamp) as first_ts,
            max(timestamp) as last_ts
            FROM point_data 
            WHERE point_id = {pointId} 
            AND timestamp > dateadd('h', -{hours}, now())";

        try
        {
            await using var conn = new NpgsqlConnection(_questDbConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                return JsonSerializer.Serialize(new
                {
                    pointId = pointId,
                    hours = hours,
                    min = reader.IsDBNull(0) ? null : (double?)reader.GetDouble(0),
                    max = reader.IsDBNull(1) ? null : (double?)reader.GetDouble(1),
                    avg = reader.IsDBNull(2) ? null : (double?)reader.GetDouble(2),
                    count = reader.GetInt64(3),
                    firstTimestamp = reader.IsDBNull(4) ? null : reader.GetDateTime(4).ToString("yyyy-MM-dd HH:mm:ss"),
                    lastTimestamp = reader.IsDBNull(5) ? null : reader.GetDateTime(5).ToString("yyyy-MM-dd HH:mm:ss")
                }, JsonOptions);
            }

            return "No data found for this point";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> CheckIngestionAsync(CancellationToken ct)
    {
        var status = new Dictionary<string, object>();

        // Check recent data flow
        try
        {
            await using var conn = new NpgsqlConnection(_questDbConnectionString);
            await conn.OpenAsync(ct);
            
            var sql = @"SELECT 
                count() as total,
                count_distinct(point_id) as unique_points
                FROM point_data 
                WHERE timestamp > dateadd('m', -5, now())";
            
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            
            if (await reader.ReadAsync(ct))
            {
                var total = reader.GetInt64(0);
                var points = reader.GetInt64(1);
                status["recentDataFlow"] = new
                {
                    last5Minutes = total,
                    uniquePoints = points,
                    isActive = total > 0
                };
            }
        }
        catch (Exception ex)
        {
            status["recentDataFlow"] = new { error = ex.Message };
        }

        return JsonSerializer.Serialize(status, JsonOptions);
    }
}

public class CoralStreamEvent
{
    public string? Text { get; set; }
    public ToolStatus? Tool { get; set; }
}

public class ToolStatus
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
}

public class CoralContext
{
    public string? Timestamp { get; set; }
}
