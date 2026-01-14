using System.Text.Json;
using System.Text.RegularExpressions;
using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace Naia.PatternEngine.Jobs;

/// <summary>
/// Proactive pattern matching that runs immediately when points are registered.
/// Unlike the cluster-based PatternMatchingJob that waits for behavioral analysis,
/// this job uses the Knowledge Base (abbreviations, naming conventions, unit mappings)
/// to suggest patterns based purely on tag names, units, and metadata.
/// 
/// This provides IMMEDIATE value for well-named tags without waiting 7 days
/// for correlation analysis to complete.
/// 
/// The job can be triggered:
/// 1. Manually via API when points are imported
/// 2. Automatically when a data source's points are first registered
/// 3. Periodically to catch any missed points
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 300)]
public sealed class ProactivePatternMatchingJob : IProactivePatternMatchingJob
{
    private readonly ILogger<ProactivePatternMatchingJob> _logger;
    private readonly PatternFlywheelOptions _options;
    private readonly string _postgresConnectionString;

    // Weights for proactive matching (different from cluster-based)
    // More weight on naming since we don't have behavioral data yet
    private const double NamingWeight = 0.50;
    private const double UnitMatchWeight = 0.25;
    private const double MetadataWeight = 0.15;
    private const double KnowledgeBoostWeight = 0.10;

    public ProactivePatternMatchingJob(
        ILogger<ProactivePatternMatchingJob> logger,
        IOptions<PatternFlywheelOptions> options,
        string postgresConnectionString)
    {
        _logger = logger;
        _options = options.Value;
        _postgresConnectionString = postgresConnectionString;
    }

