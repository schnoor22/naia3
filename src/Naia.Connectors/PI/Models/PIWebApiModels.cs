using System.Text.Json.Serialization;

namespace Naia.Connectors.PI.Models;

/// <summary>
/// PI Web API System information response.
/// </summary>
public sealed class PIWebApiSystemInfo
{
    [JsonPropertyName("Links")]
    public PIWebApiLinks? Links { get; set; }
    
    [JsonPropertyName("ProductTitle")]
    public string? ProductTitle { get; set; }
    
    [JsonPropertyName("ProductVersion")]
    public string? ProductVersion { get; set; }
}

public sealed class PIWebApiLinks
{
    [JsonPropertyName("Self")]
    public string? Self { get; set; }
    
    [JsonPropertyName("DataServers")]
    public string? DataServers { get; set; }
    
    [JsonPropertyName("AssetServers")]
    public string? AssetServers { get; set; }
}

/// <summary>
/// PI Data Archive server information.
/// </summary>
public sealed class PIDataServer
{
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("Id")]
    public Guid Id { get; set; }
    
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Path")]
    public string? Path { get; set; }
    
    [JsonPropertyName("IsConnected")]
    public bool IsConnected { get; set; }
    
    [JsonPropertyName("ServerVersion")]
    public string? ServerVersion { get; set; }
    
    [JsonPropertyName("ServerTime")]
    public DateTime? ServerTime { get; set; }
    
    [JsonPropertyName("Links")]
    public PIDataServerLinks? Links { get; set; }
}

public sealed class PIDataServerLinks
{
    [JsonPropertyName("Self")]
    public string? Self { get; set; }
    
    [JsonPropertyName("Points")]
    public string? Points { get; set; }
}

/// <summary>
/// Collection response containing multiple items.
/// </summary>
public sealed class PIWebApiItemsResponse<T>
{
    [JsonPropertyName("Links")]
    public PIWebApiResponseLinks? Links { get; set; }
    
    [JsonPropertyName("Items")]
    public List<T>? Items { get; set; }
}

public sealed class PIWebApiResponseLinks
{
    [JsonPropertyName("Self")]
    public string? Self { get; set; }
    
    [JsonPropertyName("First")]
    public string? First { get; set; }
    
    [JsonPropertyName("Last")]
    public string? Last { get; set; }
    
    [JsonPropertyName("Next")]
    public string? Next { get; set; }
    
    [JsonPropertyName("Previous")]
    public string? Previous { get; set; }
}

/// <summary>
/// PI Point definition.
/// </summary>
public sealed class PIPoint
{
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("Id")]
    public int Id { get; set; }
    
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Path")]
    public string? Path { get; set; }
    
    [JsonPropertyName("Descriptor")]
    public string? Descriptor { get; set; }
    
    [JsonPropertyName("PointClass")]
    public string? PointClass { get; set; }
    
    [JsonPropertyName("PointType")]
    public string? PointType { get; set; }
    
    [JsonPropertyName("EngineeringUnits")]
    public string? EngineeringUnits { get; set; }
    
    [JsonPropertyName("Span")]
    public double? Span { get; set; }
    
    [JsonPropertyName("Zero")]
    public double? Zero { get; set; }
    
    [JsonPropertyName("DisplayDigits")]
    public int? DisplayDigits { get; set; }
    
    [JsonPropertyName("Future")]
    public bool Future { get; set; }
    
    [JsonPropertyName("Step")]
    public bool Step { get; set; }
    
    [JsonPropertyName("Links")]
    public PIPointLinks? Links { get; set; }
}

public sealed class PIPointLinks
{
    [JsonPropertyName("Self")]
    public string? Self { get; set; }
    
    [JsonPropertyName("DataServer")]
    public string? DataServer { get; set; }
    
    [JsonPropertyName("Attributes")]
    public string? Attributes { get; set; }
    
    [JsonPropertyName("Value")]
    public string? Value { get; set; }
    
