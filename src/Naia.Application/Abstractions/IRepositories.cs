using Naia.Domain.Entities;

namespace Naia.Application.Abstractions;

/// <summary>
/// Repository for Point configuration (PostgreSQL).
/// </summary>
public interface IPointRepository
{
    Task<Point?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Point?> GetBySequenceIdAsync(long sequenceId, CancellationToken cancellationToken = default);
    Task<Point?> GetByTagNameAsync(string tagName, Guid dataSourceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Point>> GetByDataSourceIdAsync(Guid dataSourceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Point>> GetEnabledAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Point>> SearchAsync(
        string? tagNamePattern = null,
        Guid? dataSourceId = null,
        bool? isEnabled = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);
    Task<int> CountAsync(
        string? tagNamePattern = null,
        Guid? dataSourceId = null,
        bool? isEnabled = null,
        CancellationToken cancellationToken = default);
    Task<Point> AddAsync(Point point, CancellationToken cancellationToken = default);
    Task UpdateAsync(Point point, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Save all pending changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get sequence ID mappings for efficient lookups.
    /// Returns: Dictionary of TagName → SequenceId
    /// </summary>
    Task<IDictionary<string, long?>> GetTagNameToSequenceIdMapAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get point by its source address (e.g., PI tag name).
    /// </summary>
    Task<Point?> GetBySourceAddressAsync(
        string sourceAddress,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get points by multiple source addresses.
    /// </summary>
    Task<IReadOnlyList<Point>> GetBySourceAddressesAsync(
        IEnumerable<string> sourceAddresses,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository for DataSource configuration (PostgreSQL).
/// </summary>
public interface IDataSourceRepository
{
    Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DataSource?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DataSource>> GetEnabledAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DataSource>> GetByTypeAsync(DataSourceType type, CancellationToken cancellationToken = default);
    Task<DataSource> AddAsync(DataSource dataSource, CancellationToken cancellationToken = default);
    Task UpdateAsync(DataSource dataSource, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Unit of work for transactional operations.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A database transaction scope.
/// </summary>
public interface IUnitOfWorkTransaction : IDisposable, IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
