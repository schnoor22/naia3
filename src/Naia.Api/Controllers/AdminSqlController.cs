using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Naia.Infrastructure.Persistence;
using System.Data;
using System.Text.RegularExpressions;

namespace Naia.Api.Controllers;

/// <summary>
/// Admin SQL Controller - Direct SQL access for development and debugging.
/// Supports both PostgreSQL and QuestDB queries with security restrictions.
/// </summary>
[ApiController]
[Route("api/admin")]
[EnableRateLimiting("SqlConsole")]
public class AdminSqlController : ControllerBase
{
    private readonly ILogger<AdminSqlController> _logger;
    private readonly NaiaDbContext _db;
    private readonly IConfiguration _configuration;
    
    // Dangerous keywords that should NEVER appear anywhere in a query (unless master mode)
    private static readonly string[] DangerousKeywords = {
        "DROP", "TRUNCATE", "ALTER", "GRANT", "REVOKE", "CREATE USER", 
        "CREATE ROLE", "VACUUM", "REINDEX", "CLUSTER", "COPY", "\\\\copy"
    };
    
    // Keywords that require master mode (write operations)
    private static readonly string[] WriteKeywords = {
        "INSERT", "UPDATE", "DELETE", "CREATE", "MERGE", "UPSERT"
    };

    public AdminSqlController(
        ILogger<AdminSqlController> logger,
        NaiaDbContext db,
        IConfiguration configuration)
    {
        _logger = logger;
        _db = db;
        _configuration = configuration;
    }

