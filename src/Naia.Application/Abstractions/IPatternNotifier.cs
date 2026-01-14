namespace Naia.Application.Abstractions;

/// <summary>
/// Pattern notification service for broadcasting pattern events.
/// Implementations can use SignalR (for monolithic deployment) or Kafka (for distributed deployment).
/// </summary>
public interface IPatternNotifier
{
    /// <summary>
    /// Notify that a new pattern suggestion has been created.
    /// </summary>
    Task NotifySuggestionCreatedAsync(object suggestion);
    
    /// <summary>
    /// Notify that a suggestion has been approved and converted to a pattern.
    /// </summary>
    Task NotifySuggestionApprovedAsync(Guid suggestionId, string patternName);
    
    /// <summary>
    /// Notify that an existing pattern has been updated.
    /// </summary>
    Task NotifyPatternUpdatedAsync(object pattern);
    
    /// <summary>
    /// Notify that a new point cluster has been detected.
    /// </summary>
    Task NotifyClusterDetectedAsync(Guid clusterId, int pointCount, string? commonPrefix);
    
    /// <summary>
    /// Notify that the count of pending suggestions has changed.
    /// </summary>
    Task NotifyPendingCountChangedAsync(int newCount);
}
