using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System.Collections.Concurrent;

namespace Naia.Connectors.OpcUa;

/// <summary>
/// OPC UA Connector - Subscription-based data acquisition from OPC UA servers.
/// 
/// FEATURES:
/// - Subscription-based data push (not polling)
/// - Automatic session reconnection
/// - Batched Kafka publishing for efficiency
/// - Multi-connection support for multiple OPC UA servers
/// 
/// FLOW:
/// 1. Connect to OPC UA server(s) defined in configuration
/// 2. Subscribe to selected nodes (from database point list)
/// 3. Receive data change notifications via subscription
/// 4. Batch data points and publish to Kafka
/// 5. Ingestion service consumes from Kafka → QuestDB
/// </summary>
public class OpcUaConnectorWorker : BackgroundService
{
    private readonly ILogger<OpcUaConnectorWorker> _logger;
    private readonly IDataPointProducer _producer;
    private readonly IServiceProvider _serviceProvider;
    private readonly OpcUaConnectorOptions _options;
    
    private ApplicationConfiguration? _appConfig;
    private readonly ConcurrentDictionary<Guid, OpcUaConnection> _connections = new();
    private readonly ConcurrentDictionary<long, PointInfo> _pointCache = new();
    private readonly ConcurrentQueue<DataPoint> _batchQueue = new();
    private Timer? _batchTimer;
    
    // Metrics
    private long _messagesReceived;
    private long _messagesPublished;
    private long _errors;

