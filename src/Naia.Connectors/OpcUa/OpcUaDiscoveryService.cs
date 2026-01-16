using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace Naia.Connectors.OpcUa;

/// <summary>
/// Service for discovering OPC UA address space and returning structured node metadata.
/// Used by the Point Discovery API to browse servers and present intelligent import options.
/// </summary>
public class OpcUaDiscoveryService
{
    private readonly ILogger<OpcUaDiscoveryService> _logger;
    
    public OpcUaDiscoveryService(ILogger<OpcUaDiscoveryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Browse an OPC UA server and discover all data nodes.
    /// </summary>
    public async Task<OpcUaDiscoveryResult> DiscoverNodesAsync(
        string endpointUrl,
        string? startNodeId = null,
        int maxDepth = 10,
        CancellationToken ct = default)
    {
        var result = new OpcUaDiscoveryResult
        {
            EndpointUrl = endpointUrl,
            DiscoveredAt = DateTime.UtcNow
        };

        Session? session = null;
        try
        {
            _logger.LogInformation("Discovering OPC UA address space: {Endpoint}", endpointUrl);

            // Create application configuration
            var appConfig = await CreateApplicationConfigAsync();

            // Connect to server
            session = await ConnectAsync(endpointUrl, appConfig, ct);
            if (session == null)
            {
                result.Error = "Failed to connect to OPC UA server";
                return result;
            }

            result.ServerInfo = new OpcUaServerInfo
            {
                ApplicationName = session.Endpoint.Server.ApplicationName?.Text ?? "Unknown",
                ApplicationUri = session.Endpoint.Server.ApplicationUri,
                ProductUri = session.Endpoint.Server.ProductUri,
                ServerState = "Running"
            };

            // Determine start node
            var startNode = string.IsNullOrEmpty(startNodeId)
                ? ObjectIds.ObjectsFolder
                : NodeId.Parse(startNodeId);

            // Browse recursively
            var nodes = new List<DiscoveredNode>();
            await BrowseRecursiveAsync(session, startNode, "", nodes, 0, maxDepth, ct);

            result.Nodes = nodes;
            result.TotalNodeCount = nodes.Count;
            result.DataNodeCount = nodes.Count(n => n.IsDataNode);
            result.Success = true;

            _logger.LogInformation("✅ Discovered {Total} nodes ({Data} data nodes) from {Endpoint}",
                result.TotalNodeCount, result.DataNodeCount, endpointUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover OPC UA address space: {Endpoint}", endpointUrl);
            result.Error = ex.Message;
        }
        finally
        {
            if (session != null)
            {
                try
                {
                    session.Close();
                    session.Dispose();
                }
                catch { }
            }
        }

        return result;
    }

    /// <summary>
    /// Browse a specific node and return its children (non-recursive).
    /// </summary>
    public async Task<List<DiscoveredNode>> BrowseChildrenAsync(
        string endpointUrl,
        string nodeId,
        CancellationToken ct = default)
    {
        var nodes = new List<DiscoveredNode>();
        Session? session = null;

        try
        {
            var appConfig = await CreateApplicationConfigAsync();
            session = await ConnectAsync(endpointUrl, appConfig, ct);
            if (session == null) return nodes;

            var parentNode = NodeId.Parse(nodeId);
            await BrowseChildrenInternalAsync(session, parentNode, "", nodes, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to browse children of {NodeId}", nodeId);
        }
        finally
        {
            if (session != null)
            {
                try
                {
                    session.Close();
                    session.Dispose();
                }
                catch { }
            }
        }

        return nodes;
    }

    /// <summary>
    /// Search for nodes matching a pattern.
    /// </summary>
    public async Task<List<DiscoveredNode>> SearchNodesAsync(
        string endpointUrl,
        string searchPattern,
        int maxResults = 100,
        CancellationToken ct = default)
    {
        // First, discover all nodes
        var discovery = await DiscoverNodesAsync(endpointUrl, null, 15, ct);
        
        if (!discovery.Success || discovery.Nodes == null)
            return new List<DiscoveredNode>();

        // Filter by search pattern (case-insensitive)
        var pattern = searchPattern.ToLowerInvariant();
        return discovery.Nodes
            .Where(n => n.DisplayName.ToLowerInvariant().Contains(pattern) ||
                       n.BrowseName.ToLowerInvariant().Contains(pattern) ||
                       n.NodePath.ToLowerInvariant().Contains(pattern))
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Get detailed information about a specific node.
    /// </summary>
    public async Task<DiscoveredNode?> GetNodeDetailsAsync(
        string endpointUrl,
        string nodeId,
        CancellationToken ct = default)
    {
        Session? session = null;
        try
        {
            var appConfig = await CreateApplicationConfigAsync();
            session = await ConnectAsync(endpointUrl, appConfig, ct);
            if (session == null) return null;

            var node = new NodeId(nodeId);
            return await ReadNodeDetailsAsync(session, node, "", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get node details for {NodeId}", nodeId);
            return null;
        }
        finally
        {
            if (session != null)
            {
                try
                {
                    session.Close();
                    session.Dispose();
                }
                catch { }
            }
        }
    }

    private async Task<ApplicationConfiguration> CreateApplicationConfigAsync()
    {
        var pkiPath = Path.Combine(AppContext.BaseDirectory, "PKI");
        
        var config = new ApplicationConfiguration
        {
            ApplicationName = "NAIA Discovery Client",
            ApplicationUri = $"urn:naia:discovery:{Guid.NewGuid():N}",
            ApplicationType = ApplicationType.Client,
            ProductUri = "http://naia.energy/Discovery",

            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiPath, "own"),
                    SubjectName = "CN=NAIA Discovery, O=NAIA Energy"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiPath, "trusted")
                },
                AutoAcceptUntrustedCertificates = true,
                RejectSHA1SignedCertificates = false,
                AddAppCertToTrustedStore = true
            },

            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 30000,
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
                DefaultSessionTimeout = 60000
            }
        };

        await config.Validate(ApplicationType.Client);

        config.CertificateValidator.CertificateValidation += (validator, e) =>
        {
            e.Accept = true; // Auto-accept for discovery
        };

        return config;
    }

