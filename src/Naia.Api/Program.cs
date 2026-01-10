using Microsoft.EntityFrameworkCore;
using Naia.Application.Abstractions;
using Naia.Connectors;
using Naia.Connectors.Abstractions;
using Naia.Connectors.PI;
using Naia.Domain.ValueObjects;
using Naia.Infrastructure;
using Naia.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add NAIA infrastructure services
builder.Services.AddNaiaInfrastructure(builder.Configuration);

// Add PI Web API connector (for discovery/testing endpoints)
builder.Services.AddPIWebApiConnector(builder.Configuration);

// Add API services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "NAIA API", Version = "v3.0" });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ============================================================================
// HEALTH CHECK ENDPOINT
// ============================================================================
app.MapGet("/health", async (
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
    
    return Results.Ok(new
    {
        data = points,
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
    
    var current = await cache.GetAsync(point.PointSequenceId);
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
    
    var startTime = start ?? DateTime.UtcNow.AddHours(-1);
    var endTime = end ?? DateTime.UtcNow;
    
    var data = await tsReader.ReadRangeAsync(
        point.PointSequenceId, 
        startTime, 
        endTime, 
        limit);
    
    return Results.Ok(new
    {
        pointId = id,
        sequenceId = point.PointSequenceId,
        tagName = point.Name,
        start = startTime,
        end = endTime,
        count = data.Count,
        data
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

app.MapGet("/api/pipeline/metrics", async (IIngestionPipeline pipeline) =>
{
    var metrics = await pipeline.GetMetricsAsync();
    return Results.Ok(metrics);
})
.WithName("GetPipelineMetrics")
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
// PI WEB API ENDPOINTS
// ============================================================================
app.MapGet("/api/pi/health", async (PIWebApiConnector connector) =>
{
    var health = await connector.CheckHealthAsync();
    return Results.Ok(new
    {
        connected = health.IsHealthy,
        message = health.Message,
        responseTimeMs = health.ResponseTime.TotalMilliseconds,
        details = health.Details
    });
})
.WithName("PIHealthCheck")
.WithTags("PI System");

app.MapPost("/api/pi/initialize", async (
    PIWebApiConnector connector,
    IConfiguration config) =>
{
    var options = config.GetSection("PIWebApi").Get<PIWebApiOptions>();
    if (options == null || string.IsNullOrEmpty(options.BaseUrl))
    {
        return Results.BadRequest("PI Web API not configured. Set PIWebApi section in appsettings.");
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
    
    await connector.InitializeAsync(connConfig);
    
    return connector.IsAvailable 
        ? Results.Ok(new { status = "connected", dataArchive = options.DataArchive })
        : Results.Problem("Failed to connect to PI Web API");
})
.WithName("PIInitialize")
.WithTags("PI System");

app.MapGet("/api/pi/points", async (
    PIWebApiConnector connector,
    string? filter = null,
    int maxResults = 100) =>
{
    if (!connector.IsAvailable)
    {
        return Results.BadRequest("PI connector not initialized. Call /api/pi/initialize first.");
    }
    
    var points = await connector.DiscoverPointsAsync(filter, maxResults);
    
    return Results.Ok(new
    {
        count = points.Count,
        filter = filter ?? "*",
        points = points.Select(p => new
        {
            p.SourceAddress,
            p.Name,
            p.Description,
            p.EngineeringUnits,
            p.PointType,
            p.WebId
        })
    });
})
.WithName("PIDiscoverPoints")
.WithTags("PI System");

app.MapGet("/api/pi/points/{tagName}/current", async (
    string tagName,
    PIWebApiConnector connector) =>
{
    if (!connector.IsAvailable)
    {
        return Results.BadRequest("PI connector not initialized. Call /api/pi/initialize first.");
    }
    
    try
    {
        var value = await connector.ReadCurrentValueAsync(tagName);
        return Results.Ok(new
        {
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
    PIWebApiConnector connector) =>
{
    if (!connector.IsAvailable)
    {
        return Results.BadRequest("PI connector not initialized. Call /api/pi/initialize first.");
    }
    
    var values = await connector.ReadCurrentValuesAsync(tagNames);
    
    return Results.Ok(new
    {
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

app.MapGet("/api/pi/points/{tagName}/history", async (
    string tagName,
    PIWebApiConnector connector,
    DateTime? start = null,
    DateTime? end = null) =>
{
    if (!connector.IsAvailable)
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
    PIWebApiConnector connector) =>
{
    if (!connector.IsAvailable)
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

app.Run();
