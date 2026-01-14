using Microsoft.AspNetCore.Mvc;
using Naia.Api.Hubs;
using Naia.Application.Abstractions;

namespace Naia.Api.Controllers;

/// <summary>
/// API endpoints for pattern suggestions - the heart of NAIA's learning system.
/// Enables the feedback loop: AI suggests → Human approves/rejects → AI learns.
/// 
/// With Hangfire: Status updates are stored in PostgreSQL and the PatternLearningJob
/// picks them up on its next run (hourly) to update pattern confidence.
/// </summary>
[ApiController]
[Route("api/suggestions")]
public class SuggestionsController : ControllerBase
{
    private readonly ILogger<SuggestionsController> _logger;
    private readonly ISuggestionRepository _suggestionRepository;
    private readonly IPatternNotifier _patternNotifier;

    public SuggestionsController(
        ILogger<SuggestionsController> logger,
        ISuggestionRepository suggestionRepository,
        IPatternNotifier patternNotifier)
    {
        _logger = logger;
        _suggestionRepository = suggestionRepository;
        _patternNotifier = patternNotifier;
    }

    /// <summary>
    /// Get all pending suggestions awaiting human review.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<SuggestionDto>>> GetPending(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var suggestions = await _suggestionRepository.GetPendingAsync(skip, take, ct);
        var total = await _suggestionRepository.CountPendingAsync(ct);

        return Ok(new PaginatedResult<SuggestionDto>
        {
            Data = suggestions,
            Total = total,
            Skip = skip,
            Take = take
        });
    }

    /// <summary>
    /// Get suggestion details including matched points and expected roles.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SuggestionDetailDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(id, ct);
        if (suggestion is null)
            return NotFound();

        return Ok(suggestion);
    }

    /// <summary>
    /// Get suggestion statistics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<SuggestionStatsDto>> GetStats(CancellationToken ct = default)
    {
        var stats = await _suggestionRepository.GetStatsAsync(ct);
        return Ok(stats);
    }

    /// <summary>
    /// Approve a suggestion - binds points to pattern and increases confidence.
    /// This is how NAIA learns from your decisions!
    /// 
    /// The PatternLearningJob (Hangfire) will process approved suggestions
    /// on its next run to update pattern confidence and create point bindings.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult> Approve(
        Guid id,
        [FromBody] ApproveRequest? request = null,
        CancellationToken ct = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(id, ct);
        if (suggestion is null)
            return NotFound();

        if (suggestion.Status != SuggestionStatus.Pending)
            return BadRequest($"Suggestion is already {suggestion.Status}");

        _logger.LogInformation(
            "User approved suggestion {SuggestionId} for pattern {PatternName} with confidence {Confidence:P1}",
            id, suggestion.PatternName, suggestion.Confidence);

        // Update status - PatternLearningJob will process on next run
        await _suggestionRepository.UpdateStatusAsync(
            id, 
            SuggestionStatus.Approved, 
            userId: request?.UserId ?? "anonymous",
            ct: ct);

        // Notify connected clients via Kafka → SignalR
        var pendingCount = await _suggestionRepository.CountPendingAsync(ct);
        await _patternNotifier.NotifyPendingCountChangedAsync(pendingCount);
        await _patternNotifier.NotifySuggestionApprovedAsync(id, suggestion.PatternName);

        return Ok(new { message = "Suggestion approved - NAIA is learning!", suggestionId = id });
    }

    /// <summary>
    /// Reject a suggestion - decreases pattern confidence.
    /// Rejection feedback helps NAIA avoid similar false positives.
    /// 
    /// The PatternLearningJob (Hangfire) will process rejected suggestions
    /// on its next run to decrease pattern confidence.
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult> Reject(
        Guid id,
        [FromBody] RejectRequest? request = null,
        CancellationToken ct = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(id, ct);
        if (suggestion is null)
            return NotFound();

        if (suggestion.Status != SuggestionStatus.Pending)
            return BadRequest($"Suggestion is already {suggestion.Status}");

        _logger.LogInformation(
            "User rejected suggestion {SuggestionId} for pattern {PatternName}. Reason: {Reason}",
            id, suggestion.PatternName, request?.Reason ?? "Not specified");

        // Update status - PatternLearningJob will process on next run
        await _suggestionRepository.UpdateStatusAsync(
            id, 
            SuggestionStatus.Rejected, 
            rejectionReason: request?.Reason,
            userId: request?.UserId ?? "anonymous",
            ct: ct);

        // Notify connected clients via Kafka → SignalR
        var pendingCount = await _suggestionRepository.CountPendingAsync(ct);
        await _patternNotifier.NotifyPendingCountChangedAsync(pendingCount);

        return Ok(new { message = "Feedback recorded - NAIA will improve!", suggestionId = id });
    }

    /// <summary>
    /// Defer a suggestion for later review.
    /// </summary>
    [HttpPost("{id:guid}/defer")]
    public async Task<ActionResult> Defer(Guid id, CancellationToken ct = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(id, ct);
        if (suggestion is null)
            return NotFound();

        if (suggestion.Status != SuggestionStatus.Pending)
            return BadRequest($"Suggestion is already {suggestion.Status}");

        await _suggestionRepository.UpdateStatusAsync(id, SuggestionStatus.Deferred, ct: ct);

        return Ok(new { message = "Suggestion deferred", suggestionId = id });
    }
}

/// <summary>
/// API endpoints for the pattern library - NAIA's learned knowledge base.
/// </summary>
[ApiController]
[Route("api/patterns")]
public class PatternsController : ControllerBase
{
    private readonly ILogger<PatternsController> _logger;
    private readonly IPatternRepository _patternRepository;

    public PatternsController(
        ILogger<PatternsController> logger,
        IPatternRepository patternRepository)
    {
        _logger = logger;
        _patternRepository = patternRepository;
    }

    /// <summary>
    /// Get all patterns in the library.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PatternDto>>> GetAll(CancellationToken ct = default)
    {
        var patterns = await _patternRepository.GetAllAsync(ct);
        return Ok(patterns);
    }

    /// <summary>
    /// Get pattern details including roles and recent examples.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PatternDetailDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var pattern = await _patternRepository.GetByIdAsync(id, ct);
        if (pattern is null)
            return NotFound();

        return Ok(pattern);
    }

    /// <summary>
    /// Get patterns by category.
    /// </summary>
    [HttpGet("category/{category}")]
    public async Task<ActionResult<IReadOnlyList<PatternDto>>> GetByCategory(
        string category,
        CancellationToken ct = default)
    {
        var patterns = await _patternRepository.GetByCategoryAsync(category, ct);
        return Ok(patterns);
    }

    /// <summary>
    /// Get pattern learning statistics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<PatternStatsDto>> GetStats(CancellationToken ct = default)
    {
        var stats = await _patternRepository.GetStatsAsync(ct);
        return Ok(stats);
    }
}

// ============================================================================
// Request/Response DTOs
// ============================================================================

public record ApproveRequest
{
    public string? UserId { get; init; }
    public Dictionary<Guid, string>? RoleAssignments { get; init; }
}

public record RejectRequest
{
    public string? UserId { get; init; }
    public string? Reason { get; init; }
}

public record PaginatedResult<T>
{
    public required IReadOnlyList<T> Data { get; init; }
    public required int Total { get; init; }
    public required int Skip { get; init; }
    public required int Take { get; init; }
}
