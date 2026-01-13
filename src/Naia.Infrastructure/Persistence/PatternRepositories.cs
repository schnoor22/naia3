using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Naia.Application.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Naia.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of suggestion repository.
/// </summary>
public class SuggestionRepository : ISuggestionRepository
{
    private readonly NaiaDbContext _db;
    private readonly ILogger<SuggestionRepository> _logger;
    private readonly string _connectionString;

    public SuggestionRepository(NaiaDbContext db, ILogger<SuggestionRepository> logger, IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("PostgreSql") 
            ?? "Host=localhost;Database=naia;Username=naia;Password=naia_dev_password;SslMode=Disable;Pooling=false";
    }

    public async Task<IReadOnlyList<SuggestionDto>> GetPendingAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var sql = @"
            SELECT 
                s.id, s.cluster_id, s.pattern_id, p.name as pattern_name,
                s.overall_confidence, COALESCE(c.point_count, array_length(c.point_ids, 1)), s.status, s.created_at, c.common_prefix
            FROM pattern_suggestions s
            JOIN patterns p ON s.pattern_id = p.id
            JOIN behavioral_clusters c ON s.cluster_id = c.id
            WHERE s.status = 'pending'
            ORDER BY s.overall_confidence DESC, s.created_at DESC
            LIMIT @take OFFSET @skip";

        var suggestions = new List<SuggestionDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("take", take);
        cmd.Parameters.AddWithValue("skip", skip);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            suggestions.Add(new SuggestionDto
            {
                Id = reader.GetGuid(0),
                ClusterId = reader.GetGuid(1),
                PatternId = reader.GetGuid(2),
                PatternName = reader.GetString(3),
                Confidence = reader.GetDouble(4),
                PointCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                Status = Enum.Parse<SuggestionStatus>(reader.GetString(6), true),
                CreatedAt = reader.GetDateTime(7),
                CommonPrefix = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return suggestions;
    }

    public async Task<SuggestionDetailDto?> GetByIdAsync(Guid suggestionId, CancellationToken ct = default)
    {
        var sql = @"
            SELECT 
                s.id, s.cluster_id, s.pattern_id, p.name as pattern_name,
                s.overall_confidence, s.naming_score, s.correlation_score, s.range_score, s.rate_score,
                COALESCE(c.point_count, array_length(c.point_ids, 1)), s.status, s.created_at, c.common_prefix, s.reason,
                c.point_ids, c.point_names
            FROM pattern_suggestions s
            JOIN patterns p ON s.pattern_id = p.id
            JOIN behavioral_clusters c ON s.cluster_id = c.id
            WHERE s.id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", suggestionId);
        
        using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return null;

            var pointIds = (Guid[])reader.GetValue(14);
            var pointNames = reader.IsDBNull(15) ? new string[pointIds.Length] : (string[])reader.GetValue(15);
            
            // Ensure we have names for all point IDs
            if (pointNames.Length != pointIds.Length)
            {
                pointNames = pointIds.Select((_, i) => i < pointNames.Length ? pointNames[i] : "").ToArray();
            }

            var points = pointIds.Zip(pointNames, (id, name) => new SuggestionPointDto
            {
                PointId = id,
                PointName = name,
                SuggestedRole = null,
                RoleConfidence = null
            }).ToList();

            return new SuggestionDetailDto
            {
                Id = reader.GetGuid(0),
                ClusterId = reader.GetGuid(1),
                PatternId = reader.GetGuid(2),
                PatternName = reader.GetString(3),
                Confidence = reader.GetDouble(4),
                NamingScore = reader.GetDouble(5),
                CorrelationScore = reader.GetDouble(6),
                RangeScore = reader.GetDouble(7),
                RateScore = reader.GetDouble(8),
                PointCount = reader.GetInt32(9),
                Status = Enum.Parse<SuggestionStatus>(reader.GetString(10), true),
                CreatedAt = reader.GetDateTime(11),
                CommonPrefix = reader.IsDBNull(12) ? null : reader.GetString(12),
                Reason = reader.GetString(13),
                Points = points,
                ExpectedRoles = new List<PatternRoleDto>()
            };
        }
    }