    private async Task<Session?> ConnectAsync(
        string endpointUrl,
        ApplicationConfiguration appConfig,
        CancellationToken ct)
    {
        try
        {
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointUrl, useSecurity: false);
            var endpointConfig = EndpointConfiguration.Create(appConfig);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfig);

            return await Session.Create(
                appConfig,
                endpoint,
                updateBeforeConnect: false,
                sessionName: $"NAIA-Discovery-{Guid.NewGuid():N}",
                sessionTimeout: 30000,
                new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: null,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to {Endpoint}", endpointUrl);
            return null;
        }
    }

    private async Task BrowseRecursiveAsync(
        Session session,
        NodeId nodeId,
        string parentPath,
        List<DiscoveredNode> nodes,
        int currentDepth,
        int maxDepth,
        CancellationToken ct)
    {
        if (currentDepth > maxDepth) return;
        if (ct.IsCancellationRequested) return;

        try
        {
            _logger.LogDebug("Browsing node {NodeId} at depth {Depth}", nodeId, currentDepth);
            
            var browseDescription = new BrowseDescription
            {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
                ResultMask = (uint)BrowseResultMask.All
            };

            var browseResult = session.Browse(
                null,
                null,
                100000,
                new BrowseDescriptionCollection { browseDescription },
                out var results,
                out var diagnosticInfos);

            if (results == null || results.Count == 0)
            {
                _logger.LogDebug("No results for node {NodeId}", nodeId);
                return;
            }

            var references = results[0].References;
            if (references == null)
            {
                _logger.LogDebug("No references for node {NodeId}", nodeId);
                return;
            }
            
            _logger.LogDebug("Found {Count} children for node {NodeId}", references.Count, nodeId);

            foreach (var reference in references)
            {
                var childNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                var browseName = reference.BrowseName.Name;
                var displayName = reference.DisplayName.Text ?? browseName;
                var nodePath = string.IsNullOrEmpty(parentPath) ? browseName : $"{parentPath}.{browseName}";

                var discoveredNode = new DiscoveredNode
                {
                    NodeId = childNodeId.ToString(),
                    BrowseName = browseName,
                    DisplayName = displayName,
                    NodePath = nodePath,
                    NodeClass = reference.NodeClass.ToString(),
                    IsDataNode = reference.NodeClass == NodeClass.Variable,
                    ParentNodeId = nodeId.ToString(),
                    Depth = currentDepth + 1
                };

                // Read additional attributes for Variable nodes
                if (reference.NodeClass == NodeClass.Variable)
                {
                    try
                    {
                        var details = await ReadNodeDetailsAsync(session, childNodeId, nodePath, ct);
                        if (details != null)
                        {
                            discoveredNode.DataType = details.DataType;
                            discoveredNode.EngineeringUnits = details.EngineeringUnits;
                            discoveredNode.Description = details.Description;
                            discoveredNode.CurrentValue = details.CurrentValue;
                        }
                    }
                    catch
                    {
                        // Continue without details
                    }
                }

                nodes.Add(discoveredNode);

                // Recurse into folders/objects
                if (reference.NodeClass == NodeClass.Object)
                {
                    await BrowseRecursiveAsync(session, childNodeId, nodePath, nodes, currentDepth + 1, maxDepth, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Browse error at depth {Depth} for node {NodeId}", currentDepth, nodeId);
        }
    }

    private async Task BrowseChildrenInternalAsync(
        Session session,
        NodeId nodeId,
        string parentPath,
        List<DiscoveredNode> nodes,
        CancellationToken ct)
    {
        await BrowseRecursiveAsync(session, nodeId, parentPath, nodes, 0, 1, ct);
    }

    private async Task<DiscoveredNode?> ReadNodeDetailsAsync(
        Session session,
        NodeId nodeId,
        string nodePath,
        CancellationToken ct)
    {
        try
        {
            var nodesToRead = new ReadValueIdCollection
            {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DisplayName },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Description },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DataType },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.BrowseName }
            };

            session.Read(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                out var results,
                out var diagnosticInfos);

            if (results == null || results.Count < 5)
                return null;

            var node = new DiscoveredNode
            {
                NodeId = nodeId.ToString(),
                NodePath = nodePath,
                IsDataNode = true,
                DisplayName = results[0].Value as LocalizedText != null
                    ? ((LocalizedText)results[0].Value).Text
                    : nodeId.Identifier?.ToString() ?? "Unknown",
                Description = results[1].Value as LocalizedText != null
                    ? ((LocalizedText)results[1].Value).Text
                    : null,
                BrowseName = results[4].Value as QualifiedName != null
                    ? ((QualifiedName)results[4].Value).Name
                    : nodeId.Identifier?.ToString() ?? "Unknown"
            };

            // Data type
            if (results[2].Value is NodeId dataTypeId)
            {
                node.DataType = MapDataType(dataTypeId);
            }

            // Current value
            if (StatusCode.IsGood(results[3].StatusCode) && results[3].Value != null)
            {
                node.CurrentValue = results[3].Value?.ToString();
            }

            return node;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read node details: {NodeId}", nodeId);
            return null;
        }
    }

    private static string MapDataType(NodeId dataTypeId)
    {
        if (dataTypeId == DataTypeIds.Double) return "Double";
        if (dataTypeId == DataTypeIds.Float) return "Float";
        if (dataTypeId == DataTypeIds.Int32) return "Int32";
        if (dataTypeId == DataTypeIds.Int64) return "Int64";
        if (dataTypeId == DataTypeIds.Int16) return "Int16";
        if (dataTypeId == DataTypeIds.UInt32) return "UInt32";
        if (dataTypeId == DataTypeIds.UInt64) return "UInt64";
        if (dataTypeId == DataTypeIds.UInt16) return "UInt16";
        if (dataTypeId == DataTypeIds.Boolean) return "Boolean";
        if (dataTypeId == DataTypeIds.String) return "String";
        if (dataTypeId == DataTypeIds.DateTime) return "DateTime";
        return dataTypeId.Identifier?.ToString() ?? "Unknown";
    }
}

