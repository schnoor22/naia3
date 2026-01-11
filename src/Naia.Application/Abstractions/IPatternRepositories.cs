namespace Naia.Application.Abstractions;

/// <summary>
/// Repository for pattern suggestions and feedback (PostgreSQL).
/// </summary>
public interface ISuggestionRepository
{
    /// <summary>
    /// Get all pending suggestions (not yet approved/rejected).
    /// </summary>
    Task<IReadOnlyList<SuggestionDto>> GetPendingAsync(
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get a suggestion by ID with full details.
    /// </summary>
    Task<SuggestionDetailDto?> GetByIdAsync(Guid suggestionId, CancellationToken ct = default);
    
    /// <summary>
    /// Get suggestions for a specific cluster.
    /// </summary>
    Task<IReadOnlyList<SuggestionDto>> GetByClusterIdAsync(Guid clusterId, CancellationToken ct = default);
    
    /// <summary>
    /// Count pending suggestions.
    /// </summary>
    Task<int> CountPendingAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Save a new suggestion (from Kafka consumer).
    /// </summary>
    Task SaveSuggestionAsync(SuggestionCreatedEvent suggestion, CancellationToken ct = default);
    
    /// <summary>
    /// Update suggestion status after user action.
    /// </summary>
    Task UpdateStatusAsync(
        Guid suggestionId, 
        SuggestionStatus status, 
        string? rejectionReason = null, 
        string? userId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get suggestion statistics.
    /// </summary>
    Task<SuggestionStatsDto> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// Repository for patterns (learned equipment templates).
/// </summary>
public interface IPatternRepository
{
    /// <summary>
    /// Get all patterns ordered by confidence.
    /// </summary>
    Task<IReadOnlyList<PatternDto>> GetAllAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Get a pattern by ID with roles.
    /// </summary>
    Task<PatternDetailDto?> GetByIdAsync(Guid patternId, CancellationToken ct = default);
    
    /// <summary>
    /// Get patterns by category (pump, compressor, etc.).
    /// </summary>
    Task<IReadOnlyList<PatternDto>> GetByCategoryAsync(string category, CancellationToken ct = default);
    
    /// <summary>
    /// Get pattern learning statistics.
    /// </summary>
    Task<PatternStatsDto> GetStatsAsync(CancellationToken ct = default);
}

// ============================================================================
// DTOs
// ============================================================================

public record SuggestionDto
{
    public required Guid Id { get; init; }
    public required Guid ClusterId { get; init; }
    public required Guid PatternId { get; init; }
    public required string PatternName { get; init; }
    public required double Confidence { get; init; }
    public required int PointCount { get; init; }
    public required SuggestionStatus Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public string? CommonPrefix { get; init; }
}

public record SuggestionDetailDto : SuggestionDto
{
    public required double NamingScore { get; init; }
    public required double CorrelationScore { get; init; }
    public required double RangeScore { get; init; }
    public required double RateScore { get; init; }
    public required string Reason { get; init; }
    public required List<SuggestionPointDto> Points { get; init; }
    public required List<PatternRoleDto> ExpectedRoles { get; init; }
}

public record SuggestionPointDto
{
    public required Guid PointId { get; init; }
    public required string PointName { get; init; }
    public string? SuggestedRole { get; init; }
    public double? RoleConfidence { get; init; }
}

public record SuggestionStatsDto
{
    public required int PendingCount { get; init; }
    public required int ApprovedToday { get; init; }
    public required int RejectedToday { get; init; }
    public required int TotalApproved { get; init; }
    public required int TotalRejected { get; init; }
    public required double ApprovalRate { get; init; }
}

public enum SuggestionStatus
{
    Pending,
    Approved,
    Rejected,
    Deferred,
    Expired
}

public record PatternDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required double Confidence { get; init; }
    public required int ExampleCount { get; init; }
    public required int RoleCount { get; init; }
    public required bool IsSystemPattern { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? LastMatchedAt { get; init; }
}

public record PatternDetailDto : PatternDto
{
    public required List<PatternRoleDto> Roles { get; init; }
    public required List<PatternExampleDto> RecentExamples { get; init; }
}

public record PatternRoleDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required List<string> NamingPatterns { get; init; }
    public double? ExpectedMinValue { get; init; }
    public double? ExpectedMaxValue { get; init; }
    public string? ExpectedUnits { get; init; }
    public required bool IsRequired { get; init; }
}

public record PatternExampleDto
{
    public required Guid ClusterId { get; init; }
    public required string CommonPrefix { get; init; }
    public required int PointCount { get; init; }
    public required DateTime BoundAt { get; init; }
}

public record PatternStatsDto
{
    public required int TotalPatterns { get; init; }
    public required int SystemPatterns { get; init; }
    public required int LearnedPatterns { get; init; }
    public required double AverageConfidence { get; init; }
    public required int TotalBindings { get; init; }
    public required PatternDto? MostConfident { get; init; }
    public required PatternDto? MostUsed { get; init; }
}

/// <summary>
/// Event data for saving suggestions (decoupled from PatternEngine events).
/// </summary>
public record SuggestionCreatedEvent
{
    public required Guid SuggestionId { get; init; }
    public required Guid ClusterId { get; init; }
    public required Guid PatternId { get; init; }
    public required string PatternName { get; init; }
    public required double OverallConfidence { get; init; }
    public required double NamingScore { get; init; }
    public required double CorrelationScore { get; init; }
    public required double RangeScore { get; init; }
    public required double RateScore { get; init; }
    public required string Reason { get; init; }
    public required int PointCount { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