    public async Task<IReadOnlyList<SuggestionDto>> GetByClusterIdAsync(Guid clusterId, CancellationToken ct = default)
    {
        var sql = @"
            SELECT 
                s.id, s.cluster_id, s.pattern_id, p.name as pattern_name,
                s.overall_confidence, c.point_count, s.status, s.created_at, c.common_prefix
            FROM pattern_suggestions s
            JOIN patterns p ON s.pattern_id = p.id
            JOIN behavioral_clusters c ON s.cluster_id = c.id
            WHERE s.cluster_id = @clusterId
            ORDER BY s.overall_confidence DESC";

        var suggestions = new List<SuggestionDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("clusterId", clusterId);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            suggestions.Add(new SuggestionDto
            {
                Id = reader.GetGuid(0),
                ClusterId = reader.GetGuid(1),
                PatternId = reader.GetGuid(2),
                PatternName = reader.GetString(3),
                Confidence = reader.GetDouble(4),
                PointCount = reader.GetInt32(5),
                Status = Enum.Parse<SuggestionStatus>(reader.GetString(6), true),
                CreatedAt = reader.GetDateTime(7),
                CommonPrefix = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return suggestions;
    }

    public async Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        var sql = "SELECT COUNT(*) FROM pattern_suggestions WHERE status = 'pending'";
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task SaveSuggestionAsync(SuggestionCreatedEvent suggestion, CancellationToken ct = default)
    {
        var sql = @"
            INSERT INTO pattern_suggestions 
                (id, cluster_id, pattern_id, overall_confidence, naming_score, correlation_score, 
                 range_score, rate_score, reason, status, created_at)
            VALUES 
                (@id, @clusterId, @patternId, @confidence, @namingScore, @correlationScore,
                 @rangeScore, @rateScore, @reason, 'pending', @createdAt)
            ON CONFLICT (cluster_id, pattern_id) DO UPDATE SET
                overall_confidence = EXCLUDED.overall_confidence,
                naming_score = EXCLUDED.naming_score,
                correlation_score = EXCLUDED.correlation_score,
                range_score = EXCLUDED.range_score,
                rate_score = EXCLUDED.rate_score,
                reason = EXCLUDED.reason";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", suggestion.SuggestionId);
        cmd.Parameters.AddWithValue("clusterId", suggestion.ClusterId);
        cmd.Parameters.AddWithValue("patternId", suggestion.PatternId);
        cmd.Parameters.AddWithValue("confidence", suggestion.OverallConfidence);
        cmd.Parameters.AddWithValue("namingScore", suggestion.NamingScore);
        cmd.Parameters.AddWithValue("correlationScore", suggestion.CorrelationScore);
        cmd.Parameters.AddWithValue("rangeScore", suggestion.RangeScore);
        cmd.Parameters.AddWithValue("rateScore", suggestion.RateScore);
        cmd.Parameters.AddWithValue("reason", suggestion.Reason);
        cmd.Parameters.AddWithValue("createdAt", suggestion.CreatedAt);
        
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Saved suggestion {SuggestionId} for pattern {PatternId}", 
            suggestion.SuggestionId, suggestion.PatternId);
    }

    public async Task UpdateStatusAsync(Guid suggestionId, SuggestionStatus status, string? reason = null, string? userId = null, CancellationToken ct = default)
    {
        var sql = @"
            UPDATE pattern_suggestions 
            SET status = @status, 
                reviewed_at = CURRENT_TIMESTAMP,
                reviewed_by = @userId,
                rejection_reason = @reason
            WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", suggestionId);
        cmd.Parameters.AddWithValue("status", status.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
        
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<SuggestionStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var sql = @"
            SELECT 
                COUNT(*) FILTER (WHERE status = 'pending') as pending,
                COUNT(*) FILTER (WHERE status = 'approved' AND reviewed_at::date = CURRENT_DATE) as approved_today,
                COUNT(*) FILTER (WHERE status = 'rejected' AND reviewed_at::date = CURRENT_DATE) as rejected_today,
                COUNT(*) FILTER (WHERE status = 'approved') as total_approved,
                COUNT(*) FILTER (WHERE status = 'rejected') as total_rejected
            FROM pattern_suggestions";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        if (!await reader.ReadAsync(ct))
            return new SuggestionStatsDto
            {
                PendingCount = 0, ApprovedToday = 0, RejectedToday = 0,
                TotalApproved = 0, TotalRejected = 0, ApprovalRate = 0
            };

        var totalApproved = reader.GetInt32(3);
        var totalRejected = reader.GetInt32(4);
        var total = totalApproved + totalRejected;

        return new SuggestionStatsDto
        {
            PendingCount = reader.GetInt32(0),
            ApprovedToday = reader.GetInt32(1),
            RejectedToday = reader.GetInt32(2),
            TotalApproved = totalApproved,
            TotalRejected = totalRejected,
            ApprovalRate = total > 0 ? (double)totalApproved / total : 0
        };
    }
}

/// <summary>
/// PostgreSQL implementation of pattern repository.
/// </summary>
public class PatternRepository : IPatternRepository
{
    private readonly NaiaDbContext _db;
    private readonly ILogger<PatternRepository> _logger;
    private readonly string _connectionString;

