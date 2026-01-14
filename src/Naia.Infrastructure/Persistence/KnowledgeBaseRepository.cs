using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Naia.Application.Abstractions;
using Npgsql;

namespace Naia.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of the Knowledge Base repository.
/// Manages seeds for the semantic intelligence layer.
/// </summary>
public class KnowledgeBaseRepository : IKnowledgeBaseRepository
{
    private readonly ILogger<KnowledgeBaseRepository> _logger;
    private readonly string _connectionString;

    public KnowledgeBaseRepository(
        ILogger<KnowledgeBaseRepository> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("PostgreSQL") 
            ?? configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
    }

    // ============================================================================
    // Abbreviations
    // ============================================================================

    public async Task<IReadOnlyList<AbbreviationDto>> GetAbbreviationsAsync(
        string? context = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        var sql = @"
            SELECT id, abbreviation, expansion, context, measurement_type, 
                   priority, is_active, notes, created_at, updated_at
            FROM knowledge_abbreviations
            WHERE (@context::text IS NULL OR LOWER(context) = LOWER(@context))
            ORDER BY priority DESC, abbreviation
            OFFSET @skip LIMIT @take";

        var results = new List<AbbreviationDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("context", context ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("skip", skip);
        cmd.Parameters.AddWithValue("take", take);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapAbbreviation(reader));
        }
        
