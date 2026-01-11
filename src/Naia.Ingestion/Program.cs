using Naia.Connectors;
using Naia.Infrastructure;
using Naia.Ingestion;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Add NAIA Infrastructure (PostgreSQL, QuestDB, Redis, Kafka)
builder.Services.AddNaiaInfrastructure(builder.Configuration);

// Add connectors (PI, OPC UA, etc.) based on configuration
builder.Services.AddNaiaConnectors(builder.Configuration);

// Add the main ingestion worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════════════════╗
║                                                                           ║
║   ███╗   ██╗ █████╗ ██╗ █████╗     ██╗███╗   ██╗ ██████╗ ███████╗███████╗████████╗║
║   ████╗  ██║██╔══██╗██║██╔══██╗    ██║████╗  ██║██╔════╝ ██╔════╝██╔════╝╚══██╔══╝║
║   ██╔██╗ ██║███████║██║███████║    ██║██╔██╗ ██║██║  ███╗█████╗  ███████╗   ██║   ║
║   ██║╚██╗██║██╔══██║██║██╔══██║    ██║██║╚██╗██║██║   ██║██╔══╝  ╚════██║   ██║   ║
║   ██║ ╚████║██║  ██║██║██║  ██║    ██║██║ ╚████║╚██████╔╝███████╗███████║   ██║   ║
║   ╚═╝  ╚═══╝╚═╝  ╚═╝╚═╝╚═╝  ╚═╝    ╚═╝╚═╝  ╚═══╝ ╚═════╝ ╚══════╝╚══════╝   ╚═╝   ║
║                                                                           ║
║   The First Industrial Historian That Learns From You                     ║
║                                                                           ║
║   Pipeline: Kafka → Deduplication → QuestDB + Redis                       ║
║   Mode: Consumer (Historian Storage Engine)                               ║
║                                                                           ║
╚═══════════════════════════════════════════════════════════════════════════╝
");

host.Run();
