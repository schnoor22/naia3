using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Naia.PatternEngine.Events;
using Naia.PatternEngine.Services;
using Npgsql;
using StackExchange.Redis;

namespace Naia.PatternEngine.Workers;

/// <summary>
/// Consumes ClusterCreated events and matches clusters against known patterns.
/// Uses multi-factor scoring: naming similarity, correlation patterns, value ranges, update rates.
/// Publishes SuggestionCreated events for high-confidence matches.
/// </summary>
public sealed class PatternMatcherWorker : BaseKafkaConsumer<ClusterCreated>
{
    private readonly IPatternEventPublisher _eventPublisher;
    private readonly IConnectionMultiplexer _redis;
    private readonly PatternMatchingOptions _options;
    private readonly PatternKafkaOptions _kafkaOptions;
    private readonly string _postgresConnectionString;

    public PatternMatcherWorker(
        ILogger<PatternMatcherWorker> logger,
        IOptions<PatternFlywheelOptions> options,
        IPatternEventPublisher eventPublisher,
        IConnectionMultiplexer redis,
        string postgresConnectionString)
        : base(
            logger,
            options.Value.Kafka.BootstrapServers,
            options.Value.Kafka.PatternMatcherGroupId,
            options.Value.Kafka.ClustersCreatedTopic)
    {
        _eventPublisher = eventPublisher;
        _redis = redis;
        _options = options.Value.PatternMatching;
        _kafkaOptions = options.Value.Kafka;
        _postgresConnectionString = postgresConnectionString;
    }

    protected override async Task ProcessMessageAsync(
        ClusterCreated message,
        string key,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug(
            "Processing cluster {ClusterId} with {MemberCount} members",
            message.ClusterId, message.PointIds.Count);

        // Load point metadata for the cluster
        var points = await LoadPointMetadataAsync(message.PointIds, cancellationToken);
        if (points.Count == 0)
        {
            Logger.LogWarning("No point metadata found for cluster {ClusterId}", message.ClusterId);
            return;
        }

        // Load known patterns
        var patterns = await LoadPatternsAsync(cancellationToken);
        if (patterns.Count == 0)
        {
            Logger.LogDebug("No patterns available for matching");
            return;
        }

        // Load behavioral data from Redis
        var behaviors = await LoadBehaviorsAsync(message.PointIds, cancellationToken);

        // Match cluster against each pattern
        var suggestions = new List<(string Key, SuggestionCreated Event)>();

        foreach (var pattern in patterns)
        {
            var match = CalculatePatternMatch(
                message, points, behaviors, pattern);

            if (match.OverallConfidence >= _options.MinConfidenceForSuggestion)
            {
                var suggestion = new SuggestionCreated
                {
                    SuggestionId = Guid.NewGuid(),
                    ClusterId = message.ClusterId,
                    PatternId = pattern.PatternId,
                    PatternName = pattern.Name,
                    OverallConfidence = match.OverallConfidence,
                    NamingScore = match.NamingScore,
                    CorrelationScore = match.CorrelationScore,
                    RangeScore = match.RangeScore,
                    RateScore = match.RateScore,
                    Reason = $"Matched {match.MatchedPointIds.Count} points to pattern '{pattern.Name}' with {match.OverallConfidence:P1} confidence",
                    PointCount = match.MatchedPointIds.Count,
                    CreatedAt = DateTime.UtcNow
                };

                suggestions.Add((suggestion.SuggestionId.ToString(), suggestion));
            }
        }

        // Sort by confidence and take top N
        var topSuggestions = suggestions
            .OrderByDescending(s => s.Event.OverallConfidence)
            .Take(_options.MaxSuggestionsPerCluster)
            .ToList();

        if (topSuggestions.Count > 0)
        {
            await _eventPublisher.PublishBatchAsync(
                _kafkaOptions.SuggestionsCreatedTopic,
                topSuggestions,
                cancellationToken);

            Logger.LogInformation(
                "Published {Count} suggestions for cluster {ClusterId}, best match: {PatternName} ({Confidence:P1})",
                topSuggestions.Count,
                message.ClusterId,
                topSuggestions[0].Event.PatternName,
                topSuggestions[0].Event.OverallConfidence);
        }
    }