    /// <summary>
    /// Execute a SQL query against PostgreSQL.
    /// SELECT queries allowed for all authenticated users.
    /// Write queries require Master access.
    /// </summary>
    [HttpPost("sql")]
    public async Task<IActionResult> ExecuteSql([FromBody] SqlQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "Query is required" });

        var query = request.Query.Trim();
        var queryUpper = query.ToUpperInvariant();
        
        var hasMasterAccess = Request.Headers.TryGetValue("X-Master-Token", out var token) &&
            token.ToString() == (_configuration["MasterToken"] ?? Environment.GetEnvironmentVariable("NAIA_MASTER_TOKEN"));
        
        // SECURITY: Check for dangerous keywords that are NEVER allowed
        foreach (var keyword in DangerousKeywords)
        {
            if (queryUpper.Contains(keyword))
            {
                _logger.LogWarning("BLOCKED dangerous SQL keyword '{Keyword}' from {IP}", 
                    keyword, HttpContext.Connection.RemoteIpAddress);
                await AuditQueryAsync("BLOCKED", query, 0, 0, hasMasterAccess, $"Blocked: {keyword}");
                return StatusCode(403, new { error = $"Query contains forbidden keyword: {keyword}" });
            }
        }
        
        // Check if this is a read-only query
        var isReadOnly = queryUpper.StartsWith("SELECT") || 
                         queryUpper.StartsWith("EXPLAIN") ||
                         queryUpper.StartsWith("SHOW") ||
                         (queryUpper.StartsWith("WITH") && !ContainsWriteKeyword(queryUpper));

        // Non-read-only queries require master access
        if (!isReadOnly && !hasMasterAccess)
        {
            _logger.LogWarning("Write SQL blocked - no master access from {IP}", 
                HttpContext.Connection.RemoteIpAddress);
            await AuditQueryAsync("DENIED", query, 0, 0, false, "Write operation without master access");
            return StatusCode(403, new { error = "Write operations require Master access (X-Master-Token header)" });
        }

        _logger.LogInformation("SQL query from {IP} (Master: {IsMaster}): {Query}", 
            HttpContext.Connection.RemoteIpAddress,
            hasMasterAccess,
            query.Length > 100 ? query.Substring(0, 100) + "..." : query);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            using var connection = _db.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = 30; // 30 second timeout

            if (isReadOnly)
            {
                // Return result set
                using var reader = await command.ExecuteReaderAsync();
                
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }

                var rows = new List<Dictionary<string, object?>>();
                int rowCount = 0;
                const int maxRows = 1000;

                while (await reader.ReadAsync() && rowCount < maxRows)
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        // Convert special types
                        if (value is DateTime dt)
                            value = dt.ToString("yyyy-MM-dd HH:mm:ss");
                        else if (value is DateTimeOffset dto)
                            value = dto.ToString("yyyy-MM-dd HH:mm:ss zzz");
                        else if (value is Guid g)
                            value = g.ToString();
                        row[columns[i]] = value;
                    }
                    rows.Add(row);
                    rowCount++;
                }

                stopwatch.Stop();
                await AuditQueryAsync("SELECT", query, rowCount, (int)stopwatch.ElapsedMilliseconds, hasMasterAccess, null);

                return Ok(new
                {
                    columns,
                    rows,
                    rowCount,
                    truncated = rowCount >= maxRows,
                    executionMs = stopwatch.ElapsedMilliseconds
                });
            }
            else
            {
                // Execute non-query (INSERT, UPDATE, DELETE, etc.)
                var affectedRows = await command.ExecuteNonQueryAsync();
                
                stopwatch.Stop();
                
                _logger.LogWarning("Write query executed (Master Mode): {Query} - {Rows} rows affected",
                    query.Length > 50 ? query.Substring(0, 50) + "..." : query,
                    affectedRows);
                
                await AuditQueryAsync("WRITE", query, affectedRows, (int)stopwatch.ElapsedMilliseconds, true, null);

                return Ok(new { affectedRows, success = true, executionMs = stopwatch.ElapsedMilliseconds });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "SQL query error");
            await AuditQueryAsync("ERROR", query, 0, (int)stopwatch.ElapsedMilliseconds, hasMasterAccess, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
    }
    
    private static bool ContainsWriteKeyword(string queryUpper)
    {
        return WriteKeywords.Any(kw => Regex.IsMatch(queryUpper, $@"\b{kw}\b"));
    }
    
    private async Task AuditQueryAsync(string queryType, string query, int rowCount, int executionMs, bool isMasterMode, string? error)
    {
        try
        {
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO sql_audit_log (user_name, ip_address, query_type, query_text, row_count, execution_ms, is_master_mode, error_message)
                VALUES (@user, @ip, @type, @query, @rows, @ms, @master, @error)";
            
            var pUser = cmd.CreateParameter();
            pUser.ParameterName = "@user";
            pUser.Value = (object?)User.Identity?.Name ?? "anonymous";
            cmd.Parameters.Add(pUser);
            
            var pIp = cmd.CreateParameter();
            pIp.ParameterName = "@ip";
            pIp.Value = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            cmd.Parameters.Add(pIp);
            
            var pType = cmd.CreateParameter();
            pType.ParameterName = "@type";
            pType.Value = queryType;
            cmd.Parameters.Add(pType);
            
            var pQuery = cmd.CreateParameter();
            pQuery.ParameterName = "@query";
            pQuery.Value = query.Length > 4000 ? query.Substring(0, 4000) : query;
            cmd.Parameters.Add(pQuery);
            
            var pRows = cmd.CreateParameter();
            pRows.ParameterName = "@rows";
            pRows.Value = rowCount;
            cmd.Parameters.Add(pRows);
            
            var pMs = cmd.CreateParameter();
            pMs.ParameterName = "@ms";
            pMs.Value = executionMs;
            cmd.Parameters.Add(pMs);
            
            var pMaster = cmd.CreateParameter();
            pMaster.ParameterName = "@master";
            pMaster.Value = isMasterMode;
            cmd.Parameters.Add(pMaster);
            
            var pError = cmd.CreateParameter();
            pError.ParameterName = "@error";
            pError.Value = (object?)error ?? DBNull.Value;
            cmd.Parameters.Add(pError);
            
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            // Audit logging failure shouldn't block the query
            _logger.LogWarning(ex, "Failed to write SQL audit log");
        }
    }

    /// <summary>
    /// Execute a query against QuestDB.
    /// </summary>
    [HttpPost("questdb")]
    public async Task<IActionResult> ExecuteQuestDb([FromBody] SqlQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "Query is required" });

        var query = request.Query.Trim();
        
        // Only SELECT queries for QuestDB via HTTP
        if (!query.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only SELECT queries supported for QuestDB" });
        }

        try
        {
            var questDbEndpoint = _configuration["QuestDb:HttpEndpoint"] ?? "http://localhost:9000";
            
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var response = await client.GetAsync($"{questDbEndpoint}/exec?query={Uri.EscapeDataString(query)}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return BadRequest(new { error = $"QuestDB error: {errorText}" });
            }

            var result = await response.Content.ReadAsStringAsync();
            return Content(result, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QuestDB query error");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get Redis cache stats and sample keys.
    /// </summary>
    [HttpGet("redis")]
    public async Task<IActionResult> GetRedisInfo()
    {
        try
        {
            var redis = HttpContext.RequestServices.GetService<StackExchange.Redis.IConnectionMultiplexer>();
            if (redis == null || !redis.IsConnected)
            {
                return Ok(new { connected = false, error = "Redis not connected" });
            }

            var server = redis.GetServer(redis.GetEndPoints().First());
            var db = redis.GetDatabase();
            
            // Get some stats
            var info = await server.InfoAsync();
            var keyCount = server.DatabaseSize();
            
            // Sample some keys
            var sampleKeys = server.Keys(pattern: "*", pageSize: 20).Take(20).ToList();
            var samples = new List<object>();
            
            foreach (var key in sampleKeys)
            {
                var type = await db.KeyTypeAsync(key);
                var ttl = await db.KeyTimeToLiveAsync(key);
                samples.Add(new
                {
                    key = key.ToString(),
                    type = type.ToString(),
                    ttl = ttl?.TotalSeconds
                });
            }

            return Ok(new
            {
                connected = true,
                keyCount,
                samples,
                endpoints = redis.GetEndPoints().Select(e => e.ToString())
            });
        }
        catch (Exception ex)
        {
            return Ok(new { connected = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get environment and configuration info for debugging.
    /// Requires Master access.
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        if (!User.HasClaim("master_access", "true"))
            return Forbid();

        return Ok(new
        {
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            machineName = Environment.MachineName,
            osVersion = Environment.OSVersion.ToString(),
            dotnetVersion = Environment.Version.ToString(),
            processId = Environment.ProcessId,
            workingDirectory = Environment.CurrentDirectory,
            connectionStrings = new
            {
                postgres = MaskConnectionString(_configuration.GetConnectionString("DefaultConnection")),
                questdb = _configuration["QuestDb:HttpEndpoint"],
                redis = MaskConnectionString(_configuration.GetConnectionString("Redis"))
            },
            timestamp = DateTime.UtcNow
        });
    }

    private string? MaskConnectionString(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        // Mask password in connection string
        return System.Text.RegularExpressions.Regex.Replace(
            connectionString, 
            @"(password|pwd)=[^;]+", 
            "$1=***", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

public class SqlQueryRequest
{
    public string Query { get; set; } = "";
}
