using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Naia.Application.Abstractions;

namespace Naia.Infrastructure.Persistence;

/// <summary>
/// In-memory point lookup cache with periodic refresh from PostgreSQL.
/// Provides fast O(1) lookups by SequenceId, GUID, and Name for pattern engine workers.
/// </summary>
public sealed class PointLookupService : IPointLookupService, IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PointLookupService> _logger;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
    
    private readonly ConcurrentDictionary<long, PointLookupResult> _bySequenceId = new();
    private readonly ConcurrentDictionary<Guid, PointLookupResult> _byId = new();
    private readonly ConcurrentDictionary<string, PointLookupResult> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, List<PointLookupResult>> _byDataSource = new();
    
    private Timer? _refreshTimer;
    private DateTime? _lastRefreshTime;
    private int _cachedPointCount;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public DateTime? LastRefreshTime => _lastRefreshTime;
    public int CachedPointCount => _cachedPointCount;

    public PointLookupService(
        IServiceScopeFactory scopeFactory,
        ILogger<PointLookupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PointLookupService starting - performing initial cache load");
        
        // Initial load
        _ = RefreshCacheAsync(cancellationToken);
        
        // Schedule periodic refresh
        _refreshTimer = new Timer(
            async _ => await RefreshCacheAsync(CancellationToken.None),
            null,
            _refreshInterval,
            _refreshInterval);
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PointLookupService stopping");
        _refreshTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public async Task RefreshCacheAsync(CancellationToken ct = default)
    {
        if (!await _refreshLock.WaitAsync(TimeSpan.FromSeconds(5), ct))
        {
            _logger.LogWarning("Cache refresh already in progress, skipping");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NaiaDbContext>();
            
            // Load ALL points, not just those with SequenceId
            // Points without SequenceId will be cached by name for name-based lookups
            // This prevents data loss during the gap between Point creation and SequenceId assignment
            var points = await db.Points
                .AsNoTracking()
                .Select(p => new PointLookupResult
                {
                    Id = p.Id,
                    SequenceId = p.PointSequenceId ?? 0,  // 0 means not yet assigned
                    Name = p.Name,
                    DataSourceId = p.DataSourceId,
                    EngineeringUnits = p.EngineeringUnits,
                    DataType = p.ValueType.ToString(),
                    Description = p.Description,
                    HasSequenceId = p.PointSequenceId.HasValue  // Track if SequenceId is assigned
                })
                .ToListAsync(ct);

            // Clear and rebuild caches
            _bySequenceId.Clear();
            _byId.Clear();
            _byName.Clear();
            _byDataSource.Clear();

            var dataSourceGroups = new Dictionary<Guid, List<PointLookupResult>>();
            var pointsWithSequenceId = 0;
            var pointsWithoutSequenceId = 0;

            foreach (var point in points)
            {
                // Only add to SequenceId cache if it has a valid SequenceId
                if (point.HasSequenceId && point.SequenceId > 0)
                {
                    _bySequenceId[point.SequenceId] = point;
                    pointsWithSequenceId++;
                }
                else
                {
                    pointsWithoutSequenceId++;
                }
                
                // Always cache by Id and Name for lookups
                _byId[point.Id] = point;
                _byName[point.Name] = point;

                if (point.DataSourceId.HasValue)
                {
                    if (!dataSourceGroups.TryGetValue(point.DataSourceId.Value, out var list))
                    {
                        list = new List<PointLookupResult>();
                        dataSourceGroups[point.DataSourceId.Value] = list;
                    }
                    list.Add(point);
                }
            }

            foreach (var kvp in dataSourceGroups)
            {
                _byDataSource[kvp.Key] = kvp.Value;
            }

            _cachedPointCount = points.Count;
            _lastRefreshTime = DateTime.UtcNow;

            _logger.LogInformation(
                "Point lookup cache refreshed: {TotalPoints} points ({WithSequenceId} with SequenceId, {WithoutSequenceId} pending) from {DataSourceCount} data sources",
                points.Count, pointsWithSequenceId, pointsWithoutSequenceId, dataSourceGroups.Count);
            
            if (pointsWithoutSequenceId > 0)
            {
                _logger.LogWarning(
                    "{Count} points do not have PointSequenceId assigned - data for these points will use name-based lookup",
                    pointsWithoutSequenceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh point lookup cache");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public Task<PointLookupResult?> GetBySequenceIdAsync(long sequenceId, CancellationToken ct = default)
    {
        _bySequenceId.TryGetValue(sequenceId, out var result);
        return Task.FromResult(result);
    }

    public Task<PointLookupResult?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _byId.TryGetValue(id, out var result);
        return Task.FromResult(result);
    }

    public Task<PointLookupResult?> GetByNameAsync(string tagName, CancellationToken ct = default)
    {
        _byName.TryGetValue(tagName, out var result);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PointLookupResult>> GetByDataSourceAsync(Guid dataSourceId, CancellationToken ct = default)
    {
        if (_byDataSource.TryGetValue(dataSourceId, out var list))
        {
            return Task.FromResult<IReadOnlyList<PointLookupResult>>(list);
        }
        return Task.FromResult<IReadOnlyList<PointLookupResult>>(Array.Empty<PointLookupResult>());
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshLock.Dispose();
    }
}

// Extension to register the service
public static class PointLookupServiceExtensions
{
    public static IServiceCollection AddPointLookupService(this IServiceCollection services)
    {
        services.AddSingleton<PointLookupService>();
        services.AddSingleton<IPointLookupService>(sp => sp.GetRequiredService<PointLookupService>());
        services.AddHostedService(sp => sp.GetRequiredService<PointLookupService>());
        return services;
    }
}