    public OpcUaConnectorWorker(
        ILogger<OpcUaConnectorWorker> logger,
        IDataPointProducer producer,
        IServiceProvider serviceProvider,
        IOptions<OpcUaConnectorOptions> options)
    {
        _logger = logger;
        _producer = producer;
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    public OpcUaConnectorStatus GetStatus()
    {
        return new OpcUaConnectorStatus
        {
            Enabled = _options.Enabled,
            ConnectionCount = _connections.Count,
            ConnectedCount = _connections.Values.Count(c => c.SessionManager?.IsConnected == true),
            MonitoredItemCount = _connections.Values.Sum(c => c.MonitoredItemCount),
            MessagesReceived = _messagesReceived,
            MessagesPublished = _messagesPublished,
            Errors = _errors,
            Connections = _connections.Values.Select(c => new OpcUaConnectionStatus
            {
                DataSourceId = c.Options.DataSourceId,
                Name = c.Options.Name,
                EndpointUrl = c.Options.EndpointUrl,
                IsConnected = c.SessionManager?.IsConnected == true,
                MonitoredItemCount = c.MonitoredItemCount,
                LastDataReceived = c.LastDataReceived
            }).ToList()
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("OPC UA Connector is disabled. Set OpcUa:Enabled=true to activate.");
            return;
        }

        _logger.LogInformation("╔══════════════════════════════════════════════════════════════════╗");
        _logger.LogInformation("║   🔌 OPC UA CONNECTOR - Industrial Data Acquisition             ║");
        _logger.LogInformation("╠══════════════════════════════════════════════════════════════════╣");
        _logger.LogInformation("║   Connections Configured: {Count,-43}║", _options.Connections.Count);
        _logger.LogInformation("╚══════════════════════════════════════════════════════════════════╝");

        try
        {
            // Initialize OPC UA application configuration
            await InitializeApplicationConfigAsync(stoppingToken);

            // Start batch publishing timer
            _batchTimer = new Timer(
                FlushBatch,
                null,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(500));

            // Connect to each configured OPC UA server
            var connectTasks = _options.Connections
                .Where(c => c.Enabled && c.AutoConnect)
                .Select(c => ConnectToServerAsync(c, stoppingToken));

            await Task.WhenAll(connectTasks);

            _logger.LogInformation("OPC UA Connector initialized. Connected: {Count}/{Total}",
                _connections.Values.Count(c => c.SessionManager?.IsConnected == true),
                _options.Connections.Count);

            // Keep running until cancelled
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("OPC UA Connector shutting down...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OPC UA Connector fatal error");
            throw;
        }
    }

    private async Task InitializeApplicationConfigAsync(CancellationToken ct)
    {
        var application = new ApplicationInstance
        {
            ApplicationName = "NAIA OPC UA Connector",
            ApplicationType = ApplicationType.Client
        };

        _appConfig = new ApplicationConfiguration
        {
            ApplicationName = "NAIA OPC UA Connector",
            ApplicationUri = $"urn:naia:opcua:connector:{Environment.MachineName}",
            ApplicationType = ApplicationType.Client,
            ProductUri = "http://naia.energy/OpcConnector",

            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(_options.PkiPath, "own"),
                    SubjectName = "CN=NAIA OPC UA Connector, O=NAIA Energy"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(_options.PkiPath, "trusted")
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(_options.PkiPath, "issuers")
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(_options.PkiPath, "rejected")
                },
                AutoAcceptUntrustedCertificates = _options.AutoAcceptUntrustedCertificates,
                RejectSHA1SignedCertificates = false,
                AddAppCertToTrustedStore = true
            },

            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 60000,
                MaxStringLength = 1048576,
                MaxByteStringLength = 1048576,
                MaxArrayLength = 65535,
                MaxMessageSize = 4194304,
                MaxBufferSize = 65535,
                ChannelLifetime = 300000,
                SecurityTokenLifetime = 3600000
            },

            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = 60000,
                WellKnownDiscoveryUrls = new StringCollection
                {
                    "opc.tcp://{0}:4840",
                    "opc.tcp://{0}:4841",
                    "opc.tcp://{0}:4842",
                    "opc.tcp://{0}:4843"
                }
            }
        };

        await _appConfig.Validate(ApplicationType.Client);

        // Create certificate if needed (non-critical for development)
        try
        {
            var certOk = await application.CheckApplicationInstanceCertificate(
                silent: true,
                minimumKeySize: 2048);

            if (!certOk)
            {
                _logger.LogWarning("Certificate validation failed - auto-accept is enabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Certificate generation failed (non-critical, continuing with auto-accept)");
        }

        // Set up certificate validation callback
        _appConfig.CertificateValidator.CertificateValidation += (validator, e) =>
        {
            if (_options.AutoAcceptUntrustedCertificates)
            {
                e.Accept = true;
                _logger.LogDebug("Auto-accepted certificate: {Subject}", e.Certificate.Subject);
            }
        };

        _logger.LogInformation("OPC UA application configuration initialized");
    }

    private async Task ConnectToServerAsync(OpcUaConnectionOptions connOptions, CancellationToken ct)
    {
        _logger.LogInformation("Connecting to OPC UA server: {Name} ({Endpoint})",
            connOptions.Name, connOptions.EndpointUrl);

        var sessionManager = new OpcUaSessionManager(_logger, connOptions, _appConfig!);
        var connection = new OpcUaConnection
        {
            Options = connOptions,
            SessionManager = sessionManager
        };

        _connections[connOptions.DataSourceId] = connection;

        sessionManager.SessionConnected += async (sender, session) =>
        {
            await OnSessionConnectedAsync(connection, session, ct);
        };

        sessionManager.SessionDisconnected += (sender, args) =>
        {
            _logger.LogWarning("Session disconnected: {Name}", connOptions.Name);
            connection.Subscription = null;
        };

        await sessionManager.ConnectAsync(ct);
    }

    private async Task OnSessionConnectedAsync(OpcUaConnection connection, Session session, CancellationToken ct)
    {
        _logger.LogInformation("Session connected, setting up subscriptions for {Name}", connection.Options.Name);

        try
        {
            // Create a scope for the scoped IPointRepository
            using (var scope = _serviceProvider.CreateScope())
            {
                var pointRepository = scope.ServiceProvider.GetRequiredService<IPointRepository>();
                
                // Load points for this data source from database
                var points = await pointRepository.GetByDataSourceIdAsync(connection.Options.DataSourceId, ct);

                if (!points.Any())
                {
                    _logger.LogWarning("No points configured for data source {Name}. Browse and import points first.",
                        connection.Options.Name);
                    return;
                }

                // Filter to points that have sequence IDs (assigned by database)
                var validPoints = points.Where(p => p.PointSequenceId.HasValue).ToList();
                if (!validPoints.Any())
                {
                    _logger.LogWarning("No points with assigned sequence IDs for data source {Name}",
                        connection.Options.Name);
                    return;
                }

                // Cache point information for fast lookup
                foreach (var point in validPoints)
                {
                    _pointCache[point.PointSequenceId!.Value] = new PointInfo
                    {
                        PointSequenceId = point.PointSequenceId!.Value,
                        PointName = point.Name,
                        DataSourceId = connection.Options.DataSourceId.ToString(),
                        SourceTag = point.SourceAddress ?? point.Name
                    };
                }

                // Create subscription
                var subscription = connection.SessionManager!.CreateSubscription(connection.Options.PublishingIntervalMs);
                connection.Subscription = subscription;

                // Create monitored items for each point
                var monitoredItems = new List<MonitoredItem>();
                foreach (var point in validPoints.Where(p => p.IsEnabled))
                {
                    // Use SourceAddress as the OPC UA NodeId
                    if (string.IsNullOrEmpty(point.SourceAddress))
                    {
                        _logger.LogWarning("Point {Name} has no SourceAddress (OPC UA NodeId), skipping", point.Name);
                        continue;
                    }

                    // Parse namespace notation like "ns=2;s=TAG_NAME" properly
                    NodeId nodeId;
                    try
                    {
                        nodeId = NodeId.Parse(point.SourceAddress);
                    }
                    catch
                    {
                        // Fallback for legacy data without namespace notation
                        nodeId = new NodeId(point.SourceAddress, 2);
                    }

                    var monitoredItem = new MonitoredItem(subscription.DefaultItem)
                    {
                        DisplayName = point.Name,
                        StartNodeId = nodeId,
                        AttributeId = Attributes.Value,
                        SamplingInterval = connection.Options.SamplingIntervalMs,
                        QueueSize = (uint)connection.Options.QueueSize,
                        DiscardOldest = connection.Options.DiscardOldest
                    };

                    // Store point sequence ID in handle for fast lookup
                    monitoredItem.Handle = point.PointSequenceId!.Value;

                    monitoredItem.Notification += (item, e) =>
                    {
                        OnDataChange(connection, item, e);
                    };

                    monitoredItems.Add(monitoredItem);
                }

                subscription.AddItems(monitoredItems);
                subscription.ApplyChanges();

                connection.MonitoredItemCount = monitoredItems.Count;

                _logger.LogInformation("✅ Created {Count} monitored items for {Name}",
                    monitoredItems.Count, connection.Options.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set up subscriptions for {Name}", connection.Options.Name);
            Interlocked.Increment(ref _errors);
        }
    }

    private void OnDataChange(OpcUaConnection connection, MonitoredItem item, MonitoredItemNotificationEventArgs e)
    {
        try
        {
            if (e.NotificationValue is not MonitoredItemNotification notification)
                return;

            Interlocked.Increment(ref _messagesReceived);
            connection.LastDataReceived = DateTime.UtcNow;

            var pointSequenceId = (long)item.Handle;
            var dataValue = notification.Value;

            // Convert value to double
            if (!TryConvertToDouble(dataValue.Value, out var value))
            {
                _logger.LogDebug("Could not convert value for {NodeId}: {Value}",
                    item.StartNodeId, dataValue.Value);
                return;
            }

            // Get cached point info
            if (!_pointCache.TryGetValue(pointSequenceId, out var pointInfo))
            {
                _logger.LogWarning("Unknown point sequence ID: {Id}", pointSequenceId);
                return;
            }

            // Create data point
            var dataPoint = DataPoint.FromOpc(
                pointSequenceId,
                pointInfo.PointName,
                dataValue.SourceTimestamp != DateTime.MinValue ? dataValue.SourceTimestamp : DateTime.UtcNow,
                value,
                dataValue.StatusCode.Code,
                pointInfo.DataSourceId,
                pointInfo.SourceTag);

            // Add to batch queue
            _batchQueue.Enqueue(dataPoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing data change for {NodeId}", item.StartNodeId);
            Interlocked.Increment(ref _errors);
        }
    }

    private void FlushBatch(object? state)
    {
        try
        {
            var points = new List<DataPoint>();
            while (_batchQueue.TryDequeue(out var point) && points.Count < 1000)
            {
                points.Add(point);
            }

            if (points.Count == 0) return;

            // Group by data source for proper Kafka partitioning
            var batches = points.GroupBy(p => p.DataSourceId);

            foreach (var group in batches)
            {
                var batch = DataPointBatch.Create(group, group.Key);

                _ = _producer.PublishAsync(batch).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Failed to publish batch to Kafka");
                        Interlocked.Increment(ref _errors);
                    }
                    else
                    {
                        Interlocked.Add(ref _messagesPublished, batch.Count);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing batch to Kafka");
            Interlocked.Increment(ref _errors);
        }
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        result = 0;
        if (value == null) return false;

        try
        {
            result = Convert.ToDouble(value);
            return !double.IsNaN(result) && !double.IsInfinity(result);
        }
        catch
        {
            return false;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OPC UA Connector stopping...");

        _batchTimer?.Dispose();
        FlushBatch(null); // Final flush

        foreach (var connection in _connections.Values)
        {
            if (connection.SessionManager != null)
            {
                await connection.SessionManager.DisposeAsync();
            }
        }

        _connections.Clear();
        await base.StopAsync(cancellationToken);
    }
}

/// <summary>
/// Represents an active OPC UA connection.
/// </summary>
internal class OpcUaConnection
{
    public required OpcUaConnectionOptions Options { get; init; }
    public OpcUaSessionManager? SessionManager { get; set; }
    public Subscription? Subscription { get; set; }
    public int MonitoredItemCount { get; set; }
    public DateTime? LastDataReceived { get; set; }
}

/// <summary>
/// Cached point information for fast lookup.
/// </summary>
internal class PointInfo
{
    public long PointSequenceId { get; init; }
    public required string PointName { get; init; }
    public required string DataSourceId { get; init; }
    public required string SourceTag { get; init; }
}

/// <summary>
/// Status of the OPC UA connector.
/// </summary>
public class OpcUaConnectorStatus
{
    public bool Enabled { get; init; }
    public int ConnectionCount { get; init; }
    public int ConnectedCount { get; init; }
    public int MonitoredItemCount { get; init; }
    public long MessagesReceived { get; init; }
    public long MessagesPublished { get; init; }
    public long Errors { get; init; }
    public List<OpcUaConnectionStatus> Connections { get; init; } = new();
}

/// <summary>
/// Status of a single OPC UA connection.
/// </summary>
public class OpcUaConnectionStatus
{
    public Guid DataSourceId { get; init; }
    public required string Name { get; init; }
    public required string EndpointUrl { get; init; }
    public bool IsConnected { get; init; }
    public int MonitoredItemCount { get; init; }
    public DateTime? LastDataReceived { get; init; }
}