    public PatternRepository(NaiaDbContext db, ILogger<PatternRepository> logger, IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("PostgreSql") 
            ?? "Host=localhost;Database=naia;Username=naia;Password=naia_dev_password;SslMode=Disable;Pooling=false";
    }

    public async Task<IReadOnlyList<PatternDto>> GetAllAsync(CancellationToken ct = default)
    {
        var sql = @"
            SELECT 
                p.id, p.name, p.category, p.description, p.confidence,
                p.example_count, COUNT(r.id) as role_count, p.is_system_pattern,
                p.created_at, p.last_matched_at
            FROM patterns p
            LEFT JOIN pattern_roles r ON p.id = r.pattern_id
            GROUP BY p.id
            ORDER BY p.confidence DESC, p.example_count DESC";

        var patterns = new List<PatternDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        while (await reader.ReadAsync(ct))
        {
            patterns.Add(new PatternDto
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                Category = reader.GetString(2),
                Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Confidence = reader.GetDouble(4),
                ExampleCount = reader.GetInt32(5),
                RoleCount = reader.GetInt32(6),
                IsSystemPattern = reader.GetBoolean(7),
                CreatedAt = reader.GetDateTime(8),
                LastMatchedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
            });
        }

        return patterns;
    }

    public async Task<PatternDetailDto?> GetByIdAsync(Guid patternId, CancellationToken ct = default)
    {
        // Get pattern
        var patternSql = @"
            SELECT id, name, category, description, confidence, example_count,
                   is_system_pattern, created_at, last_matched_at
            FROM patterns WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var patternCmd = new NpgsqlCommand(patternSql, conn);
        patternCmd.Parameters.AddWithValue("id", patternId);
        
        await using var patternReader = await patternCmd.ExecuteReaderAsync(ct);
        if (!await patternReader.ReadAsync(ct))
            return null;

        var pattern = new PatternDetailDto
        {
            Id = patternReader.GetGuid(0),
            Name = patternReader.GetString(1),
            Category = patternReader.GetString(2),
            Description = patternReader.IsDBNull(3) ? "" : patternReader.GetString(3),
            Confidence = patternReader.GetDouble(4),
            ExampleCount = patternReader.GetInt32(5),
            RoleCount = 0, // Will be set from roles query
            IsSystemPattern = patternReader.GetBoolean(6),
            CreatedAt = patternReader.GetDateTime(7),
            LastMatchedAt = patternReader.IsDBNull(8) ? null : patternReader.GetDateTime(8),
            Roles = new List<PatternRoleDto>(),
            RecentExamples = new List<PatternExampleDto>()
        };

        await patternReader.CloseAsync();

        // Get roles
        var rolesSql = @"
            SELECT id, name, description, naming_patterns, expected_min, expected_max, expected_units, is_required
            FROM pattern_roles WHERE pattern_id = @patternId";
        
        await using var rolesCmd = new NpgsqlCommand(rolesSql, conn);
        rolesCmd.Parameters.AddWithValue("patternId", patternId);
        
        await using var rolesReader = await rolesCmd.ExecuteReaderAsync(ct);
        while (await rolesReader.ReadAsync(ct))
        {
            pattern.Roles.Add(new PatternRoleDto
            {
                Id = rolesReader.GetGuid(0),
                Name = rolesReader.GetString(1),
                Description = rolesReader.IsDBNull(2) ? "" : rolesReader.GetString(2),
                NamingPatterns = ((string[])rolesReader.GetValue(3)).ToList(),
                ExpectedMinValue = rolesReader.IsDBNull(4) ? null : rolesReader.GetDouble(4),
                ExpectedMaxValue = rolesReader.IsDBNull(5) ? null : rolesReader.GetDouble(5),
                ExpectedUnits = rolesReader.IsDBNull(6) ? null : rolesReader.GetString(6),
                IsRequired = rolesReader.GetBoolean(7)
            });
        }

        // Update role count
        pattern = pattern with { RoleCount = pattern.Roles.Count };

        await rolesReader.CloseAsync();

        // Get recent examples (approved clusters)
        var examplesSql = @"
            SELECT c.id, c.common_prefix, c.point_count, s.reviewed_at
            FROM behavioral_clusters c
            JOIN pattern_suggestions s ON c.id = s.cluster_id
            WHERE s.pattern_id = @patternId AND s.status = 'approved'
            ORDER BY s.reviewed_at DESC
            LIMIT 10";
        
        await using var examplesCmd = new NpgsqlCommand(examplesSql, conn);
        examplesCmd.Parameters.AddWithValue("patternId", patternId);
        
        await using var examplesReader = await examplesCmd.ExecuteReaderAsync(ct);
        while (await examplesReader.ReadAsync(ct))
        {
            pattern.RecentExamples.Add(new PatternExampleDto
            {
                ClusterId = examplesReader.GetGuid(0),
                CommonPrefix = examplesReader.IsDBNull(1) ? "" : examplesReader.GetString(1),
                PointCount = examplesReader.GetInt32(2),
                BoundAt = examplesReader.GetDateTime(3)
            });
        }

        return pattern;
    }

