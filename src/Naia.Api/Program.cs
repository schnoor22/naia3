using Microsoft.EntityFrameworkCore;
using Naia.Api.Dtos;
using Naia.Api.Hubs;
using Naia.Api.Middleware;
using Naia.Application.Abstractions;
using Naia.Connectors;
using Naia.Connectors.Abstractions;
using Naia.Connectors.PI;
using Naia.Domain.Entities;
using Naia.Domain.ValueObjects;
using Naia.Infrastructure;
using Naia.Infrastructure.Persistence;
using Naia.Infrastructure.Telemetry;
using Naia.Api.Services;
using Naia.PatternEngine;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;
using System.Threading.RateLimiting;

// =============================================================================
// SERILOG CONFIGURATION - Structured Logging to PostgreSQL + Console
// =============================================================================
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}")
    .WriteTo.PostgreSQL(
        connectionString: "Host=localhost;Port=5432;Database=naia;Username=naia;Password=naia_dev_password;SslMode=Disable",
        tableName: "logs",
        needAutoCreateTable: true)
    .CreateLogger();

try
{
    Log.Information("═══════════════════════════════════════════════════════════════════");
    Log.Information("  Starting NAIA API - The First Industrial Historian That Learns");
    Log.Information("═══════════════════════════════════════════════════════════════════");

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for all logging
builder.Host.UseSerilog();

// Add NAIA infrastructure services (PostgreSQL, QuestDB, Redis, Kafka)
builder.Services.AddNaiaInfrastructure(builder.Configuration);

// Add PI connectors (registered but inactive - PI disabled in config)
builder.Services.AddPIWebApiConnector(builder.Configuration);

// Add PI → Kafka data ingestion service (singleton to maintain state)
builder.Services.AddSingleton<PIDataIngestionService>();

// Add Backfill Orchestrator (background service for historical data backfill)
builder.Services.AddSingleton<BackfillOrchestrator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackfillOrchestrator>());

// =============================================================================
// CLAUDE DEBUG CONSOLE - AI-Powered Debugging (Master Mode Only)
// =============================================================================
builder.Services.AddHttpClient("Claude");
builder.Services.AddSingleton<ClaudeDebugService>();

// =============================================================================
// CORAL AI ASSISTANT - The Nurturing Guide to NAIA's Data Ocean
// =============================================================================
builder.Services.AddSingleton<CoralAssistantService>();

// =============================================================================
// PATTERN FLYWHEEL - The Learning Engine (Hangfire Jobs)
// =============================================================================
// Point lookup cache for pattern workers
builder.Services.AddPointLookupService();

// Pattern engine with Hangfire job scheduling
builder.Services.AddPatternEngine(builder.Configuration);

// Pattern repositories for API
builder.Services.AddScoped<ISuggestionRepository, SuggestionRepository>();
builder.Services.AddScoped<IPatternRepository, PatternRepository>();
builder.Services.AddScoped<IKnowledgeBaseRepository, KnowledgeBaseRepository>();

// SignalR for real-time suggestion notifications
builder.Services.AddSignalR();
builder.Services.AddSingleton<IPatternHubNotifier, PatternHubNotifier>();

// =============================================================================
// OPENTELEMETRY - Distributed Tracing
// =============================================================================
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";
var serviceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "naia-api";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: "3.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["host.name"] = Environment.MachineName
        }))
    .WithTracing(tracing => tracing
        .AddSource(NaiaMetrics.ActivitySource.Name)
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.Filter = ctx => 
                !ctx.Request.Path.StartsWithSegments("/metrics") &&
                !ctx.Request.Path.StartsWithSegments("/health") &&
                !ctx.Request.Path.StartsWithSegments("/hangfire");
        })
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
        })
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpEndpoint);
        }));

// Add Controllers for pattern APIs
builder.Services.AddControllers();

// Add API services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "NAIA API", Version = "v3.0" });
});

// Add CORS for UI development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowUI", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",  // Vite dev server
                "http://localhost:5052",  // Production
                "http://localhost:5000",  // Legacy
                "http://localhost:5001"   // Alternative
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials(); // Required for SignalR
    });
});

// =============================================================================
// RATE LIMITING - Protect against abuse and DoS
// =============================================================================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    // Global limiter: 100 requests per second per IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromSeconds(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10
        });
    });
    
    // SQL Console: Very restricted - 10 queries per minute per IP
    options.AddPolicy("SqlConsole", httpContext =>
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 2
        });
    });
    
    // Coral AI: 20 requests per minute per IP (expensive AI operations)
    options.AddPolicy("CoralAI", httpContext =>
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ipAddress, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 5
        });
    });
    
    options.OnRejected = async (context, token) =>
    {
        Log.Warning("Rate limit exceeded for {IP} on {Path}", 
            context.HttpContext.Connection.RemoteIpAddress,
            context.HttpContext.Request.Path);
        
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded",
            retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) 
                ? retryAfter.TotalSeconds : 60
        }, token);
    };
});

// Configure to listen on all interfaces (IPv4 and IPv6)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(5000);
});

var app = builder.Build();

// =============================================================================
// PROMETHEUS METRICS - /metrics endpoint
// =============================================================================
app.UseHttpMetrics(options =>
{
    // Track request duration, count, and in-progress requests
    options.AddCustomLabel("host", context => context.Request.Host.Host);
});

// Configure the HTTP request pipeline.
app.UseCors("AllowUI");

// Rate limiting - must be early to protect all endpoints
app.UseRateLimiter();

// Master access override - must be early in pipeline
app.UseMasterAccess();

// Serve static files from wwwroot (for embedded SPA)
app.UseDefaultFiles();
app.UseStaticFiles();

// Swagger enabled in all environments for debugging
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "NAIA API v3.0");
    c.RoutePrefix = "swagger";
});

// =============================================================================
// PATTERN FLYWHEEL - Hangfire Dashboard & Job Scheduling
// =============================================================================
app.UsePatternEngine(builder.Configuration);

// ============================================================================
// HEALTH CHECK ENDPOINT
// ============================================================================
app.MapGet("/api/health", async (
    NaiaDbContext db,
    ITimeSeriesReader questDb,
    ICurrentValueCache redis) =>
{
    var checks = new Dictionary<string, object>();
    
    // PostgreSQL check
    try
    {
        await db.Database.CanConnectAsync();
        checks["postgresql"] = new { status = "healthy" };
    }
    catch (Exception ex)
    {
        checks["postgresql"] = new { status = "unhealthy", error = ex.Message };
    }
    
    // QuestDB check
    try
    {
        await questDb.GetLastValueAsync(0);
        checks["questdb"] = new { status = "healthy" };
    }
    catch (Exception ex)
    {
        checks["questdb"] = new { status = "unhealthy", error = ex.Message };
    }
    
    // Redis check
    try
    {
        await redis.GetAsync(0);
        checks["redis"] = new { status = "healthy" };
    }
    catch (Exception ex)
    {
        checks["redis"] = new { status = "unhealthy", error = ex.Message };
    }
    
    var allHealthy = checks.Values.All(c => 
        c.GetType().GetProperty("status")?.GetValue(c)?.ToString() == "healthy");
    
    return Results.Json(new
    {
        status = allHealthy ? "healthy" : "degraded",
        checks,
        timestamp = DateTime.UtcNow
    });
})
.WithName("HealthCheck")
.WithTags("System");

// Version endpoint for debugging
var apiStartTime = DateTime.UtcNow;
app.MapGet("/api/version", () =>
{
    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
    var version = assembly.GetName().Version?.ToString() ?? "3.0.0";
    var buildDate = System.IO.File.GetLastWriteTimeUtc(assembly.Location);
    
    return Results.Ok(new
    {
        version = "3.0.0",
        apiVersion = version,
        buildDate = buildDate.ToString("yyyy-MM-dd HH:mm:ss UTC"),
        apiStartTime = apiStartTime.ToString("yyyy-MM-dd HH:mm:ss UTC"),
        uptime = (DateTime.UtcNow - apiStartTime).ToString(@"d\.hh\:mm\:ss"),
        environment = app.Environment.EnvironmentName,
        machineName = Environment.MachineName,
        dotnetVersion = Environment.Version.ToString(),
        timestamp = DateTime.UtcNow
    });
})
.WithName("GetVersion")
.WithTags("System")
.WithOpenApi();

// ============================================================================
// DATA SOURCE ENDPOINTS
// ============================================================================
app.MapGet("/api/datasources", async (IDataSourceRepository repo) =>
{
    var sources = await repo.GetAllAsync();
    return Results.Ok(sources);
})
.WithName("GetDataSources")
.WithTags("DataSources");

app.MapGet("/api/datasources/{id:guid}", async (Guid id, IDataSourceRepository repo) =>
{
    var source = await repo.GetByIdAsync(id);
    return source is null ? Results.NotFound() : Results.Ok(source);
})
.WithName("GetDataSource")
.WithTags("DataSources");

