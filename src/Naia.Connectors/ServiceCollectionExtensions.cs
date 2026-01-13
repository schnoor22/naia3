using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naia.Connectors.EiaGrid;
using Naia.Connectors.OpcSimulator;
using Naia.Connectors.PI;
using Naia.Connectors.Replay;
using Naia.Connectors.Weather;

namespace Naia.Connectors;

/// <summary>
/// Extension methods for registering NAIA connectors with dependency injection.
/// 
/// AVAILABLE CONNECTORS:
/// - PI Web API: Real-time data from OSIsoft PI historian
/// - Wind Farm Replay: Historical Kelmarsh wind farm data simulation
/// - Weather API: Real-time weather data from Open-Meteo
/// - EIA Grid: US electricity grid data from Energy Information Administration
/// - OPC UA Simulator: Simulated renewable energy assets (planned)
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
        // Register options WITHOUT ValidateOnStart - we'll validate on-demand only
        services.AddOptions<PIWebApiOptions>()
            .Bind(configuration.GetSection(PIWebApiOptions.SectionName));
        
        // Register validator but don't force it to run at startup
        services.AddSingleton<IValidateOptions<PIWebApiOptions>, PIWebApiOptionsValidator>();
        
        // Register HttpClient with Windows auth and SSL bypass support
        services.AddHttpClient<PIWebApiConnector>(client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler
            {
                UseDefaultCredentials = true,
                PreAuthenticate = true
            };
            
            // Bypass SSL certificate validation for self-signed certs
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                // Accept all certificates (development only!)
                return true;
            };
            
            return handler;
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
        // Register Kafka producer for connectors that publish directly to Kafka
        // (PIIngestionWorker, WindFarmReplayWorker)
        // Infrastructure only registers IDataPointProducer (wrapper), not raw IProducer
        var kafkaBootstrapServers = configuration.GetSection("Kafka:BootstrapServers").Value 
            ?? "localhost:9092";
        
        services.AddSingleton<Confluent.Kafka.IProducer<string, string>>(sp =>
        {
            var config = new Confluent.Kafka.ProducerConfig
            {
                BootstrapServers = kafkaBootstrapServers,
                ClientId = "naia-connectors",
                CompressionType = Confluent.Kafka.CompressionType.Lz4,
                EnableIdempotence = true,
                Acks = Confluent.Kafka.Acks.All,
                MessageSendMaxRetries = 3
            };
            
            return new Confluent.Kafka.ProducerBuilder<string, string>(config).Build();
        });
        
        // PI Web API Connector
        var piEnabled = configuration.GetValue<bool>("PIWebApi:Enabled", false);
        if (piEnabled)
        {
            services.AddPIWebApiConnector(configuration);
            services.AddHostedService<PIIngestionWorker>();
        }
        
        // Wind Farm Replay Connector (Kelmarsh data - legacy)
        var replayEnabled = configuration.GetValue<bool>("WindFarmReplay:Enabled", false);
        if (replayEnabled)
        {
            services.AddWindFarmReplayConnector(configuration);
        }
        
        // Generic CSV Replay Connector (Multi-site industrial data)
        var genericCsvEnabled = configuration.GetValue<bool>("GenericCsvReplay:Enabled", false);
        if (genericCsvEnabled)
        {
            services.AddGenericCsvReplayConnector(configuration);
        }
        
        // OPC UA Simulator Connector
        var opcEnabled = configuration.GetValue<bool>("OpcSimulator:Enabled", false);
        if (opcEnabled)
        {
            services.AddOpcSimulatorConnector(configuration);
        }
        
        // Weather API Connector
        var weatherEnabled = configuration.GetValue<bool>("WeatherApi:Enabled", false);
        if (weatherEnabled)
        {
            services.AddWeatherApiConnector(configuration);
        }
        
        // EIA Grid API Connector
        var eiaEnabled = configuration.GetValue<bool>("EiaGrid:Enabled", false);
        if (eiaEnabled)
        {
            services.AddEiaGridApiConnector(configuration);
        }
        
        return services;
    }
    
    /// <summary>
    /// Adds Generic CSV Replay connector services.
    /// Replays CSV data from multiple industrial sites.
    /// </summary>
    public static IServiceCollection AddGenericCsvReplayConnector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register options
        services.AddOptions<GenericCsvReplayOptions>()
            .Bind(configuration.GetSection(GenericCsvReplayOptions.SectionName))
            .ValidateDataAnnotations();
        
        // Register CSV reader
        services.AddSingleton<GenericCsvReader>();
        
        // Register the replay worker
        services.AddHostedService<GenericCsvReplayWorker>();
        
        return services;
    }
    
    /// <summary>
    /// Adds Wind Farm Replay connector services.
    /// Replays historical Kelmarsh wind turbine data through Kafka.
    /// </summary>
    public static IServiceCollection AddWindFarmReplayConnector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register options
        services.AddOptions<ReplayOptions>()
            .Bind(configuration.GetSection(ReplayOptions.SectionName))
            .ValidateDataAnnotations();
        
        // Register CSV reader
        services.AddSingleton<KelmarshCsvReader>();
        
        // Register the replay worker
        services.AddHostedService<WindFarmReplayWorker>();
        
        return services;
    }
    
    /// <summary>
    /// Adds OPC UA Simulator connector services.
    /// Connects to the NAIA OPC UA Simulator for testing.
    /// </summary>
    
    /// <summary>
    /// Adds Weather API connector services using Open-Meteo (free, no API key required).
    /// Provides real-time weather observations for configured locations.
    /// </summary>
    public static IServiceCollection AddWeatherApiConnector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register options
        services.AddOptions<WeatherApiOptions>()
            .Bind(configuration.GetSection(WeatherApiOptions.SectionName))
            .ValidateOnStart();
        
        // Register HttpClient
        services.AddHttpClient<WeatherApiConnector>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "NAIA/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        
        // Register connector as singleton
        services.AddSingleton<WeatherApiConnector>();
        
        // Register the ingestion worker
        services.AddHostedService<WeatherIngestionWorker>();
        
        return services;
    }
    
    /// <summary>
    /// Adds EIA Grid Data API connector services for US electricity grid data.
    /// Requires free API key from https://www.eia.gov/opendata/
    /// </summary>
    public static IServiceCollection AddEiaGridApiConnector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register options
        services.AddOptions<EiaGridApiOptions>()
            .Bind(configuration.GetSection(EiaGridApiOptions.SectionName))
            .ValidateOnStart();
        
        // Register HttpClient
        services.AddHttpClient<EiaGridApiConnector>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "NAIA/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        
        // Register connector as singleton
        services.AddSingleton<EiaGridApiConnector>();
        
        // Register the ingestion worker
        services.AddHostedService<EiaGridIngestionWorker>();
        
        return services;
    }
    public static IServiceCollection AddOpcSimulatorConnector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register options
        services.AddOptions<OpcSimulatorOptions>()
            .Bind(configuration.GetSection(OpcSimulatorOptions.SectionName))
            .ValidateDataAnnotations();
        
        // Register the OPC worker
        services.AddHostedService<OpcSimulatorWorker>();
        
        return services;
    }
}
