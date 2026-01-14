namespace Naia.Application.Abstractions;

/// <summary>
/// Repository for managing Knowledge Base seeds (industry standards, abbreviations, etc.).
/// Used by the "Seed Master" role to maintain the semantic intelligence layer.
/// </summary>
public interface IKnowledgeBaseRepository
{
    // ============================================================================
    // Abbreviations
    // ============================================================================
    
    /// <summary>Get all abbreviations, optionally filtered by context.</summary>
    Task<IReadOnlyList<AbbreviationDto>> GetAbbreviationsAsync(
        string? context = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default);
    
    /// <summary>Search abbreviations by pattern or expansion text.</summary>
    Task<IReadOnlyList<AbbreviationDto>> SearchAbbreviationsAsync(
        string searchTerm,
        CancellationToken ct = default);
    
    /// <summary>Get a single abbreviation by ID.</summary>
    Task<AbbreviationDto?> GetAbbreviationByIdAsync(int id, CancellationToken ct = default);
    
    /// <summary>Create a new abbreviation.</summary>
    Task<int> CreateAbbreviationAsync(CreateAbbreviationRequest request, CancellationToken ct = default);
    
    /// <summary>Update an existing abbreviation.</summary>
    Task<bool> UpdateAbbreviationAsync(int id, UpdateAbbreviationRequest request, CancellationToken ct = default);
    
    /// <summary>Delete an abbreviation.</summary>
    Task<bool> DeleteAbbreviationAsync(int id, CancellationToken ct = default);
    
    // ============================================================================
    // Industry Standards
    // ============================================================================
    
    /// <summary>Get all industry standards.</summary>
    Task<IReadOnlyList<IndustryStandardDto>> GetStandardsAsync(CancellationToken ct = default);
    
    /// <summary>Get a standard by ID.</summary>
    Task<IndustryStandardDto?> GetStandardByIdAsync(int id, CancellationToken ct = default);
    
    /// <summary>Create a new standard.</summary>
    Task<int> CreateStandardAsync(CreateStandardRequest request, CancellationToken ct = default);
    
    /// <summary>Update an existing standard.</summary>
    Task<bool> UpdateStandardAsync(int id, UpdateStandardRequest request, CancellationToken ct = default);
    
    /// <summary>Delete a standard.</summary>
    Task<bool> DeleteStandardAsync(int id, CancellationToken ct = default);
    
    // ============================================================================
    // Measurement Types
    // ============================================================================
    
    /// <summary>Get all measurement types.</summary>
    Task<IReadOnlyList<MeasurementTypeDto>> GetMeasurementTypesAsync(CancellationToken ct = default);
    
    /// <summary>Get a measurement type by ID.</summary>
    Task<MeasurementTypeDto?> GetMeasurementTypeByIdAsync(int id, CancellationToken ct = default);
    
    /// <summary>Create a new measurement type.</summary>
    Task<int> CreateMeasurementTypeAsync(CreateMeasurementTypeRequest request, CancellationToken ct = default);
    
    /// <summary>Update a measurement type.</summary>
    Task<bool> UpdateMeasurementTypeAsync(int id, UpdateMeasurementTypeRequest request, CancellationToken ct = default);
    
    /// <summary>Delete a measurement type.</summary>
    Task<bool> DeleteMeasurementTypeAsync(int id, CancellationToken ct = default);
    
    // ============================================================================
    // Unit Mappings
    // ============================================================================
    
    /// <summary>Get all unit mappings for a measurement type.</summary>
    Task<IReadOnlyList<UnitMappingDto>> GetUnitMappingsAsync(
        int? measurementTypeId = null,
        CancellationToken ct = default);
    
    /// <summary>Create a new unit mapping.</summary>
    Task<int> CreateUnitMappingAsync(CreateUnitMappingRequest request, CancellationToken ct = default);
    
    /// <summary>Delete a unit mapping.</summary>
    Task<bool> DeleteUnitMappingAsync(int id, CancellationToken ct = default);
    
    // ============================================================================
    // Equipment Taxonomy
    // ============================================================================
    
    /// <summary>Get equipment taxonomy as a tree structure.</summary>
    Task<IReadOnlyList<EquipmentTaxonomyDto>> GetEquipmentTaxonomyAsync(
        int? parentId = null,
        CancellationToken ct = default);
    
    /// <summary>Get a taxonomy node by ID.</summary>
    Task<EquipmentTaxonomyDto?> GetEquipmentTaxonomyByIdAsync(int id, CancellationToken ct = default);
    
    /// <summary>Create a new equipment taxonomy node.</summary>
    Task<int> CreateEquipmentTaxonomyAsync(CreateEquipmentTaxonomyRequest request, CancellationToken ct = default);
    