// ============================================================================
// POINT ENDPOINTS
// ============================================================================
app.MapGet("/api/points", async (
    IPointRepository repo,
    string? tagName = null,
    Guid? dataSourceId = null,
    bool? enabled = null,
    int skip = 0,
    int take = 100) =>
{
    var points = await repo.SearchAsync(tagName, dataSourceId, enabled, skip, take);
    var total = await repo.CountAsync(tagName, dataSourceId, enabled);
    
    // Convert to DTOs to avoid circular references
    var pointDtos = points.Select(p => p.ToDto()).ToList();
    
    return Results.Ok(new
    {
        data = pointDtos,
        total,
        skip,
        take
    });
})
.WithName("SearchPoints")
.WithTags("Points");

app.MapGet("/api/points/{id:guid}", async (Guid id, IPointRepository repo) =>
{
    var point = await repo.GetByIdAsync(id);
    return point is null ? Results.NotFound() : Results.Ok(point);
})
.WithName("GetPoint")
.WithTags("Points");

app.MapDelete("/api/points/bulk", async (
    HttpContext httpContext,
    IPointRepository pointRepo,
    IDataSourceRepository dataSourceRepo,
    NaiaDbContext dbContext,
    ILogger<Program> logger) =>
{
    var request = await httpContext.Request.ReadFromJsonAsync<BulkDeletePointsRequest>();
    if (request is null || request.PointIds.Count == 0)
        return Results.BadRequest("No point IDs provided");

    var deletedCount = 0;
    var csvFilesDeleted = 0;
    var errors = new List<string>();

    foreach (var pointId in request.PointIds)
    {
        try
        {
            var point = await pointRepo.GetByIdAsync(pointId);
            if (point is null)
            {
                errors.Add($"Point {pointId} not found");
                continue;
            }

            // If CSV deletion requested, try to find and delete the CSV file
            if (request.DeleteCsvFiles && point.DataSourceId.HasValue)
            {
                var dataSource = await dataSourceRepo.GetByIdAsync(point.DataSourceId.Value);
                if (dataSource?.SourceType == DataSourceType.GenericCsvReplay)
                {
                    // Try to find CSV file based on point name
                    // Format: /opt/naia/data/solar/{pendleton|bluewater}/{pointname}.csv
                    var basePaths = new[]
                    {
                        "/opt/naia/data/solar/pendleton",
                        "/opt/naia/data/solar/bluewater"
                    };

                    foreach (var basePath in basePaths)
                    {
                        // Try exact match first
                        var csvPath = Path.Combine(basePath, $"{point.Name}.csv");
                        if (File.Exists(csvPath))
                        {
                            try
                            {
                                File.Delete(csvPath);
                                csvFilesDeleted++;
                                logger.LogInformation("Deleted CSV file: {Path}", csvPath);
                                break;
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to delete CSV file: {Path}", csvPath);
                                errors.Add($"Failed to delete CSV for {point.Name}: {ex.Message}");
                            }
                        }
                    }
                }
            }

            // Delete the point from database
            await pointRepo.DeleteAsync(pointId);
            deletedCount++;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete point {PointId}", pointId);
            errors.Add($"Failed to delete point {pointId}: {ex.Message}");
        }
    }

    // Save changes to database
    await dbContext.SaveChangesAsync();

    return Results.Ok(new
    {
        deletedPoints = deletedCount,
        csvFilesDeleted,
        errors = errors.Count > 0 ? errors : null
    });
})
.WithName("BulkDeletePoints")
.WithTags("Points");

// ============================================================================
// TIME-SERIES DATA ENDPOINTS
// ============================================================================
app.MapGet("/api/points/{id:guid}/current", async (
    Guid id,
    IPointRepository pointRepo,
    ICurrentValueCache cache) =>
{
    var point = await pointRepo.GetByIdAsync(id);
    if (point is null) return Results.NotFound();
    
    if (point.PointSequenceId is null)
        return Results.BadRequest("Point not yet synchronized to time-series database");
    
    var current = await cache.GetAsync(point.PointSequenceId.Value);
    return current is null 
        ? Results.NotFound("No current value") 
        : Results.Ok(current);
})
.WithName("GetCurrentValue")
.WithTags("TimeSeries");

app.MapGet("/api/points/{id:guid}/history", async (
    Guid id,
    IPointRepository pointRepo,
    ITimeSeriesReader tsReader,
    DateTime? start = null,
    DateTime? end = null,
    int limit = 1000) =>
{
    var point = await pointRepo.GetByIdAsync(id);
    if (point is null) return Results.NotFound();
    
    if (point.PointSequenceId is null)
        return Results.BadRequest("Point not yet synchronized to time-series database");
    
    var startTime = start ?? DateTime.UtcNow.AddHours(-1);
    var endTime = end ?? DateTime.UtcNow;
    
    var data = await tsReader.ReadRangeAsync(
        point.PointSequenceId.Value, 
        startTime, 
        endTime, 
        limit);
    
    // Transform to DTO format with string timestamps and quality as string
    var dataDto = data.Select(d => new
    {
        timestamp = d.Timestamp.ToString("O"),
        value = d.Value,
        quality = d.Quality.ToString()
    }).ToList();
    
    return Results.Ok(new
    {
        pointId = id,
        sequenceId = point.PointSequenceId,
        tagName = point.Name,
        start = startTime,
        end = endTime,
        count = data.Count,
        data = dataDto
    });
})
.WithName("GetHistory")
.WithTags("TimeSeries");

// ============================================================================
// INGESTION ENDPOINT (for testing)
// ============================================================================
app.MapPost("/api/ingest", async (
    DataPointBatch batch,
    IDataPointProducer producer) =>
{
    var result = await producer.PublishAsync(batch);
    
    return result.Success 
        ? Results.Accepted(value: new { batchId = batch.BatchId, status = "queued" })
        : Results.Problem(result.ErrorMessage ?? "Failed to queue batch");
})
.WithName("IngestBatch")
.WithTags("Ingestion");

// ============================================================================
// PIPELINE STATUS ENDPOINT
// ============================================================================
app.MapGet("/api/pipeline/health", async (IIngestionPipeline pipeline) =>
{
    var health = await pipeline.GetHealthAsync();
    return Results.Ok(health);
})
.WithName("GetPipelineHealth")
.WithTags("Pipeline");

app.MapGet("/api/pipeline/metrics", async (ITimeSeriesReader questDb, IConfiguration config) =>
{
    try
    {
        // Get QuestDB connection string from configuration
        var questDbConfig = config.GetSection("QuestDb");
        var pgWireEndpoint = questDbConfig.GetValue<string>("PgWireEndpoint") ?? "localhost:8812";
        var host = pgWireEndpoint.Contains(':') ? pgWireEndpoint.Split(':')[0] : pgWireEndpoint;
        var port = pgWireEndpoint.Contains(':') ? pgWireEndpoint.Split(':')[1] : "8812";
        var connectionString = $"Host={host};Port={port};Database=qdb;Username=admin;Password=quest;Timeout=5;ServerCompatibilityMode=NoTypeLoading";
        
        // Query QuestDB for actual row count and recent activity
        using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        
        // Get total count - simple query that QuestDB supports
        long totalCount = 0;
        using (var countCmd = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM point_data", connection))
        {
            totalCount = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);
        }
        
        // Get latest timestamp
        DateTime? latestTimestamp = null;
        using (var maxCmd = new Npgsql.NpgsqlCommand("SELECT MAX(timestamp) FROM point_data", connection))
        {
            var result = await maxCmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
            {
                latestTimestamp = (DateTime)result;
            }
        }
        
        // Determine if pipeline is active (data within last 5 minutes)
        var isRunning = latestTimestamp.HasValue && 
                        (DateTime.UtcNow - latestTimestamp.Value).TotalMinutes < 5;
        
        // Calculate points per second from last minute
        double pointsPerSecond = 0;
        if (isRunning && latestTimestamp.HasValue)
        {
            var oneMinuteAgo = latestTimestamp.Value.AddMinutes(-1);
            using var rateCmd = new Npgsql.NpgsqlCommand(
                $"SELECT COUNT(*) FROM point_data WHERE timestamp >= '{oneMinuteAgo:yyyy-MM-ddTHH:mm:ss.fffZ}'", 
                connection);
            
            var lastMinuteCount = (long)(await rateCmd.ExecuteScalarAsync() ?? 0L);
            pointsPerSecond = lastMinuteCount / 60.0;
        }
        
        // Estimate batches (assuming ~90 points per batch)
        var estimatedBatches = totalCount > 0 ? totalCount / 90 : 0;
        
        return Results.Ok(new
        {
            isRunning = isRunning,
            pointsPerSecond = Math.Round(pointsPerSecond, 1),
            totalPointsIngested = totalCount,
            batchesProcessed = estimatedBatches,
            errors = 0,
            lastUpdateTime = DateTime.UtcNow.ToString("o"),
            latestDataTimestamp = latestTimestamp?.ToString("o")
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            isRunning = false,
            pointsPerSecond = 0.0,
            totalPointsIngested = 0L,
            batchesProcessed = 0L,
            errors = 1,
            lastUpdateTime = DateTime.UtcNow.ToString("o"),
            error = ex.Message
        });
    }
})
.WithName("GetPipelineMetrics")
.WithTags("Pipeline");

