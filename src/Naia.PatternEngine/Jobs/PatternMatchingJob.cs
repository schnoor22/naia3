using System.Text.Json;
using System.Text.RegularExpressions;
using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Npgsql;
using StackExchange.Redis;

namespace Naia.PatternEngine.Jobs;

/// <summary>
/// Matches detected clusters against the pattern library using multi-factor scoring.
/// This is the fourth stage of the Pattern Flywheel - identifying what equipment
/// each cluster represents.
/// 
/// Scoring weights:
/// - 30% Naming similarity (regex patterns against point names)
/// - 40% Correlation patterns (behavioral fingerprint match)
/// - 20% Value range similarity
/// - 10% Update rate similarity
/// 
/// Creates suggestions for matches above 50% confidence.
/// Runs every 15 minutes via Hangfire scheduler, after cluster detection.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 840)]
public sealed class PatternMatchingJob : IPatternMatchingJob
{
    private readonly ILogger<PatternMatchingJob> _logger;
    private readonly PatternFlywheelOptions _options;
    private readonly IConnectionMultiplexer _redis;
    private readonly string _postgresConnectionString;

    private const double NamingWeight = 0.30;
    private const double CorrelationWeight = 0.40;
    private const double RangeWeight = 0.20;
    private const double RateWeight = 0.10;

    public PatternMatchingJob(
        ILogger<PatternMatchingJob> logger,
        IOptions<PatternFlywheelOptions> options,
        IConnectionMultiplexer redis,
        string postgresConnectionString)
    {
        _logger = logger;
        _options = options.Value;
        _redis = redis;
        _postgresConnectionString = postgresConnectionString;
    }