        return results;
    }

    public async Task<IReadOnlyList<AbbreviationDto>> SearchAbbreviationsAsync(
        string searchTerm,
        CancellationToken ct = default)
    {
        var sql = @"
            SELECT id, abbreviation, expansion, context, measurement_type,
                   priority, is_active, notes, created_at, updated_at
            FROM knowledge_abbreviations
            WHERE LOWER(abbreviation) LIKE LOWER(@search)
               OR LOWER(expansion) LIKE LOWER(@search)
            ORDER BY 
                CASE WHEN LOWER(abbreviation) = LOWER(@exact) THEN 0 ELSE 1 END,
                priority DESC
            LIMIT 50";

        var results = new List<AbbreviationDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("search", $"%{searchTerm}%");
        cmd.Parameters.AddWithValue("exact", searchTerm);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapAbbreviation(reader));
        }
        
        return results;
    }

    public async Task<AbbreviationDto?> GetAbbreviationByIdAsync(int id, CancellationToken ct = default)
    {
        var sql = @"
            SELECT id, abbreviation, expansion, context, measurement_type,
                   priority, is_active, notes, created_at, updated_at
            FROM knowledge_abbreviations
            WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapAbbreviation(reader);
        }
        
        return null;
    }

    public async Task<int> CreateAbbreviationAsync(CreateAbbreviationRequest request, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO knowledge_abbreviations 
                (abbreviation, expansion, context, measurement_type, priority, notes)
            VALUES (@abbr, @exp, @ctx, @mt, @priority, @notes)
            RETURNING id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("abbr", request.Abbreviation.ToUpperInvariant());
        cmd.Parameters.AddWithValue("exp", request.Expansion);
        cmd.Parameters.AddWithValue("ctx", request.Context);
        cmd.Parameters.AddWithValue("mt", request.MeasurementType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("priority", request.Priority);
        cmd.Parameters.AddWithValue("notes", request.Notes ?? (object)DBNull.Value);
        
        var result = await cmd.ExecuteScalarAsync(ct);
        var id = Convert.ToInt32(result);
        
        _logger.LogInformation("Created abbreviation {Abbr} -> {Exp} with ID {Id}", 
            request.Abbreviation, request.Expansion, id);
        
        return id;
    }

    public async Task<bool> UpdateAbbreviationAsync(int id, UpdateAbbreviationRequest request, CancellationToken ct = default)
    {
        var updates = new List<string>();
        var parameters = new List<NpgsqlParameter> { new("id", id) };

        if (request.Expansion != null)
        {
            updates.Add("expansion = @exp");
            parameters.Add(new("exp", request.Expansion));
        }
        if (request.Context != null)
        {
            updates.Add("context = @ctx");
            parameters.Add(new("ctx", request.Context));
        }
        if (request.MeasurementType != null)
        {
            updates.Add("measurement_type = @mt");
            parameters.Add(new("mt", request.MeasurementType));
        }
        if (request.Priority.HasValue)
        {
            updates.Add("priority = @priority");
            parameters.Add(new("priority", request.Priority.Value));
        }
        if (request.IsActive.HasValue)
        {
            updates.Add("is_active = @active");
            parameters.Add(new("active", request.IsActive.Value));
        }
        if (request.Notes != null)
        {
            updates.Add("notes = @notes");
            parameters.Add(new("notes", request.Notes));
        }

        if (updates.Count == 0) return false;

        updates.Add("updated_at = CURRENT_TIMESTAMP");
        
        var sql = $"UPDATE knowledge_abbreviations SET {string.Join(", ", updates)} WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());
        
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<bool> DeleteAbbreviationAsync(int id, CancellationToken ct = default)
    {
        var sql = "DELETE FROM knowledge_abbreviations WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        
        if (affected > 0)
            _logger.LogInformation("Deleted abbreviation ID {Id}", id);
        
        return affected > 0;
    }

    private static AbbreviationDto MapAbbreviation(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Abbreviation = reader.GetString(1),
        Expansion = reader.GetString(2),
        Context = reader.GetString(3),
        MeasurementType = reader.IsDBNull(4) ? null : reader.GetString(4),
        Priority = reader.GetInt32(5),
        IsActive = reader.GetBoolean(6),
        Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
        CreatedAt = reader.GetDateTime(8),
        UpdatedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
    };

    // ============================================================================
    // Industry Standards
    // ============================================================================

    public async Task<IReadOnlyList<IndustryStandardDto>> GetStandardsAsync(CancellationToken ct = default)
    {
        var sql = @"
            SELECT id, standard_code, name, description, industry, 
                   version, document_url, is_active, created_at
            FROM knowledge_industry_standards
            ORDER BY industry, standard_code";

        var results = new List<IndustryStandardDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapStandard(reader));
        }
        
        return results;
    }

    public async Task<IndustryStandardDto?> GetStandardByIdAsync(int id, CancellationToken ct = default)
    {
        var sql = @"
            SELECT id, standard_code, name, description, industry,
                   version, document_url, is_active, created_at
            FROM knowledge_industry_standards
            WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapStandard(reader);
        }
        
        return null;
    }

    public async Task<int> CreateStandardAsync(CreateStandardRequest request, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO knowledge_industry_standards 
                (standard_code, name, description, industry, version, document_url)
            VALUES (@code, @name, @desc, @industry, @version, @url)
            RETURNING id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("code", request.StandardCode);
        cmd.Parameters.AddWithValue("name", request.Name);
        cmd.Parameters.AddWithValue("desc", request.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("industry", request.Industry);
        cmd.Parameters.AddWithValue("version", request.Version ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("url", request.DocumentUrl ?? (object)DBNull.Value);
        
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<bool> UpdateStandardAsync(int id, UpdateStandardRequest request, CancellationToken ct = default)
    {
        var updates = new List<string>();
        var parameters = new List<NpgsqlParameter> { new("id", id) };

        if (request.Name != null) { updates.Add("name = @name"); parameters.Add(new("name", request.Name)); }
        if (request.Description != null) { updates.Add("description = @desc"); parameters.Add(new("desc", request.Description)); }
        if (request.Industry != null) { updates.Add("industry = @industry"); parameters.Add(new("industry", request.Industry)); }
        if (request.Version != null) { updates.Add("version = @version"); parameters.Add(new("version", request.Version)); }
        if (request.DocumentUrl != null) { updates.Add("document_url = @url"); parameters.Add(new("url", request.DocumentUrl)); }
        if (request.IsActive.HasValue) { updates.Add("is_active = @active"); parameters.Add(new("active", request.IsActive.Value)); }

        if (updates.Count == 0) return false;

        var sql = $"UPDATE knowledge_industry_standards SET {string.Join(", ", updates)} WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());
        
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteStandardAsync(int id, CancellationToken ct = default)
    {
        var sql = "DELETE FROM knowledge_industry_standards WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static IndustryStandardDto MapStandard(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        StandardCode = reader.GetString(1),
        Name = reader.GetString(2),
        Description = reader.IsDBNull(3) ? null : reader.GetString(3),
        Industry = reader.GetString(4),
        Version = reader.IsDBNull(5) ? null : reader.GetString(5),
        DocumentUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
        IsActive = reader.GetBoolean(7),
        CreatedAt = reader.GetDateTime(8)
    };

    // ============================================================================
    // Measurement Types
    // ============================================================================

    public async Task<IReadOnlyList<MeasurementTypeDto>> GetMeasurementTypesAsync(CancellationToken ct = default)
    {
        var sql = @"
            SELECT mt.id, mt.name, mt.category, mt.base_unit, mt.typical_min, 
                   mt.typical_max, mt.description, mt.is_active,
                   COALESCE(array_agg(u.unit_symbol) FILTER (WHERE u.unit_symbol IS NOT NULL), '{}') as units
            FROM knowledge_measurement_types mt
            LEFT JOIN knowledge_unit_mappings u ON mt.id = u.measurement_type_id
            GROUP BY mt.id
            ORDER BY mt.category, mt.name";

        var results = new List<MeasurementTypeDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        while (await reader.ReadAsync(ct))
        {
            results.Add(new MeasurementTypeDto
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Category = reader.GetString(2),
                BaseUnit = reader.GetString(3),
                TypicalMin = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                TypicalMax = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                Description = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsActive = reader.GetBoolean(7),
                Units = ((string[])reader.GetValue(8)).ToList()
            });
        }
        
        return results;
    }

    public async Task<MeasurementTypeDto?> GetMeasurementTypeByIdAsync(int id, CancellationToken ct = default)
    {
        var types = await GetMeasurementTypesAsync(ct);
        return types.FirstOrDefault(t => t.Id == id);
    }

    public async Task<int> CreateMeasurementTypeAsync(CreateMeasurementTypeRequest request, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO knowledge_measurement_types 
                (name, category, base_unit, typical_min, typical_max, description)
            VALUES (@name, @category, @baseUnit, @min, @max, @desc)
            RETURNING id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", request.Name);
        cmd.Parameters.AddWithValue("category", request.Category);
        cmd.Parameters.AddWithValue("baseUnit", request.BaseUnit);
        cmd.Parameters.AddWithValue("min", request.TypicalMin ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("max", request.TypicalMax ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("desc", request.Description ?? (object)DBNull.Value);
        
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<bool> UpdateMeasurementTypeAsync(int id, UpdateMeasurementTypeRequest request, CancellationToken ct = default)
    {
        var updates = new List<string>();
        var parameters = new List<NpgsqlParameter> { new("id", id) };

        if (request.Name != null) { updates.Add("name = @name"); parameters.Add(new("name", request.Name)); }
        if (request.Category != null) { updates.Add("category = @category"); parameters.Add(new("category", request.Category)); }
        if (request.BaseUnit != null) { updates.Add("base_unit = @baseUnit"); parameters.Add(new("baseUnit", request.BaseUnit)); }
        if (request.TypicalMin.HasValue) { updates.Add("typical_min = @min"); parameters.Add(new("min", request.TypicalMin.Value)); }
        if (request.TypicalMax.HasValue) { updates.Add("typical_max = @max"); parameters.Add(new("max", request.TypicalMax.Value)); }
        if (request.Description != null) { updates.Add("description = @desc"); parameters.Add(new("desc", request.Description)); }
        if (request.IsActive.HasValue) { updates.Add("is_active = @active"); parameters.Add(new("active", request.IsActive.Value)); }

        if (updates.Count == 0) return false;

        var sql = $"UPDATE knowledge_measurement_types SET {string.Join(", ", updates)} WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());
        
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteMeasurementTypeAsync(int id, CancellationToken ct = default)
    {
        var sql = "DELETE FROM knowledge_measurement_types WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ============================================================================
    // Unit Mappings
    // ============================================================================

    public async Task<IReadOnlyList<UnitMappingDto>> GetUnitMappingsAsync(
        int? measurementTypeId = null,
        CancellationToken ct = default)
    {
        var sql = @"
            SELECT u.id, u.unit_symbol, u.unit_name, u.measurement_type_id, 
                   mt.name as measurement_type_name, u.conversion_factor, u.is_base_unit
            FROM knowledge_unit_mappings u
            JOIN knowledge_measurement_types mt ON u.measurement_type_id = mt.id
            WHERE (@mtId IS NULL OR u.measurement_type_id = @mtId)
            ORDER BY mt.name, u.unit_symbol";

        var results = new List<UnitMappingDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("mtId", measurementTypeId ?? (object)DBNull.Value);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new UnitMappingDto
            {
                Id = reader.GetInt32(0),
                UnitSymbol = reader.GetString(1),
                UnitName = reader.GetString(2),
                MeasurementTypeId = reader.GetInt32(3),
                MeasurementTypeName = reader.GetString(4),
                ConversionFactor = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                IsBaseUnit = reader.GetBoolean(6)
            });
        }
        
        return results;
    }

    public async Task<int> CreateUnitMappingAsync(CreateUnitMappingRequest request, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO knowledge_unit_mappings 
                (unit_symbol, unit_name, measurement_type_id, conversion_factor, is_base_unit)
            VALUES (@symbol, @name, @mtId, @factor, @isBase)
            RETURNING id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol", request.UnitSymbol);
        cmd.Parameters.AddWithValue("name", request.UnitName);
        cmd.Parameters.AddWithValue("mtId", request.MeasurementTypeId);
        cmd.Parameters.AddWithValue("factor", request.ConversionFactor ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("isBase", request.IsBaseUnit);
        
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<bool> DeleteUnitMappingAsync(int id, CancellationToken ct = default)
    {
        var sql = "DELETE FROM knowledge_unit_mappings WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ============================================================================
    // Equipment Taxonomy
    // ============================================================================

    public async Task<IReadOnlyList<EquipmentTaxonomyDto>> GetEquipmentTaxonomyAsync(
        int? parentId = null,
        CancellationToken ct = default)
    {
        var sql = @"
            WITH RECURSIVE tree AS (
                SELECT id, name, parent_id, 1 as level, name::text as path
                FROM knowledge_equipment_taxonomy
                WHERE parent_id IS NULL
                UNION ALL
                SELECT e.id, e.name, e.parent_id, t.level + 1, 
                       t.path || ' > ' || e.name
                FROM knowledge_equipment_taxonomy e
                JOIN tree t ON e.parent_id = t.id
            )
            SELECT e.id, e.name, e.parent_id, p.name as parent_name, 
                   t.level, t.path, e.description, e.industry, e.is_active,
                   (SELECT COUNT(*) FROM knowledge_equipment_taxonomy c WHERE c.parent_id = e.id) as child_count
            FROM knowledge_equipment_taxonomy e
            LEFT JOIN knowledge_equipment_taxonomy p ON e.parent_id = p.id
            JOIN tree t ON e.id = t.id
            WHERE (@parentId IS NULL OR e.parent_id = @parentId OR (e.parent_id IS NULL AND @parentId = 0))
            ORDER BY t.path";

        var results = new List<EquipmentTaxonomyDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("parentId", parentId ?? (object)DBNull.Value);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new EquipmentTaxonomyDto
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                ParentId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                ParentName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Level = reader.GetInt32(4),
                Path = reader.GetString(5),
                Description = reader.IsDBNull(6) ? null : reader.GetString(6),
                Industry = reader.IsDBNull(7) ? null : reader.GetString(7),
                IsActive = reader.GetBoolean(8),
                ChildCount = reader.GetInt32(9)
            });
        }
        
        return results;
    }

    public async Task<EquipmentTaxonomyDto?> GetEquipmentTaxonomyByIdAsync(int id, CancellationToken ct = default)
    {
        var allNodes = await GetEquipmentTaxonomyAsync(ct: ct);
        return allNodes.FirstOrDefault(n => n.Id == id);
    }

    public async Task<int> CreateEquipmentTaxonomyAsync(CreateEquipmentTaxonomyRequest request, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO knowledge_equipment_taxonomy 
                (name, parent_id, description, industry)
            VALUES (@name, @parentId, @desc, @industry)
            RETURNING id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", request.Name);
        cmd.Parameters.AddWithValue("parentId", request.ParentId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("desc", request.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("industry", request.Industry ?? (object)DBNull.Value);
        
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<bool> UpdateEquipmentTaxonomyAsync(int id, UpdateEquipmentTaxonomyRequest request, CancellationToken ct = default)
    {
        var updates = new List<string>();
        var parameters = new List<NpgsqlParameter> { new("id", id) };

        if (request.Name != null) { updates.Add("name = @name"); parameters.Add(new("name", request.Name)); }
        if (request.ParentId.HasValue) { updates.Add("parent_id = @parentId"); parameters.Add(new("parentId", request.ParentId.Value)); }
        if (request.Description != null) { updates.Add("description = @desc"); parameters.Add(new("desc", request.Description)); }
        if (request.Industry != null) { updates.Add("industry = @industry"); parameters.Add(new("industry", request.Industry)); }
        if (request.IsActive.HasValue) { updates.Add("is_active = @active"); parameters.Add(new("active", request.IsActive.Value)); }

        if (updates.Count == 0) return false;

        var sql = $"UPDATE knowledge_equipment_taxonomy SET {string.Join(", ", updates)} WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());
        
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteEquipmentTaxonomyAsync(int id, CancellationToken ct = default)
    {
        // Check for children first
        var checkSql = "SELECT COUNT(*) FROM knowledge_equipment_taxonomy WHERE parent_id = @id";
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var checkCmd = new NpgsqlCommand(checkSql, conn);
        checkCmd.Parameters.AddWithValue("id", id);
        
        var childCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct));
        if (childCount > 0)
        {
            _logger.LogWarning("Cannot delete equipment taxonomy node {Id} - has {Count} children", id, childCount);
            return false;
        }

        var sql = "DELETE FROM knowledge_equipment_taxonomy WHERE id = @id";
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ============================================================================
    // Manufacturer Profiles
    // ============================================================================

    public async Task<IReadOnlyList<ManufacturerProfileDto>> GetManufacturersAsync(CancellationToken ct = default)
    {
        var sql = @"
            SELECT id, name, alias, industry, tag_prefixes, naming_patterns, 
                   notes, is_active, created_at
            FROM knowledge_manufacturer_profiles
            ORDER BY name";

        var results = new List<ManufacturerProfileDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapManufacturer(reader));
        }
        
        return results;
    }

    public async Task<ManufacturerProfileDto?> GetManufacturerByIdAsync(int id, CancellationToken ct = default)
    {
        var sql = @"
            SELECT id, name, alias, industry, tag_prefixes, naming_patterns,
                   notes, is_active, created_at
            FROM knowledge_manufacturer_profiles
            WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapManufacturer(reader);
        }
        
        return null;
    }

    public async Task<int> CreateManufacturerAsync(CreateManufacturerRequest request, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO knowledge_manufacturer_profiles 
                (name, alias, industry, tag_prefixes, naming_patterns, notes)
            VALUES (@name, @alias, @industry, @prefixes, @patterns, @notes)
            RETURNING id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", request.Name);
        cmd.Parameters.AddWithValue("alias", request.Alias ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("industry", request.Industry);
        cmd.Parameters.AddWithValue("prefixes", request.TagPrefixes?.ToArray() ?? Array.Empty<string>());
        cmd.Parameters.AddWithValue("patterns", request.NamingPatterns?.ToArray() ?? Array.Empty<string>());
        cmd.Parameters.AddWithValue("notes", request.Notes ?? (object)DBNull.Value);
        
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<bool> UpdateManufacturerAsync(int id, UpdateManufacturerRequest request, CancellationToken ct = default)
    {
        var updates = new List<string>();
        var parameters = new List<NpgsqlParameter> { new("id", id) };

        if (request.Name != null) { updates.Add("name = @name"); parameters.Add(new("name", request.Name)); }
        if (request.Alias != null) { updates.Add("alias = @alias"); parameters.Add(new("alias", request.Alias)); }
        if (request.Industry != null) { updates.Add("industry = @industry"); parameters.Add(new("industry", request.Industry)); }
        if (request.TagPrefixes != null) { updates.Add("tag_prefixes = @prefixes"); parameters.Add(new("prefixes", request.TagPrefixes.ToArray())); }
        if (request.NamingPatterns != null) { updates.Add("naming_patterns = @patterns"); parameters.Add(new("patterns", request.NamingPatterns.ToArray())); }
        if (request.Notes != null) { updates.Add("notes = @notes"); parameters.Add(new("notes", request.Notes)); }
        if (request.IsActive.HasValue) { updates.Add("is_active = @active"); parameters.Add(new("active", request.IsActive.Value)); }

        if (updates.Count == 0) return false;

        var sql = $"UPDATE knowledge_manufacturer_profiles SET {string.Join(", ", updates)} WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());
        
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteManufacturerAsync(int id, CancellationToken ct = default)
    {
        var sql = "DELETE FROM knowledge_manufacturer_profiles WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static ManufacturerProfileDto MapManufacturer(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        Alias = reader.IsDBNull(2) ? null : reader.GetString(2),
        Industry = reader.GetString(3),
        TagPrefixes = reader.IsDBNull(4) ? new List<string>() : ((string[])reader.GetValue(4)).ToList(),
        NamingPatterns = reader.IsDBNull(5) ? new List<string>() : ((string[])reader.GetValue(5)).ToList(),
        Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
        IsActive = reader.GetBoolean(7),
        CreatedAt = reader.GetDateTime(8)
    };

    // ============================================================================
    // Synonyms
    // ============================================================================

    public async Task<IReadOnlyList<SynonymDto>> GetSynonymsAsync(
        string? canonicalTerm = null,
        CancellationToken ct = default)
    {
        var sql = @"
            SELECT id, canonical_term, synonym, context, created_at
            FROM knowledge_synonyms
            WHERE (@term IS NULL OR LOWER(canonical_term) = LOWER(@term))
            ORDER BY canonical_term, synonym";

        var results = new List<SynonymDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("term", canonicalTerm ?? (object)DBNull.Value);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SynonymDto
            {
                Id = reader.GetInt32(0),
                CanonicalTerm = reader.GetString(1),
                Synonym = reader.GetString(2),
                Context = reader.GetString(3),
                CreatedAt = reader.GetDateTime(4)
            });
        }
        
        return results;
    }

    public async Task<int> CreateSynonymAsync(CreateSynonymRequest request, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO knowledge_synonyms (canonical_term, synonym, context)
            VALUES (@term, @synonym, @context)
            RETURNING id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("term", request.CanonicalTerm);
        cmd.Parameters.AddWithValue("synonym", request.Synonym);
        cmd.Parameters.AddWithValue("context", request.Context);
        
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<bool> DeleteSynonymAsync(int id, CancellationToken ct = default)
    {
        var sql = "DELETE FROM knowledge_synonyms WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ============================================================================
    // Statistics & Overview
    // ============================================================================

    public async Task<KnowledgeBaseStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var sql = @"
            SELECT 
                (SELECT COUNT(*) FROM knowledge_abbreviations) as abbr_total,
                (SELECT COUNT(*) FROM knowledge_abbreviations WHERE is_active) as abbr_active,
                (SELECT COUNT(*) FROM knowledge_industry_standards) as standards,
                (SELECT COUNT(*) FROM knowledge_measurement_types) as mtypes,
                (SELECT COUNT(*) FROM knowledge_unit_mappings) as units,
                (SELECT COUNT(*) FROM knowledge_equipment_taxonomy) as equipment,
                (SELECT COUNT(*) FROM knowledge_manufacturer_profiles) as manufacturers,
                (SELECT COUNT(*) FROM knowledge_synonyms) as synonyms,
                (SELECT MAX(updated_at) FROM knowledge_abbreviations) as last_abbr_update,
                (SELECT MAX(created_at) FROM knowledge_industry_standards) as last_std_update";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        if (!await reader.ReadAsync(ct))
        {
            return new KnowledgeBaseStatsDto
            {
                TotalAbbreviations = 0,
                TotalStandards = 0,
                TotalMeasurementTypes = 0,
                TotalUnitMappings = 0,
                TotalEquipmentTypes = 0,
                TotalManufacturers = 0,
                TotalSynonyms = 0,
                ActiveAbbreviations = 0,
                LastUpdatedAt = DateTime.UtcNow,
                AbbreviationsByContext = new Dictionary<string, int>(),
                StandardsByIndustry = new Dictionary<string, int>()
            };
        }

        var lastAbbrUpdate = reader.IsDBNull(8) ? DateTime.UtcNow : reader.GetDateTime(8);
        var lastStdUpdate = reader.IsDBNull(9) ? DateTime.UtcNow : reader.GetDateTime(9);

        await reader.CloseAsync();

        // Get abbreviations by context
        var contextSql = @"
            SELECT context, COUNT(*) 
            FROM knowledge_abbreviations 
            WHERE is_active 
            GROUP BY context";
        
        await using var contextCmd = new NpgsqlCommand(contextSql, conn);
        var contextCounts = new Dictionary<string, int>();
        
        await using (var contextReader = await contextCmd.ExecuteReaderAsync(ct))
        {
            while (await contextReader.ReadAsync(ct))
            {
                contextCounts[contextReader.GetString(0)] = contextReader.GetInt32(1);
            }
        }

        // Get standards by industry
        var industrySql = @"
            SELECT industry, COUNT(*) 
            FROM knowledge_industry_standards 
            WHERE is_active 
            GROUP BY industry";
        
        await using var industryCmd = new NpgsqlCommand(industrySql, conn);
        var industryCounts = new Dictionary<string, int>();
        
        await using (var industryReader = await industryCmd.ExecuteReaderAsync(ct))
        {
            while (await industryReader.ReadAsync(ct))
            {
                industryCounts[industryReader.GetString(0)] = industryReader.GetInt32(1);
            }
        }

        // Re-read main stats
        await using var cmd2 = new NpgsqlCommand(sql, conn);
        await using var reader2 = await cmd2.ExecuteReaderAsync(ct);
        await reader2.ReadAsync(ct);

        return new KnowledgeBaseStatsDto
        {
            TotalAbbreviations = reader2.GetInt32(0),
            ActiveAbbreviations = reader2.GetInt32(1),
            TotalStandards = reader2.GetInt32(2),
            TotalMeasurementTypes = reader2.GetInt32(3),
            TotalUnitMappings = reader2.GetInt32(4),
            TotalEquipmentTypes = reader2.GetInt32(5),
            TotalManufacturers = reader2.GetInt32(6),
            TotalSynonyms = reader2.GetInt32(7),
            LastUpdatedAt = lastAbbrUpdate > lastStdUpdate ? lastAbbrUpdate : lastStdUpdate,
            AbbreviationsByContext = contextCounts,
            StandardsByIndustry = industryCounts
        };
    }

    public async Task<IReadOnlyList<SeedEffectivenessDto>> GetSeedEffectivenessAsync(
        int topN = 50,
        CancellationToken ct = default)
    {
        var sql = @"
            SELECT seed_type, seed_id, seed_value, times_used, successful_matches,
                   CASE WHEN times_used > 0 THEN successful_matches::float / times_used ELSE 0 END as success_rate,
                   last_used_at
            FROM knowledge_seed_effectiveness
            ORDER BY times_used DESC
            LIMIT @topN";

        var results = new List<SeedEffectivenessDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("topN", topN);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SeedEffectivenessDto
            {
                SeedType = reader.GetString(0),
                SeedId = reader.GetInt32(1),
                SeedValue = reader.GetString(2),
                TimesUsed = reader.GetInt32(3),
                SuccessfulMatches = reader.GetInt32(4),
                SuccessRate = reader.GetDouble(5),
                LastUsedAt = reader.GetDateTime(6)
            });
        }
        
        return results;
    }

    public async Task<IReadOnlyList<SeedAuditLogDto>> GetAuditLogAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        var sql = @"
            SELECT id, table_name, record_id, action, old_value, new_value, 
                   changed_by, changed_at, change_reason
            FROM knowledge_seed_audit_log
            ORDER BY changed_at DESC
            OFFSET @skip LIMIT @take";

        var results = new List<SeedAuditLogDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("skip", skip);
        cmd.Parameters.AddWithValue("take", take);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SeedAuditLogDto
            {
                Id = reader.GetInt32(0),
                TableName = reader.GetString(1),
                RecordId = reader.GetInt32(2),
                Action = reader.GetString(3),
                OldValue = reader.IsDBNull(4) ? "" : reader.GetString(4),
                NewValue = reader.IsDBNull(5) ? "" : reader.GetString(5),
                ChangedBy = reader.GetString(6),
                ChangedAt = reader.GetDateTime(7),
                ChangeReason = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        
        return results;
    }

    // ============================================================================
    // Documentation Format Schemas (stub implementations)
    // TODO: Implement database schema and full CRUD when documentation formats are needed
    // ============================================================================

    public Task<IReadOnlyList<DocumentationFormatDto>> GetDocumentationFormatsAsync(CancellationToken ct = default)
    {
        // Return empty list for now - documentation format feature not yet implemented
        return Task.FromResult<IReadOnlyList<DocumentationFormatDto>>(Array.Empty<DocumentationFormatDto>());
    }

    public Task<DocumentationFormatDto?> GetDocumentationFormatByIdAsync(int id, CancellationToken ct = default)
    {
        // Not yet implemented
        return Task.FromResult<DocumentationFormatDto?>(null);
    }

    public Task<int> CreateDocumentationFormatAsync(CreateDocumentationFormatRequest request, CancellationToken ct = default)
    {
        // Not yet implemented - would create in database
        throw new NotImplementedException("Documentation format creation not yet implemented. Database schema required.");
    }

    public Task<bool> UpdateDocumentationFormatAsync(int id, UpdateDocumentationFormatRequest request, CancellationToken ct = default)
    {
        // Not yet implemented
        throw new NotImplementedException("Documentation format update not yet implemented. Database schema required.");
    }

    public Task<bool> DeleteDocumentationFormatAsync(int id, CancellationToken ct = default)
    {
        // Not yet implemented
        throw new NotImplementedException("Documentation format deletion not yet implemented. Database schema required.");
    }

    public Task<DocumentationValidationResultDto> ValidateDocumentationAsync(
        int formatId,
        string documentationContent,
        CancellationToken ct = default)
    {
        // Return a valid result for now - validation not yet implemented
        return Task.FromResult(new DocumentationValidationResultDto
        {
            IsValid = true,
            FormatId = formatId,
            MatchScore = 1.0,
            Errors = new List<ValidationErrorDto>(),
            Warnings = new List<ValidationWarningDto>()
        });
    }
}