// ============================================================================
// KAFKA / FULL PIPELINE STATUS ENDPOINT
// ============================================================================
app.MapGet("/api/pipeline/kafka", (
    PIDataIngestionService piIngestion,
    IDataPointProducer producer) =>
{
    var producerStatus = piIngestion.GetStatus();
    
    return Results.Ok(new
    {
        architecture = new
        {
            description = "Industrial Historian Pipeline - The First That Learns From You",
            dataFlow = new[]
            {
                "PI System (sdhqpisrvr01) - Source of truth for industrial data",
                "PIDataIngestionService - Polls PI, publishes to Kafka",
                "Kafka (naia.datapoints) - Message backbone, buffering, replay",
                "Naia.Ingestion Worker - Consumes, deduplicates, persists",
                "QuestDB - Time-series storage (ILP protocol)",
                "Redis - Current value cache (sub-ms reads)",
                "Pattern Engine - Learns from your organization patterns"
            },
            topics = new
            {
                datapoints = "naia.datapoints - Raw ingested data",
                dlq = "naia.datapoints.dlq - Failed messages",
                behavior = "naia.points.behavior - Behavioral fingerprints",
                clusters = "naia.clusters.created - Detected clusters",
                suggestions = "naia.suggestions.created - Pattern suggestions",
                feedback = "naia.patterns.feedback - User approvals",
                patterns = "naia.patterns.updated - Learned patterns"
            }
        },
        producer = producerStatus,
        endpoints = new
        {
            startIngestion = "POST /api/ingestion/start",
            stopIngestion = "POST /api/ingestion/stop",
            status = "GET /api/ingestion/status",
            directIngest = "POST /api/ingest",
            pipelineHealth = "GET /api/pipeline/health",
            kafkaUI = "http://localhost:8080"
        }
    });
})
.WithName("GetKafkaPipelineStatus")
.WithTags("Pipeline");

// ============================================================================
// RUN MIGRATIONS ON STARTUP (dev only)
// ============================================================================
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NaiaDbContext>();
    
    try
    {
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Database migration failed - database may not be ready");
    }
}

// ============================================================================
// PI SYSTEM ENDPOINTS (AF SDK or Web API)
// ============================================================================
app.MapGet("/api/pi/health", async (IServiceProvider sp, IConfiguration config) =>
{
    var connector = sp.GetService<PIWebApiConnector>();
    if (connector == null) return Results.Problem("PI Web API connector not configured");
    
    var health = await connector.CheckHealthAsync();
    return Results.Ok(new
    {
        connectorType = "PI Web API",
        connected = health.IsHealthy,
        message = health.Message,
        responseTimeMs = health.ResponseTime.TotalMilliseconds,
        details = health.Details
    });
})
.WithName("PIHealthCheck")
.WithTags("PI System");

app.MapPost("/api/pi/initialize", async (
    IServiceProvider sp,
    IConfiguration config) =>
{
    var options = config.GetSection("PIWebApi").Get<PIWebApiOptions>();
    
    if (options == null || string.IsNullOrEmpty(options.DataArchive))
    {
        return Results.BadRequest("PI configuration missing. Set PIWebApi section in appsettings.");
    }
    
    var connConfig = new ConnectorConfiguration
    {
        ConnectionString = options.BaseUrl,
        PiDataArchive = options.DataArchive,
        AfServerName = options.AfServer,
        UseWindowsAuth = options.UseWindowsAuth,
        Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        MaxConcurrentRequests = options.MaxConcurrentRequests,
        Credentials = new Dictionary<string, string>()
    };
    
    if (!options.UseWindowsAuth && !string.IsNullOrEmpty(options.Username))
    {
        connConfig.Credentials["Username"] = options.Username;
        connConfig.Credentials["Password"] = options.Password ?? "";
    }
    
    var connector = sp.GetService<PIWebApiConnector>();
    if (connector == null) return Results.Problem("PI Web API connector not configured");
    
    await connector.InitializeAsync(connConfig);
    
    return connector.IsAvailable 
        ? Results.Ok(new { status = "connected", connectorType = "Web API", dataArchive = options.DataArchive })
        : Results.Problem("Failed to connect via Web API");
})
.WithName("PIInitialize")
.WithTags("PI System");

app.MapGet("/api/pi/points", async (
    IServiceProvider sp,
    IConfiguration config,
    IPointRepository pointRepo,
    string? filter = null,
    int maxResults = 100) =>
{
    IDiscoverableConnector? connector = sp.GetService<PIWebApiConnector>();
    
    if (connector == null || !connector.IsAvailable)
    {
        return Results.BadRequest("PI connector not initialized. Call /api/pi/initialize first.");
    }
    
    var points = await connector.DiscoverPointsAsync(filter, maxResults);
    
    // Check which points already exist in the database
    var sourceAddresses = points.Select(p => p.SourceAddress).ToList();
    var existingPoints = (await pointRepo.GetBySourceAddressesAsync(sourceAddresses))
        .ToDictionary(p => p.SourceAddress ?? "", p => p);
    
    return Results.Ok(new
    {
        connectorType = "Web API",
        count = points.Count,
        filter = filter ?? "*",
        points = points.Select(p => new
        {
            p.SourceAddress,
            p.Name,
            p.Description,
            p.EngineeringUnits,
            p.PointType,
            attributes = p.Attributes,
            existsInDatabase = existingPoints.ContainsKey(p.SourceAddress)
        })
    });
})
.WithName("PIDiscoverPoints")
.WithTags("PI System");

app.MapGet("/api/pi/points/{tagName}/current", async (
    string tagName,
    IServiceProvider sp,
    IConfiguration config) =>
{
    ICurrentValueConnector? connector = sp.GetService<PIWebApiConnector>();
    
    if (connector == null || !connector.IsAvailable)
    {
        return Results.BadRequest("PI connector not initialized. Call /api/pi/initialize first.");
    }
    
    try
    {
        var value = await connector.ReadCurrentValueAsync(tagName);
        return Results.Ok(new
        {
            connectorType = "Web API",
            tagName,
            value = value.Value,
            timestamp = value.Timestamp,
            quality = value.Quality.ToString(),
            units = value.Units
        });
    }
    catch (Exception ex)
    {
        return Results.NotFound(new { tagName, error = ex.Message });
    }
})
.WithName("PIGetCurrentValue")
.WithTags("PI System");

app.MapPost("/api/pi/points/current", async (
    string[] tagNames,
    IServiceProvider sp,
    IConfiguration config) =>
{
    ICurrentValueConnector? connector = sp.GetService<PIWebApiConnector>();
    
    if (connector == null || !connector.IsAvailable)
    {
        return Results.BadRequest("PI connector not initialized. Call /api/pi/initialize first.");
    }
    
    var values = await connector.ReadCurrentValuesAsync(tagNames);
    
    return Results.Ok(new
    {
        connectorType = "Web API",
        count = values.Count,
        requested = tagNames.Length,
        values = values.Select(v => new
        {
            tagName = v.Key,
            value = v.Value.Value,
            timestamp = v.Value.Timestamp,
            quality = v.Value.Quality.ToString(),
            units = v.Value.Units
        })
    });
})
.WithName("PIGetCurrentValues")
.WithTags("PI System");

