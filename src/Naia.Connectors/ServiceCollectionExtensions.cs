using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naia.Connectors.PI;

namespace Naia.Connectors;

/// <summary>
/// Extension methods for registering NAIA connectors with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PI Web API connector services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration containing PIWebApi section</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPIWebApiConnector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register options
        services.AddOptions<PIWebApiOptions>()
            .Bind(configuration.GetSection(PIWebApiOptions.SectionName))
            .ValidateOnStart();
        
        services.AddSingleton<IValidateOptions<PIWebApiOptions>, PIWebApiOptionsValidator>();
        
        // Register HttpClient with Windows auth support
        services.AddHttpClient<PIWebApiConnector>(client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            return new HttpClientHandler
            {
                UseDefaultCredentials = true, // Enable Windows auth
                PreAuthenticate = true
            };
        });
        
        // Register the connector as singleton (maintains WebId cache)
        services.AddSingleton<PIWebApiConnector>();
        
        return services;
    }
    
    /// <summary>
    /// Adds PI Ingestion Worker as a hosted service.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration containing Kafka settings</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPIIngestionWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Ensure PI connector is registered
        services.AddPIWebApiConnector(configuration);
        
        // Register Kafka producer
        services.AddSingleton<IProducer<string, string>>(sp =>
        {
            var kafkaConfig = new ProducerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
                ClientId = "naia-pi-ingestion",
                Acks = Acks.Leader,
                EnableIdempotence = false, // For higher throughput
                LingerMs = 5, // Batch for 5ms
                BatchSize = 16384, // 16KB batches
                CompressionType = CompressionType.Lz4
            };
            
            return new ProducerBuilder<string, string>(kafkaConfig).Build();
        });
        
        // Register the worker
        services.AddHostedService<PIIngestionWorker>();
        
        return services;
    }
    
    /// <summary>
    /// Adds all NAIA connector services including ingestion workers.
    /// </summary>
    public static IServiceCollection AddNaiaConnectors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var piEnabled = configuration.GetValue<bool>("PIWebApi:Enabled", false);
        
        if (piEnabled)
        {
            services.AddPIIngestionWorker(configuration);
        }
        
        return services;
    }
}