    /// <summary>Update an equipment taxonomy node.</summary>
    Task<bool> UpdateEquipmentTaxonomyAsync(int id, UpdateEquipmentTaxonomyRequest request, CancellationToken ct = default);
    
    /// <summary>Delete an equipment taxonomy node.</summary>
    Task<bool> DeleteEquipmentTaxonomyAsync(int id, CancellationToken ct = default);
    
    // ============================================================================
    // Manufacturer Profiles
    // ============================================================================
    
    /// <summary>Get all manufacturer profiles.</summary>
    Task<IReadOnlyList<ManufacturerProfileDto>> GetManufacturersAsync(CancellationToken ct = default);
    
    /// <summary>Get a manufacturer profile by ID.</summary>
    Task<ManufacturerProfileDto?> GetManufacturerByIdAsync(int id, CancellationToken ct = default);
    
    /// <summary>Create a new manufacturer profile.</summary>
    Task<int> CreateManufacturerAsync(CreateManufacturerRequest request, CancellationToken ct = default);
    
    /// <summary>Update a manufacturer profile.</summary>
    Task<bool> UpdateManufacturerAsync(int id, UpdateManufacturerRequest request, CancellationToken ct = default);
    
    /// <summary>Delete a manufacturer profile.</summary>
    Task<bool> DeleteManufacturerAsync(int id, CancellationToken ct = default);
    
    // ============================================================================
    // Synonyms
    // ============================================================================
    
    /// <summary>Get all synonyms for a term.</summary>
    Task<IReadOnlyList<SynonymDto>> GetSynonymsAsync(
        string? canonicalTerm = null,
        CancellationToken ct = default);
    
    /// <summary>Create a new synonym.</summary>
    Task<int> CreateSynonymAsync(CreateSynonymRequest request, CancellationToken ct = default);
    
    /// <summary>Delete a synonym.</summary>
    Task<bool> DeleteSynonymAsync(int id, CancellationToken ct = default);
    
    // ============================================================================
    // Statistics & Overview
    // ============================================================================
    
    /// <summary>Get knowledge base statistics.</summary>
    Task<KnowledgeBaseStatsDto> GetStatsAsync(CancellationToken ct = default);
    
    /// <summary>Get seed effectiveness report.</summary>
    Task<IReadOnlyList<SeedEffectivenessDto>> GetSeedEffectivenessAsync(
        int topN = 50,
        CancellationToken ct = default);
    
    /// <summary>Get audit log for seed changes.</summary>
    Task<IReadOnlyList<SeedAuditLogDto>> GetAuditLogAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default);
}

// ============================================================================
// DTOs - Abbreviations
// ============================================================================

