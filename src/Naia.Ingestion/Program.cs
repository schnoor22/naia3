using Naia.Connectors;
using Naia.Ingestion;

var builder = Host.CreateApplicationBuilder(args);

// Add connectors (PI, OPC UA, etc.) based on configuration
builder.Services.AddNaiaConnectors(builder.Configuration);

// Add the main ingestion worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
