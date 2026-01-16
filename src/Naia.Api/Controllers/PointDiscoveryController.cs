using Microsoft.AspNetCore.Mvc;
using Naia.Application.Abstractions;
using Naia.Connectors.OpcUa;
using Naia.Domain.Entities;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Naia.Api.Controllers;

/// <summary>
/// Point Discovery API - Browse OPC UA address spaces and import points with intelligent enrichment.
/// 
/// WORKFLOW:
/// 1. /discover - Browse server and return all nodes with AI-enriched metadata
/// 2. /search - Full-text search across discovered nodes
/// 3. /templates - Get template-based point selections (e.g., "Wind Turbine Standard")
/// 4. /import - Import selected nodes as NAIA points
/// 
/// This replaces the traditional tree browser with a search-first, intelligent approach.
/// </summary>
[ApiController]
[Route("api/datasources/{dataSourceId:guid}/discover")]
public class PointDiscoveryController : ControllerBase
{
    private readonly ILogger<PointDiscoveryController> _logger;
    private readonly IDataSourceRepository _dataSourceRepository;
    private readonly IPointRepository _pointRepository;
    private readonly IKnowledgeBaseRepository _knowledgeRepository;
    private readonly OpcUaDiscoveryService _discoveryService;

    public PointDiscoveryController(
        ILogger<PointDiscoveryController> logger,
        IDataSourceRepository dataSourceRepository,
        IPointRepository pointRepository,
        IKnowledgeBaseRepository knowledgeRepository,
        OpcUaDiscoveryService discoveryService)
    {
        _logger = logger;
        _dataSourceRepository = dataSourceRepository;
        _pointRepository = pointRepository;
        _knowledgeRepository = knowledgeRepository;
        _discoveryService = discoveryService;
    }

