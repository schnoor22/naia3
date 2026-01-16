using Microsoft.EntityFrameworkCore;
using Naia.Application.Abstractions;
using Naia.Domain.Entities;

namespace Naia.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL repository for Point entities.
/// </summary>
public sealed class PointRepository : IPointRepository
{
    private readonly NaiaDbContext _context;
    
    public PointRepository(NaiaDbContext context)
    {
        _context = context;
    }
    
    public async Task<Point?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Points
            .Include(p => p.DataSource)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }
    
    public async Task<Point?> GetBySequenceIdAsync(long sequenceId, CancellationToken cancellationToken = default)
    {
        return await _context.Points
            .Include(p => p.DataSource)
            .FirstOrDefaultAsync(p => p.PointSequenceId == sequenceId, cancellationToken);
    }
    
    public async Task<Point?> GetByTagNameAsync(string tagName, Guid dataSourceId, CancellationToken cancellationToken = default)
    {
        return await _context.Points
            .Include(p => p.DataSource)
            .FirstOrDefaultAsync(p => p.Name == tagName && p.DataSourceId == dataSourceId, cancellationToken);
    }
    
    public async Task<IReadOnlyList<Point>> GetByDataSourceIdAsync(Guid dataSourceId, CancellationToken cancellationToken = default)
    {
        return await _context.Points
            .Where(p => p.DataSourceId == dataSourceId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IReadOnlyList<Point>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Points
            .Include(p => p.DataSource)
            .Where(p => p.IsEnabled && (p.DataSource == null || p.DataSource.IsEnabled))
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IReadOnlyList<Point>> SearchAsync(
        string? tagNamePattern = null,
        Guid? dataSourceId = null,
        bool? isEnabled = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Points
            .Include(p => p.DataSource)
            .AsQueryable();
        
        if (!string.IsNullOrWhiteSpace(tagNamePattern))
        {
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{tagNamePattern}%"));
        }
        
        if (dataSourceId.HasValue)
        {
            query = query.Where(p => p.DataSourceId == dataSourceId.Value);
        }
        
        if (isEnabled.HasValue)
        {
            query = query.Where(p => p.IsEnabled == isEnabled.Value);
        }
        
        return await query
            .OrderBy(p => p.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<int> CountAsync(
        string? tagNamePattern = null,
        Guid? dataSourceId = null,
        bool? isEnabled = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Points.AsQueryable();
        
        if (!string.IsNullOrWhiteSpace(tagNamePattern))
        {
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{tagNamePattern}%"));
        }
        
        if (dataSourceId.HasValue)
        {
            query = query.Where(p => p.DataSourceId == dataSourceId.Value);
        }
        
        if (isEnabled.HasValue)
        {
            query = query.Where(p => p.IsEnabled == isEnabled.Value);
        }
        
        return await query.CountAsync(cancellationToken);
    }
    
    public async Task<Point> AddAsync(Point point, CancellationToken cancellationToken = default)
    {
        _context.Points.Add(point);
        return point;
    }
    
    public Task UpdateAsync(Point point, CancellationToken cancellationToken = default)
    {
        _context.Points.Update(point);
        return Task.CompletedTask;
    }
    
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var point = await _context.Points.FindAsync(new object[] { id }, cancellationToken);
        if (point != null)
        {
            _context.Points.Remove(point);
        }
    }
    
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
    
    public async Task<IDictionary<string, long?>> GetTagNameToSequenceIdMapAsync(
        Guid dataSourceId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Points
            .Where(p => p.DataSourceId == dataSourceId)
            .ToDictionaryAsync(
                p => p.Name,
                p => p.PointSequenceId,
                cancellationToken);
    }
    
    public async Task<Point?> GetBySourceAddressAsync(
        string sourceAddress,
        CancellationToken cancellationToken = default)
    {
        return await _context.Points
            .Include(p => p.DataSource)
            .FirstOrDefaultAsync(p => p.SourceAddress == sourceAddress, cancellationToken);
    }
    
    public async Task<IReadOnlyList<Point>> GetBySourceAddressesAsync(
        IEnumerable<string> sourceAddresses,
        CancellationToken cancellationToken = default)
    {
        var addressList = sourceAddresses.ToList();
        return await _context.Points
            .Include(p => p.DataSource)
            .Where(p => p.SourceAddress != null && addressList.Contains(p.SourceAddress))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// PostgreSQL repository for DataSource entities.
/// </summary>
public sealed class DataSourceRepository : IDataSourceRepository
{
    private readonly NaiaDbContext _context;
    
    public DataSourceRepository(NaiaDbContext context)
    {
        _context = context;
    }
    
    public async Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.DataSources
            .FirstOrDefaultAsync(ds => ds.Id == id, cancellationToken);
    }
    
    public async Task<DataSource?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.DataSources
            .FirstOrDefaultAsync(ds => ds.Name == name, cancellationToken);
    }
    
    public async Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.DataSources
            .OrderBy(ds => ds.Name)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IReadOnlyList<DataSource>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        return await _context.DataSources
            .Where(ds => ds.IsEnabled)
            .OrderBy(ds => ds.Name)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<IReadOnlyList<DataSource>> GetByTypeAsync(DataSourceType type, CancellationToken cancellationToken = default)
    {
        return await _context.DataSources
            .Where(ds => ds.SourceType == type)
            .OrderBy(ds => ds.Name)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<DataSource> AddAsync(DataSource dataSource, CancellationToken cancellationToken = default)
    {
        _context.DataSources.Add(dataSource);
        return dataSource;
    }
    
    public Task UpdateAsync(DataSource dataSource, CancellationToken cancellationToken = default)
    {
        _context.DataSources.Update(dataSource);
        return Task.CompletedTask;
    }
    
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dataSource = await _context.DataSources.FindAsync(new object[] { id }, cancellationToken);
        if (dataSource != null)
        {
            _context.DataSources.Remove(dataSource);
        }
    }
}

/// <summary>
/// Unit of work implementation for PostgreSQL.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly NaiaDbContext _context;
    
    public UnitOfWork(NaiaDbContext context)
    {
        _context = context;
    }
    
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
    
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        return new UnitOfWorkTransaction(transaction);
    }
}

internal sealed class UnitOfWorkTransaction : IUnitOfWorkTransaction
{
    private readonly Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction _transaction;
    private bool _disposed;
    
    public UnitOfWorkTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }
    
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.CommitAsync(cancellationToken);
    }
    
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.RollbackAsync(cancellationToken);
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _transaction.Dispose();
            _disposed = true;
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _transaction.DisposeAsync();
            _disposed = true;
        }
    }
}