/// <summary>
/// Result of OPC UA address space discovery.
/// </summary>
public class OpcUaDiscoveryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public required string EndpointUrl { get; init; }
    public DateTime DiscoveredAt { get; init; }
    public OpcUaServerInfo? ServerInfo { get; set; }
    public List<DiscoveredNode>? Nodes { get; set; }
    public int TotalNodeCount { get; set; }
    public int DataNodeCount { get; set; }
}

/// <summary>
/// OPC UA server information.
/// </summary>
public class OpcUaServerInfo
{
    public string? ApplicationName { get; set; }
    public string? ApplicationUri { get; set; }
    public string? ProductUri { get; set; }
    public string? ServerState { get; set; }
}

/// <summary>
/// A discovered OPC UA node.
/// </summary>
public class DiscoveredNode
{
    public required string NodeId { get; init; }
    public required string BrowseName { get; set; }
    public required string DisplayName { get; set; }
    public required string NodePath { get; set; }
    public string NodeClass { get; set; } = "Variable";
    public bool IsDataNode { get; set; }
    public string? ParentNodeId { get; set; }
    public int Depth { get; set; }
    public string? DataType { get; set; }
    public string? EngineeringUnits { get; set; }
    public string? Description { get; set; }
    public string? CurrentValue { get; set; }
    
    // Enrichment from Knowledge Base
    public string? DetectedEquipmentType { get; set; }
    public string? DetectedMeasurementType { get; set; }
    public double ConfidenceScore { get; set; }
    public string? SuggestedName { get; set; }
    public string? SuggestedDescription { get; set; }
    public string? SuggestedUnits { get; set; }
}
