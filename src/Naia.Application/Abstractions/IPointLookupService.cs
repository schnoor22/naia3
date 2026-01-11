namespace Naia.Application.Abstractions;

/// <summary>
/// Service for fast point lookups with in-memory caching.
/// Used by pattern engine workers to resolve point names and sequence IDs.
/// </summary>
public interface IPointLookupService
{
    /// <summary>
    /// Get point information by sequence ID.
    /// </summary>
    Task<PointLookupResult?> GetBySequenceIdAsync(long sequenceId, CancellationToken ct = default);
    
    /// <summary>
    /// Get point information by GUID.
    /// </summary>
    Task<PointLookupResult?> GetByIdAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>
    /// Get point information by tag name.
    /// </summary>
    Task<PointLookupResult?> GetByNameAsync(string tagName, CancellationToken ct = default);
    
    /// <summary>
    /// Get all points for a data source.
    /// </summary>
    Task<IReadOnlyList<PointLookupResult>> GetByDataSourceAsync(Guid dataSourceId, CancellationToken ct = default);
    
    /// <summary>
    /// Refresh the cache from PostgreSQL.
    /// </summary>
    Task RefreshCacheAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Get the last cache refresh time.
    /// </summary>
    DateTime? LastRefreshTime { get; }
    
    /// <summary>
    /// Get the number of cached points.
    /// </summary>
    int CachedPointCount { get; }
}

/// <summary>
/// Lightweight point information for pattern engine lookups.
/// </summary>
public record PointLookupResult
{
    public required Guid Id { get; init; }
    public required long SequenceId { get; init; }
    public required string Name { get; init; }
    public Guid? DataSourceId { get; init; }
    public string? EngineeringUnits { get; init; }
    public string? DataType { get; init; }
    public string? Description { get; init; }
}
