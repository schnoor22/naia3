using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Naia.Infrastructure.Persistence;
using System.Data;

namespace Naia.Api.Controllers;

/// <summary>
/// Admin SQL Controller - Direct SQL access for development and debugging.
/// Supports both PostgreSQL and QuestDB queries.
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminSqlController : ControllerBase
{
    private readonly ILogger<AdminSqlController> _logger;
    private readonly NaiaDbContext _db;
    private readonly IConfiguration _configuration;

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
        var isSelect = query.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                       query.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) ||
                       query.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase);
        
        var hasMasterAccess = User.HasClaim("master_access", "true");

        // Non-SELECT queries require master access
        if (!isSelect && !hasMasterAccess)
        {
            return StatusCode(403, new { error = "Write operations require Master access" });
        }

        _logger.LogInformation("SQL query from {User}: {Query}", 
            User.Identity?.Name ?? "Anonymous",
            query.Length > 100 ? query.Substring(0, 100) + "..." : query);

        try
        {
            using var connection = _db.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = 30; // 30 second timeout

            if (isSelect)
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

                return Ok(new
                {
                    columns,
                    rows,
                    rowCount,
                    truncated = rowCount >= maxRows
                });
            }
            else
            {
                // Execute non-query (INSERT, UPDATE, DELETE, etc.)
                var affectedRows = await command.ExecuteNonQueryAsync();
                
                _logger.LogWarning("Write query executed by {User}: {Query} - {Rows} rows affected",
                    User.Identity?.Name ?? "Master",
                    query.Length > 50 ? query.Substring(0, 50) + "..." : query,
                    affectedRows);

                return Ok(new { affectedRows, success = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL query error");
            return BadRequest(new { error = ex.Message });
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
