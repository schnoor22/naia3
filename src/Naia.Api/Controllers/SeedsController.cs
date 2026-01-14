using Microsoft.AspNetCore.Mvc;
using Naia.Application.Abstractions;

namespace Naia.Api.Controllers;

/// <summary>
/// API endpoints for managing Knowledge Base seeds.
/// Used by "Seed Master" role to maintain the semantic intelligence layer.
/// 
/// The Knowledge Base powers proactive pattern matching, enabling NAIA
/// to understand points immediately without waiting for behavioral data.
/// </summary>
[ApiController]
[Route("api/seeds")]
public class SeedsController : ControllerBase
{
    private readonly ILogger<SeedsController> _logger;
    private readonly IKnowledgeBaseRepository _repo;

    public SeedsController(
        ILogger<SeedsController> logger,
        IKnowledgeBaseRepository knowledgeBaseRepository)
    {
        _logger = logger;
        _repo = knowledgeBaseRepository;
    }

    // ============================================================================
    // Overview & Statistics
    // ============================================================================

    /// <summary>
    /// Get Knowledge Base statistics - counts, contexts, and last update times.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<KnowledgeBaseStatsDto>> GetStats(CancellationToken ct = default)
    {
        var stats = await _repo.GetStatsAsync(ct);
        return Ok(stats);
    }

    /// <summary>
    /// Get seed effectiveness report showing which seeds are most useful.
    /// </summary>
    [HttpGet("effectiveness")]
    public async Task<ActionResult<IReadOnlyList<SeedEffectivenessDto>>> GetEffectiveness(
        [FromQuery] int topN = 50,
        CancellationToken ct = default)
    {
        var effectiveness = await _repo.GetSeedEffectivenessAsync(topN, ct);
        return Ok(effectiveness);
    }

    /// <summary>
    /// Get audit log of seed changes.
    /// </summary>
    [HttpGet("audit")]
    public async Task<ActionResult<IReadOnlyList<SeedAuditLogDto>>> GetAuditLog(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        var logs = await _repo.GetAuditLogAsync(skip, take, ct);
        return Ok(logs);
    }

    // ============================================================================
    // Abbreviations
    // ============================================================================

    /// <summary>
    /// Get all abbreviations, optionally filtered by context (Wind, Solar, General, etc.).
    /// </summary>
    [HttpGet("abbreviations")]
    public async Task<ActionResult<IReadOnlyList<AbbreviationDto>>> GetAbbreviations(
        [FromQuery] string? context = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        var abbreviations = await _repo.GetAbbreviationsAsync(context, skip, take, ct);
        return Ok(abbreviations);
    }

    /// <summary>
    /// Search abbreviations by pattern or expansion text.
    /// </summary>
    [HttpGet("abbreviations/search")]
    public async Task<ActionResult<IReadOnlyList<AbbreviationDto>>> SearchAbbreviations(
        [FromQuery] string q,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Search query is required");

        var results = await _repo.SearchAbbreviationsAsync(q, ct);
        return Ok(results);
    }

    /// <summary>
    /// Get a single abbreviation by ID.
    /// </summary>
    [HttpGet("abbreviations/{id:int}")]
    public async Task<ActionResult<AbbreviationDto>> GetAbbreviation(int id, CancellationToken ct = default)
    {
        var abbr = await _repo.GetAbbreviationByIdAsync(id, ct);
        if (abbr is null)
            return NotFound();

        return Ok(abbr);
    }

    /// <summary>
    /// Create a new abbreviation.
    /// </summary>
    [HttpPost("abbreviations")]
    public async Task<ActionResult<AbbreviationDto>> CreateAbbreviation(
        [FromBody] CreateAbbreviationRequest request,
        CancellationToken ct = default)
    {
        var id = await _repo.CreateAbbreviationAsync(request, ct);
        var abbr = await _repo.GetAbbreviationByIdAsync(id, ct);
        
        _logger.LogInformation("Created abbreviation: {Abbr} -> {Exp}", 
            request.Abbreviation, request.Expansion);
        
        return CreatedAtAction(nameof(GetAbbreviation), new { id }, abbr);
    }

