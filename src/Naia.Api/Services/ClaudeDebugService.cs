using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Naia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Naia.Api.Services;

/// <summary>
/// Claude AI Debug Service - Embeds Claude directly into NAIA for intelligent debugging.
/// This gives Claude access to read code, query databases, check logs, and even rebuild!
/// 
/// The magic: Claude can call "tools" to investigate issues, just like a human developer.
/// </summary>
public class ClaudeDebugService
{
    private readonly ILogger<ClaudeDebugService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _projectRoot;

    public ClaudeDebugService(
        ILogger<ClaudeDebugService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _httpClient = httpClientFactory.CreateClient("Claude");
        
        // Get API key from environment or config
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") 
            ?? configuration["Claude:ApiKey"] 
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY not configured");
        
        // Project root for file access
        _projectRoot = configuration["Claude:ProjectRoot"] ?? "/home/naia/naia";
    }

    /// <summary>
    /// Send a message to Claude with full tool access for debugging NAIA.
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        string userMessage, 
        List<ChatMessage>? conversationHistory = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new List<object>();
        
        // Add conversation history
        if (conversationHistory != null)
        {
            foreach (var msg in conversationHistory)
            {
                messages.Add(new { role = msg.Role, content = msg.Content });
            }
        }
        
        // Add current message
        messages.Add(new { role = "user", content = userMessage });

        var systemPrompt = GetSystemPrompt();
        var tools = GetToolDefinitions();

        var requestBody = new
        {
            model = "claude-sonnet-4-20250514",
            max_tokens = 8192,
            stream = true,
            system = systemPrompt,
            tools = tools,
            messages = messages
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage? response = null;
        string? connectionError = null;
        
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send request to Claude API");
            connectionError = $"[Error: Failed to connect to Claude API: {ex.Message}]";
        }
        
        if (connectionError != null)
        {
            yield return connectionError;
            yield break;
        }
        
        if (response == null || !response.IsSuccessStatusCode)
        {
            var error = response != null ? await response.Content.ReadAsStringAsync(ct) : "No response";
            _logger.LogError("Claude API error: {StatusCode} - {Error}", response?.StatusCode, error);
            yield return $"[Error: Claude API returned {response?.StatusCode}]";
            yield break;
        }

        var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var currentToolUse = new StringBuilder();
        var pendingToolCalls = new List<ToolCall>();
        string? currentToolId = null;
        string? currentToolName = null;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line.Substring(6);
            if (data == "[DONE]") break;

            // Parse without try-catch to allow yield
            var parseResult = TryParseEvent(data);
            if (parseResult == null) continue;

            var (eventType, eventData) = parseResult.Value;