app.MapPost("/api/pi/points/add", async (
    AddPointsRequest request,
    IPointRepository pointRepo,
    IDataSourceRepository dataSourceRepo,
    IUnitOfWork unitOfWork) =>
{
    if (request?.Points == null || request.Points.Count == 0)
    {
        return Results.BadRequest("No points provided");
    }

    // Get or create PI Web API data source
    var dataSources = await dataSourceRepo.GetAllAsync();
    var piDataSource = dataSources.FirstOrDefault(ds => ds.SourceType == DataSourceType.PiWebApi);
    
    if (piDataSource == null)
    {
        // Create a default PI data source if needed
        piDataSource = DataSource.Create(
            "OSIsoft PI Web API",
            DataSourceType.PiWebApi,
            description: "Discovered PI points via Web API");
        await dataSourceRepo.AddAsync(piDataSource);
        await unitOfWork.SaveChangesAsync();
    }

    var addedCount = 0;
    var errors = new List<string>();

    foreach (var point in request.Points)
    {
        try
        {
            var tagName = point.Name ?? point.SourceAddress ?? "unknown";
            
            // Check if point already exists
            var existing = await pointRepo.GetByTagNameAsync(tagName, piDataSource.Id);
            if (existing != null)
            {
                errors.Add($"{tagName}: Point already exists");
                continue;
            }

            // Create a point entity from the discovered point
            var newPoint = Point.Create(
                name: tagName,
                description: point.Description,
                engineeringUnits: point.EngineeringUnits,
                dataSourceId: piDataSource.Id,
                sourceAddress: point.SourceAddress);

            await pointRepo.AddAsync(newPoint);
            addedCount++;
        }
        catch (Exception ex)
        {
            errors.Add($"{point.SourceAddress ?? "unknown"}: {ex.Message}");
        }
    }

    if (addedCount > 0)
    {
        await unitOfWork.SaveChangesAsync();
    }

    return Results.Ok(new
    {
        success = true,
        addedCount,
        totalRequested = request.Points.Count,
        errors = errors.Count > 0 ? errors : null,
        message = $"Successfully added {addedCount} points to ingestion pipeline"
    });
})
.WithName("AddPIPoints")
.WithTags("PI System");

app.MapGet("/api/pi/points/{tagName}/history", async (
    string tagName,
    IServiceProvider sp,
    IConfiguration config,
    DateTime? start = null,
    DateTime? end = null) =>
{
    IHistoricalDataConnector? connector = sp.GetService<PIWebApiConnector>();
    
    if (connector == null || !connector.IsAvailable)
    {
        return Results.BadRequest("PI connector not initialized. Call /api/pi/initialize first.");
    }
    
    var startTime = start ?? DateTime.UtcNow.AddHours(-1);
    var endTime = end ?? DateTime.UtcNow;
    
    try
    {
        var data = await connector.ReadHistoricalDataAsync(tagName, startTime, endTime);
        return Results.Ok(new
        {
            connectorType = "Web API",
            tagName,
            startTime,
            endTime,
            units = data.Units,
            count = data.Values.Count,
            values = data.Values.Select(v => new
            {
                timestamp = v.Timestamp,
                value = v.Value,
                quality = v.Quality.ToString()
            })
        });
    }
    catch (Exception ex)
    {
        return Results.NotFound(new { tagName, error = ex.Message });
    }
})
.WithName("PIGetHistory")
.WithTags("PI System");

app.MapGet("/api/pi/points/{tagName}/metadata", async (
    string tagName,
    IServiceProvider sp,
    IConfiguration config) =>
{
    IDiscoverableConnector? connector = sp.GetService<PIWebApiConnector>();
    
    if (connector == null || !connector.IsAvailable)
    {
        return Results.BadRequest("PI connector not initialized. Call /api/pi/initialize first.");
    }
    
    var metadata = await connector.GetPointMetadataAsync(tagName);
    
    return metadata is null
        ? Results.NotFound(new { tagName, error = "Point not found" })
        : Results.Ok(metadata);
})
.WithName("PIGetPointMetadata")
.WithTags("PI System");

// ============================================================================
// LIVE DATA INGESTION ENDPOINTS
// ============================================================================

app.MapPost("/api/ingestion/start", async (
    PIDataIngestionService ingestion,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Starting PI → Kafka ingestion pipeline");
    await ingestion.StartAsync();
    return Results.Ok(new
    {
        status = "started",
        message = "PI → Kafka ingestion started. Data flows: PI → Kafka → QuestDB + Redis",
        pipeline = new
        {
            producer = "PI System (AF SDK)",
            broker = "Kafka (naia.datapoints)",
            consumer = "Naia.Ingestion Worker",
            storage = "QuestDB + Redis"
        },
        startTime = DateTime.UtcNow
    });
})
.WithName("StartIngestion")
.WithTags("Ingestion");

app.MapPost("/api/ingestion/stop", async (
    PIDataIngestionService ingestion,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Stopping PI → Kafka ingestion pipeline");
    await ingestion.StopAsync();
    return Results.Ok(new
    {
        status = "stopped",
        message = "PI ingestion stopped. Naia.Ingestion worker continues processing queued messages.",
        stopTime = DateTime.UtcNow
    });
})
.WithName("StopIngestion")
.WithTags("Ingestion");

app.MapGet("/api/ingestion/status", (PIDataIngestionService piIngestion) =>
{
    // Get PI ingestion service status (may be disabled/not-running)
    dynamic piStatus = piIngestion.GetStatus();
    
    // In production, data flows from WindFarmReplayWorker → Kafka → QuestDB
    // The PI connector is just one optional source among many
    
    return Results.Ok(new
    {
        // PI connector status (disabled in production, which is ok)
        piConnector = new
        {
            isRunning = (bool)piStatus.isRunning,
            pointsConfigured = (int)piStatus.pointsConfigured,
            messagesPublished = (int)piStatus.messagesPublished,
            errors = (int)piStatus.errors
        },
        
        // Replay worker is enabled in production
        replayWorker = new
        {
            isEnabled = true,
            description = "WindFarmReplayWorker - Kelmarsh wind farm historical data simulation",
            dataSource = "/opt/naia/data/kelmarsh/scada_2019",
            status = "Check logs: journalctl -u naia-ingestion -f"
        },
        
        // Overall system
        system = new
        {
            isRunning = true,
            activeConnectors = "WindFarmReplayWorker (PI disabled in production)",
            dataFlow = "Replay Worker → Kafka (naia.datapoints) → Ingestion Worker → QuestDB",
            note = "PI Connector errors are expected - PI Web API is not configured in production"
        }
    });
})
.WithName("GetIngestionStatus")
.WithTags("Ingestion");

