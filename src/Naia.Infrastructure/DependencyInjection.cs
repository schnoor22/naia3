using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using Naia.Infrastructure.Caching;
using Naia.Infrastructure.Messaging;
using Naia.Infrastructure.Persistence;
using Naia.Infrastructure.Pipeline;
using Naia.Infrastructure.Resilience;
using Naia.Infrastructure.TimeSeries;
using Npgsql;
using StackExchange.Redis;

namespace Naia.Infrastructure;

/// <summary>
/// Extension methods for configuring NAIA infrastructure services.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Add all NAIA infrastructure services.
    /// </summary>
    public static IServiceCollection AddNaiaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<QuestDbOptions>(configuration.GetSection(QuestDbOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.SectionName));
        services.Configure<ShadowBufferOptions>(configuration.GetSection(ShadowBufferOptions.SectionName));
        
        // PostgreSQL
        services.AddPostgreSql(configuration);
        
        // QuestDB
        services.AddQuestDb(configuration);
        
        // Redis
        services.AddRedis(configuration);
        
        // Kafka
        services.AddKafka(configuration);
        
        // Data Resilience (TIC + Shadow Historian)
        services.AddDataResilience(configuration);
        
        // Pipeline MUST be singleton because it maintains long-lived background tasks and state.
        // If scoped, it gets disposed when the DI scope exits but ProcessLoopAsync continues running.
        services.AddSingleton<IIngestionPipeline, IngestionPipeline>();
        
        // Point lookup service (for pattern engine and connectors)
        services.AddPointLookupService();
        
        return services;
    }
    
    /// <summary>
    /// Configure PostgreSQL with EF Core.
    /// </summary>
    public static IServiceCollection AddPostgreSql(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var baseConnectionString = configuration.GetConnectionString("PostgreSql")
            ?? "Host=localhost;Database=naia;Username=naia;Password=naia_dev_password;SslMode=Disable;Pooling=false";
        
        // Suppress loading table list from PostgreSQL - this prevents enum type introspection
        // which fails because we don't define PostgreSQL enum types (we use string-based enums via EF Core)
        AppContext.SetSwitch("Npgsql.LoadTableList", false);
        
        // Configure NpgsqlDataSource
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(baseConnectionString);
        var dataSource = dataSourceBuilder.Build();
        
        services.AddDbContext<NaiaDbContext>(options =>
        {
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
                
                npgsql.CommandTimeout(30);
                npgsql.MigrationsAssembly("Naia.Infrastructure");
            });
            
            // Performance optimizations
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });
        
        // Repositories
        services.AddScoped<IPointRepository, PointRepository>();
        services.AddScoped<IDataSourceRepository, DataSourceRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        
        return services;
    }
    
    /// <summary>
    /// Configure QuestDB time-series storage.
    /// </summary>
    public static IServiceCollection AddQuestDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ITimeSeriesWriter, QuestDbTimeSeriesWriter>();
        services.AddSingleton<ITimeSeriesReader, QuestDbTimeSeriesReader>();
        
        return services;
    }
    
    /// <summary>
    /// Configure Redis caching.
    /// </summary>
    public static IServiceCollection AddRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConfig = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>()
            ?? new RedisOptions();
        
        // Connection multiplexer (singleton, thread-safe)
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = ConfigurationOptions.Parse(redisConfig.ConnectionString);
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;
            options.ConnectTimeout = 5000;
            return ConnectionMultiplexer.Connect(options);
        });
        
        services.AddSingleton<ICurrentValueCache, RedisCurrentValueCache>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        
        return services;
    }
    
    /// <summary>
    /// Configure Kafka messaging.
    /// </summary>
    public static IServiceCollection AddKafka(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IDataPointProducer, KafkaDataPointProducer>();
        
        // Consumer MUST be singleton because it maintains connection state across the application lifetime.
        // It's a long-lived object that subscribes to Kafka and stays connected in the ProcessLoopAsync.
        // If scoped, it gets disposed when the DI scope exits (after pipeline.StartAsync()),
        // causing the consumer to disconnect and reconnect repeatedly.
        services.AddSingleton<IDataPointConsumer>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KafkaDataPointConsumer>>();
            return new KafkaDataPointConsumer(options, logger);
        });
        
        // Pattern notifications via Kafka
        services.AddSingleton<Naia.Application.Abstractions.IPatternNotifier>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Naia.Infrastructure.Messaging.KafkaPatternNotifier>>();
            return new Naia.Infrastructure.Messaging.KafkaPatternNotifier(options, logger);
        });
        
        return services;
    }
    
    /// <summary>
    /// Configure Data Resilience services (Temporal Integrity Chain + Shadow Historian).
    /// These services provide zero-data-loss guarantees by:
    /// 1. TIC: Cryptographic chain linking batches for instant gap detection
    /// 2. Shadow Buffer: Local SQLite backup of all data before Kafka
    /// 3. Gap Recovery: Automatic healing using shadow buffer data
    /// </summary>
    public static IServiceCollection AddDataResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Shadow Buffer (SQLite-based local backup)
        services.AddSingleton<IShadowBuffer, SqliteShadowBuffer>();
        
        // Temporal Integrity Chain (Redis-based chain service)
        services.AddSingleton<IIntegrityChainService, RedisIntegrityChainService>();
        
        // Gap Recovery Service (coordinates TIC + Shadow for auto-healing)
        services.AddSingleton<IGapRecoveryService, GapRecoveryService>();
        
        return services;
    }
}

/// <summary>
/// Redis configuration options.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";
    
    /// <summary>Redis connection string</summary>
    public string ConnectionString { get; set; } = "localhost:6379";
    
    /// <summary>Current value TTL in seconds</summary>
    public int CurrentValueTtlSeconds { get; set; } = 3600;
    
    /// <summary>Idempotency key TTL in seconds</summary>
    public int IdempotencyTtlSeconds { get; set; } = 86400;
}

/// <summary>
/// QuestDB configuration options.
/// </summary>
public sealed class QuestDbOptions
{
    public const string SectionName = "QuestDb";
    
    /// <summary>HTTP endpoint for ILP ingestion</summary>
    public string HttpEndpoint { get; set; } = "http://localhost:9000";
    
    /// <summary>PostgreSQL wire protocol endpoint for queries</summary>
    public string PgWireEndpoint { get; set; } = "localhost:8812";
    
    /// <summary>Table name for point data</summary>
    public string TableName { get; set; } = "point_data";
    
    /// <summary>Auto-flush interval in milliseconds</summary>
    public int AutoFlushIntervalMs { get; set; } = 1000;
    
    /// <summary>Auto-flush row count</summary>
    public int AutoFlushRows { get; set; } = 10000;
}