    public async Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken)
    {
        context?.WriteLine("Starting pattern matching job...");
        _logger.LogInformation("Starting pattern matching job");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var clustersProcessed = 0;
        var suggestionsCreated = 0;

        try
        {
            // Load active clusters without suggestions
            var clusters = await LoadUnmatchedClustersAsync(cancellationToken);
            context?.WriteLine($"Found {clusters.Count} clusters to match");

            if (clusters.Count == 0)
            {
                context?.WriteLine("No unmatched clusters found");
                return;
            }

            // Load pattern library
            var patterns = await LoadPatternsAsync(cancellationToken);
            context?.WriteLine($"Loaded {patterns.Count} patterns");

            if (patterns.Count == 0)
            {
                context?.WriteLine("No patterns available for matching");
                return;
            }

            var progressBar = context?.WriteProgressBar();
            var processed = 0;

            foreach (var cluster in clusters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Load point metadata and behaviors for cluster
                var points = await LoadPointMetadataAsync(cluster.PointIds, cancellationToken);
                var behaviors = await LoadBehaviorsAsync(cluster.PointIds, cancellationToken);

                // Score against each pattern
                var matches = new List<PatternMatch>();

                foreach (var pattern in patterns)
                {
                    var match = ScorePatternMatch(cluster, points, behaviors, pattern);
                    if (match.OverallConfidence >= _options.PatternMatching.MinConfidenceForSuggestion)
                    {
                        matches.Add(match);
                    }
                }

                // Take top N matches and create suggestions
                var topMatches = matches
                    .OrderByDescending(m => m.OverallConfidence)
                    .Take(_options.PatternMatching.MaxSuggestionsPerCluster)
                    .ToList();

                foreach (var match in topMatches)
                {
                    await CreateSuggestionAsync(cluster, match, points, cancellationToken);
                    suggestionsCreated++;
                }

                clustersProcessed++;
                processed++;
                progressBar?.SetValue(100.0 * processed / clusters.Count);
            }

            stopwatch.Stop();
            context?.WriteLine($"Completed: {clustersProcessed} clusters processed, {suggestionsCreated} suggestions created, {stopwatch.ElapsedMilliseconds}ms");

            _logger.LogInformation(
                "Pattern matching complete: {Clusters} clusters, {Suggestions} suggestions, {Duration}ms",
                clustersProcessed, suggestionsCreated, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pattern matching job failed");
            context?.WriteLine($"ERROR: {ex.Message}");
            throw;
        }
    }

    private async Task<List<ClusterInfo>> LoadUnmatchedClustersAsync(CancellationToken cancellationToken)
    {
        var clusters = new List<ClusterInfo>();

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        // Get active clusters that don't have recent pending suggestions
        var sql = @"
            SELECT bc.id, bc.member_point_ids, COALESCE(bc.cohesion, bc.average_cohesion)
            FROM behavioral_clusters bc
            WHERE bc.is_active = true
              AND NOT EXISTS (
                  SELECT 1 FROM pattern_suggestions ps
                  WHERE ps.cluster_id = bc.id
                    AND ps.status = 'pending'
                    AND ps.created_at > NOW() - INTERVAL '1 hour'
              )
            ORDER BY bc.detected_at DESC
            LIMIT 100
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var memberPointIdsJson = reader.GetString(1);
            var pointIds = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(memberPointIdsJson) ?? new List<Guid>();
            
            clusters.Add(new ClusterInfo
            {
                Id = reader.GetGuid(0),
                PointIds = pointIds,
                Cohesion = reader.GetDouble(2)
            });
        }

        return clusters;
    }

    private async Task<List<PatternInfo>> LoadPatternsAsync(CancellationToken cancellationToken)
    {
        var patterns = new List<PatternInfo>();

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            SELECT p.id, p.name, p.description, p.confidence,
                   pr.id as role_id, pr.name as role_name, pr.naming_patterns,
                   pr.typical_unit, pr.typical_min, pr.typical_max, 
                   pr.typical_update_rate_ms, pr.is_required
            FROM patterns p
            LEFT JOIN pattern_roles pr ON pr.pattern_id = p.id
            WHERE p.is_active = true
            ORDER BY p.confidence DESC, p.id
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        PatternInfo? current = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            var patternId = reader.GetGuid(0);

            if (current?.Id != patternId)
            {
                current = new PatternInfo
                {
                    Id = patternId,
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Confidence = reader.GetDouble(3),
                    Roles = new List<PatternRoleInfo>()
                };
                patterns.Add(current);
            }

            if (!reader.IsDBNull(4))
            {
                current.Roles.Add(new PatternRoleInfo
                {
                    Id = reader.GetGuid(4),
                    Name = reader.GetString(5),
                    NamingPatterns = reader.IsDBNull(6) ? new List<string>() : ((string[])reader.GetValue(6)).ToList(),
                    TypicalUnit = reader.IsDBNull(7) ? null : reader.GetString(7),
                    TypicalMin = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    TypicalMax = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                    TypicalUpdateRateMs = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                    IsRequired = reader.GetBoolean(11)
                });
            }
        }

        return patterns;
    }

    private async Task<List<PointMeta>> LoadPointMetadataAsync(
        List<Guid> pointIds,
        CancellationToken cancellationToken)
    {
        var points = new List<PointMeta>();

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            SELECT id, name, address, description, engineering_unit,
                   value_type, min_value, max_value
            FROM points
            WHERE id = ANY(@PointIds)
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@PointIds", pointIds.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            points.Add(new PointMeta
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                Address = reader.IsDBNull(2) ? null : reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                EngineeringUnit = reader.IsDBNull(4) ? null : reader.GetString(4),
                ValueType = reader.IsDBNull(5) ? null : reader.GetString(5),
                MinValue = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                MaxValue = reader.IsDBNull(7) ? null : reader.GetDouble(7)
            });
        }

        return points;
    }

    private async Task<Dictionary<Guid, BehaviorData>> LoadBehaviorsAsync(
        List<Guid> pointIds,
        CancellationToken cancellationToken)
    {
        var behaviors = new Dictionary<Guid, BehaviorData>();

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            SELECT point_id, mean_value, std_deviation, min_value, max_value, update_rate_hz
            FROM behavioral_stats
            WHERE point_id = ANY(@PointIds)
              AND calculated_at > NOW() - INTERVAL '24 hours'
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@PointIds", pointIds.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            behaviors[reader.GetGuid(0)] = new BehaviorData
            {
                Mean = reader.GetDouble(1),
                StdDev = reader.GetDouble(2),
                Min = reader.GetDouble(3),
                Max = reader.GetDouble(4),
                UpdateRateHz = reader.GetDouble(5)
            };
        }

        return behaviors;
    }

    private PatternMatch ScorePatternMatch(
        ClusterInfo cluster,
        List<PointMeta> points,
        Dictionary<Guid, BehaviorData> behaviors,
        PatternInfo pattern)
    {
        var namingScore = CalculateNamingScore(points, pattern.Roles);
        var correlationScore = cluster.Cohesion; // Use cluster cohesion as correlation score
        var rangeScore = CalculateRangeScore(points, behaviors, pattern.Roles);
        var rateScore = CalculateRateScore(behaviors, pattern.Roles);

        var overallConfidence = 
            namingScore * NamingWeight +
            correlationScore * CorrelationWeight +
            rangeScore * RangeWeight +
            rateScore * RateWeight;

        return new PatternMatch
        {
            PatternId = pattern.Id,
            PatternName = pattern.Name,
            OverallConfidence = overallConfidence,
            NamingScore = namingScore,
            CorrelationScore = correlationScore,
            RangeScore = rangeScore,
            RateScore = rateScore
        };
    }

    private double CalculateNamingScore(List<PointMeta> points, List<PatternRoleInfo> roles)
    {
        if (roles.Count == 0 || points.Count == 0)
            return 0.5; // Neutral score

        var matchedRoles = 0;
        var totalRequiredRoles = roles.Count(r => r.IsRequired);

        foreach (var role in roles)
        {
            foreach (var pattern in role.NamingPatterns)
            {
                try
                {
                    var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                    if (points.Any(p => regex.IsMatch(p.Name)))
                    {
                        matchedRoles++;
                        break;
                    }
                }
                catch
                {
                    // Invalid regex, skip
                }
            }
        }

        // Score based on how many roles were matched
        return totalRequiredRoles > 0 
            ? (double)matchedRoles / totalRequiredRoles 
            : (double)matchedRoles / Math.Max(roles.Count, 1);
    }

    private double CalculateRangeScore(
        List<PointMeta> points,
        Dictionary<Guid, BehaviorData> behaviors,
        List<PatternRoleInfo> roles)
    {
        if (roles.Count == 0 || behaviors.Count == 0)
            return 0.5;

        var scores = new List<double>();

        foreach (var point in points)
        {
            if (!behaviors.TryGetValue(point.Id, out var behavior))
                continue;

            // Find best matching role for this point
            foreach (var role in roles.Where(r => r.TypicalMin.HasValue && r.TypicalMax.HasValue))
            {
                var expectedMin = role.TypicalMin!.Value;
                var expectedMax = role.TypicalMax!.Value;
                var expectedRange = expectedMax - expectedMin;

                if (expectedRange <= 0)
                    continue;

                // Check if actual range overlaps with expected
                var overlapMin = Math.Max(behavior.Min, expectedMin);
                var overlapMax = Math.Min(behavior.Max, expectedMax);
                
                if (overlapMax > overlapMin)
                {
                    var overlap = (overlapMax - overlapMin) / expectedRange;
                    scores.Add(Math.Min(overlap, 1.0));
                }
            }
        }

        return scores.Count > 0 ? scores.Average() : 0.5;
    }

    private double CalculateRateScore(
        Dictionary<Guid, BehaviorData> behaviors,
        List<PatternRoleInfo> roles)
    {
        if (roles.Count == 0 || behaviors.Count == 0)
            return 0.5;

        var rolesWithRate = roles.Where(r => r.TypicalUpdateRateMs.HasValue).ToList();
        if (rolesWithRate.Count == 0)
            return 0.5;

        var scores = new List<double>();

        foreach (var behavior in behaviors.Values)
        {
            var actualRateMs = behavior.UpdateRateHz > 0 ? 1000.0 / behavior.UpdateRateHz : 0;

            foreach (var role in rolesWithRate)
            {
                var expectedRateMs = role.TypicalUpdateRateMs!.Value;
                if (expectedRateMs <= 0)
                    continue;

                // Score based on how close actual rate is to expected (log scale)
                var ratio = actualRateMs / expectedRateMs;
                var score = Math.Exp(-Math.Pow(Math.Log(Math.Max(ratio, 0.01)), 2) / 2);
                scores.Add(score);
            }
        }

        return scores.Count > 0 ? scores.Average() : 0.5;
    }

    private async Task CreateSuggestionAsync(
        ClusterInfo cluster,
        PatternMatch match,
        List<PointMeta> points,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var suggestionId = Guid.NewGuid();
        var commonPrefix = ExtractCommonPrefix(points.Select(p => p.Name).ToList());

        var sql = @"
            INSERT INTO pattern_suggestions (
                id, cluster_id, pattern_id, overall_confidence,
                naming_score, correlation_score, range_score, rate_score,
                reason, status, created_at
            ) VALUES (
                @Id, @ClusterId, @PatternId, @Confidence,
                @NamingScore, @CorrelationScore, @RangeScore, @RateScore,
                @Reason, 'pending', @CreatedAt
            )
            ON CONFLICT (cluster_id, pattern_id) DO UPDATE SET
                overall_confidence = EXCLUDED.overall_confidence,
                naming_score = EXCLUDED.naming_score,
                correlation_score = EXCLUDED.correlation_score,
                range_score = EXCLUDED.range_score,
                rate_score = EXCLUDED.rate_score,
                reason = EXCLUDED.reason,
                status = 'pending',
                created_at = EXCLUDED.created_at
        ";

        var reason = $"Matched {points.Count} points ({commonPrefix}*) to '{match.PatternName}' with {match.OverallConfidence:P0} confidence. " +
                     $"Scores: Naming={match.NamingScore:P0}, Correlation={match.CorrelationScore:P0}, " +
                     $"Range={match.RangeScore:P0}, Rate={match.RateScore:P0}";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", suggestionId);
        cmd.Parameters.AddWithValue("@ClusterId", cluster.Id);
        cmd.Parameters.AddWithValue("@PatternId", match.PatternId);
        cmd.Parameters.AddWithValue("@Confidence", match.OverallConfidence);
        cmd.Parameters.AddWithValue("@NamingScore", match.NamingScore);
        cmd.Parameters.AddWithValue("@CorrelationScore", match.CorrelationScore);
        cmd.Parameters.AddWithValue("@RangeScore", match.RangeScore);
        cmd.Parameters.AddWithValue("@RateScore", match.RateScore);
        cmd.Parameters.AddWithValue("@Reason", reason);
        cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogDebug(
            "Created suggestion {SuggestionId}: {Pattern} for cluster {ClusterId} ({Confidence:P0})",
            suggestionId, match.PatternName, cluster.Id, match.OverallConfidence);
    }

    private string ExtractCommonPrefix(List<string> names)
    {
        if (names.Count == 0)
            return "";
        if (names.Count == 1)
            return names[0];

        var prefix = names[0];
        foreach (var name in names.Skip(1))
        {
            while (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.Length > 0)
            {
                prefix = prefix[..^1];
            }
        }

        // Trim to last separator
        var lastSep = prefix.LastIndexOfAny(['_', '-', '.', ' ']);
        if (lastSep > 0)
            prefix = prefix[..(lastSep + 1)];

        return prefix;
    }
}

internal sealed record ClusterInfo
{
    public Guid Id { get; init; }
    public List<Guid> PointIds { get; init; } = new();
    public double Cohesion { get; init; }
}

internal sealed record PatternInfo
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public double Confidence { get; init; }
    public List<PatternRoleInfo> Roles { get; init; } = new();
}

internal sealed record PatternRoleInfo
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public List<string> NamingPatterns { get; init; } = new();
    public string? TypicalUnit { get; init; }
    public double? TypicalMin { get; init; }
    public double? TypicalMax { get; init; }
    public double? TypicalUpdateRateMs { get; init; }
    public bool IsRequired { get; init; }
}

internal sealed record PointMeta
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Address { get; init; }
    public string? Description { get; init; }
    public string? EngineeringUnit { get; init; }
    public string? ValueType { get; init; }
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
}

internal sealed record BehaviorData
{
    public double Mean { get; init; }
    public double StdDev { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double UpdateRateHz { get; init; }
}

internal sealed record PatternMatch
{
    public Guid PatternId { get; init; }
    public required string PatternName { get; init; }
    public double OverallConfidence { get; init; }
    public double NamingScore { get; init; }
    public double CorrelationScore { get; init; }
    public double RangeScore { get; init; }
    public double RateScore { get; init; }
}