    private async Task<List<PointMetadata>> LoadPointMetadataAsync(
        List<Guid> pointIds,
        CancellationToken cancellationToken)
    {
        var points = new List<PointMetadata>();

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            SELECT id, name, address, description, engineering_unit, 
                   value_type, min_value, max_value
            FROM points
            WHERE id = ANY($1)
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(pointIds.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            points.Add(new PointMetadata
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

    private async Task<List<PatternDefinition>> LoadPatternsAsync(CancellationToken cancellationToken)
    {
        var patterns = new List<PatternDefinition>();

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            SELECT p.id, p.name, p.description, p.confidence,
                   pr.id as role_id, pr.name as role_name, pr.naming_patterns,
                   pr.typical_unit, pr.typical_min, pr.typical_max, 
                   pr.typical_update_rate_ms
            FROM patterns p
            LEFT JOIN pattern_roles pr ON pr.pattern_id = p.id
            WHERE p.is_active = true
            ORDER BY p.confidence DESC, p.id
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var currentPattern = (PatternDefinition?)null;
        while (await reader.ReadAsync(cancellationToken))
        {
            var patternId = reader.GetGuid(0);

            if (currentPattern?.PatternId != patternId)
            {
                currentPattern = new PatternDefinition
                {
                    PatternId = patternId,
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Confidence = reader.GetDouble(3),
                    Roles = new List<PatternRole>()
                };
                patterns.Add(currentPattern);
            }

            if (!reader.IsDBNull(4))
            {
                currentPattern.Roles.Add(new PatternRole
                {
                    RoleId = reader.GetGuid(4),
                    Name = reader.GetString(5),
                    NamingPatterns = reader.IsDBNull(6) ? new List<string>() : 
                        ParseJsonArray(reader.GetString(6)),
                    TypicalUnit = reader.IsDBNull(7) ? null : reader.GetString(7),
                    TypicalMin = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    TypicalMax = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                    TypicalUpdateRateMs = reader.IsDBNull(10) ? null : reader.GetDouble(10)
                });
            }
        }

        return patterns;
    }

    private List<string> ParseJsonArray(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task<Dictionary<Guid, PointBehavior>> LoadBehaviorsAsync(
        List<Guid> pointIds,
        CancellationToken cancellationToken)
    {
        var behaviors = new Dictionary<Guid, PointBehavior>();
        var db = _redis.GetDatabase();

        foreach (var pointId in pointIds)
        {
            var hash = await db.HashGetAllAsync($"naia:behavior:{pointId}:metrics");
            if (hash.Length == 0) continue;

            var dict = hash.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

            behaviors[pointId] = new PointBehavior
            {
                Mean = double.TryParse(dict.GetValueOrDefault("mean"), out var mean) ? mean : 0,
                StdDev = double.TryParse(dict.GetValueOrDefault("stddev"), out var stddev) ? stddev : 0,
                Min = double.TryParse(dict.GetValueOrDefault("min"), out var min) ? min : 0,
                Max = double.TryParse(dict.GetValueOrDefault("max"), out var max) ? max : 0,
                MedianRateMs = double.TryParse(dict.GetValueOrDefault("medianRate"), out var rate) ? rate : 0
            };
        }

        return behaviors;
    }

    private PatternMatchResult CalculatePatternMatch(
        ClusterCreated cluster,
        List<PointMetadata> points,
        Dictionary<Guid, PointBehavior> behaviors,
        PatternDefinition pattern)
    {
        var result = new PatternMatchResult
        {
            Evidence = new List<string>()
        };

        if (pattern.Roles.Count == 0)
        {
            return result;
        }

        // Try to assign each point to a role
        var roleAssignments = new Dictionary<Guid, Guid>(); // PointId -> RoleId
        var usedRoles = new HashSet<Guid>();
        var roleScores = new Dictionary<(Guid PointId, Guid RoleId), RoleMatchScore>();

        // Calculate match scores for all point-role combinations
        foreach (var point in points)
        {
            behaviors.TryGetValue(point.Id, out var behavior);

            foreach (var role in pattern.Roles)
            {
                var score = CalculateRoleMatchScore(point, behavior, role);
                roleScores[(point.Id, role.RoleId)] = score;
            }
        }

        // Greedy assignment: assign points to roles by best match
        var orderedScores = roleScores
            .OrderByDescending(x => x.Value.Total)
            .ToList();

        var assignedPoints = new HashSet<Guid>();
        foreach (var kvp in orderedScores)
        {
            var (pointId, roleId) = kvp.Key;
            var score = kvp.Value;

            if (assignedPoints.Contains(pointId) || usedRoles.Contains(roleId))
                continue;

            if (score.Total > 0.3) // Minimum threshold for assignment
            {
                roleAssignments[pointId] = roleId;
                assignedPoints.Add(pointId);
                usedRoles.Add(roleId);

                var roleName = pattern.Roles.First(r => r.RoleId == roleId).Name;
                var pointName = points.First(p => p.Id == pointId).Name;
                result.Evidence.Add($"Assigned '{pointName}' to role '{roleName}' (score: {score.Total:F2})");
            }
        }

        // Calculate overall scores
        if (roleAssignments.Count == 0)
        {
            return result;
        }

        var matchedScores = roleAssignments
            .Select(ra => roleScores[(ra.Key, ra.Value)])
            .ToList();

        result.NamingScore = matchedScores.Average(s => s.Naming);
        result.RangeScore = matchedScores.Average(s => s.Range);
        result.RateScore = matchedScores.Average(s => s.Rate);
        
        // Correlation score based on cluster cohesion
        result.CorrelationScore = cluster.CohesionScore;

        // Weighted overall confidence
        result.OverallConfidence = 
            _options.NamingWeight * result.NamingScore +
            _options.CorrelationWeight * result.CorrelationScore +
            _options.RangeWeight * result.RangeScore +
            _options.RateWeight * result.RateScore;

        // Bonus for matching more roles
        var roleMatchRatio = (double)roleAssignments.Count / pattern.Roles.Count;
        result.OverallConfidence *= (0.5 + 0.5 * roleMatchRatio);

        // Factor in pattern's historical confidence
        result.OverallConfidence *= pattern.Confidence;

        result.MatchedPointIds = roleAssignments.Keys.ToList();
        result.RoleAssignments = roleAssignments
            .ToDictionary(x => x.Key.ToString(), x => x.Value.ToString());

        result.Evidence.Add($"Pattern match: {roleAssignments.Count}/{pattern.Roles.Count} roles ({roleMatchRatio:P0})");
        result.Evidence.Add($"Cluster cohesion: {cluster.CohesionScore:F2}");

        return result;
    }

    private RoleMatchScore CalculateRoleMatchScore(
        PointMetadata point,
        PointBehavior? behavior,
        PatternRole role)
    {
        var score = new RoleMatchScore();

        // 1. Naming similarity
        score.Naming = CalculateNamingScore(point, role);

        // 2. Range match
        if (behavior != null && role.TypicalMin.HasValue && role.TypicalMax.HasValue)
        {
            var typicalRange = role.TypicalMax.Value - role.TypicalMin.Value;
            var actualRange = behavior.Max - behavior.Min;
            
            if (typicalRange > 0)
            {
                var rangeRatio = actualRange / typicalRange;
                score.Range = 1.0 - Math.Min(1.0, Math.Abs(1.0 - rangeRatio));
            }

            // Check if actual values fall within expected range
            var inRange = behavior.Min >= role.TypicalMin.Value * 0.5 &&
                          behavior.Max <= role.TypicalMax.Value * 2.0;
            if (!inRange)
            {
                score.Range *= 0.5;
            }
        }

        // 3. Update rate match
        if (behavior != null && role.TypicalUpdateRateMs.HasValue && behavior.MedianRateMs > 0)
        {
            var rateRatio = behavior.MedianRateMs / role.TypicalUpdateRateMs.Value;
            score.Rate = 1.0 - Math.Min(1.0, Math.Abs(1.0 - rateRatio) / 5.0);
        }

        // 4. Unit match
        if (!string.IsNullOrEmpty(point.EngineeringUnit) && !string.IsNullOrEmpty(role.TypicalUnit))
        {
            if (NormalizeUnit(point.EngineeringUnit) == NormalizeUnit(role.TypicalUnit))
            {
                score.Range += 0.2; // Bonus for unit match
            }
        }

        score.Total = (score.Naming + score.Range + score.Rate) / 3.0;

        return score;
    }

    private double CalculateNamingScore(PointMetadata point, PatternRole role)
    {
        if (role.NamingPatterns.Count == 0)
            return 0.5; // Neutral if no patterns defined

        var searchText = $"{point.Name} {point.Address} {point.Description}".ToLowerInvariant();
        var bestScore = 0.0;

        foreach (var pattern in role.NamingPatterns)
        {
            try
            {
                if (Regex.IsMatch(searchText, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
                {
                    bestScore = Math.Max(bestScore, 1.0);
                }
                else
                {
                    // Partial match using keywords
                    var keywords = pattern.Split(new[] { '|', '(', ')', '[', ']', '.', '*', '?' }, 
                        StringSplitOptions.RemoveEmptyEntries);
                    var matchCount = keywords.Count(k => searchText.Contains(k.ToLowerInvariant()));
                    var partialScore = keywords.Length > 0 ? (double)matchCount / keywords.Length * 0.6 : 0;
                    bestScore = Math.Max(bestScore, partialScore);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Fallback to simple contains
                if (searchText.Contains(pattern.ToLowerInvariant()))
                {
                    bestScore = Math.Max(bestScore, 0.5);
                }
            }
        }

        return bestScore;
    }

    private string NormalizeUnit(string unit)
    {
        return unit.ToLowerInvariant()
            .Replace("°", "deg")
            .Replace("celsius", "c")
            .Replace("fahrenheit", "f")
            .Replace("percent", "%")
            .Replace(" ", "")
            .Trim();
    }
}

internal sealed class PointMetadata
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Description { get; set; }
    public string? EngineeringUnit { get; set; }
    public string? ValueType { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
}

internal sealed class PointBehavior
{
    public double Mean { get; set; }
    public double StdDev { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double MedianRateMs { get; set; }
}

internal sealed class PatternDefinition
{
    public Guid PatternId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Confidence { get; set; }
    public List<PatternRole> Roles { get; set; } = new();
}

internal sealed class PatternRole
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> NamingPatterns { get; set; } = new();
    public string? TypicalUnit { get; set; }
    public double? TypicalMin { get; set; }
    public double? TypicalMax { get; set; }
    public double? TypicalUpdateRateMs { get; set; }
}

internal sealed class PatternMatchResult
{
    public double OverallConfidence { get; set; }
    public double NamingScore { get; set; }
    public double CorrelationScore { get; set; }
    public double RangeScore { get; set; }
    public double RateScore { get; set; }
    public List<Guid> MatchedPointIds { get; set; } = new();
    public Dictionary<string, string> RoleAssignments { get; set; } = new();
    public List<string> Evidence { get; set; } = new();
}

internal sealed class RoleMatchScore
{
    public double Naming { get; set; }
    public double Range { get; set; }
    public double Rate { get; set; }
    public double Total { get; set; }
}
