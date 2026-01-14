using Hangfire;
using Hangfire.PostgreSql;
using Naia.Infrastructure;
using Naia.Infrastructure.Telemetry;
using Naia.PatternEngine;
using Naia.PatternWorker;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Infrastructure services (PostgreSQL, QuestDB, Redis, Kafka)
builder.Services.AddNaiaInfrastructure(builder.Configuration);

// PatternEngine with Hangfire jobs
builder.Services.AddPatternEngine(builder.Configuration);

// OpenTelemetry tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("Naia.PatternWorker", serviceVersion: "3.0.0"))
    .WithTracing(tracing => tracing
        .AddSource(NaiaMetrics.ActivitySource.Name)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";
            options.Endpoint = new Uri(otlpEndpoint);
        }));

// Hangfire server
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 4;
    options.ServerName = "PatternWorker";
    options.Queues = new[] { "pattern-analysis", "default" };
});

// Background worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Track uptime metric
var uptimeTask = Task.Run(async () =>
{
    var startTime = DateTimeOffset.UtcNow;
    while (true)
    {
        var uptime = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
        NaiaMetrics.ApplicationUptime.Set(uptime);
        await Task.Delay(TimeSpan.FromSeconds(15));
    }
});

host.Run();
