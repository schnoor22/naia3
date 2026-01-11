using Microsoft.AspNetCore.SignalR;
using Naia.Application.Abstractions;

namespace Naia.Api.Hubs;

/// <summary>
/// SignalR hub for real-time pattern suggestions and learning updates.
/// Clients connect to receive instant notifications when:
/// - New suggestions are created
/// - Patterns are updated (confidence changes)
/// - Clusters are detected
/// </summary>
public class PatternHub : Hub
{
    private readonly ILogger<PatternHub> _logger;
    private readonly ISuggestionRepository? _suggestionRepository;

    public PatternHub(
        ILogger<PatternHub> logger,
        ISuggestionRepository? suggestionRepository = null)
    {
        _logger = logger;
        _suggestionRepository = suggestionRepository;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected to PatternHub: {ConnectionId}", Context.ConnectionId);
        
        // Send current pending count on connect
        if (_suggestionRepository != null)
        {
            var pendingCount = await _suggestionRepository.CountPendingAsync();
            await Clients.Caller.SendAsync("PendingCount", pendingCount);
        }
        
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected from PatternHub: {ConnectionId}, Exception: {Exception}",
            Context.ConnectionId, exception?.Message);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to suggestions for a specific data source.
    /// </summary>
    public async Task SubscribeToDataSource(Guid dataSourceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"datasource:{dataSourceId}");
        _logger.LogDebug("Client {ConnectionId} subscribed to data source {DataSourceId}",
            Context.ConnectionId, dataSourceId);
    }

    /// <summary>
    /// Unsubscribe from a data source's suggestions.
    /// </summary>
    public async Task UnsubscribeFromDataSource(Guid dataSourceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"datasource:{dataSourceId}");
    }

    /// <summary>
    /// Subscribe to all pattern updates (for admin dashboard).
    /// </summary>
    public async Task SubscribeToAllPatterns()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all-patterns");
    }
}

/// <summary>
/// Service for broadcasting pattern events to connected SignalR clients.
/// Injected into controllers and Hangfire jobs to push real-time updates.
/// </summary>
public interface IPatternHubNotifier
{
    Task NotifySuggestionCreatedAsync(SuggestionDto suggestion);
    Task NotifySuggestionApprovedAsync(Guid suggestionId, string patternName);
    Task NotifyPatternUpdatedAsync(PatternDto pattern);
    Task NotifyClusterDetectedAsync(Guid clusterId, int pointCount, string? commonPrefix);
    Task NotifyPendingCountChangedAsync(int newCount);
}

/// <summary>
/// SignalR-based implementation of pattern notifications.
/// </summary>
public class PatternHubNotifier : IPatternHubNotifier
{
    private readonly IHubContext<PatternHub> _hubContext;
    private readonly ILogger<PatternHubNotifier> _logger;

    public PatternHubNotifier(
        IHubContext<PatternHub> hubContext,
        ILogger<PatternHubNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifySuggestionCreatedAsync(SuggestionDto suggestion)
    {
        _logger.LogDebug("Broadcasting suggestion created: {SuggestionId} for pattern {PatternName}",
            suggestion.Id, suggestion.PatternName);

        // Broadcast to all connected clients
        await _hubContext.Clients.All.SendAsync("SuggestionCreated", suggestion);
    }

    public async Task NotifySuggestionApprovedAsync(Guid suggestionId, string patternName)
    {
        _logger.LogDebug("Broadcasting suggestion approved: {SuggestionId} for pattern {PatternName}",
            suggestionId, patternName);

        await _hubContext.Clients.All.SendAsync("SuggestionApproved", new
        {
            SuggestionId = suggestionId,
            PatternName = patternName,
            ApprovedAt = DateTime.UtcNow
        });
    }

    public async Task NotifyPatternUpdatedAsync(PatternDto pattern)
    {
        _logger.LogDebug("Broadcasting pattern updated: {PatternId} ({PatternName}) confidence: {Confidence:P1}",
            pattern.Id, pattern.Name, pattern.Confidence);

        await _hubContext.Clients.Group("all-patterns").SendAsync("PatternUpdated", pattern);
    }

    public async Task NotifyClusterDetectedAsync(Guid clusterId, int pointCount, string? commonPrefix)
    {
        _logger.LogDebug("Broadcasting cluster detected: {ClusterId} with {PointCount} points",
            clusterId, pointCount);

        await _hubContext.Clients.All.SendAsync("ClusterDetected", new
        {
            ClusterId = clusterId,
            PointCount = pointCount,
            CommonPrefix = commonPrefix,
            DetectedAt = DateTime.UtcNow
        });
    }

    public async Task NotifyPendingCountChangedAsync(int newCount)
    {
        await _hubContext.Clients.All.SendAsync("PendingCount", newCount);
    }
}