// ============================================================================
// END-TO-END TEST ENDPOINT: Discover PI Points → Create Metadata → Ingest Live Data
// ============================================================================
app.MapPost("/api/pi/test-end-to-end", async (
    IServiceProvider sp,
    IConfiguration config,
    IPointRepository pointRepo,
    IDataSourceRepository dataSourceRepo,
    IUnitOfWork unitOfWork,
    ITimeSeriesWriter questDbWriter,
    ITimeSeriesReader questDbReader,
    ICurrentValueCache redis) =>
{
    var connector = sp.GetService<PIWebApiConnector>() as ICurrentValueConnector;
        
    if (connector == null)
        return Results.Problem("PI connector not configured");
        
    if (connector is not IDiscoverableConnector discoverable)
        return Results.Problem("Connector does not support discovery");
        
    var log = new List<string>();
    
    try
    {
        // Step 1: Create/Find PI DataSource in PostgreSQL
        log.Add("Step 1: Creating/Finding PI DataSource...");
        var piServerName = config["PIWebApi:DataArchive"] ?? "PI_System";
        var existingDataSource = (await dataSourceRepo.GetAllAsync())
            .FirstOrDefault(ds => ds.SourceType == DataSourceType.PiWebApi);
            
        DataSource dataSource;
        if (existingDataSource != null)
        {
            dataSource = existingDataSource;
            log.Add($"✅ Found existing: {dataSource.Name} (ID: {dataSource.Id})");
        }
        else
        {
            dataSource = DataSource.Create(
                $"PI System - {piServerName}",
                DataSourceType.PiWebApi,
                config["PIWebApi:BaseUrl"],
                "PI Web API Connection",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    DataArchive = config["PIWebApi:DataArchive"],
                    ConnectorType = "Web API"
                }));
            await dataSourceRepo.AddAsync(dataSource);
            await unitOfWork.SaveChangesAsync();
            log.Add($"✅ Created new DataSource: {dataSource.Name} (ID: {dataSource.Id})");
        }
        dataSource.UpdateConnectionStatus(ConnectionStatus.Connected);
        await unitOfWork.SaveChangesAsync();
        
        // Step 2: Discover PI points
        log.Add("Step 2: Discovering SINUSOID* points from PI...");
        var discovered = await discoverable.DiscoverPointsAsync("SINUSOID*");
        
        if (discovered.Count == 0)
        {
            log.Add("⚠️ No SINUSOID points found, trying CDT158...");
            discovered = await discoverable.DiscoverPointsAsync("CDT158");
        }
        
        if (discovered.Count == 0)
        {
            log.Add("⚠️ No test points found, getting first 3 points...");
            var allPoints = await discoverable.DiscoverPointsAsync("*");
            discovered = allPoints.Take(3).ToList();
        }
        
        var testPoints = discovered.Take(3).ToList();
        log.Add($"✅ Found {testPoints.Count} points: {string.Join(", ", testPoints.Select(p => p.Name))}");
        
        // Step 3: Create Point metadata in PostgreSQL
        log.Add("Step 3: Creating Point metadata in PostgreSQL...");
        var points = new List<Point>();
        
        foreach (var disc in testPoints)
        {
            // Search for existing point by tag name
            var searchResults = await pointRepo.SearchAsync(tagNamePattern: disc.Name, take: 1);
            var existing = searchResults.FirstOrDefault();
            
            if (existing != null)
            {
                points.Add(existing);
                log.Add($"⏭️ Point exists: {existing.Name} (SeqId: {existing.PointSequenceId})");
                continue;
            }
            
            var point = Point.Create(
                disc.Name,
                PointValueType.Float64,
                PointKind.Input,
                disc.Description,
                disc.EngineeringUnits,
                dataSource.Id,
                disc.SourceAddress);
            await pointRepo.AddAsync(point);
            points.Add(point);
            log.Add($"✅ Created: {point.Name}");
        }
        await unitOfWork.SaveChangesAsync();
        
        // Refresh to get PointSequenceId assigned by database
        for (int i = 0; i < points.Count; i++)
        {
            var refreshed = await pointRepo.GetByIdAsync(points[i].Id);
            if (refreshed != null)
            {
                points[i] = refreshed;
                log.Add($"   PointSequenceId: {points[i].Name} = {points[i].PointSequenceId}");
            }
        }
        
        // Step 4: Read current values from PI
        log.Add("Step 4: Reading live values from PI System...");
        var valuesDict = await connector.ReadCurrentValuesAsync(
            points.Select(p => p.SourceAddress!));
        var values = valuesDict.Values.ToList();
        log.Add($"✅ Read {values.Count} live values from PI");
        
        // Step 5: Write to QuestDB
        log.Add("Step 5: Writing time-series data to QuestDB...");
        var dataPoints = new List<DataPoint>();
        
        foreach (var point in points)
        {
            if (point.PointSequenceId == 0)
            {
                log.Add($"⚠️ Skipping {point.Name} - no PointSequenceId");
                continue;
            }
            
            if (!valuesDict.TryGetValue(point.SourceAddress!, out var value))
            {
                log.Add($"[WARNING] No value found for {point.Name}");
                continue;
            }
            
            if (!point.PointSequenceId.HasValue)
            {
                log.Add($"[WARNING] Skipping {point.Name} - not yet assigned sequence ID");
                continue;
            }
            
            var isGood = value.Quality == Naia.Connectors.Abstractions.DataQuality.Good;
            var dataPoint = DataPoint.FromPi(
                point.PointSequenceId.Value,
                point.Name,
                value.Timestamp,
                Convert.ToDouble(value.Value),
                isGood,
                dataSource.Id.ToString(),
                point.SourceAddress);
            dataPoints.Add(dataPoint);
            log.Add($"✅ QuestDB: {point.Name} = {value.Value} {value.Units} @ {value.Timestamp:yyyy-MM-dd HH:mm:ss}");
        }
        
        if (dataPoints.Any())
        {
            var batch = DataPointBatch.Create(dataPoints, dataSource.Id.ToString());
            await questDbWriter.WriteAsync(batch);
            await questDbWriter.FlushAsync();
            log.Add("✅ QuestDB flush complete");
        }
        
        // Step 6: Write to Redis cache
        log.Add("Step 6: Caching current values in Redis...");
        var currentValues = new List<CurrentValue>();
        
        foreach (var point in points)
        {
            if (!point.PointSequenceId.HasValue || point.PointSequenceId == 0) continue;
            
            if (!valuesDict.TryGetValue(point.SourceAddress!, out var value))
                continue;
            
            var currentValue = new CurrentValue
            {
                PointSequenceId = point.PointSequenceId.Value,
                PointName = point.Name,
                Timestamp = value.Timestamp,
                Value = Convert.ToDouble(value.Value),
                Quality = value.Quality == Naia.Connectors.Abstractions.DataQuality.Good 
                    ? Naia.Domain.ValueObjects.DataQuality.Good 
                    : Naia.Domain.ValueObjects.DataQuality.Bad,
                EngineeringUnits = value.Units
            };
            currentValues.Add(currentValue);
        }
        
        if (currentValues.Any())
        {
            await redis.SetManyAsync(currentValues);
            log.Add($"✅ Cached {currentValues.Count} values in Redis");
        }
        
        // Step 7: Verify by reading back from QuestDB
        log.Add("Step 7: Verifying data in QuestDB...");
        foreach (var point in points)
        {
            if (!point.PointSequenceId.HasValue || point.PointSequenceId == 0) continue;
            
            var last = await questDbReader.GetLastValueAsync(point.PointSequenceId.Value);
            if (last != null)
            {
                log.Add($"[OK] Verified: {point.Name} = {last.Value} @ {last.Timestamp:HH:mm:ss}");
            }
            else
            {
                log.Add($"⚠️ Not found in QuestDB: {point.Name}");
            }
        }
        
        log.Add("");
        log.Add("🎉 END-TO-END TEST COMPLETE!");
        log.Add($"📊 {points.Count} points are now registered and ingesting live data");
        log.Add($"📍 Data pipeline:");
        log.Add($"   PI System ({piServerName})");
        log.Add($"     ↓ AF SDK");
        log.Add($"   Point Metadata (PostgreSQL)");
        log.Add($"     ↓ {string.Join(", ", points.Select(p => p.Name))}");
        log.Add($"   Time Series (QuestDB) + Cache (Redis)");
        
        return Results.Ok(new
        {
            success = true,
            pointsCreated = points.Count,
            dataSource = new
            {
                id = dataSource.Id,
                name = dataSource.Name,
                type = dataSource.SourceType.ToString(),
                status = dataSource.Status.ToString()
            },
            points = points.Select(p => new
            {
                id = p.Id,
                sequenceId = p.PointSequenceId,
                name = p.Name,
                sourceAddress = p.SourceAddress,
                description = p.Description,
                units = p.EngineeringUnits
            }),
            log
        });
    }
    catch (Exception ex)
    {
        log.Add($"❌ ERROR: {ex.Message}");
        log.Add($"Stack: {ex.StackTrace}");
        
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            stackTrace = ex.StackTrace,
            log
        }, statusCode: 500);
    }
})
.WithName("PITestEndToEnd")
.WithTags("PI System")
.WithDescription("Complete end-to-end test: Discover PI points → Create metadata in PostgreSQL → Read live values → Store in QuestDB → Cache in Redis");

// ============================================================================
// SETUP PIPELINE ENDPOINT - One-command PI → NAIA integration
// ============================================================================
// This is the KEY endpoint for "The First Industrial Historian That Learns From You"
// It discovers PI points, registers them in NAIA, and starts the learning pipeline