            switch (eventType)
            {
                case "content_block_start":
                    if (eventData.TryGetProperty("content_block", out var block))
                    {
                        var blockType = block.GetProperty("type").GetString();
                        if (blockType == "tool_use")
                        {
                            currentToolId = block.GetProperty("id").GetString();
                            currentToolName = block.GetProperty("name").GetString();
                            currentToolUse.Clear();
                            yield return $"\n🔧 *Using tool: {currentToolName}*\n";
                        }
                    }
                    break;

                case "content_block_delta":
                    if (eventData.TryGetProperty("delta", out var delta))
                    {
                        var deltaType = delta.GetProperty("type").GetString();
                        
                        if (deltaType == "text_delta" && delta.TryGetProperty("text", out var textProp))
                        {
                            yield return textProp.GetString() ?? "";
                        }
                        else if (deltaType == "input_json_delta" && delta.TryGetProperty("partial_json", out var jsonProp))
                        {
                            currentToolUse.Append(jsonProp.GetString() ?? "");
                        }
                    }
                    break;

                case "content_block_stop":
                    if (currentToolId != null && currentToolName != null)
                    {
                        pendingToolCalls.Add(new ToolCall
                        {
                            Id = currentToolId,
                            Name = currentToolName,
                            Input = currentToolUse.ToString()
                        });
                        currentToolId = null;
                        currentToolName = null;
                    }
                    break;

                case "message_stop":
                    // Execute any pending tool calls
                    foreach (var toolCall in pendingToolCalls)
                    {
                        yield return $"\n📋 *Executing {toolCall.Name}...*\n";
                        var result = await ExecuteToolAsync(toolCall.Name, toolCall.Input, ct);
                        yield return $"```\n{TruncateResult(result)}\n```\n";
                        
                        // Continue conversation with tool result
                        await foreach (var continuation in ContinueWithToolResultAsync(
                            messages, toolCall, result, ct))
                        {
                            yield return continuation;
                        }
                    }
                    break;
            }
        }
    }

    private (string eventType, JsonElement data)? TryParseEvent(string data)
    {
        try
        {
            var eventData = JsonSerializer.Deserialize<JsonElement>(data);
            var eventType = eventData.GetProperty("type").GetString();
            if (eventType != null)
                return (eventType, eventData);
        }
        catch (JsonException)
        {
            // Ignore parse errors
        }
        return null;
    }

    private async IAsyncEnumerable<string> ContinueWithToolResultAsync(
        List<object> previousMessages,
        ToolCall toolCall,
        string result,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Build the continuation request with tool result
        var messages = new List<object>(previousMessages);
        
        // Add assistant's tool use
        messages.Add(new
        {
            role = "assistant",
            content = new[]
            {
                new
                {
                    type = "tool_use",
                    id = toolCall.Id,
                    name = toolCall.Name,
                    input = JsonSerializer.Deserialize<JsonElement>(
                        string.IsNullOrEmpty(toolCall.Input) ? "{}" : toolCall.Input)
                }
            }
        });

        // Add tool result
        messages.Add(new
        {
            role = "user",
            content = new[]
            {
                new
                {
                    type = "tool_result",
                    tool_use_id = toolCall.Id,
                    content = result
                }
            }
        });

        var requestBody = new
        {
            model = "claude-sonnet-4-20250514",
            max_tokens = 8192,
            stream = true,
            system = GetSystemPrompt(),
            tools = GetToolDefinitions(),
            messages = messages
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            yield return "[Error continuing after tool use]";
            yield break;
        }

        var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line.Substring(6);
            if (data == "[DONE]") break;

            var parseResult = TryParseEvent(data);
            if (parseResult == null) continue;

            var (eventType, eventData) = parseResult.Value;

            if (eventType == "content_block_delta" && eventData.TryGetProperty("delta", out var delta))
            {
                if (delta.GetProperty("type").GetString() == "text_delta" && delta.TryGetProperty("text", out var textProp))
                {
                    yield return textProp.GetString() ?? "";
                }
            }
        }
    }

    /// <summary>
    /// Execute a tool and return the result.
    /// </summary>
    private async Task<string> ExecuteToolAsync(string toolName, string inputJson, CancellationToken ct)
    {
        try
        {
            var input = string.IsNullOrEmpty(inputJson) 
                ? new Dictionary<string, JsonElement>()
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputJson);

            return toolName switch
            {
                "read_file" => await ReadFileAsync(
                    input?.GetValueOrDefault("path").GetString() ?? ""),
                
                "list_directory" => await ListDirectoryAsync(
                    input?.GetValueOrDefault("path").GetString() ?? ""),
                
                "search_code" => await SearchCodeAsync(
                    input?.GetValueOrDefault("pattern").GetString() ?? "",
                    input?.GetValueOrDefault("file_pattern").GetString()),
                
                "query_questdb" => await QueryQuestDbAsync(
                    input?.GetValueOrDefault("query").GetString() ?? ""),
                
                "query_postgres" => await QueryPostgresAsync(
                    input?.GetValueOrDefault("query").GetString() ?? ""),
                
                "get_recent_logs" => await GetRecentLogsAsync(
                    input?.GetValueOrDefault("service").GetString() ?? "naia-api",
                    input?.TryGetValue("lines", out var lines) == true ? lines.GetInt32() : 50),
                
                "get_system_status" => await GetSystemStatusAsync(),
                
                "run_build" => await RunBuildAsync(),
                
                "get_ingestion_status" => await GetIngestionStatusAsync(),
                
                _ => $"Unknown tool: {toolName}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return $"Error: {ex.Message}";
        }
    }

    #region Tool Implementations

    private async Task<string> ReadFileAsync(string relativePath)
    {
        var fullPath = Path.Combine(_projectRoot, relativePath.TrimStart('/'));
        
        // Security: Ensure path is within project
        if (!Path.GetFullPath(fullPath).StartsWith(_projectRoot))
            return "Error: Access denied - path outside project root";

        if (!File.Exists(fullPath))
            return $"File not found: {relativePath}";

        var content = await File.ReadAllTextAsync(fullPath);
        
        // Truncate large files
        if (content.Length > 50000)
            content = content.Substring(0, 50000) + "\n\n[... truncated, file too large ...]";

        return content;
    }

    private Task<string> ListDirectoryAsync(string relativePath)
    {
        var fullPath = Path.Combine(_projectRoot, relativePath.TrimStart('/'));
        
        if (!Path.GetFullPath(fullPath).StartsWith(_projectRoot))
            return Task.FromResult("Error: Access denied");

        if (!Directory.Exists(fullPath))
            return Task.FromResult($"Directory not found: {relativePath}");

        var entries = Directory.GetFileSystemEntries(fullPath)
            .Select(e => Path.GetFileName(e) + (Directory.Exists(e) ? "/" : ""))
            .OrderBy(e => !e.EndsWith("/"))
            .ThenBy(e => e)
            .ToList();

        return Task.FromResult(string.Join("\n", entries));
    }

    private Task<string> SearchCodeAsync(string pattern, string? filePattern)
    {
        var results = new StringBuilder();
        var searchPath = _projectRoot;
        var searchPattern = filePattern ?? "*.cs";

        try
        {
            var files = Directory.GetFiles(searchPath, searchPattern, SearchOption.AllDirectories)
                .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/") && 
                           !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                .Take(100);

            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        var relativePath = Path.GetRelativePath(_projectRoot, file);
                        results.AppendLine($"{relativePath}:{i + 1}: {lines[i].Trim()}");
                        
                        if (results.Length > 10000) 
                        {
                            results.AppendLine("\n[... more results truncated ...]");
                            return Task.FromResult(results.ToString());
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Search error: {ex.Message}");
        }

        return Task.FromResult(results.Length > 0 
            ? results.ToString() 
            : "No matches found");
    }

    private async Task<string> QueryQuestDbAsync(string query)
    {
        // Safety: Only allow SELECT queries
        if (!query.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return "Error: Only SELECT queries are allowed";

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var questDbEndpoint = _configuration["QuestDb:HttpEndpoint"] ?? "http://localhost:9000";
            
            using var client = new HttpClient();
            var response = await client.GetAsync($"{questDbEndpoint}/exec?query={Uri.EscapeDataString(query)}");
            var result = await response.Content.ReadAsStringAsync();
            
            return TruncateResult(result);
        }
        catch (Exception ex)
        {
            return $"QuestDB query error: {ex.Message}";
        }
    }

    private async Task<string> QueryPostgresAsync(string query)
    {
        // Safety: Only allow SELECT queries
        if (!query.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return "Error: Only SELECT queries are allowed";

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NaiaDbContext>();
            
            var results = new List<string>();
            using var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            
            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            
            using var reader = await cmd.ExecuteReaderAsync();
            
            // Get column names
            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.GetName(i))
                .ToList();
            results.Add(string.Join(" | ", columns));
            results.Add(new string('-', results[0].Length));
            
            int rowCount = 0;
            while (await reader.ReadAsync() && rowCount < 100)
            {
                var values = Enumerable.Range(0, reader.FieldCount)
                    .Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "")
                    .ToList();
                results.Add(string.Join(" | ", values));
                rowCount++;
            }

            if (rowCount >= 100)
                results.Add("\n[... more rows truncated ...]");

            return string.Join("\n", results);
        }
        catch (Exception ex)
        {
            return $"PostgreSQL query error: {ex.Message}";
        }
    }

    private async Task<string> GetRecentLogsAsync(string service, int lines)
    {
        try
        {
            // Read from PostgreSQL logs table
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NaiaDbContext>();
            
            var query = $@"
                SELECT timestamp, level, message, exception 
                FROM logs 
                WHERE message LIKE '%{service}%' OR source_context LIKE '%{service}%'
                ORDER BY timestamp DESC 
                LIMIT {Math.Min(lines, 100)}";

            using var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            
            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            
            var results = new StringBuilder();
            using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                var timestamp = reader.GetDateTime(0).ToString("HH:mm:ss");
                var level = reader.GetString(1);
                var message = reader.GetString(2);
                results.AppendLine($"[{timestamp} {level}] {message}");
            }

            return results.Length > 0 ? results.ToString() : "No logs found";
        }
        catch (Exception ex)
        {
            return $"Log retrieval error: {ex.Message}";
        }
    }

    private async Task<string> GetSystemStatusAsync()
    {
        var status = new StringBuilder();
        
        try
        {
            // Check API health
            status.AppendLine("=== NAIA System Status ===\n");
            
            using var scope = _serviceProvider.CreateScope();
            
            // PostgreSQL
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<NaiaDbContext>();
                await db.Database.CanConnectAsync();
                status.AppendLine("✅ PostgreSQL: Connected");
            }
            catch (Exception ex)
            {
                status.AppendLine($"❌ PostgreSQL: {ex.Message}");
            }

            // QuestDB
            try
            {
                var questDbEndpoint = _configuration["QuestDb:HttpEndpoint"] ?? "http://localhost:9000";
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.GetAsync($"{questDbEndpoint}/exec?query=SELECT%20count()%20FROM%20point_data");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    status.AppendLine($"✅ QuestDB: Connected - {result}");
                }
            }
            catch (Exception ex)
            {
                status.AppendLine($"❌ QuestDB: {ex.Message}");
            }

            // Redis
            try
            {
                var redis = scope.ServiceProvider.GetService<StackExchange.Redis.IConnectionMultiplexer>();
                if (redis?.IsConnected == true)
                    status.AppendLine("✅ Redis: Connected");
                else
                    status.AppendLine("⚠️ Redis: Not connected");
            }
            catch (Exception ex)
            {
                status.AppendLine($"❌ Redis: {ex.Message}");
            }

            status.AppendLine($"\nServer Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            status.AppendLine($"Machine: {Environment.MachineName}");
            status.AppendLine($".NET Version: {Environment.Version}");
        }
        catch (Exception ex)
        {
            status.AppendLine($"Error getting status: {ex.Message}");
        }

        return status.ToString();
    }

    private async Task<string> RunBuildAsync()
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build --configuration Release",
                    WorkingDirectory = _projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var output = new StringBuilder();
            process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) output.AppendLine($"ERR: {e.Data}"); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            output.AppendLine($"\nBuild completed with exit code: {process.ExitCode}");
            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Build failed: {ex.Message}";
        }
    }

    private async Task<string> GetIngestionStatusAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var ingestionService = scope.ServiceProvider.GetService<PIDataIngestionService>();
            
            if (ingestionService == null)
                return "Ingestion service not found";

            // Query recent data from QuestDB
            var questDbEndpoint = _configuration["QuestDb:HttpEndpoint"] ?? "http://localhost:9000";
            using var client = new HttpClient();
            
            var countQuery = "SELECT count() FROM point_data WHERE timestamp > dateadd('h', -1, now())";
            var response = await client.GetAsync($"{questDbEndpoint}/exec?query={Uri.EscapeDataString(countQuery)}");
            var countResult = await response.Content.ReadAsStringAsync();

            return $"Ingestion Status:\n{countResult}\n\n(Recent 1 hour data count from QuestDB)";
        }
        catch (Exception ex)
        {
            return $"Error getting ingestion status: {ex.Message}";
        }
    }

    #endregion

    #region Helper Methods

    private string GetSystemPrompt()
    {
        return @"You are NAIA's embedded AI debugger - a senior engineer with deep knowledge of the NAIA Industrial Historian codebase. You have direct access to:

1. **Source Code**: Read any file in the project (C#, TypeScript, SQL, configs)
2. **Databases**: Query QuestDB (time-series) and PostgreSQL (metadata)
3. **System Logs**: View recent application logs
4. **System Status**: Check health of all services
5. **Build System**: Trigger builds to validate changes

## NAIA Architecture Quick Reference:
- **Naia.Api**: ASP.NET Core API (port 5000)
- **Naia.Web**: SvelteKit UI
- **Naia.Infrastructure**: QuestDB, PostgreSQL, Redis, Kafka
- **Naia.Connectors**: PI, OPC-UA, CSV data sources
- **Naia.PatternEngine**: AI pattern detection (Hangfire jobs)

## Key Files:
- `src/Naia.Api/Program.cs` - API entry point, all endpoints
- `src/Naia.Infrastructure/Persistence/*` - Database repositories
- `src/Naia.Connectors/GenericCsv/*` - CSV replay connector
- `appsettings.json` files - Configuration

## Your Role:
- Help diagnose issues by examining code and data
- Explain how components work
- Suggest fixes for problems
- Guide users through debugging steps

Use your tools to investigate before answering. Be concise but thorough.
Always show your reasoning and what you found.";
    }

    private object[] GetToolDefinitions()
    {
        return new object[]
        {
            new
            {
                name = "read_file",
                description = "Read the contents of a source file. Use relative paths from project root.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Relative path to file, e.g., 'src/Naia.Api/Program.cs'" }
                    },
                    required = new[] { "path" }
                }
            },
            new
            {
                name = "list_directory",
                description = "List files and directories in a path",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Relative path to directory, e.g., 'src/Naia.Api'" }
                    },
                    required = new[] { "path" }
                }
            },
            new
            {
                name = "search_code",
                description = "Search for a pattern across all source files",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new { type = "string", description = "Text pattern to search for" },
                        file_pattern = new { type = "string", description = "File glob pattern, e.g., '*.cs' or '*.ts'" }
                    },
                    required = new[] { "pattern" }
                }
            },
            new
            {
                name = "query_questdb",
                description = "Run a SELECT query against QuestDB time-series database. Tables: point_data (timestamp, point_id, value, quality)",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "SQL SELECT query" }
                    },
                    required = new[] { "query" }
                }
            },
            new
            {
                name = "query_postgres",
                description = "Run a SELECT query against PostgreSQL. Tables: points, data_sources, patterns, suggestions, elements, logs",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "SQL SELECT query" }
                    },
                    required = new[] { "query" }
                }
            },
            new
            {
                name = "get_recent_logs",
                description = "Get recent application logs",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        service = new { type = "string", description = "Service name: naia-api, naia-ingestion, etc." },
                        lines = new { type = "integer", description = "Number of log lines (default 50, max 100)" }
                    },
                    required = new[] { "service" }
                }
            },
            new
            {
                name = "get_system_status",
                description = "Get current status of all NAIA services (PostgreSQL, QuestDB, Redis, etc.)",
                input_schema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new
            {
                name = "run_build",
                description = "Run 'dotnet build' to compile the solution. Use to validate code changes.",
                input_schema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new
            {
                name = "get_ingestion_status",
                description = "Get current data ingestion status and recent point counts",
                input_schema = new
                {
                    type = "object",
                    properties = new { }
                }
            }
        };
    }

    private string TruncateResult(string result, int maxLength = 5000)
    {
        if (result.Length <= maxLength) return result;
        return result.Substring(0, maxLength) + "\n\n[... truncated ...]";
    }

    #endregion
}

public class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}

public class ToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Input { get; set; } = "";
}