    public async Task<IReadOnlyList<PatternDto>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        var sql = @"
            SELECT 
                p.id, p.name, p.category, p.description, p.confidence,
                p.example_count, COUNT(r.id) as role_count, p.is_system_pattern,
                p.created_at, p.last_matched_at
            FROM patterns p
            LEFT JOIN pattern_roles r ON p.id = r.pattern_id
            WHERE LOWER(p.category) = LOWER(@category)
            GROUP BY p.id
            ORDER BY p.confidence DESC";

        var patterns = new List<PatternDto>();
        
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("category", category);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        while (await reader.ReadAsync(ct))
        {
            patterns.Add(new PatternDto
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                Category = reader.GetString(2),
                Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Confidence = reader.GetDouble(4),
                ExampleCount = reader.GetInt32(5),
                RoleCount = reader.GetInt32(6),
                IsSystemPattern = reader.GetBoolean(7),
                CreatedAt = reader.GetDateTime(8),
                LastMatchedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
            });
        }

        return patterns;
    }

    public async Task<PatternStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var sql = @"
            SELECT 
                COUNT(*) as total,
                COUNT(*) FILTER (WHERE is_system_pattern = true) as system_patterns,
                COUNT(*) FILTER (WHERE is_system_pattern = false) as learned_patterns,
                AVG(confidence) as avg_confidence
            FROM patterns";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        
        if (!await reader.ReadAsync(ct))
            return new PatternStatsDto
            {
                TotalPatterns = 0, SystemPatterns = 0, LearnedPatterns = 0,
                AverageConfidence = 0, TotalBindings = 0, MostConfident = null, MostUsed = null
            };

        var stats = new PatternStatsDto
        {
            TotalPatterns = reader.GetInt32(0),
            SystemPatterns = reader.GetInt32(1),
            LearnedPatterns = reader.GetInt32(2),
            AverageConfidence = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
            TotalBindings = 0, // TODO: Count from point_pattern_bindings
            MostConfident = null,
            MostUsed = null
        };

        await reader.CloseAsync();

        // Get binding count
        await using var bindingsCmd = new NpgsqlCommand("SELECT COUNT(*) FROM point_pattern_bindings", conn);
        var bindingsCount = await bindingsCmd.ExecuteScalarAsync(ct);
        stats = stats with { TotalBindings = Convert.ToInt32(bindingsCount) };

        return stats;
    }
}