    /// <summary>
    /// Discover all available points from an OPC UA data source.
    /// Returns enriched metadata with equipment type detection and suggested naming.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DiscoverPoints(
        Guid dataSourceId,
        [FromQuery] int maxDepth = 10,
        CancellationToken ct = default)
    {
        var dataSource = await _dataSourceRepository.GetByIdAsync(dataSourceId, ct);
        if (dataSource == null)
            return NotFound(new { error = "Data source not found" });

        if (dataSource.SourceType != DataSourceType.OpcUa)
            return BadRequest(new { error = "Data source is not an OPC UA connection" });

        // Parse connection configuration
        var endpointUrl = GetEndpointUrl(dataSource);
        if (string.IsNullOrEmpty(endpointUrl))
            return BadRequest(new { error = "OPC UA endpoint URL not configured" });

        _logger.LogInformation("Discovering points for data source {Name} ({Endpoint})",
            dataSource.Name, endpointUrl);

        // Discover OPC UA address space
        var discoveryResult = await _discoveryService.DiscoverNodesAsync(endpointUrl, null, maxDepth, ct);

        if (!discoveryResult.Success)
            return StatusCode(500, new { error = discoveryResult.Error });

        // Enrich nodes with knowledge base
        var enrichedNodes = await EnrichNodesAsync(discoveryResult.Nodes ?? new(), dataSource, ct);

        // Get already imported points
        var existingPoints = await _pointRepository.GetByDataSourceIdAsync(dataSourceId, ct);
        var existingSourceAddresses = existingPoints.Select(p => p.SourceAddress).ToHashSet();

        // Mark which nodes are already imported
        foreach (var node in enrichedNodes)
        {
            node.IsImported = existingSourceAddresses.Contains(node.NodeId);
        }

        // Group by detected equipment type
        var groupedByEquipment = enrichedNodes
            .Where(n => n.IsDataNode)
            .GroupBy(n => n.DetectedEquipmentType ?? "Unknown")
            .Select(g => new EquipmentGroup
            {
                EquipmentType = g.Key,
                NodeCount = g.Count(),
                ImportedCount = g.Count(n => n.IsImported),
                Nodes = g.OrderBy(n => n.NodePath).ToList()
            })
            .OrderByDescending(g => g.NodeCount)
            .ToList();

        return Ok(new DiscoveryResponse
        {
            DataSourceId = dataSourceId,
            DataSourceName = dataSource.Name,
            EndpointUrl = endpointUrl,
            DiscoveredAt = discoveryResult.DiscoveredAt,
            ServerInfo = discoveryResult.ServerInfo,
            TotalNodes = discoveryResult.TotalNodeCount,
            DataNodes = discoveryResult.DataNodeCount,
            ImportedCount = existingPoints.Count(),
            EquipmentGroups = groupedByEquipment,
            AllNodes = enrichedNodes.Where(n => n.IsDataNode).OrderBy(n => n.NodePath).ToList()
        });
    }

    /// <summary>
    /// Search for points matching a pattern.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchPoints(
        Guid dataSourceId,
        [FromQuery] string q,
        [FromQuery] string? equipmentType = null,
        [FromQuery] string? measurementType = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q) && string.IsNullOrEmpty(equipmentType))
            return BadRequest(new { error = "Search query or equipment type required" });

        var dataSource = await _dataSourceRepository.GetByIdAsync(dataSourceId, ct);
        if (dataSource == null)
            return NotFound(new { error = "Data source not found" });

        var endpointUrl = GetEndpointUrl(dataSource);
        if (string.IsNullOrEmpty(endpointUrl))
            return BadRequest(new { error = "OPC UA endpoint URL not configured" });

        // Search nodes
        var nodes = await _discoveryService.SearchNodesAsync(endpointUrl, q ?? "", limit, ct);

        // Enrich and filter
        var enrichedNodes = await EnrichNodesAsync(nodes, dataSource, ct);

        if (!string.IsNullOrEmpty(equipmentType))
        {
            enrichedNodes = enrichedNodes
                .Where(n => n.DetectedEquipmentType?.Equals(equipmentType, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        if (!string.IsNullOrEmpty(measurementType))
        {
            enrichedNodes = enrichedNodes
                .Where(n => n.DetectedMeasurementType?.Equals(measurementType, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        return Ok(new
        {
            query = q,
            filters = new { equipmentType, measurementType },
            count = enrichedNodes.Count,
            nodes = enrichedNodes
        });
    }

    /// <summary>
    /// Get available templates for bulk point selection.
    /// Templates are based on industry standards (IEC 61400 for wind, etc.)
    /// </summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates(
        Guid dataSourceId,
        CancellationToken ct = default)
    {
        var dataSource = await _dataSourceRepository.GetByIdAsync(dataSourceId, ct);
        if (dataSource == null)
            return NotFound(new { error = "Data source not found" });

        // Return available templates based on detected equipment
        var templates = new List<PointTemplate>
        {
            new PointTemplate
            {
                Id = "wind-turbine-standard",
                Name = "Wind Turbine - Standard Monitoring",
                Description = "Essential points for wind turbine performance monitoring (IEC 61400 based)",
                Industry = "Wind",
                EquipmentType = "Wind Turbine",
                PointPatterns = new[]
                {
                    "*Power*", "*WindSpeed*", "*RotorSpeed*", "*GeneratorSpeed*",
                    "*PitchAngle*", "*NacelleDirection*", "*Status*",
                    "*GearboxTemp*", "*GeneratorTemp*", "*BearingTemp*"
                },
                ExpectedPointCount = 15,
                Priority = 1
            },
            new PointTemplate
            {
                Id = "wind-turbine-full",
                Name = "Wind Turbine - Full Monitoring",
                Description = "Complete point set including all electrical, mechanical, and diagnostic points",
                Industry = "Wind",
                EquipmentType = "Wind Turbine",
                PointPatterns = new[] { "GE-2.5-*.*", "V110-*.*" },
                ExpectedPointCount = 25,
                Priority = 2
            },
            new PointTemplate
            {
                Id = "solar-inverter-standard",
                Name = "Solar Inverter - Standard Monitoring",
                Description = "Essential points for solar inverter performance (DC/AC power, efficiency)",
                Industry = "Solar",
                EquipmentType = "Solar Inverter",
                PointPatterns = new[]
                {
                    "*DcPower*", "*AcPower*", "*DcVoltage*", "*DcCurrent*",
                    "*Efficiency*", "*Status*", "*CabinetTemp*"
                },
                ExpectedPointCount = 12,
                Priority = 1
            },
            new PointTemplate
            {
                Id = "bess-standard",
                Name = "BESS - Standard Monitoring",
                Description = "Battery storage essential points (SOC, power, temperatures)",
                Industry = "Storage",
                EquipmentType = "Battery Storage",
                PointPatterns = new[]
                {
                    "*SOC*", "*SOH*", "*ActivePower*", "*Voltage*", "*Current*",
                    "*CellTemp*", "*Status*", "*OperatingMode*"
                },
                ExpectedPointCount = 15,
                Priority = 1
            },
            new PointTemplate
            {
                Id = "met-tower-standard",
                Name = "Met Tower - Standard",
                Description = "Meteorological measurements (wind, temperature, pressure)",
                Industry = "Renewable",
                EquipmentType = "Met Tower",
                PointPatterns = new[]
                {
                    "*WindSpeed*", "*WindDir*", "*AmbientTemp*",
                    "*Humidity*", "*Pressure*", "*Irradiance*", "*GHI*", "*POA*"
                },
                ExpectedPointCount = 10,
                Priority = 1
            },
            new PointTemplate
            {
                Id = "substation-standard",
                Name = "Substation - Standard",
                Description = "Grid interconnection points (power, voltage, frequency)",
                Industry = "Power",
                EquipmentType = "Substation",
                PointPatterns = new[]
                {
                    "*TotalPower*", "*Voltage*", "*Frequency*",
                    "*Breaker*", "*TransformerTemp*"
                },
                ExpectedPointCount = 8,
                Priority = 1
            }
        };

        return Ok(new { templates });
    }

    /// <summary>
    /// Apply a template to select points matching the template patterns.
    /// </summary>
    [HttpPost("templates/{templateId}/apply")]
    public async Task<IActionResult> ApplyTemplate(
        Guid dataSourceId,
        string templateId,
        CancellationToken ct = default)
    {
        // First discover all nodes
        var discoveryResult = await DiscoverPoints(dataSourceId, 10, ct);
        if (discoveryResult is not OkObjectResult okResult || okResult.Value is not DiscoveryResponse response)
            return discoveryResult;

        // Get the template
        var templatesResult = await GetTemplates(dataSourceId, ct);
        if (templatesResult is not OkObjectResult templatesOk)
            return templatesResult;

        // Find matching nodes based on template patterns
        var matchingNodes = response.AllNodes
            .Where(n => !n.IsImported)
            .Where(n => MatchesTemplate(n, templateId))
            .ToList();

        return Ok(new
        {
            templateId,
            matchingCount = matchingNodes.Count,
            nodes = matchingNodes
        });
    }

    /// <summary>
    /// Import selected nodes as NAIA points.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> ImportPoints(
        Guid dataSourceId,
        [FromBody] ImportPointsRequest request,
        CancellationToken ct = default)
    {
        var dataSource = await _dataSourceRepository.GetByIdAsync(dataSourceId, ct);
        if (dataSource == null)
            return NotFound(new { error = "Data source not found" });

        if (request.NodeIds == null || request.NodeIds.Count == 0)
            return BadRequest(new { error = "No nodes selected for import" });

        _logger.LogInformation("Importing {Count} points for data source {Name}",
            request.NodeIds.Count, dataSource.Name);

        // Get point prefix from config or data source
        var pointPrefix = request.PointPrefix ?? GetPointPrefix(dataSource);

        var importedPoints = new List<ImportedPointInfo>();
        var errors = new List<string>();

        foreach (var nodeInfo in request.Nodes ?? new())
        {
            try
            {
                // Generate NAIA point name
                var pointName = GeneratePointName(nodeInfo, pointPrefix);

                // Check if point already exists (search by name with data source scope)
                var existingPoints = await _pointRepository.SearchAsync(
                    tagNamePattern: pointName,
                    dataSourceId: dataSourceId,
                    take: 1,
                    cancellationToken: ct);
                if (existingPoints.Any())
                {
                    errors.Add($"Point already exists: {pointName}");
                    continue;
                }

                // Create point
                var point = Point.Create(
                    name: pointName,
                    dataSourceId: dataSourceId,
                    valueType: MapToValueType(nodeInfo.DataType),
                    description: nodeInfo.SuggestedDescription ?? nodeInfo.Description ?? nodeInfo.DisplayName,
                    engineeringUnits: nodeInfo.SuggestedUnits ?? nodeInfo.EngineeringUnits,
                    sourceAddress: nodeInfo.NodeId);

                await _pointRepository.AddAsync(point, ct);

                importedPoints.Add(new ImportedPointInfo
                {
                    PointId = point.Id,
                    PointSequenceId = point.PointSequenceId ?? 0,
                    PointName = pointName,
                    SourceNodeId = nodeInfo.NodeId,
                    SourcePath = nodeInfo.NodePath
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import node {NodeId}", nodeInfo.NodeId);
                errors.Add($"Failed to import {nodeInfo.NodePath}: {ex.Message}");
            }
        }

        // Save all added points to the database
        if (importedPoints.Count > 0)
        {
            await _pointRepository.SaveChangesAsync(ct);
            _logger.LogInformation("Saved {Count} points to database for {DataSource}", 
                importedPoints.Count, dataSource.Name);
        }

        return Ok(new
        {
            success = true,
            importedCount = importedPoints.Count,
            errorCount = errors.Count,
            importedPoints,
            errors
        });
    }

    /// <summary>
    /// Browse a specific node's children (for tree navigation).
    /// </summary>
    [HttpGet("browse")]
    public async Task<IActionResult> BrowseChildren(
        Guid dataSourceId,
        [FromQuery] string? nodeId = null,
        CancellationToken ct = default)
    {
        var dataSource = await _dataSourceRepository.GetByIdAsync(dataSourceId, ct);
        if (dataSource == null)
            return NotFound(new { error = "Data source not found" });

        var endpointUrl = GetEndpointUrl(dataSource);
        if (string.IsNullOrEmpty(endpointUrl))
            return BadRequest(new { error = "OPC UA endpoint URL not configured" });

        var nodes = await _discoveryService.BrowseChildrenAsync(
            endpointUrl,
            nodeId ?? "i=85", // ObjectsFolder
            ct);

        var enrichedNodes = await EnrichNodesAsync(nodes, dataSource, ct);

        return Ok(new
        {
            parentNodeId = nodeId,
            children = enrichedNodes
        });
    }

    #region Helpers

    private async Task<List<EnrichedDiscoveredNode>> EnrichNodesAsync(
        List<DiscoveredNode> nodes,
        DataSource dataSource,
        CancellationToken ct)
    {
        // Load abbreviations for expansion
        var abbreviations = await _knowledgeRepository.GetAbbreviationsAsync(null, 0, 1000, ct);
        var abbrDict = abbreviations.ToDictionary(
            a => a.Abbreviation.ToUpperInvariant(),
            a => a,
            StringComparer.OrdinalIgnoreCase);

        var enriched = new List<EnrichedDiscoveredNode>();

        foreach (var node in nodes)
        {
            var enrichedNode = new EnrichedDiscoveredNode
            {
                NodeId = node.NodeId,
                BrowseName = node.BrowseName,
                DisplayName = node.DisplayName,
                NodePath = node.NodePath,
                NodeClass = node.NodeClass,
                IsDataNode = node.IsDataNode,
                ParentNodeId = node.ParentNodeId,
                Depth = node.Depth,
                DataType = node.DataType,
                EngineeringUnits = node.EngineeringUnits,
                Description = node.Description,
                CurrentValue = node.CurrentValue
            };

            // Detect equipment type from path
            enrichedNode.DetectedEquipmentType = DetectEquipmentType(node.NodePath);
            enrichedNode.DetectedMeasurementType = DetectMeasurementType(node.BrowseName);

            // Suggest NAIA name
            var prefix = GetPointPrefix(dataSource);
            enrichedNode.SuggestedName = GeneratePointName(node, prefix);

            // Expand abbreviations for description
            enrichedNode.SuggestedDescription = ExpandAbbreviations(node.BrowseName, abbrDict);

            // Detect units from measurement type
            enrichedNode.SuggestedUnits = DetectUnits(node.BrowseName, enrichedNode.DetectedMeasurementType);

            // Calculate confidence score
            enrichedNode.ConfidenceScore = CalculateConfidence(enrichedNode);

            enriched.Add(enrichedNode);
        }

        return enriched;
    }

    private string? GetEndpointUrl(DataSource dataSource)
    {
        if (!string.IsNullOrEmpty(dataSource.ConnectionString))
            return dataSource.ConnectionString;

        if (!string.IsNullOrEmpty(dataSource.ConfigurationJson))
        {
            try
            {
                var config = JsonSerializer.Deserialize<JsonElement>(dataSource.ConfigurationJson);
                if (config.TryGetProperty("endpointUrl", out var url) ||
                    config.TryGetProperty("serverUrl", out url))
                {
                    return url.GetString();
                }
            }
            catch { }
        }

        return null;
    }

    private string GetPointPrefix(DataSource dataSource)
    {
        // Try to get from config
        if (!string.IsNullOrEmpty(dataSource.ConfigurationJson))
        {
            try
            {
                var config = JsonSerializer.Deserialize<JsonElement>(dataSource.ConfigurationJson);
                if (config.TryGetProperty("pointPrefix", out var prefix))
                {
                    return prefix.GetString() ?? "opc";
                }
            }
            catch { }
        }

        // Generate from name
        var name = dataSource.Name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");

        // Take first word or abbreviation
        var parts = name.Split('-');
        return parts.Length > 0 ? parts[0] : "opc";
    }

    private string GeneratePointName(DiscoveredNode node, string prefix)
    {
        // Transform OPC path to NAIA naming convention
        // Example: "GE-2.5-001.Power" -> "naia.thornton.ge-2-5-001.power"
        var path = node.NodePath
            .Replace(".", "-")
            .Replace("_", "-")
            .ToLowerInvariant();

        // Remove special characters
        path = Regex.Replace(path, @"[^a-z0-9\-]", "");

        // Clean up multiple dashes
        path = Regex.Replace(path, @"-+", "-").Trim('-');

        return $"naia.{prefix}.{path}";
    }

    private string GeneratePointName(NodeImportInfo node, string prefix)
    {
        if (!string.IsNullOrEmpty(node.CustomName))
            return node.CustomName;

        return GeneratePointName(new DiscoveredNode
        {
            NodeId = node.NodeId,
            NodePath = node.NodePath,
            BrowseName = node.BrowseName ?? "",
            DisplayName = node.DisplayName ?? ""
        }, prefix);
    }

    private string DetectEquipmentType(string nodePath)
    {
        var pathLower = nodePath.ToLowerInvariant();

        if (pathLower.Contains("ge-2.5") || pathLower.Contains("v110") || 
            pathLower.Contains("turbine") || pathLower.Contains("wtg"))
            return "Wind Turbine";

        if (pathLower.Contains("sma") || pathLower.Contains("inverter"))
            return "Solar Inverter";

        if (pathLower.Contains("trk") || pathLower.Contains("tracker"))
            return "Solar Tracker";

        if (pathLower.Contains("tesla") || pathLower.Contains("byd") || 
            pathLower.Contains("bank") || pathLower.Contains("bess"))
            return "Battery Storage";

        if (pathLower.Contains("pcs"))
            return "Power Conversion";

        if (pathLower.Contains("met") || pathLower.Contains("weather"))
            return "Met Tower";

        if (pathLower.Contains("sub") || pathLower.Contains("poi"))
            return "Substation";

        if (pathLower.Contains("node"))
            return "Collection Node";

        return "Unknown";
    }

    private string? DetectMeasurementType(string browseName)
    {
        var nameLower = browseName.ToLowerInvariant();

        if (nameLower.Contains("power") || nameLower.Contains("pwr"))
            return "Power";
        if (nameLower.Contains("voltage") || nameLower.Contains("volt"))
            return "Voltage";
        if (nameLower.Contains("current") || nameLower.Contains("amp"))
            return "Current";
        if (nameLower.Contains("temp") || nameLower.Contains("temperature"))
            return "Temperature";
        if (nameLower.Contains("speed") || nameLower.Contains("rpm"))
            return "Speed";
        if (nameLower.Contains("freq"))
            return "Frequency";
        if (nameLower.Contains("soc") || nameLower.Contains("charge"))
            return "State of Charge";
        if (nameLower.Contains("irrad") || nameLower.Contains("ghi") || nameLower.Contains("poa"))
            return "Irradiance";
        if (nameLower.Contains("status") || nameLower.Contains("state"))
            return "Status";
        if (nameLower.Contains("angle") || nameLower.Contains("pitch") || nameLower.Contains("yaw"))
            return "Angle";
        if (nameLower.Contains("energy") || nameLower.Contains("kwh") || nameLower.Contains("mwh"))
            return "Energy";

        return null;
    }

    private string? DetectUnits(string browseName, string? measurementType)
    {
        return measurementType switch
        {
            "Power" => browseName.Contains("MW") ? "MW" : "kW",
            "Voltage" => browseName.Contains("kV") ? "kV" : "V",
            "Current" => "A",
            "Temperature" => "°C",
            "Speed" when browseName.Contains("Wind") => "m/s",
            "Speed" => "RPM",
            "Frequency" => "Hz",
            "State of Charge" => "%",
            "Irradiance" => "W/m²",
            "Angle" => "°",
            "Energy" => browseName.Contains("MWh") ? "MWh" : "kWh",
            _ => null
        };
    }

    private string ExpandAbbreviations(string name, Dictionary<string, AbbreviationDto> abbreviations)
    {
        var result = name;
        var words = Regex.Split(name, @"([A-Z][a-z]+|[A-Z]+(?=[A-Z][a-z])|[^A-Za-z]+)");

        foreach (var word in words.Where(w => !string.IsNullOrEmpty(w)))
        {
            if (abbreviations.TryGetValue(word.ToUpperInvariant(), out var abbr))
            {
                result = result.Replace(word, abbr.Expansion);
            }
        }

        return result;
    }

    private double CalculateConfidence(EnrichedDiscoveredNode node)
    {
        double score = 0.5; // Base confidence

        if (!string.IsNullOrEmpty(node.DetectedEquipmentType) && node.DetectedEquipmentType != "Unknown")
            score += 0.2;

        if (!string.IsNullOrEmpty(node.DetectedMeasurementType))
            score += 0.15;

        if (!string.IsNullOrEmpty(node.SuggestedUnits))
            score += 0.1;

        if (!string.IsNullOrEmpty(node.Description))
            score += 0.05;

        return Math.Min(1.0, score);
    }

    private bool MatchesTemplate(EnrichedDiscoveredNode node, string templateId)
    {
        // Simple pattern matching for templates
        return templateId switch
        {
            "wind-turbine-standard" => 
                node.DetectedEquipmentType == "Wind Turbine" &&
                (node.DetectedMeasurementType is "Power" or "Speed" or "Temperature" or "Status" or "Angle"),
            
            "wind-turbine-full" =>
                node.DetectedEquipmentType == "Wind Turbine",
            
            "solar-inverter-standard" =>
                node.DetectedEquipmentType == "Solar Inverter" &&
                (node.DetectedMeasurementType is "Power" or "Voltage" or "Current" or "Status" or "Temperature"),
            
            "bess-standard" =>
                (node.DetectedEquipmentType == "Battery Storage" || node.DetectedEquipmentType == "Power Conversion") &&
                (node.DetectedMeasurementType is "State of Charge" or "Power" or "Voltage" or "Current" or "Temperature" or "Status"),
            
            "met-tower-standard" =>
                node.DetectedEquipmentType == "Met Tower",
            
            "substation-standard" =>
                node.DetectedEquipmentType == "Substation",
            
            _ => false
        };
    }

    private static PointValueType MapToValueType(string? dataType)
    {
        return dataType?.ToLowerInvariant() switch
        {
            "double" or "float" => PointValueType.Float64,
            "int32" or "int" or "integer" => PointValueType.Int32,
            "int64" or "long" => PointValueType.Int64,
            "boolean" or "bool" => PointValueType.Boolean,
            "string" => PointValueType.String,
            _ => PointValueType.Float64
        };
    }

    #endregion
}

#region DTOs

public class DiscoveryResponse
{
    public Guid DataSourceId { get; init; }
    public required string DataSourceName { get; init; }
    public required string EndpointUrl { get; init; }
    public DateTime DiscoveredAt { get; init; }
    public OpcUaServerInfo? ServerInfo { get; init; }
    public int TotalNodes { get; init; }
    public int DataNodes { get; init; }
    public int ImportedCount { get; init; }
    public List<EquipmentGroup> EquipmentGroups { get; init; } = new();
    public List<EnrichedDiscoveredNode> AllNodes { get; init; } = new();
}

public class EquipmentGroup
{
    public required string EquipmentType { get; init; }
    public int NodeCount { get; init; }
    public int ImportedCount { get; init; }
    public List<EnrichedDiscoveredNode> Nodes { get; init; } = new();
}

public class EnrichedDiscoveredNode
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
    
    // Enrichment
    public string? DetectedEquipmentType { get; set; }
    public string? DetectedMeasurementType { get; set; }
    public double ConfidenceScore { get; set; }
    public string? SuggestedName { get; set; }
    public string? SuggestedDescription { get; set; }
    public string? SuggestedUnits { get; set; }
    public bool IsImported { get; set; }
}

public class PointTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Industry { get; init; }
    public required string EquipmentType { get; init; }
    public string[] PointPatterns { get; init; } = Array.Empty<string>();
    public int ExpectedPointCount { get; init; }
    public int Priority { get; init; }
}

public class ImportPointsRequest
{
    public List<string>? NodeIds { get; set; }
    public List<NodeImportInfo>? Nodes { get; set; }
    public string? PointPrefix { get; set; }
}

public class NodeImportInfo
{
    public required string NodeId { get; init; }
    public required string NodePath { get; init; }
    public string? BrowseName { get; set; }
    public string? DisplayName { get; set; }
    public string? DataType { get; set; }
    public string? Description { get; set; }
    public string? EngineeringUnits { get; set; }
    public string? SuggestedDescription { get; set; }
    public string? SuggestedUnits { get; set; }
    public string? CustomName { get; set; }
}

public class ImportedPointInfo
{
    public Guid PointId { get; init; }
    public long PointSequenceId { get; init; }
    public required string PointName { get; init; }
    public required string SourceNodeId { get; init; }
    public required string SourcePath { get; init; }
}

#endregion