    [JsonPropertyName("RecordedData")]
    public string? RecordedData { get; set; }
    
    [JsonPropertyName("PlotData")]
    public string? PlotData { get; set; }
    
    [JsonPropertyName("SummaryData")]
    public string? SummaryData { get; set; }
    
    [JsonPropertyName("InterpolatedData")]
    public string? InterpolatedData { get; set; }
}

/// <summary>
/// A single timestamped value from PI.
/// </summary>
public sealed class PITimedValue
{
    [JsonPropertyName("Timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("Value")]
    public object? Value { get; set; }
    
    [JsonPropertyName("UnitsAbbreviation")]
    public string? UnitsAbbreviation { get; set; }
    
    [JsonPropertyName("Good")]
    public bool Good { get; set; }
    
    [JsonPropertyName("Questionable")]
    public bool Questionable { get; set; }
    
    [JsonPropertyName("Substituted")]
    public bool Substituted { get; set; }
    
    [JsonPropertyName("Annotated")]
    public bool Annotated { get; set; }
}

/// <summary>
/// Stream value response (current value).
/// </summary>
public sealed class PIStreamValue
{
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Path")]
    public string? Path { get; set; }
    
    [JsonPropertyName("Value")]
    public PITimedValue? Value { get; set; }
    
    [JsonPropertyName("Links")]
    public PIStreamLinks? Links { get; set; }
}

public sealed class PIStreamLinks
{
    [JsonPropertyName("Self")]
    public string? Self { get; set; }
    
    [JsonPropertyName("Source")]
    public string? Source { get; set; }
}

/// <summary>
/// Batch stream values response.
/// </summary>
public sealed class PIStreamSetValue
{
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Path")]
    public string? Path { get; set; }
    
    [JsonPropertyName("Items")]
    public List<PITimedValue>? Items { get; set; }
    
    [JsonPropertyName("UnitsAbbreviation")]
    public string? UnitsAbbreviation { get; set; }
}

/// <summary>
/// Extended point attributes response.
/// </summary>
public sealed class PIPointAttributes
{
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Items")]
    public Dictionary<string, PIPointAttribute>? Items { get; set; }
}

public sealed class PIPointAttribute
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Value")]
    public object? Value { get; set; }
}

/// <summary>
/// AF Server information.
/// </summary>
public sealed class PIAssetServer
{
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("Id")]
    public Guid Id { get; set; }
    
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("Path")]
    public string? Path { get; set; }
    
    [JsonPropertyName("IsConnected")]
    public bool IsConnected { get; set; }
    
    [JsonPropertyName("ServerVersion")]
    public string? ServerVersion { get; set; }
    
    [JsonPropertyName("Links")]
    public PIAssetServerLinks? Links { get; set; }
}

public sealed class PIAssetServerLinks
{
    [JsonPropertyName("Self")]
    public string? Self { get; set; }
    
    [JsonPropertyName("Databases")]
    public string? Databases { get; set; }
}

/// <summary>
/// AF Database information.
/// </summary>
public sealed class PIAssetDatabase
{
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("Id")]
    public Guid Id { get; set; }
    
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("Path")]
    public string? Path { get; set; }
    
    [JsonPropertyName("Links")]
    public PIAssetDatabaseLinks? Links { get; set; }
}

public sealed class PIAssetDatabaseLinks
{
    [JsonPropertyName("Self")]
    public string? Self { get; set; }
    
    [JsonPropertyName("Elements")]
    public string? Elements { get; set; }
    
    [JsonPropertyName("ElementTemplates")]
    public string? ElementTemplates { get; set; }
}

/// <summary>
/// AF Element (asset) information.
/// </summary>
public sealed class PIElement
{
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("Id")]
    public Guid Id { get; set; }
    
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("Path")]
    public string? Path { get; set; }
    
    [JsonPropertyName("TemplateName")]
    public string? TemplateName { get; set; }
    
    [JsonPropertyName("HasChildren")]
    public bool HasChildren { get; set; }
    
    [JsonPropertyName("Links")]
    public PIElementLinks? Links { get; set; }
}