public record AbbreviationDto
{
    public required int Id { get; init; }
    public required string Abbreviation { get; init; }
    public required string Expansion { get; init; }
    public required string Context { get; init; }
    public string? MeasurementType { get; init; }
    public required int Priority { get; init; }
    public required bool IsActive { get; init; }
    public string? Notes { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public record CreateAbbreviationRequest
{
    public required string Abbreviation { get; init; }
    public required string Expansion { get; init; }
    public string Context { get; init; } = "General";
    public string? MeasurementType { get; init; }
    public int Priority { get; init; } = 100;
    public string? Notes { get; init; }
}

public record UpdateAbbreviationRequest
{
    public string? Expansion { get; init; }
    public string? Context { get; init; }
    public string? MeasurementType { get; init; }
    public int? Priority { get; init; }
    public bool? IsActive { get; init; }
    public string? Notes { get; init; }
}

// ============================================================================
// DTOs - Industry Standards
// ============================================================================

public record IndustryStandardDto
{
    public required int Id { get; init; }
    public required string StandardCode { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Industry { get; init; }
    public string? Version { get; init; }
    public string? DocumentUrl { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public record CreateStandardRequest
{
    public required string StandardCode { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Industry { get; init; }
    public string? Version { get; init; }
    public string? DocumentUrl { get; init; }
}

public record UpdateStandardRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Industry { get; init; }
    public string? Version { get; init; }
    public string? DocumentUrl { get; init; }
    public bool? IsActive { get; init; }
}

// ============================================================================
// DTOs - Measurement Types
// ============================================================================

public record MeasurementTypeDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string BaseUnit { get; init; }
    public double? TypicalMin { get; init; }
    public double? TypicalMax { get; init; }
    public string? Description { get; init; }
    public required bool IsActive { get; init; }
    public required List<string> Units { get; init; } // Associated unit symbols
}

public record CreateMeasurementTypeRequest
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string BaseUnit { get; init; }
    public double? TypicalMin { get; init; }
    public double? TypicalMax { get; init; }
    public string? Description { get; init; }
}

public record UpdateMeasurementTypeRequest
{
    public string? Name { get; init; }
    public string? Category { get; init; }
    public string? BaseUnit { get; init; }
    public double? TypicalMin { get; init; }
    public double? TypicalMax { get; init; }
    public string? Description { get; init; }
    public bool? IsActive { get; init; }
}

// ============================================================================
// DTOs - Unit Mappings
// ============================================================================

public record UnitMappingDto
{
    public required int Id { get; init; }
    public required string UnitSymbol { get; init; }
    public required string UnitName { get; init; }
    public required int MeasurementTypeId { get; init; }
    public required string MeasurementTypeName { get; init; }
    public double? ConversionFactor { get; init; }
    public required bool IsBaseUnit { get; init; }
}

public record CreateUnitMappingRequest
{
    public required string UnitSymbol { get; init; }
    public required string UnitName { get; init; }
    public required int MeasurementTypeId { get; init; }
    public double? ConversionFactor { get; init; }
    public bool IsBaseUnit { get; init; } = false;
}

// ============================================================================
// DTOs - Equipment Taxonomy
// ============================================================================

public record EquipmentTaxonomyDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public int? ParentId { get; init; }
    public string? ParentName { get; init; }
    public required int Level { get; init; }
    public required string Path { get; init; } // e.g., "Rotating Equipment > Pump > Centrifugal"
    public string? Description { get; init; }
    public string? Industry { get; init; }
    public required bool IsActive { get; init; }
    public int ChildCount { get; init; }
}

public record CreateEquipmentTaxonomyRequest
{
    public required string Name { get; init; }
    public int? ParentId { get; init; }
    public string? Description { get; init; }
    public string? Industry { get; init; }
}

public record UpdateEquipmentTaxonomyRequest
{
    public string? Name { get; init; }
    public int? ParentId { get; init; }
    public string? Description { get; init; }
    public string? Industry { get; init; }
    public bool? IsActive { get; init; }
}

// ============================================================================
// DTOs - Manufacturer Profiles
// ============================================================================

public record ManufacturerProfileDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Alias { get; init; }
    public required string Industry { get; init; }
    public required List<string> TagPrefixes { get; init; }
    public required List<string> NamingPatterns { get; init; }
    public string? Notes { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public record CreateManufacturerRequest
{
    public required string Name { get; init; }
    public string? Alias { get; init; }
    public required string Industry { get; init; }
    public List<string>? TagPrefixes { get; init; }
    public List<string>? NamingPatterns { get; init; }
    public string? Notes { get; init; }
}

public record UpdateManufacturerRequest
{
    public string? Name { get; init; }
    public string? Alias { get; init; }
    public string? Industry { get; init; }
    public List<string>? TagPrefixes { get; init; }
    public List<string>? NamingPatterns { get; init; }
    public string? Notes { get; init; }
    public bool? IsActive { get; init; }
}

// ============================================================================
// DTOs - Synonyms
// ============================================================================

public record SynonymDto
{
    public required int Id { get; init; }
    public required string CanonicalTerm { get; init; }
    public required string Synonym { get; init; }
    public required string Context { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public record CreateSynonymRequest
{
    public required string CanonicalTerm { get; init; }
    public required string Synonym { get; init; }
    public string Context { get; init; } = "General";
}

// ============================================================================
// DTOs - Statistics & Audit
// ============================================================================

public record KnowledgeBaseStatsDto
{
    public required int TotalAbbreviations { get; init; }
    public required int TotalStandards { get; init; }
    public required int TotalMeasurementTypes { get; init; }
    public required int TotalUnitMappings { get; init; }
    public required int TotalEquipmentTypes { get; init; }
    public required int TotalManufacturers { get; init; }
    public required int TotalSynonyms { get; init; }
    public required int ActiveAbbreviations { get; init; }
    public required DateTime LastUpdatedAt { get; init; }
    public required Dictionary<string, int> AbbreviationsByContext { get; init; }
    public required Dictionary<string, int> StandardsByIndustry { get; init; }
}

public record SeedEffectivenessDto
{
    public required string SeedType { get; init; }
    public required int SeedId { get; init; }
    public required string SeedValue { get; init; }
    public required int TimesUsed { get; init; }
    public required int SuccessfulMatches { get; init; }
    public required double SuccessRate { get; init; }
    public required DateTime LastUsedAt { get; init; }
}

public record SeedAuditLogDto
{
    public required int Id { get; init; }
    public required string TableName { get; init; }
    public required int RecordId { get; init; }
    public required string Action { get; init; } // INSERT, UPDATE, DELETE
    public required string OldValue { get; init; }
    public required string NewValue { get; init; }
    public required string ChangedBy { get; init; }
    public required DateTime ChangedAt { get; init; }
    public string? ChangeReason { get; init; }
}
