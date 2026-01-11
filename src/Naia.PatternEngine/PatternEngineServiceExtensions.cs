using Hangfire;
using Hangfire.Console;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naia.PatternEngine.Configuration;
using Naia.PatternEngine.Jobs;
using StackExchange.Redis;

namespace Naia.PatternEngine;

/// <summary>
/// Extension methods for registering the Pattern Flywheel services with Hangfire.
/// </summary>
public static class PatternEngineServiceExtensions
{
    /// <summary>
    /// Adds the Pattern Flywheel services and Hangfire job scheduler to the service collection.
    /// </summary>
    public static IServiceCollection AddPatternEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration
        services.Configure<PatternFlywheelOptions>(
            configuration.GetSection(PatternFlywheelOptions.SectionName));

        var options = configuration
            .GetSection(PatternFlywheelOptions.SectionName)
            .Get<PatternFlywheelOptions>() ?? new PatternFlywheelOptions();

        if (!options.Enabled)
        {
            return services;
        }

        // Get connection strings
        var postgresConnection = configuration.GetConnectionString("PostgreSQL") 
            ?? "Host=localhost;Database=naia;Username=naia;Password=naia";
        var questDbConnection = configuration.GetConnectionString("QuestDB")
            ?? "Host=localhost;Port=8812;Database=qdb;Username=admin;Password=quest";
        
        // QuestDB doesn't have PostgreSQL system catalogs - configure Npgsql compatibility mode
        if (!questDbConnection.Contains("Server Compatibility Mode", StringComparison.OrdinalIgnoreCase))
        {
            questDbConnection += ";Server Compatibility Mode=NoTypeLoading";
        }

        // Configure Hangfire with PostgreSQL storage
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(postgresConnection))
            .UseConsole());

        // Configure Hangfire server with queues
        services.AddHangfireServer(serverOptions =>
        {
            serverOptions.WorkerCount = options.Hangfire.WorkerCount;
            serverOptions.Queues = new[] { "analysis", "matching", "learning", "maintenance", "default" };
        });

        // Register job implementations with their dependencies
        services.AddScoped<IBehavioralAnalysisJob>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BehavioralAnalysisJob>>();
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new BehavioralAnalysisJob(logger, opts, redis, postgresConnection, questDbConnection);
        });

        services.AddScoped<ICorrelationAnalysisJob>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CorrelationAnalysisJob>>();
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new CorrelationAnalysisJob(logger, opts, redis, postgresConnection, questDbConnection);
        });

        services.AddScoped<IClusterDetectionJob>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClusterDetectionJob>>();
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new ClusterDetectionJob(logger, opts, redis, postgresConnection);
        });

        services.AddScoped<IPatternMatchingJob>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PatternMatchingJob>>();
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new PatternMatchingJob(logger, opts, redis, postgresConnection);
        });

        services.AddScoped<IPatternLearningJob>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PatternLearningJob>>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new PatternLearningJob(logger, opts, postgresConnection);
        });

        services.AddScoped<IMaintenanceJob>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MaintenanceJob>>();
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new MaintenanceJob(logger, opts, redis, postgresConnection);
        });

        return services;
    }

    /// <summary>
    /// Configures the Hangfire dashboard and schedules recurring pattern flywheel jobs.
    /// Call this in Program.cs after app.Build().
    /// </summary>
    public static IApplicationBuilder UsePatternEngine(
        this IApplicationBuilder app,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(PatternFlywheelOptions.SectionName)
            .Get<PatternFlywheelOptions>() ?? new PatternFlywheelOptions();

        if (!options.Enabled)
        {
            return app;
        }

        // Configure Hangfire Dashboard
        if (options.Hangfire.EnableDashboard)
        {
            app.UseHangfireDashboard(options.Hangfire.DashboardPath, new DashboardOptions
            {
                DashboardTitle = "NAIA Pattern Flywheel Jobs",
                DisplayStorageConnectionString = false
            });
        }

        // Schedule recurring jobs
        RecurringJob.AddOrUpdate<IBehavioralAnalysisJob>(
            "pattern-behavioral-analysis",
            job => job.ExecuteAsync(null!, CancellationToken.None),
            options.Hangfire.BehavioralAnalysisCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<ICorrelationAnalysisJob>(
            "pattern-correlation-analysis",
            job => job.ExecuteAsync(null!, CancellationToken.None),
            options.Hangfire.CorrelationAnalysisCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<IClusterDetectionJob>(
            "pattern-cluster-detection",
            job => job.ExecuteAsync(null!, CancellationToken.None),
            options.Hangfire.ClusterDetectionCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<IPatternMatchingJob>(
            "pattern-matching",
            job => job.ExecuteAsync(null!, CancellationToken.None),
            options.Hangfire.PatternMatchingCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<IPatternLearningJob>(
            "pattern-learning",
            job => job.ExecuteAsync(null!, CancellationToken.None),
            options.Hangfire.PatternLearningCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<IMaintenanceJob>(
            "pattern-maintenance",
            job => job.ExecuteAsync(null!, CancellationToken.None),
            options.Hangfire.MaintenanceCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        return app;
    }
}