public sealed class PIElementLinks
{
    [JsonPropertyName("Self")]
    public string? Self { get; set; }
    
    [JsonPropertyName("Attributes")]
    public string? Attributes { get; set; }
    
    [JsonPropertyName("Elements")]
    public string? Elements { get; set; }
    
    [JsonPropertyName("Parent")]
    public string? Parent { get; set; }
    
    [JsonPropertyName("Database")]
    public string? Database { get; set; }
}

/// <summary>
/// AF Attribute (element property with data reference).
/// </summary>
public sealed class PIAttribute
{
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("Id")]
    public Guid Id { get; set; }
    
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("Path")]
    public string? Path { get; set; }
    
    [JsonPropertyName("Type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("TypeQualifier")]
    public string? TypeQualifier { get; set; }
    
    [JsonPropertyName("DefaultUnitsName")]
    public string? DefaultUnitsName { get; set; }
    
    [JsonPropertyName("DefaultValue")]
    public object? DefaultValue { get; set; }
    
    [JsonPropertyName("DataReferencePlugIn")]
    public string? DataReferencePlugIn { get; set; }
    
    [JsonPropertyName("ConfigString")]
    public string? ConfigString { get; set; }
    
    [JsonPropertyName("HasChildren")]
    public bool HasChildren { get; set; }
    
    [JsonPropertyName("Links")]
    public PIAttributeLinks? Links { get; set; }
}

public sealed class PIAttributeLinks
{
    [JsonPropertyName("Self")]
    public string? Self { get; set; }
    
    [JsonPropertyName("Value")]
    public string? Value { get; set; }
    
    [JsonPropertyName("Element")]
    public string? Element { get; set; }
    
    [JsonPropertyName("Point")]
    public string? Point { get; set; }
}

/// <summary>
/// Search results from PI Web API.
/// </summary>
public sealed class PISearchResults
{
    [JsonPropertyName("TotalHits")]
    public int TotalHits { get; set; }
    
    [JsonPropertyName("Items")]
    public List<PISearchItem>? Items { get; set; }
    
    [JsonPropertyName("Links")]
    public PIWebApiResponseLinks? Links { get; set; }
}

public sealed class PISearchItem
{
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("Path")]
    public string? Path { get; set; }
    
    [JsonPropertyName("ItemType")]
    public string? ItemType { get; set; }
    
    [JsonPropertyName("Links")]
    public Dictionary<string, string>? Links { get; set; }
}

/// <summary>
/// PI Web API search/query response.
/// </summary>
public sealed class PIWebApiSearchResponse
{
    [JsonPropertyName("TotalHits")]
    public int? TotalHits { get; set; }
    
    [JsonPropertyName("Items")]
    public List<PISearchResult>? Items { get; set; }
    
    [JsonPropertyName("Errors")]
    public List<PISearchError>? Errors { get; set; }
    
    [JsonPropertyName("Links")]
    public PIWebApiResponseLinks? Links { get; set; }
}

/// <summary>
/// A single search result from PI Web API search/query endpoint.
/// </summary>
public sealed class PISearchResult
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("Description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("ItemType")]
    public string? ItemType { get; set; }
    
    [JsonPropertyName("DataType")]
    public string? DataType { get; set; }
    
    [JsonPropertyName("UoM")]
    public string? UoM { get; set; }
    
    [JsonPropertyName("WebId")]
    public string? WebId { get; set; }
    
    [JsonPropertyName("UniqueID")]
    public string? UniqueID { get; set; }
    
    [JsonPropertyName("Score")]
    public double? Score { get; set; }
    
    [JsonPropertyName("Links")]
    public Dictionary<string, string>? Links { get; set; }
}

public sealed class PISearchError
{
    [JsonPropertyName("Message")]
    public string? Message { get; set; }
    
    [JsonPropertyName("Status")]
    public int Status { get; set; }
}
