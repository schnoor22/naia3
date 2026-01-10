using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naia.PatternEngine.Configuration;
using Naia.PatternEngine.Services;
using Naia.PatternEngine.Workers;
using StackExchange.Redis;

namespace Naia.PatternEngine;

/// <summary>
/// Extension methods for registering the Pattern Flywheel services.
/// </summary>
public static class PatternEngineServiceExtensions
{
    /// <summary>
    /// Adds the Pattern Flywheel services to the service collection.
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

        // Register shared services
        services.AddSingleton<IPatternEventPublisher>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PatternEventPublisher>>();
            return new PatternEventPublisher(logger, options.Kafka);
        });

        // Get connection strings
        var postgresConnection = configuration.GetConnectionString("PostgreSQL") 
            ?? "Host=localhost;Database=naia;Username=naia;Password=naia";
        var questDbConnection = configuration.GetConnectionString("QuestDB")
            ?? "Host=localhost;Port=8812;Database=qdb;Username=admin;Password=quest";

        // Register workers as hosted services
        services.AddSingleton<IHostedService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BehavioralAggregator>>();
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var publisher = sp.GetRequiredService<IPatternEventPublisher>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new BehavioralAggregator(logger, opts, publisher, redis);
        });

        services.AddSingleton<IHostedService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CorrelationProcessor>>();
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var publisher = sp.GetRequiredService<IPatternEventPublisher>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new CorrelationProcessor(logger, opts, publisher, redis, questDbConnection);
        });

        services.AddSingleton<IHostedService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClusterDetectionWorker>>();
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var publisher = sp.GetRequiredService<IPatternEventPublisher>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new ClusterDetectionWorker(logger, opts, publisher, redis);
        });

        services.AddSingleton<IHostedService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PatternMatcherWorker>>();
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var publisher = sp.GetRequiredService<IPatternEventPublisher>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new PatternMatcherWorker(logger, opts, publisher, redis, postgresConnection);
        });

        services.AddSingleton<IHostedService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PatternLearnerWorker>>();
            var publisher = sp.GetRequiredService<IPatternEventPublisher>();
            var opts = Microsoft.Extensions.Options.Options.Create(options);
            return new PatternLearnerWorker(logger, opts, publisher, postgresConnection);
        });

        return services;
    }
}