    /// <summary>
    /// Match all unanalyzed points against patterns using knowledge base.
    /// </summary>
    public async Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken)
    {
        context?.WriteLine("Starting proactive pattern matching...");
        _logger.LogInformation("Starting proactive pattern matching job");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var pointsAnalyzed = 0;
        var suggestionsCreated = 0;

        try
        {
            // Load points that haven't been proactively analyzed yet
            var points = await LoadUnanalyzedPointsAsync(cancellationToken);
            context?.WriteLine($"Found {points.Count} points to analyze");

            if (points.Count == 0)
            {
                context?.WriteLine("No unanalyzed points found");
                return;
            }

            // Load pattern library with roles
            var patterns = await LoadPatternsWithRolesAsync(cancellationToken);
            context?.WriteLine($"Loaded {patterns.Count} patterns");

            // Load knowledge base
            var abbreviations = await LoadAbbreviationsAsync(cancellationToken);
            var unitMappings = await LoadUnitMappingsAsync(cancellationToken);
            var namingConventions = await LoadNamingConventionsAsync(cancellationToken);
            context?.WriteLine($"Knowledge base loaded: {abbreviations.Count} abbreviations, {unitMappings.Count} units, {namingConventions.Count} conventions");

            // Group points by common prefix (likely same equipment)
            var pointGroups = GroupPointsByPrefix(points);
            context?.WriteLine($"Grouped into {pointGroups.Count} potential equipment groups");

            var progressBar = context?.WriteProgressBar();
            var processed = 0;

            foreach (var group in pointGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Parse each point name using knowledge base
                var parsedPoints = group.Points.Select(p => ParsePointName(p, abbreviations, unitMappings)).ToList();

                // Score against patterns
                var matches = new List<ProactiveMatch>();

                foreach (var pattern in patterns)
                {
                    var match = ScoreProactiveMatch(group, parsedPoints, pattern, namingConventions);
                    if (match.OverallConfidence >= 0.40) // Lower threshold for proactive
                    {
                        match.RoleAssignments = AssignRoles(parsedPoints, pattern);
                        matches.Add(match);
                    }
                }

                // Create suggestions for top matches
                var topMatches = matches
                    .OrderByDescending(m => m.OverallConfidence)
                    .Take(3)
                    .ToList();

                foreach (var match in topMatches)
                {
                    // Create a temporary cluster for this point group
                    var clusterId = await CreateProactiveClusterAsync(group, cancellationToken);
                    await CreateProactiveSuggestionAsync(clusterId, match, group, cancellationToken);
                    suggestionsCreated++;
                }

                pointsAnalyzed += group.Points.Count;
                processed++;
                progressBar?.SetValue((double)processed / pointGroups.Count * 100);
            }

            // Mark points as proactively analyzed
            await MarkPointsAnalyzedAsync(points.Select(p => p.Id).ToList(), cancellationToken);

            stopwatch.Stop();
            context?.WriteLine($"Completed: {pointsAnalyzed} points analyzed, {suggestionsCreated} suggestions created in {stopwatch.ElapsedMilliseconds}ms");
            _logger.LogInformation("Proactive matching complete: {Points} points, {Suggestions} suggestions in {Elapsed}ms",
                pointsAnalyzed, suggestionsCreated, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in proactive pattern matching");
            context?.WriteLine($"ERROR: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Match a specific set of points (e.g., just imported).
    /// </summary>
    public async Task MatchPointsAsync(List<Guid> pointIds, PerformContext? context, CancellationToken cancellationToken)
    {
        context?.WriteLine($"Proactive matching for {pointIds.Count} specific points...");
        
        // Load just these points
        var points = await LoadPointsByIdsAsync(pointIds, cancellationToken);
        
        if (points.Count == 0) return;

        // Load knowledge base
        var abbreviations = await LoadAbbreviationsAsync(cancellationToken);
        var unitMappings = await LoadUnitMappingsAsync(cancellationToken);
        var namingConventions = await LoadNamingConventionsAsync(cancellationToken);
        var patterns = await LoadPatternsWithRolesAsync(cancellationToken);

        // Group and match
        var pointGroups = GroupPointsByPrefix(points);

        foreach (var group in pointGroups)
        {
            var parsedPoints = group.Points.Select(p => ParsePointName(p, abbreviations, unitMappings)).ToList();

            foreach (var pattern in patterns)
            {
                var match = ScoreProactiveMatch(group, parsedPoints, pattern, namingConventions);
                if (match.OverallConfidence >= 0.40)
                {
                    match.RoleAssignments = AssignRoles(parsedPoints, pattern);
                    var clusterId = await CreateProactiveClusterAsync(group, cancellationToken);
                    await CreateProactiveSuggestionAsync(clusterId, match, group, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Execute proactive pattern matching for a specific data source.
    /// </summary>
    public async Task ExecuteForDataSourceAsync(Guid dataSourceId, PerformContext? context, CancellationToken cancellationToken)
    {
        context?.WriteLine($"Proactive matching for data source {dataSourceId}...");
        _logger.LogInformation("Starting proactive pattern matching for data source {DataSourceId}", dataSourceId);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var suggestionsCreated = 0;

        try
        {
            // Load points for this specific data source
            var points = await LoadPointsForDataSourceAsync(dataSourceId, cancellationToken);
            context?.WriteLine($"Found {points.Count} points in data source");

            if (points.Count == 0)
            {
                context?.WriteLine("No points found for data source");
                return;
            }

            // Load knowledge base and patterns
            var abbreviations = await LoadAbbreviationsAsync(cancellationToken);
            var unitMappings = await LoadUnitMappingsAsync(cancellationToken);
            var namingConventions = await LoadNamingConventionsAsync(cancellationToken);
            var patterns = await LoadPatternsWithRolesAsync(cancellationToken);

            // Group and match
            var pointGroups = GroupPointsByPrefix(points);
            context?.WriteLine($"Grouped into {pointGroups.Count} potential equipment groups");

            var progressBar = context?.WriteProgressBar();
            var processed = 0;

            foreach (var group in pointGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var parsedPoints = group.Points.Select(p => ParsePointName(p, abbreviations, unitMappings)).ToList();

                foreach (var pattern in patterns)
                {
                    var match = ScoreProactiveMatch(group, parsedPoints, pattern, namingConventions);
                    if (match.OverallConfidence >= 0.40)
                    {
                        match.RoleAssignments = AssignRoles(parsedPoints, pattern);
                        var clusterId = await CreateProactiveClusterAsync(group, cancellationToken);
                        await CreateProactiveSuggestionAsync(clusterId, match, group, cancellationToken);
                        suggestionsCreated++;
                    }
                }

                processed++;
                progressBar?.SetValue((double)processed / pointGroups.Count * 100);
            }

            stopwatch.Stop();
            context?.WriteLine($"Completed: {points.Count} points analyzed, {suggestionsCreated} suggestions in {stopwatch.ElapsedMilliseconds}ms");
            _logger.LogInformation("Proactive matching for data source complete: {Points} points, {Suggestions} suggestions in {Elapsed}ms",
                points.Count, suggestionsCreated, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in proactive pattern matching for data source {DataSourceId}", dataSourceId);
            context?.WriteLine($"ERROR: {ex.Message}");
            throw;
        }
    }
    
    private async Task<List<ProactivePointInfo>> LoadPointsForDataSourceAsync(Guid dataSourceId, CancellationToken ct)
    {
        var points = new List<ProactivePointInfo>();
        
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            SELECT p.id, p.name, p.description, p.engineering_units, p.source_address, p.data_source_id
            FROM points p
            WHERE p.is_enabled = true
              AND p.data_source_id = @dataSourceId
            ORDER BY p.name";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@dataSourceId", dataSourceId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            points.Add(new ProactivePointInfo
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                EngineeringUnits = reader.IsDBNull(3) ? null : reader.GetString(3),
                SourceAddress = reader.IsDBNull(4) ? null : reader.GetString(4),
                DataSourceId = reader.IsDBNull(5) ? null : reader.GetGuid(5)
            });
        }

        return points;
    }

    #region Point Loading

    private async Task<List<ProactivePointInfo>> LoadUnanalyzedPointsAsync(CancellationToken ct)
    {
        var points = new List<ProactivePointInfo>();
        
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(ct);

        // Points that haven't been proactively analyzed and have no binding
        const string sql = @"
            SELECT p.id, p.name, p.description, p.engineering_units, p.source_address, p.data_source_id
            FROM points p
            LEFT JOIN point_pattern_bindings ppb ON p.id = ppb.point_id
            WHERE p.is_enabled = true
              AND ppb.id IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM behavioral_clusters bc 
                  WHERE p.id = ANY(bc.point_ids) 
                  AND bc.source_type = 'Proactive'
                  AND bc.created_at > NOW() - INTERVAL '24 hours'
              )
            ORDER BY p.created_at DESC
            LIMIT 500";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            points.Add(new ProactivePointInfo
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                EngineeringUnits = reader.IsDBNull(3) ? null : reader.GetString(3),
                SourceAddress = reader.IsDBNull(4) ? null : reader.GetString(4),
                DataSourceId = reader.IsDBNull(5) ? null : reader.GetGuid(5)
            });
        }

        return points;
    }

    private async Task<List<ProactivePointInfo>> LoadPointsByIdsAsync(List<Guid> pointIds, CancellationToken ct)
    {
        var points = new List<ProactivePointInfo>();
        if (pointIds.Count == 0) return points;

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            SELECT id, name, description, engineering_units, source_address, data_source_id
            FROM points
            WHERE id = ANY(@ids) AND is_enabled = true";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ids", pointIds.ToArray());
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            points.Add(new ProactivePointInfo
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                EngineeringUnits = reader.IsDBNull(3) ? null : reader.GetString(3),
                SourceAddress = reader.IsDBNull(4) ? null : reader.GetString(4),
                DataSourceId = reader.IsDBNull(5) ? null : reader.GetGuid(5)
            });
        }

        return points;
    }

    #endregion

    #region Knowledge Base Loading

    private async Task<Dictionary<string, AbbreviationInfo>> LoadAbbreviationsAsync(CancellationToken ct)
    {
        var abbrevs = new Dictionary<string, AbbreviationInfo>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            SELECT abbreviation, expansion, context, priority, measurement_type_id
            FROM knowledge_abbreviations
            ORDER BY priority DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var abbrev = reader.GetString(0);
            if (!abbrevs.ContainsKey(abbrev)) // Keep highest priority
            {
                abbrevs[abbrev] = new AbbreviationInfo
                {
                    Abbreviation = abbrev,
                    Expansion = reader.GetString(1),
                    Context = reader.IsDBNull(2) ? "General" : reader.GetString(2),
                    Priority = reader.GetInt32(3),
                    MeasurementTypeId = reader.IsDBNull(4) ? null : reader.GetGuid(4)
                };
            }
        }

        return abbrevs;
    }

    private async Task<Dictionary<string, UnitMappingInfo>> LoadUnitMappingsAsync(CancellationToken ct)
    {
        var units = new Dictionary<string, UnitMappingInfo>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            SELECT kum.unit_symbol, kum.unit_name, kmt.canonical_name as measurement_type
            FROM knowledge_unit_mappings kum
            JOIN knowledge_measurement_types kmt ON kmt.id = kum.measurement_type_id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var symbol = reader.GetString(0);
            units[symbol] = new UnitMappingInfo
            {
                UnitSymbol = symbol,
                UnitName = reader.IsDBNull(1) ? null : reader.GetString(1),
                MeasurementType = reader.GetString(2)
            };
        }

        return units;
    }

    private async Task<List<NamingConventionInfo>> LoadNamingConventionsAsync(CancellationToken ct)
    {
        var conventions = new List<NamingConventionInfo>();

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            SELECT knc.pattern_regex, knc.pattern_description, knc.confidence_boost, kis.standard_code
            FROM knowledge_naming_conventions knc
            LEFT JOIN knowledge_industry_standards kis ON kis.id = knc.standard_id
            ORDER BY knc.priority DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            conventions.Add(new NamingConventionInfo
            {
                PatternRegex = reader.GetString(0),
                Description = reader.IsDBNull(1) ? null : reader.GetString(1),
                ConfidenceBoost = reader.GetDouble(2),
                StandardCode = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return conventions;
    }

    private async Task<List<PatternWithRoles>> LoadPatternsWithRolesAsync(CancellationToken ct)
    {
        var patterns = new List<PatternWithRoles>();
        var patternMap = new Dictionary<Guid, PatternWithRoles>();

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(ct);

        // Load patterns
        const string patternSql = @"
            SELECT id, name, category, description, confidence
            FROM patterns
            WHERE confidence >= 0.30
            ORDER BY confidence DESC";

        await using (var cmd = new NpgsqlCommand(patternSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var pattern = new PatternWithRoles
                {
                    Id = reader.GetGuid(0),
                    Name = reader.GetString(1),
                    Category = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Confidence = reader.GetDouble(4),
                    Roles = new List<ProactivePatternRoleInfo>()
                };
                patterns.Add(pattern);
                patternMap[pattern.Id] = pattern;
            }
        }

        // Load roles
        const string rolesSql = @"
            SELECT pattern_id, id, name, naming_patterns, expected_units, 
                   expected_min, expected_max, is_required, weight
            FROM pattern_roles";

        await using (var cmd = new NpgsqlCommand(rolesSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var patternId = reader.GetGuid(0);
                if (patternMap.TryGetValue(patternId, out var pattern))
                {
                    pattern.Roles.Add(new ProactivePatternRoleInfo
                    {
                        Id = reader.GetGuid(1),
                        Name = reader.GetString(2),
                        NamingPatterns = reader.IsDBNull(3) ? new List<string>() : 
                            ((string[])reader.GetValue(3)).ToList(),
                        ExpectedUnits = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ExpectedMin = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                        ExpectedMax = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                        IsRequired = reader.GetBoolean(7),
                        Weight = reader.GetDouble(8)
                    });
                }
            }
        }

        return patterns;
    }

    #endregion

    #region Parsing & Grouping

    private List<PointGroup> GroupPointsByPrefix(List<ProactivePointInfo> points)
    {
        // Extract common prefixes from point names
        // e.g., "KSH_001_WindSpeed", "KSH_001_Power" → prefix "KSH_001"
        
        var groups = new Dictionary<string, List<ProactivePointInfo>>();

        foreach (var point in points)
        {
            var prefix = ExtractPrefix(point.Name);
            if (!groups.ContainsKey(prefix))
                groups[prefix] = new List<ProactivePointInfo>();
            groups[prefix].Add(point);
        }

        // Filter to groups with at least 3 points (minimum for pattern matching)
        return groups
            .Where(g => g.Value.Count >= 3)
            .Select(g => new PointGroup
            {
                Prefix = g.Key,
                Points = g.Value,
                DataSourceId = g.Value.First().DataSourceId
            })
            .ToList();
    }

    private string ExtractPrefix(string name)
    {
        // Try various delimiters and patterns
        var patterns = new[]
        {
            @"^([A-Za-z]+_?\d+)_", // e.g., KSH_001_
            @"^([A-Za-z]+\d+)\.", // e.g., WTG01.
            @"^([A-Za-z]{2,4}\d{1,4})[-_]", // e.g., WT01-
            @"^([A-Za-z]+)_" // e.g., PUMP_
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(name, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value.ToUpperInvariant();
        }

        // Fallback: use first segment before underscore/dot
        var firstSep = name.IndexOfAny(new[] { '_', '.', '-' });
        if (firstSep > 0)
            return name.Substring(0, firstSep).ToUpperInvariant();

        return "UNGROUPED";
    }

    private ParsedPoint ParsePointName(
        ProactivePointInfo point, 
        Dictionary<string, AbbreviationInfo> abbreviations,
        Dictionary<string, UnitMappingInfo> unitMappings)
    {
        var parsed = new ParsedPoint
        {
            Point = point,
            Tokens = new List<ParsedToken>(),
            InferredMeasurementType = null,
            MatchedAbbreviations = new List<string>()
        };

        // Split name into tokens
        var rawTokens = Regex.Split(point.Name, @"[_.\-\s]+")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        foreach (var token in rawTokens)
        {
            var parsedToken = new ParsedToken { Raw = token };

            // Check if it's in our abbreviation dictionary
            if (abbreviations.TryGetValue(token.ToUpperInvariant(), out var abbrev))
            {
                parsedToken.Expansion = abbrev.Expansion;
                parsedToken.MeasurementType = abbrev.MeasurementTypeId.HasValue ? 
                    GetMeasurementTypeName(abbrev.MeasurementTypeId.Value) : null;
                parsed.MatchedAbbreviations.Add(token);

                if (parsedToken.MeasurementType != null && parsed.InferredMeasurementType == null)
                    parsed.InferredMeasurementType = parsedToken.MeasurementType;
            }
            else if (int.TryParse(token, out _))
            {
                parsedToken.IsNumeric = true;
            }

            parsed.Tokens.Add(parsedToken);
        }

        // Try to infer measurement type from engineering units
        if (parsed.InferredMeasurementType == null && !string.IsNullOrEmpty(point.EngineeringUnits))
        {
            if (unitMappings.TryGetValue(point.EngineeringUnits, out var unitInfo))
            {
                parsed.InferredMeasurementType = unitInfo.MeasurementType;
            }
        }

        return parsed;
    }

    private string? GetMeasurementTypeName(Guid measurementTypeId)
    {
        // This would be cached in production
        return null; // Simplified - would query knowledge_measurement_types
    }

    #endregion

    #region Scoring

    private ProactiveMatch ScoreProactiveMatch(
        PointGroup group,
        List<ParsedPoint> parsedPoints,
        PatternWithRoles pattern,
        List<NamingConventionInfo> conventions)
    {
        var match = new ProactiveMatch
        {
            PatternId = pattern.Id,
            PatternName = pattern.Name,
            PatternCategory = pattern.Category
        };

        // 1. Naming Score (50%): How well do point names match role naming patterns?
        var namingScore = CalculateNamingScore(parsedPoints, pattern.Roles);
        
        // 2. Unit Match Score (25%): Do units align with expected units for roles?
        var unitScore = CalculateUnitScore(parsedPoints, pattern.Roles);
        
        // 3. Metadata Score (15%): Description/source address contains relevant terms?
        var metadataScore = CalculateMetadataScore(parsedPoints, pattern);
        
        // 4. Knowledge Boost (10%): Bonus from matching industry standard conventions
        var knowledgeBoost = CalculateKnowledgeBoost(group.Prefix, parsedPoints, conventions);

        match.NamingScore = namingScore;
        match.UnitMatchScore = unitScore;
        match.MetadataScore = metadataScore;
        match.KnowledgeBoost = knowledgeBoost;

        match.OverallConfidence = 
            (namingScore * NamingWeight) +
            (unitScore * UnitMatchWeight) +
            (metadataScore * MetadataWeight) +
            (knowledgeBoost * KnowledgeBoostWeight);

        // Apply pattern's own confidence as a multiplier
        match.OverallConfidence *= pattern.Confidence;

        // Build explanation
        match.Explanation = BuildExplanation(match, pattern, parsedPoints);

        return match;
    }

    private double CalculateNamingScore(List<ParsedPoint> points, List<ProactivePatternRoleInfo> roles)
    {
        if (roles.Count == 0) return 0;

        var matchedRoles = 0;
        var requiredRoles = roles.Count(r => r.IsRequired);
        var totalWeight = roles.Sum(r => r.Weight);
        var matchedWeight = 0.0;

        foreach (var role in roles)
        {
            var matched = false;
            foreach (var pattern in role.NamingPatterns)
            {
                try
                {
                    var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                    if (points.Any(p => regex.IsMatch(p.Point.Name)))
                    {
                        matched = true;
                        matchedRoles++;
                        matchedWeight += role.Weight;
                        break;
                    }
                }
                catch { /* Invalid regex, skip */ }
            }
        }

        // Score based on weighted role coverage
        return totalWeight > 0 ? matchedWeight / totalWeight : 0;
    }

    private double CalculateUnitScore(List<ParsedPoint> points, List<ProactivePatternRoleInfo> roles)
    {
        var rolesWithUnits = roles.Where(r => !string.IsNullOrEmpty(r.ExpectedUnits)).ToList();
        if (rolesWithUnits.Count == 0) return 0.5; // Neutral if no unit expectations

        var matches = 0;
        foreach (var role in rolesWithUnits)
        {
            // Find a point that matches this role's naming pattern
            foreach (var point in points)
            {
                var matchesNaming = role.NamingPatterns.Any(p =>
                {
                    try { return Regex.IsMatch(point.Point.Name, p, RegexOptions.IgnoreCase); }
                    catch { return false; }
                });

                if (matchesNaming && 
                    !string.IsNullOrEmpty(point.Point.EngineeringUnits) &&
                    point.Point.EngineeringUnits.Equals(role.ExpectedUnits, StringComparison.OrdinalIgnoreCase))
                {
                    matches++;
                    break;
                }
            }
        }

        return (double)matches / rolesWithUnits.Count;
    }

    private double CalculateMetadataScore(List<ParsedPoint> points, PatternWithRoles pattern)
    {
        var score = 0.0;
        var relevantTerms = pattern.Category.ToLowerInvariant().Split(' ')
            .Concat(pattern.Name.ToLowerInvariant().Split(' '))
            .Distinct()
            .Where(t => t.Length > 2)
            .ToList();

        foreach (var point in points)
        {
            var description = point.Point.Description?.ToLowerInvariant() ?? "";
            var sourceAddress = point.Point.SourceAddress?.ToLowerInvariant() ?? "";
            var combined = $"{description} {sourceAddress}";

            var matchCount = relevantTerms.Count(term => combined.Contains(term));
            if (matchCount > 0)
                score += (double)matchCount / relevantTerms.Count;
        }

        return Math.Min(1.0, score / points.Count);
    }

    private double CalculateKnowledgeBoost(
        string prefix,
        List<ParsedPoint> points,
        List<NamingConventionInfo> conventions)
    {
        var boost = 0.0;
        var firstPointName = points.FirstOrDefault()?.Point.Name ?? "";

        foreach (var convention in conventions)
        {
            try
            {
                if (Regex.IsMatch(firstPointName, convention.PatternRegex, RegexOptions.IgnoreCase))
                {
                    boost = Math.Max(boost, convention.ConfidenceBoost);
                }
            }
            catch { /* Invalid regex */ }
        }

        // Bonus for matching abbreviations (indicates well-structured naming)
        var avgAbbrevMatches = points.Average(p => p.MatchedAbbreviations.Count);
        if (avgAbbrevMatches >= 2)
            boost += 0.05;

        return Math.Min(1.0, boost);
    }

    private List<RoleAssignment> AssignRoles(List<ParsedPoint> points, PatternWithRoles pattern)
    {
        var assignments = new List<RoleAssignment>();

        foreach (var role in pattern.Roles)
        {
            ParsedPoint? bestMatch = null;
            double bestScore = 0;

            foreach (var point in points)
            {
                var score = 0.0;

                // Check naming pattern match
                var namingMatch = role.NamingPatterns.Any(p =>
                {
                    try { return Regex.IsMatch(point.Point.Name, p, RegexOptions.IgnoreCase); }
                    catch { return false; }
                });
                if (namingMatch) score += 0.6;

                // Check unit match
                if (!string.IsNullOrEmpty(role.ExpectedUnits) &&
                    point.Point.EngineeringUnits?.Equals(role.ExpectedUnits, StringComparison.OrdinalIgnoreCase) == true)
                {
                    score += 0.3;
                }

                // Check inferred measurement type match
                if (point.InferredMeasurementType != null &&
                    role.Name.Contains(point.InferredMeasurementType, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.1;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = point;
                }
            }

            if (bestMatch != null && bestScore >= 0.3)
            {
                assignments.Add(new RoleAssignment
                {
                    RoleId = role.Id,
                    RoleName = role.Name,
                    PointId = bestMatch.Point.Id,
                    PointName = bestMatch.Point.Name,
                    Confidence = bestScore,
                    MatchReason = bestScore >= 0.6 ? "Name pattern match" : 
                                  bestScore >= 0.3 ? "Unit or type inference" : "Weak match"
                });
            }
        }

        return assignments;
    }

    private MatchExplanation BuildExplanation(ProactiveMatch match, PatternWithRoles pattern, List<ParsedPoint> points)
    {
        var explanation = new MatchExplanation
        {
            Summary = $"Matched {points.Count} points to '{pattern.Name}' ({pattern.Category}) " +
                      $"with {match.OverallConfidence:P0} confidence based on naming analysis.",
            
            ScoreBreakdown = new Dictionary<string, ScoreDetail>
            {
                ["Naming"] = new ScoreDetail
                {
                    Score = match.NamingScore,
                    Weight = NamingWeight,
                    WeightedScore = match.NamingScore * NamingWeight,
                    Details = $"Matched {pattern.Roles.Count(r => r.IsRequired)} required roles by name patterns"
                },
                ["Units"] = new ScoreDetail
                {
                    Score = match.UnitMatchScore,
                    Weight = UnitMatchWeight,
                    WeightedScore = match.UnitMatchScore * UnitMatchWeight,
                    Details = $"Unit alignment with pattern expectations"
                },
                ["Metadata"] = new ScoreDetail
                {
                    Score = match.MetadataScore,
                    Weight = MetadataWeight,
                    WeightedScore = match.MetadataScore * MetadataWeight,
                    Details = "Description/address contains relevant terms"
                },
                ["Knowledge"] = new ScoreDetail
                {
                    Score = match.KnowledgeBoost,
                    Weight = KnowledgeBoostWeight,
                    WeightedScore = match.KnowledgeBoost * KnowledgeBoostWeight,
                    Details = "Matches industry standard naming conventions"
                }
            },

            MatchedRoles = match.RoleAssignments.Select(ra => new RoleMatchDetail
            {
                RoleName = ra.RoleName,
                PointName = ra.PointName,
                Confidence = ra.Confidence,
                MatchedPattern = ra.MatchReason
            }).ToList(),

            ParsedTokens = points.Take(5).SelectMany(p => 
                p.MatchedAbbreviations.Select(a => new TokenExplanation
                {
                    Token = a,
                    Expansion = p.Tokens.FirstOrDefault(t => t.Raw.Equals(a, StringComparison.OrdinalIgnoreCase))?.Expansion,
                    PointName = p.Point.Name
                })).Distinct().ToList()
        };

        return explanation;
    }

    #endregion

    #region Persistence

    private async Task<Guid> CreateProactiveClusterAsync(PointGroup group, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(ct);

        // Generate deterministic cluster ID based on point IDs
        var sortedIds = group.Points.Select(p => p.Id).OrderBy(id => id).ToArray();
        var clusterId = GenerateDeterministicGuid(sortedIds);

        const string sql = @"
            INSERT INTO behavioral_clusters (
                id, source_type, source_id, data_source_id,
                point_ids, point_names, point_count,
                common_prefix, status, created_at
            ) VALUES (
                @id, 'Proactive', @prefix, @dsId,
                @pointIds, @pointNames, @count,
                @prefix, 'pending', NOW()
            )
            ON CONFLICT (id) DO UPDATE SET updated_at = NOW()
            RETURNING id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", clusterId);
        cmd.Parameters.AddWithValue("@prefix", group.Prefix);
        cmd.Parameters.AddWithValue("@dsId", group.DataSourceId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@pointIds", sortedIds);
        cmd.Parameters.AddWithValue("@pointNames", group.Points.Select(p => p.Name).ToArray());
        cmd.Parameters.AddWithValue("@count", group.Points.Count);

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private async Task CreateProactiveSuggestionAsync(
        Guid clusterId, 
        ProactiveMatch match, 
        PointGroup group, 
        CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
            INSERT INTO pattern_suggestions (
                cluster_id, pattern_id,
                overall_confidence, naming_score, correlation_score, range_score, rate_score,
                reason, status, created_at
            ) VALUES (
                @clusterId, @patternId,
                @confidence, @naming, @correlation, @range, @rate,
                @reason, 'pending', NOW()
            )
            ON CONFLICT (cluster_id, pattern_id) DO UPDATE SET
                overall_confidence = EXCLUDED.overall_confidence,
                reason = EXCLUDED.reason,
                created_at = NOW()";

        var reasonJson = JsonSerializer.Serialize(match.Explanation);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@clusterId", clusterId);
        cmd.Parameters.AddWithValue("@patternId", match.PatternId);
        cmd.Parameters.AddWithValue("@confidence", match.OverallConfidence);
        cmd.Parameters.AddWithValue("@naming", match.NamingScore);
        cmd.Parameters.AddWithValue("@correlation", 0.0); // No correlation data yet
        cmd.Parameters.AddWithValue("@range", match.UnitMatchScore); // Use unit score as proxy
        cmd.Parameters.AddWithValue("@rate", match.MetadataScore); // Use metadata as proxy
        cmd.Parameters.AddWithValue("@reason", reasonJson);

        await cmd.ExecuteNonQueryAsync(ct);

        // Store role assignments separately
        if (match.RoleAssignments.Any())
        {
            await StoreRoleAssignmentsAsync(conn, clusterId, match.PatternId, match.RoleAssignments, ct);
        }
    }

    private async Task StoreRoleAssignmentsAsync(
        NpgsqlConnection conn,
        Guid clusterId,
        Guid patternId,
        List<RoleAssignment> assignments,
        CancellationToken ct)
    {
        // Store in a JSON column or separate table - for now, we'll update the suggestion
        // In production, you'd have a suggestion_role_assignments table
        const string sql = @"
            UPDATE pattern_suggestions 
            SET reason = reason || @roleData::jsonb
            WHERE cluster_id = @clusterId AND pattern_id = @patternId";

        var roleData = JsonSerializer.Serialize(new { role_assignments = assignments });

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@clusterId", clusterId);
        cmd.Parameters.AddWithValue("@patternId", patternId);
        cmd.Parameters.AddWithValue("@roleData", NpgsqlDbType.Jsonb, roleData);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task MarkPointsAnalyzedAsync(List<Guid> pointIds, CancellationToken ct)
    {
        // Could add a proactive_analyzed_at column to points table
        // For now, the cluster creation serves as the marker
        await Task.CompletedTask;
    }

    private Guid GenerateDeterministicGuid(Guid[] sortedIds)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = sortedIds.SelectMany(id => id.ToByteArray()).ToArray();
        var hash = md5.ComputeHash(bytes);
        return new Guid(hash);
    }

    #endregion
}

#region Data Transfer Objects

internal sealed record ProactivePointInfo
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? EngineeringUnits { get; init; }
    public string? SourceAddress { get; init; }
    public Guid? DataSourceId { get; init; }
}

internal sealed record PointGroup
{
    public required string Prefix { get; init; }
    public required List<ProactivePointInfo> Points { get; init; }
    public Guid? DataSourceId { get; init; }
}

internal sealed record ParsedPoint
{
    public required ProactivePointInfo Point { get; init; }
    public required List<ParsedToken> Tokens { get; init; }
    public string? InferredMeasurementType { get; set; }
    public required List<string> MatchedAbbreviations { get; init; }
}

internal sealed record ParsedToken
{
    public required string Raw { get; init; }
    public string? Expansion { get; set; }
    public string? MeasurementType { get; set; }
    public bool IsNumeric { get; set; }
}

internal sealed record AbbreviationInfo
{
    public required string Abbreviation { get; init; }
    public required string Expansion { get; init; }
    public required string Context { get; init; }
    public int Priority { get; init; }
    public Guid? MeasurementTypeId { get; init; }
}

internal sealed record UnitMappingInfo
{
    public required string UnitSymbol { get; init; }
    public string? UnitName { get; init; }
    public required string MeasurementType { get; init; }
}

internal sealed record NamingConventionInfo
{
    public required string PatternRegex { get; init; }
    public string? Description { get; init; }
    public double ConfidenceBoost { get; init; }
    public string? StandardCode { get; init; }
}

internal sealed record PatternWithRoles
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string? Description { get; init; }
    public double Confidence { get; init; }
    public required List<ProactivePatternRoleInfo> Roles { get; init; }
}

internal sealed record ProactivePatternRoleInfo
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required List<string> NamingPatterns { get; init; }
    public string? ExpectedUnits { get; init; }
    public double? ExpectedMin { get; init; }
    public double? ExpectedMax { get; init; }
    public bool IsRequired { get; init; }
    public double Weight { get; init; }
}

internal sealed record ProactiveMatch
{
    public Guid PatternId { get; init; }
    public required string PatternName { get; init; }
    public required string PatternCategory { get; init; }
    public double NamingScore { get; set; }
    public double UnitMatchScore { get; set; }
    public double MetadataScore { get; set; }
    public double KnowledgeBoost { get; set; }
    public double OverallConfidence { get; set; }
    public List<RoleAssignment> RoleAssignments { get; set; } = new();
    public MatchExplanation Explanation { get; set; } = null!;
}

internal sealed record RoleAssignment
{
    public Guid RoleId { get; init; }
    public required string RoleName { get; init; }
    public Guid PointId { get; init; }
    public required string PointName { get; init; }
    public double Confidence { get; init; }
    public required string MatchReason { get; init; }
}

public sealed record MatchExplanation
{
    public required string Summary { get; init; }
    public required Dictionary<string, ScoreDetail> ScoreBreakdown { get; init; }
    public required List<RoleMatchDetail> MatchedRoles { get; init; }
    public required List<TokenExplanation> ParsedTokens { get; init; }
}

public sealed record ScoreDetail
{
    public double Score { get; init; }
    public double Weight { get; init; }
    public double WeightedScore { get; init; }
    public required string Details { get; init; }
}

public sealed record RoleMatchDetail
{
    public required string RoleName { get; init; }
    public required string PointName { get; init; }
    public double Confidence { get; init; }
    public required string MatchedPattern { get; init; }
}

public sealed record TokenExplanation
{
    public required string Token { get; init; }
    public string? Expansion { get; init; }
    public required string PointName { get; init; }
}

#endregion