app.MapPost("/api/pi/setup-pipeline", async (
    IServiceProvider sp,
    IConfiguration config,
    IPointRepository pointRepo,
    IDataSourceRepository dataSourceRepo,
    IUnitOfWork unitOfWork,
    PIDataIngestionService ingestion,
    ILogger<Program> logger,
    string filter = "MLR1*",
    string? dataSourceName = null,
    string pointPrefix = "NAIA",
    bool startIngestion = true,
    int maxPoints = 100) =>
{
    var log = new List<string>();
    var startTime = DateTime.UtcNow;
    
    try
    {
        log.Add("═══════════════════════════════════════════════════════════════════");
        log.Add("  NAIA Pipeline Setup - The First Industrial Historian That Learns");
        log.Add("═══════════════════════════════════════════════════════════════════");
        log.Add($"  Filter: {filter}");
        log.Add($"  Point Prefix: {pointPrefix}");
        log.Add($"  Max Points: {maxPoints}");
        log.Add("");
        
        // Step 1: Get or initialize PI connector
        log.Add("[STEP 1] Initializing PI Connector...");
        var piServerName = config["PIWebApi:DataArchive"] ?? "unknown";
        
        ICurrentValueConnector? connector = null;
        try
        {
            connector = sp.GetService<PIWebApiConnector>() as ICurrentValueConnector;
        }
        catch (Exception ex)
        {
            log.Add($"  ✗ Failed to get PI connector from DI: {ex.Message}");
            logger.LogError(ex, "Failed to get PI connector from DI");
            return Results.Problem($"PI connector not available: {ex.Message}");
        }
            
        if (connector == null)
        {
            log.Add("  ✗ PI connector not configured in DI");
            return Results.Problem("PI connector not configured in DI");
        }
            
        // Initialize if not already available
        if (!connector.IsAvailable)
        {
            log.Add($"  Connecting to PI Data Archive: {piServerName}...");
            var options = config.GetSection("PIWebApi").Get<PIWebApiOptions>();
            
            if (options == null || string.IsNullOrEmpty(options.DataArchive))
            {
                log.Add("  ✗ PI configuration missing");
                return Results.BadRequest("PI configuration missing. Set PIWebApi section in appsettings.");
            }
            
            var connConfig = new ConnectorConfiguration
            {
                ConnectionString = options.BaseUrl,
                PiDataArchive = options.DataArchive,
                AfServerName = options.AfServer,
                UseWindowsAuth = options.UseWindowsAuth,
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                MaxConcurrentRequests = options.MaxConcurrentRequests,
                Credentials = new Dictionary<string, string>()
            };
            
            try
            {
                if (connector is PIWebApiConnector webConnectorToInit)
                {
                    await webConnectorToInit.InitializeAsync(connConfig);
                }
                else
                {
                    throw new InvalidOperationException($"Unknown connector type: {connector?.GetType().Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                log.Add($"  ✗ Failed to connect to PI: {ex.Message}");
                logger.LogError(ex, "Failed to initialize PI connector to {Server}", piServerName);
                return Results.Problem($"Failed to connect to PI Data Archive '{piServerName}': {ex.Message}");
            }
            
            if (!connector.IsAvailable)
            {
                log.Add($"  ✗ PI connector reports not available after initialization");
                return Results.Problem($"Failed to connect to PI Data Archive: {piServerName}");
            }
        }
        
        log.Add($"  ✓ Connected to PI: {piServerName}");
        
        // Step 2: Create or find DataSource
        log.Add("");
        log.Add("[STEP 2] Setting up Data Source...");
        var dsName = dataSourceName ?? $"{piServerName}-{filter.Replace("*", "").TrimEnd('.')}";
        
        var existingDataSource = (await dataSourceRepo.GetAllAsync())
            .FirstOrDefault(ds => ds.Name == dsName);
            
        DataSource dataSource;
        if (existingDataSource != null)
        {
            dataSource = existingDataSource;
            dataSource.UpdateConnectionStatus(ConnectionStatus.Connected);
            log.Add($"  ✓ Found existing DataSource: {dataSource.Name}");
        }
        else
        {
            dataSource = DataSource.Create(
                dsName,
                DataSourceType.PiWebApi,
                $"PI:{piServerName}",
                $"PI System connection for {filter} points",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    DataArchive = piServerName,
                    Filter = filter,
                    ConnectorType = "Web API"
                }));
            await dataSourceRepo.AddAsync(dataSource);
            dataSource.UpdateConnectionStatus(ConnectionStatus.Connected);
            await unitOfWork.SaveChangesAsync();
            log.Add($"  ✓ Created new DataSource: {dataSource.Name} (ID: {dataSource.Id})");
        }
        
        // Step 3: Discover points from PI
        log.Add("");
        log.Add("[STEP 3] Discovering PI Points...");
        
        if (connector is not IDiscoverableConnector discoverable)
            return Results.Problem("Connector does not support point discovery");
            
        var discovered = await discoverable.DiscoverPointsAsync(filter, maxPoints);
        log.Add($"  ✓ Found {discovered.Count} points matching '{filter}'");
        
        if (discovered.Count == 0)
        {
            log.Add("  ⚠ No points found matching filter");
            return Results.Ok(new
            {
                success = false,
                message = $"No PI points found matching filter: {filter}",
                log
            });
        }
        
        // Step 4: Check which points already exist in NAIA
        log.Add("");
        log.Add("[STEP 4] Checking existing NAIA points...");
        
        // Batch lookup for efficiency
        var sourceAddresses = discovered.Select(d => d.SourceAddress).ToList();
        var existingByAddress = (await pointRepo.GetBySourceAddressesAsync(sourceAddresses))
            .ToDictionary(p => p.SourceAddress ?? "", p => p);
        
        var newPoints = new List<(DiscoveredPoint pi, Point naia)>();
        var existingPoints = new List<Point>();
        
        foreach (var disc in discovered)
        {
            // Check by SourceAddress (PI tag name)
            if (existingByAddress.TryGetValue(disc.SourceAddress, out var existing))
            {
                existingPoints.Add(existing);
                log.Add($"  ⏭ Already exists: {existing.Name} (SeqId: {existing.PointSequenceId})");
            }
            else
            {
                // Create new point with prefix
                var naiaName = string.IsNullOrEmpty(pointPrefix) 
                    ? disc.Name 
                    : $"{pointPrefix}.{disc.Name}";
                    
                var point = Point.Create(
                    naiaName,
                    PointValueType.Float64,
                    PointKind.Input,
                    disc.Description,
                    disc.EngineeringUnits,
                    dataSource.Id,
                    disc.SourceAddress);  // PI tag name stored as SourceAddress
                    
                newPoints.Add((disc, point));
            }
        }
        
        // Step 5: Register new points in PostgreSQL
        log.Add("");
        log.Add("[STEP 5] Registering new points in NAIA...");
        
        var createdPoints = new List<Point>();
        foreach (var (disc, point) in newPoints)
        {
            await pointRepo.AddAsync(point);
            log.Add($"  ✓ Created: {point.Name} (Source: {point.SourceAddress})");
        }
        
        if (newPoints.Any())
        {
            await unitOfWork.SaveChangesAsync();
            
            // Refresh to get PointSequenceId assigned by database
            foreach (var (_, point) in newPoints)
            {
                var refreshed = await pointRepo.GetByIdAsync(point.Id);
                if (refreshed != null)
                {
                    createdPoints.Add(refreshed);
                    log.Add($"    → PointSequenceId: {refreshed.PointSequenceId}");
                }
            }
        }
        
        var allPoints = existingPoints.Concat(createdPoints).ToList();
        log.Add($"  Total points ready for ingestion: {allPoints.Count}");
        
        // Step 6: Optionally start ingestion
        if (startIngestion && !ingestion.IsRunning)
        {
            log.Add("");
            log.Add("[STEP 6] Starting ingestion pipeline...");
            await ingestion.StartAsync();
            log.Add("  ✓ PI → Kafka ingestion started");
            log.Add("  ✓ Data flowing: PI → Kafka → QuestDB + Redis");
        }
        else if (ingestion.IsRunning)
        {
            log.Add("");
            log.Add("[STEP 6] Ingestion already running - new points will be picked up automatically");
        }
        
        // Summary
        var duration = DateTime.UtcNow - startTime;
        log.Add("");
        log.Add("═══════════════════════════════════════════════════════════════════");
        log.Add("  PIPELINE SETUP COMPLETE");
        log.Add("═══════════════════════════════════════════════════════════════════");
        log.Add($"  DataSource:      {dataSource.Name}");
        log.Add($"  Points Created:  {createdPoints.Count}");
        log.Add($"  Points Existing: {existingPoints.Count}");
        log.Add($"  Total Active:    {allPoints.Count}");
        log.Add($"  Ingestion:       {(ingestion.IsRunning ? "RUNNING" : "STOPPED")}");
        log.Add($"  Duration:        {duration.TotalSeconds:F2}s");
        log.Add("");
        log.Add("  Next steps:");
        log.Add("    • Data flows automatically: PI → Kafka → QuestDB + Redis");
        log.Add("    • Pattern Engine analyzes data every 5-15 minutes");
        log.Add("    • Check Hangfire dashboard: http://localhost:5052/hangfire");
        log.Add("    • Monitor: GET /api/ingestion/status");
        
        logger.LogInformation(
            "Pipeline setup complete: {NewPoints} new points, {ExistingPoints} existing, ingestion {Status}",
            createdPoints.Count, existingPoints.Count, ingestion.IsRunning ? "running" : "stopped");
        
        return Results.Ok(new
        {
            success = true,
            dataSource = new
            {
                id = dataSource.Id,
                name = dataSource.Name,
                type = dataSource.SourceType.ToString()
            },
            points = new
            {
                created = createdPoints.Select(p => new
                {
                    id = p.Id,
                    sequenceId = p.PointSequenceId,
                    name = p.Name,
                    sourceAddress = p.SourceAddress,
                    units = p.EngineeringUnits
                }),
                existing = existingPoints.Select(p => new
                {
                    id = p.Id,
                    sequenceId = p.PointSequenceId,
                    name = p.Name,
                    sourceAddress = p.SourceAddress
                }),
                totalActive = allPoints.Count
            },
            ingestion = new
            {
                running = ingestion.IsRunning,
                status = ingestion.GetStatus()
            },
            durationMs = duration.TotalMilliseconds,
            log
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Pipeline setup failed for filter: {Filter}", filter);
        log.Add($"❌ ERROR: {ex.Message}");
        
        return Results.Json(new
        {
            success = false,
            error = ex.Message,
            stackTrace = ex.StackTrace,
            log
        }, statusCode: 500);
    }
})
.WithName("SetupPipeline")
.WithTags("PI System")
.WithDescription(@"
One-command setup: Discover PI points → Register in NAIA → Start ingestion pipeline.
This is the primary endpoint for setting up new data sources.

Parameters:
- filter: PI point name filter (default: 'MLR1*')
- dataSourceName: Optional custom name for the data source
- pointPrefix: Prefix for NAIA point names (default: 'NAIA')
- startIngestion: Auto-start the ingestion pipeline (default: true)
- maxPoints: Maximum points to discover (default: 100)

Example: POST /api/pi/setup-pipeline?filter=MLR1*&pointPrefix=NAIA&startIngestion=true
");

// ============================================================================
// BACKFILL ENDPOINTS
// ============================================================================

app.MapPost("/api/backfill/start", async (
    BackfillStartRequest request,
    BackfillOrchestrator orchestrator) =>
{
    try
    {
        var requestId = await orchestrator.QueueBackfillAsync(
            request.ConnectorType,
            request.PointAddresses,
            request.StartTime,
            request.EndTime,
            request.ChunkSize);
        
        return Results.Ok(new
        {
            success = true,
            data = new
            {
                requestId,
                connectorType = request.ConnectorType,
                pointCount = request.PointAddresses.Count,
                startTime = request.StartTime,
                endTime = request.EndTime,
                chunkSize = request.ChunkSize,
                status = "Queued",
                message = $"Backfill queued: {request.PointAddresses.Count} points from {request.StartTime:yyyy-MM-dd} to {request.EndTime:yyyy-MM-dd}"
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            success = false,
            error = ex.Message
        }, statusCode: 500);
    }
})
.WithName("StartBackfill")
.WithTags("Backfill")
.WithDescription("Queue historical data backfill from PI System (AF SDK or Web API)");

app.MapGet("/api/backfill/status", (BackfillOrchestrator orchestrator) =>
{
    var status = orchestrator.GetStatus();
    
    return Results.Ok(new
    {
        success = true,
        data = new
        {
            activeRequests = status.ActiveRequests.Select(r => new
            {
                requestId = r.RequestId,
                connectorType = r.ConnectorType,
                pointCount = r.PointAddresses.Count,
                startTime = r.StartTime,
                endTime = r.EndTime,
                status = r.Status,
                progress = r.ProgressPercentage,
                totalChunks = r.TotalChunks,
                completedChunks = r.CompletedChunks,
                failedChunks = r.FailedChunks,
                pointsProcessed = r.PointsProcessed,
                batchesPublished = r.BatchesPublished,
                queuedAt = r.QueuedAt,
                startedAt = r.StartedAt,
                completedAt = r.CompletedAt
            }),
            stats = new
            {
                completedRequests = status.Stats.CompletedRequests,
                failedRequests = status.Stats.FailedRequests,
                totalPointsBackfilled = status.Stats.TotalPointsBackfilled,
                totalBatchesPublished = status.Stats.TotalBatchesPublished,
                failedChunks = status.Stats.FailedChunks
            },
            queueDepth = status.QueueDepth
        }
    });
})
.WithName("GetBackfillStatus")
.WithTags("Backfill")
.WithDescription("Get current backfill status and statistics");

app.MapGet("/api/backfill/request/{requestId:guid}", (
    Guid requestId,
    BackfillOrchestrator orchestrator) =>
{
    var request = orchestrator.GetRequestStatus(requestId);
    
    if (request == null)
    {
        return Results.NotFound(new
        {
            success = false,
            error = $"Backfill request {requestId} not found"
        });
    }
    
    return Results.Ok(new
    {
        success = true,
        data = new
        {
            requestId = request.RequestId,
            connectorType = request.ConnectorType,
            pointAddresses = request.PointAddresses,
            startTime = request.StartTime,
            endTime = request.EndTime,
            chunkSize = request.ChunkSize,
            status = request.Status,
            progress = request.ProgressPercentage,
            totalChunks = request.TotalChunks,
            completedChunks = request.CompletedChunks,
            failedChunks = request.FailedChunks,
            pointsProcessed = request.PointsProcessed,
            batchesPublished = request.BatchesPublished,
            queuedAt = request.QueuedAt,
            startedAt = request.StartedAt,
            completedAt = request.CompletedAt,
            lastCheckpoint = request.LastCheckpoint
        }
    });
})
.WithName("GetBackfillRequest")
.WithTags("Backfill")
.WithDescription("Get status of a specific backfill request");

// =============================================================================
// MAP CONTROLLERS & SIGNALR HUBS
// =============================================================================
app.MapControllers();
app.MapHub<PatternHub>("/hubs/patterns");

// Prometheus metrics endpoint
app.MapMetrics("/metrics");

// Track application uptime
var startTime = DateTime.UtcNow;
_ = Task.Run(async () =>
{
    while (true)
    {
        NaiaMetrics.ApplicationUptime.Set((DateTime.UtcNow - startTime).TotalSeconds);
        await Task.Delay(TimeSpan.FromSeconds(15));
    }
});

// SPA fallback - must be after all API routes
app.MapFallbackToFile("index.html");

// =============================================================================
// AUTO-INITIALIZE PIPELINE ON STARTUP
// =============================================================================
// Try to auto-discover points and start ingestion if not already done
using (var scope = app.Services.CreateAsyncScope())
{
    try
    {
        var sp = scope.ServiceProvider;
        var pointRepo = sp.GetRequiredService<IPointRepository>();
        var existingPoints = await pointRepo.GetEnabledAsync();
        
        // ALWAYS rediscover on startup to avoid stale point issues
        // TODO: In production, add proper point validation/health checks instead
        if (false) // Force rediscovery - TEMPORARILY DISABLED
        {
            var ingestion = sp.GetRequiredService<PIDataIngestionService>();
            var logger = sp.GetRequiredService<ILogger<Program>>();
            
            logger.LogInformation("Auto-initializing NAIA pipeline on startup (forced rediscovery)");
            
            var config = sp.GetRequiredService<IConfiguration>();
            var dataSourceRepo = sp.GetRequiredService<IDataSourceRepository>();
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            
            // Delete ALL existing points and data sources to avoid duplicates on rediscovery
            // This ensures a clean slate for fresh auto-discovery
            var allDataSources = await dataSourceRepo.GetAllAsync();
            foreach (var ds in allDataSources)
            {
                var dsPoints = await pointRepo.GetByDataSourceIdAsync(ds.Id);
                foreach (var pt in dsPoints)
                {
                    await pointRepo.DeleteAsync(pt.Id);
                }
                await dataSourceRepo.DeleteAsync(ds.Id);
            }
            await unitOfWork.SaveChangesAsync();
            logger.LogInformation("Cleared all {Count} data sources and their points for fresh auto-discovery", allDataSources.Count);
            
            var filter = config["DefaultPointFilter"] ?? "*BESS*";
            var maxPoints = int.Parse(config["DefaultMaxPoints"] ?? "100");
            
            try
            {
                // Get or initialize PI connector
                var connector = sp.GetService<PIWebApiConnector>() as ICurrentValueConnector;
                if (connector != null && !connector.IsAvailable)
                {
                    var options = config.GetSection("PIWebApi").Get<PIWebApiOptions>();
                    if (options != null)
                    {
                        var connConfig = new ConnectorConfiguration
                        {
                            ConnectionString = options.BaseUrl,
                            PiDataArchive = options.DataArchive,
                            AfServerName = options.AfServer,
                            UseWindowsAuth = options.UseWindowsAuth,
                            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                            MaxConcurrentRequests = options.MaxConcurrentRequests,
                            Credentials = new Dictionary<string, string>()
                        };
                        
                        if (connector is PIWebApiConnector webConnector)
                        {
                            await webConnector.InitializeAsync(connConfig);
                        }
                    }
                }
                
                if (connector != null && connector.IsAvailable && connector is IDiscoverableConnector discoverable)
                {
                    logger.LogInformation("Auto-discovering points with filter: {Filter}", filter);
                    var discovered = await discoverable.DiscoverPointsAsync(filter, maxPoints);
                    
                    if (discovered.Any())
                    {
                        logger.LogInformation("Auto-discovered {Count} points, registering...", discovered.Count);
                        
                        // Create datasource
                        var dsName = $"Auto-{filter.Replace("*", "").TrimEnd('.')}";
                        var dataSource = DataSource.Create(
                            dsName,
                            DataSourceType.PiWebApi,
                            "PI:Auto",
                            $"Auto-discovered PI points: {filter}",
                            System.Text.Json.JsonSerializer.Serialize(new { Filter = filter }));
                        dataSource.UpdateConnectionStatus(ConnectionStatus.Connected);
                        await dataSourceRepo.AddAsync(dataSource);
                        await unitOfWork.SaveChangesAsync();
                        
                        // Register points
                        foreach (var disc in discovered)
                        {
                            var point = Point.Create(
                                $"NAIA.{disc.Name}",
                                PointValueType.Float64,
                                PointKind.Input,
                                disc.Description,
                                disc.EngineeringUnits,
                                dataSource.Id,
                                disc.SourceAddress);
                            await pointRepo.AddAsync(point);
                        }
                        
                        await unitOfWork.SaveChangesAsync();
                        logger.LogInformation("Registered {Count} points from auto-discovery", discovered.Count);
                        
                        // Start ingestion
                        if (!ingestion.IsRunning)
                        {
                            await ingestion.StartAsync();
                            logger.LogInformation("Auto-started PI → Kafka ingestion pipeline");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Auto-initialization failed, manual setup required via /api/pi/setup-pipeline");
            }
        }
        else
        {
            // Points already exist, just ensure ingestion is running
            var ingestion = sp.GetRequiredService<PIDataIngestionService>();
            var logger = sp.GetRequiredService<ILogger<Program>>();
            
            if (!ingestion.IsRunning)
            {
                await ingestion.StartAsync();
                logger.LogInformation("Resumed PI ingestion pipeline ({PointCount} points)", existingPoints.Count);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during auto-initialization: {ex.Message}");
    }
}

// ============================================================================
// LOGS API ENDPOINT - Query structured logs from PostgreSQL
// ============================================================================
app.MapGet("/api/logs", async (
    string? level,
    string? source,
    string? search,
    int? minutes,
    int skip = 0,
    int take = 100) =>
{
    try
    {
        using var conn = new Npgsql.NpgsqlConnection(
            "Host=localhost;Port=5432;Database=naia;Username=naia;Password=naia_dev_password;SslMode=Disable");
        await conn.OpenAsync();
        
        var whereClause = new List<string> { "1=1" };
        var parameters = new List<Npgsql.NpgsqlParameter>();
        
        // Filter by time - convert UTC to Pacific time since logs are stored in PT without timezone
        // PostgreSQL container runs in UTC, but logs are written in Pacific Time
        if (minutes.HasValue)
        {
            whereClause.Add($"timestamp > (NOW() AT TIME ZONE 'America/Los_Angeles') - INTERVAL '{minutes.Value} minutes'");
        }
        
        // Filter by level
        if (!string.IsNullOrEmpty(level) && level != "All")
        {
            // Map level name to integer
            var levelInt = level.ToLower() switch
            {
                "verbose" => 0,
                "debug" => 1,
                "information" => 2,
                "warning" => 3,
                "error" => 4,
                "fatal" => 5,
                _ => -1
            };
            
            if (levelInt >= 0)
            {
                whereClause.Add("level = @level");
                parameters.Add(new Npgsql.NpgsqlParameter("@level", levelInt));
            }
        }
        
        // Filter by source context (using log_event if available)
        if (!string.IsNullOrEmpty(source) && source != "All")
        {
            whereClause.Add("log_event::text ILIKE @source");
            parameters.Add(new Npgsql.NpgsqlParameter("@source", $"%{source}%"));
        }
        
        // Search in message
        if (!string.IsNullOrEmpty(search))
        {
            whereClause.Add("(message_template ILIKE @search OR exception ILIKE @search)");
            parameters.Add(new Npgsql.NpgsqlParameter("@search", $"%{search}%"));
        }
        
        var query = $@"
            SELECT 
                timestamp,
                level,
                message_template,
                exception,
                log_event
            FROM logs
            WHERE {string.Join(" AND ", whereClause)}
            ORDER BY timestamp DESC
            LIMIT {take} OFFSET {skip}";
        
        var logs = new List<object>();
        
        using (var cmd = new Npgsql.NpgsqlCommand(query, conn))
        {
            cmd.Parameters.AddRange(parameters.ToArray());
            using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                var levelInt = reader.IsDBNull(1) ? -1 : reader.GetInt32(1);
                var levelName = levelInt switch
                {
                    0 => "Verbose",
                    1 => "Debug",
                    2 => "Information",
                    3 => "Warning",
                    4 => "Error",
                    5 => "Fatal",
                    _ => "Unknown"
                };
                
                // Extract SourceContext from log_event JSON
                string? sourceContext = null;
                if (!reader.IsDBNull(4))
                {
                    var logEventJson = reader.GetString(4);
                    try
                    {
                        var logEvent = System.Text.Json.JsonDocument.Parse(logEventJson);
                        // SourceContext is nested: Properties.SourceContext
                        if (logEvent.RootElement.TryGetProperty("Properties", out var propertiesElement) &&
                            propertiesElement.TryGetProperty("SourceContext", out var sourceContextProp))
                        {
                            sourceContext = sourceContextProp.GetString();
                        }
                    }
                    catch { /* Ignore JSON parse errors */ }
                }
                
                // Render the message template with actual property values
                string renderedMessage = reader.IsDBNull(2) ? null : reader.GetString(2);
                if (!reader.IsDBNull(4) && !reader.IsDBNull(2))
                {
                    try
                    {
                        var logEvent = System.Text.Json.JsonDocument.Parse(reader.GetString(4));
                        var messageTemplate = reader.GetString(2);
                        
                        // Check for Renderings first (handles format specifiers like {State:l})
                        if (logEvent.RootElement.TryGetProperty("Renderings", out var renderingsElement))
                        {
                            foreach (var renderingProp in renderingsElement.EnumerateObject())
                            {
                                var propertyName = renderingProp.Name;
                                if (renderingProp.Value.ValueKind == System.Text.Json.JsonValueKind.Array &&
                                    renderingProp.Value.GetArrayLength() > 0)
                                {
                                    var firstRendering = renderingProp.Value[0];
                                    if (firstRendering.TryGetProperty("Rendering", out var renderingValue))
                                    {
                                        var rendered = renderingValue.GetString();
                                        // Replace all variations: {PropertyName}, {PropertyName:format}
                                        messageTemplate = System.Text.RegularExpressions.Regex.Replace(
                                            messageTemplate,
                                            $@"\{{{propertyName}(?::[^}}]+)?\}}",
                                            rendered ?? "",
                                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                                        );
                                    }
                                }
                            }
                        }
                        
                        // Then replace remaining placeholders from Properties
                        if (logEvent.RootElement.TryGetProperty("Properties", out var propertiesElement))
                        {
                            foreach (var property in propertiesElement.EnumerateObject())
                            {
                                var propertyName = property.Name;
                                // Skip metadata properties
                                if (propertyName == "ThreadId" || propertyName == "ProcessId" || 
                                    propertyName == "MachineName" || propertyName == "SourceContext" ||
                                    propertyName == "RequestId" || propertyName == "RequestPath" ||
                                    propertyName == "SpanId" || propertyName == "TraceId" ||
                                    propertyName == "TransportConnectionId" || propertyName == "EventId")
                                    continue;
                                
                                string valueStr = "";
                                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    valueStr = property.Value.GetString() ?? "";
                                }
                                else if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    valueStr = property.Value.GetRawText();
                                }
                                else if (property.Value.ValueKind == System.Text.Json.JsonValueKind.True ||
                                         property.Value.ValueKind == System.Text.Json.JsonValueKind.False)
                                {
                                    valueStr = property.Value.GetBoolean().ToString();
                                }
                                else
                                {
                                    valueStr = property.Value.GetRawText();
                                }
                                
                                // Replace all variations: {PropertyName}, {PropertyName:format}
                                messageTemplate = System.Text.RegularExpressions.Regex.Replace(
                                    messageTemplate,
                                    $@"\{{{propertyName}(?::[^}}]+)?\}}",
                                    valueStr,
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                                );
                            }
                        }
                        
                        renderedMessage = messageTemplate;
                    }
                    catch { /* If rendering fails, keep the template as-is */ }
                }
                
                logs.Add(new
                {
                    timestamp = reader.GetDateTime(0),
                    level = levelName,
                    source = sourceContext,
                    message = renderedMessage,
                    exception = reader.IsDBNull(3) ? null : reader.GetString(3),
                    properties = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
        }
        
        // Get total count
        var countQuery = $@"
            SELECT COUNT(*) 
            FROM logs 
            WHERE {string.Join(" AND ", whereClause)}";
        
        using var countCmd = new Npgsql.NpgsqlCommand(countQuery, conn);
        countCmd.Parameters.AddRange(parameters.ToArray());
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        
        return Results.Ok(new
        {
            logs,
            total,
            skip,
            take
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to query logs");
        return Results.Problem($"Failed to query logs: {ex.Message}");
    }
})
.WithName("GetLogs")
.WithTags("Logs");

app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// ============================================================================
// DTOs
// ============================================================================

record BackfillStartRequest(
    string ConnectorType,
    List<string> PointAddresses,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan? ChunkSize);

public class AddPointsRequest
{
    public List<DiscoveredPointDto>? Points { get; set; }
}

public class DiscoveredPointDto
{
    public string? SourceAddress { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? EngineeringUnits { get; set; }
    public string? PointType { get; set; }
    public Dictionary<string, object>? Attributes { get; set; }
}

record BulkDeletePointsRequest(List<Guid> PointIds, bool DeleteCsvFiles = false);