    /// <summary>
    /// Update an existing abbreviation.
    /// </summary>
    [HttpPatch("abbreviations/{id:int}")]
    public async Task<ActionResult<AbbreviationDto>> UpdateAbbreviation(
        int id,
        [FromBody] UpdateAbbreviationRequest request,
        CancellationToken ct = default)
    {
        var updated = await _repo.UpdateAbbreviationAsync(id, request, ct);
        if (!updated)
            return NotFound();

        var abbr = await _repo.GetAbbreviationByIdAsync(id, ct);
        return Ok(abbr);
    }

    /// <summary>
    /// Delete an abbreviation.
    /// </summary>
    [HttpDelete("abbreviations/{id:int}")]
    public async Task<IActionResult> DeleteAbbreviation(int id, CancellationToken ct = default)
    {
        var deleted = await _repo.DeleteAbbreviationAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    // ============================================================================
    // Industry Standards
    // ============================================================================

    /// <summary>
    /// Get all industry standards (IEC, ISA, API, IEEE, etc.).
    /// </summary>
    [HttpGet("standards")]
    public async Task<ActionResult<IReadOnlyList<IndustryStandardDto>>> GetStandards(CancellationToken ct = default)
    {
        var standards = await _repo.GetStandardsAsync(ct);
        return Ok(standards);
    }

    /// <summary>
    /// Get a single standard by ID.
    /// </summary>
    [HttpGet("standards/{id:int}")]
    public async Task<ActionResult<IndustryStandardDto>> GetStandard(int id, CancellationToken ct = default)
    {
        var standard = await _repo.GetStandardByIdAsync(id, ct);
        if (standard is null)
            return NotFound();

        return Ok(standard);
    }

    /// <summary>
    /// Create a new industry standard reference.
    /// </summary>
    [HttpPost("standards")]
    public async Task<ActionResult<IndustryStandardDto>> CreateStandard(
        [FromBody] CreateStandardRequest request,
        CancellationToken ct = default)
    {
        var id = await _repo.CreateStandardAsync(request, ct);
        var standard = await _repo.GetStandardByIdAsync(id, ct);
        
        return CreatedAtAction(nameof(GetStandard), new { id }, standard);
    }

    /// <summary>
    /// Update an existing standard.
    /// </summary>
    [HttpPatch("standards/{id:int}")]
    public async Task<ActionResult<IndustryStandardDto>> UpdateStandard(
        int id,
        [FromBody] UpdateStandardRequest request,
        CancellationToken ct = default)
    {
        var updated = await _repo.UpdateStandardAsync(id, request, ct);
        if (!updated)
            return NotFound();

        var standard = await _repo.GetStandardByIdAsync(id, ct);
        return Ok(standard);
    }

    /// <summary>
    /// Delete a standard.
    /// </summary>
    [HttpDelete("standards/{id:int}")]
    public async Task<IActionResult> DeleteStandard(int id, CancellationToken ct = default)
    {
        var deleted = await _repo.DeleteStandardAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    // ============================================================================
    // Measurement Types
    // ============================================================================

    /// <summary>
    /// Get all measurement types with their associated units.
    /// </summary>
    [HttpGet("measurement-types")]
    public async Task<ActionResult<IReadOnlyList<MeasurementTypeDto>>> GetMeasurementTypes(CancellationToken ct = default)
    {
        var types = await _repo.GetMeasurementTypesAsync(ct);
        return Ok(types);
    }

    /// <summary>
    /// Get a single measurement type by ID.
    /// </summary>
    [HttpGet("measurement-types/{id:int}")]
    public async Task<ActionResult<MeasurementTypeDto>> GetMeasurementType(int id, CancellationToken ct = default)
    {
        var type = await _repo.GetMeasurementTypeByIdAsync(id, ct);
        if (type is null)
            return NotFound();

        return Ok(type);
    }

    /// <summary>
    /// Create a new measurement type.
    /// </summary>
    [HttpPost("measurement-types")]
    public async Task<ActionResult<MeasurementTypeDto>> CreateMeasurementType(
        [FromBody] CreateMeasurementTypeRequest request,
        CancellationToken ct = default)
    {
        var id = await _repo.CreateMeasurementTypeAsync(request, ct);
        var type = await _repo.GetMeasurementTypeByIdAsync(id, ct);
        
        return CreatedAtAction(nameof(GetMeasurementType), new { id }, type);
    }

    /// <summary>
    /// Update a measurement type.
    /// </summary>
    [HttpPatch("measurement-types/{id:int}")]
    public async Task<ActionResult<MeasurementTypeDto>> UpdateMeasurementType(
        int id,
        [FromBody] UpdateMeasurementTypeRequest request,
        CancellationToken ct = default)
    {
        var updated = await _repo.UpdateMeasurementTypeAsync(id, request, ct);
        if (!updated)
            return NotFound();

        var type = await _repo.GetMeasurementTypeByIdAsync(id, ct);
        return Ok(type);
    }

    /// <summary>
    /// Delete a measurement type.
    /// </summary>
    [HttpDelete("measurement-types/{id:int}")]
    public async Task<IActionResult> DeleteMeasurementType(int id, CancellationToken ct = default)
    {
        var deleted = await _repo.DeleteMeasurementTypeAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    // ============================================================================
    // Unit Mappings
    // ============================================================================

    /// <summary>
    /// Get all unit mappings, optionally filtered by measurement type.
    /// </summary>
    [HttpGet("units")]
    public async Task<ActionResult<IReadOnlyList<UnitMappingDto>>> GetUnitMappings(
        [FromQuery] int? measurementTypeId = null,
        CancellationToken ct = default)
    {
        var units = await _repo.GetUnitMappingsAsync(measurementTypeId, ct);
        return Ok(units);
    }

    /// <summary>
    /// Create a new unit mapping.
    /// </summary>
    [HttpPost("units")]
    public async Task<ActionResult> CreateUnitMapping(
        [FromBody] CreateUnitMappingRequest request,
        CancellationToken ct = default)
    {
        var id = await _repo.CreateUnitMappingAsync(request, ct);
        return Created($"/api/seeds/units/{id}", new { id });
    }

    /// <summary>
    /// Delete a unit mapping.
    /// </summary>
    [HttpDelete("units/{id:int}")]
    public async Task<IActionResult> DeleteUnitMapping(int id, CancellationToken ct = default)
    {
        var deleted = await _repo.DeleteUnitMappingAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    // ============================================================================
    // Equipment Taxonomy
    // ============================================================================

    /// <summary>
    /// Get equipment taxonomy tree. Pass parentId to get children of a node.
    /// </summary>
    [HttpGet("equipment")]
    public async Task<ActionResult<IReadOnlyList<EquipmentTaxonomyDto>>> GetEquipmentTaxonomy(
        [FromQuery] int? parentId = null,
        CancellationToken ct = default)
    {
        var nodes = await _repo.GetEquipmentTaxonomyAsync(parentId, ct);
        return Ok(nodes);
    }

    /// <summary>
    /// Get a single equipment taxonomy node.
    /// </summary>
    [HttpGet("equipment/{id:int}")]
    public async Task<ActionResult<EquipmentTaxonomyDto>> GetEquipmentNode(int id, CancellationToken ct = default)
    {
        var node = await _repo.GetEquipmentTaxonomyByIdAsync(id, ct);
        if (node is null)
            return NotFound();

        return Ok(node);
    }

    /// <summary>
    /// Create a new equipment taxonomy node.
    /// </summary>
    [HttpPost("equipment")]
    public async Task<ActionResult<EquipmentTaxonomyDto>> CreateEquipmentNode(
        [FromBody] CreateEquipmentTaxonomyRequest request,
        CancellationToken ct = default)
    {
        var id = await _repo.CreateEquipmentTaxonomyAsync(request, ct);
        var node = await _repo.GetEquipmentTaxonomyByIdAsync(id, ct);
        
        return CreatedAtAction(nameof(GetEquipmentNode), new { id }, node);
    }

    /// <summary>
    /// Update an equipment taxonomy node.
    /// </summary>
    [HttpPatch("equipment/{id:int}")]
    public async Task<ActionResult<EquipmentTaxonomyDto>> UpdateEquipmentNode(
        int id,
        [FromBody] UpdateEquipmentTaxonomyRequest request,
        CancellationToken ct = default)
    {
        var updated = await _repo.UpdateEquipmentTaxonomyAsync(id, request, ct);
        if (!updated)
            return NotFound();

        var node = await _repo.GetEquipmentTaxonomyByIdAsync(id, ct);
        return Ok(node);
    }

    /// <summary>
    /// Delete an equipment taxonomy node. Fails if node has children.
    /// </summary>
    [HttpDelete("equipment/{id:int}")]
    public async Task<IActionResult> DeleteEquipmentNode(int id, CancellationToken ct = default)
    {
        var deleted = await _repo.DeleteEquipmentTaxonomyAsync(id, ct);
        if (!deleted)
            return BadRequest("Cannot delete node with children or node not found");

        return NoContent();
    }

    // ============================================================================
    // Manufacturer Profiles
    // ============================================================================

    /// <summary>
    /// Get all manufacturer profiles.
    /// </summary>
    [HttpGet("manufacturers")]
    public async Task<ActionResult<IReadOnlyList<ManufacturerProfileDto>>> GetManufacturers(CancellationToken ct = default)
    {
        var manufacturers = await _repo.GetManufacturersAsync(ct);
        return Ok(manufacturers);
    }

    /// <summary>
    /// Get a single manufacturer profile.
    /// </summary>
    [HttpGet("manufacturers/{id:int}")]
    public async Task<ActionResult<ManufacturerProfileDto>> GetManufacturer(int id, CancellationToken ct = default)
    {
        var manufacturer = await _repo.GetManufacturerByIdAsync(id, ct);
        if (manufacturer is null)
            return NotFound();

        return Ok(manufacturer);
    }

    /// <summary>
    /// Create a new manufacturer profile.
    /// </summary>
    [HttpPost("manufacturers")]
    public async Task<ActionResult<ManufacturerProfileDto>> CreateManufacturer(
        [FromBody] CreateManufacturerRequest request,
        CancellationToken ct = default)
    {
        var id = await _repo.CreateManufacturerAsync(request, ct);
        var manufacturer = await _repo.GetManufacturerByIdAsync(id, ct);
        
        return CreatedAtAction(nameof(GetManufacturer), new { id }, manufacturer);
    }

    /// <summary>
    /// Update a manufacturer profile.
    /// </summary>
    [HttpPatch("manufacturers/{id:int}")]
    public async Task<ActionResult<ManufacturerProfileDto>> UpdateManufacturer(
        int id,
        [FromBody] UpdateManufacturerRequest request,
        CancellationToken ct = default)
    {
        var updated = await _repo.UpdateManufacturerAsync(id, request, ct);
        if (!updated)
            return NotFound();

        var manufacturer = await _repo.GetManufacturerByIdAsync(id, ct);
        return Ok(manufacturer);
    }

    /// <summary>
    /// Delete a manufacturer profile.
    /// </summary>
    [HttpDelete("manufacturers/{id:int}")]
    public async Task<IActionResult> DeleteManufacturer(int id, CancellationToken ct = default)
    {
        var deleted = await _repo.DeleteManufacturerAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    // ============================================================================
    // Synonyms
    // ============================================================================

    /// <summary>
    /// Get all synonyms, optionally filtered by canonical term.
    /// </summary>
    [HttpGet("synonyms")]
    public async Task<ActionResult<IReadOnlyList<SynonymDto>>> GetSynonyms(
        [FromQuery] string? canonicalTerm = null,
        CancellationToken ct = default)
    {
        var synonyms = await _repo.GetSynonymsAsync(canonicalTerm, ct);
        return Ok(synonyms);
    }

    /// <summary>
    /// Create a new synonym mapping.
    /// </summary>
    [HttpPost("synonyms")]
    public async Task<ActionResult> CreateSynonym(
        [FromBody] CreateSynonymRequest request,
        CancellationToken ct = default)
    {
        var id = await _repo.CreateSynonymAsync(request, ct);
        return Created($"/api/seeds/synonyms/{id}", new { id });
    }

    /// <summary>
    /// Delete a synonym.
    /// </summary>
    [HttpDelete("synonyms/{id:int}")]
    public async Task<IActionResult> DeleteSynonym(int id, CancellationToken ct = default)
    {
        var deleted = await _repo.DeleteSynonymAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
