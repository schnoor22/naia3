using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;
using Npgsql;

namespace Naia.Infrastructure.TimeSeries;

/// <summary>
/// QuestDB time-series reader using PostgreSQL wire protocol.
/// 
/// QuestDB supports standard PostgreSQL protocol for queries, which allows us to:
/// - Use Npgsql (battle-tested PostgreSQL driver)
/// - Execute SQL queries with full QuestDB SQL extensions
/// - Use SAMPLE BY for time-based aggregations
/// </summary>
public sealed class QuestDbTimeSeriesReader : ITimeSeriesReader, IAsyncDisposable
{
    private readonly ILogger<QuestDbTimeSeriesReader> _logger;
    private readonly QuestDbOptions _options;
    private NpgsqlDataSource? _dataSource;
    private bool _disposed;
    
    public QuestDbTimeSeriesReader(
        IOptions<QuestDbOptions> options,
        ILogger<QuestDbTimeSeriesReader> logger)
    {
        _options = options.Value;
        _logger = logger;
    }
    
    private NpgsqlDataSource GetDataSource()
    {
        if (_dataSource != null)
            return _dataSource;
        
        // Parse endpoint (format: "host:port")
        var parts = _options.PgWireEndpoint.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? int.Parse(parts[1]) : 8812;
        
        // Configure connection string with QuestDB compatibility settings
        var connStringBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Username = "admin",
            Password = "quest",
            Database = "qdb",
            CommandTimeout = 60,
            Pooling = true,  // ENABLE pooling to handle concurrent access from multiple jobs
            MinPoolSize = 2,
            MaxPoolSize = 20,
            IncludeErrorDetail = false
        };
        
        // CRITICAL: Disable type introspection - QuestDB doesn't have pg_enum
        // Add the Server Compatibility Mode directly to the connection string
        var connString = connStringBuilder.ToString() + ";Server Compatibility Mode=NoTypeLoading";
        
        _dataSource = NpgsqlDataSource.Create(connString);
        _logger.LogInformation("QuestDB reader initialized with pooling: {Endpoint}", _options.PgWireEndpoint);
        
        return _dataSource;
    }
    
    public async Task<IReadOnlyList<DataPoint>> ReadRangeAsync(
        long pointSequenceId,
        DateTime startTime,
        DateTime endTime,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var ds = GetDataSource();
        var results = new List<DataPoint>();
        
        // QuestDB doesn't support parameterized queries well with Npgsql - use string interpolation
        var sql = $@"
            SELECT timestamp, point_id, value, quality
            FROM {_options.TableName}
            WHERE point_id = {pointSequenceId}
              AND timestamp >= '{startTime:yyyy-MM-ddTHH:mm:ss.ffffffZ}'
              AND timestamp <= '{endTime:yyyy-MM-ddTHH:mm:ss.ffffffZ}'
            ORDER BY timestamp
            {(limit.HasValue ? $"LIMIT {limit.Value}" : "")}";
        
        await using var cmd = ds.CreateCommand(sql);
        
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new DataPoint
                {
                    PointSequenceId = reader.GetInt64(1),
                    PointName = $"point_{reader.GetInt64(1)}",
                    Timestamp = reader.GetDateTime(0),
                    Value = reader.GetDouble(2),
                    Quality = (DataQuality)reader.GetInt32(3)
                });
            }
            
            _logger.LogDebug("Retrieved {Count} historical points for {PointId}", results.Count, pointSequenceId);
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("does not exist"))
        {
            // Table doesn't exist yet - return empty
            _logger.LogWarning("QuestDB query failed: {Message} | Endpoint: {Endpoint} | Table: {Table}", 
                ex.Message, _options.PgWireEndpoint, _options.TableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading historical data from QuestDB for point {PointId}", pointSequenceId);
            throw;
        }
        
        return results;
    }
    
    public async Task<DataPoint?> GetLastValueAsync(
        long pointSequenceId,
        CancellationToken cancellationToken = default)
    {
        var ds = GetDataSource();
        
        // QuestDB doesn't support parameterized queries well with Npgsql - use string interpolation
        var sql = $@"
            SELECT timestamp, point_id, value, quality
            FROM {_options.TableName}
            WHERE point_id = {pointSequenceId}
            ORDER BY timestamp DESC
            LIMIT 1";
        
        await using var cmd = ds.CreateCommand(sql);
        
        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                
                if (await reader.ReadAsync(cancellationToken))
                {
                    return new DataPoint
                    {
                        PointSequenceId = reader.GetInt64(1),
                        PointName = $"point_{reader.GetInt64(1)}",
                        Timestamp = reader.GetDateTime(0),
                        Value = reader.GetDouble(2),
                        Quality = (DataQuality)reader.GetInt32(3)
                    };
                }
                
                // Successfully read (no data), exit retry loop
                break;
            }
            catch (NpgsqlException ex) when (ex.Message.Contains("does not exist"))
            {
                _logger.LogDebug("Table {Table} does not exist yet", _options.TableName);
                break; // Don't retry for missing table
            }
            catch (EndOfStreamException ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "QuestDB connection interrupted for point {PointId}, retrying (attempt {Attempt}/{MaxRetries})", 
                    pointSequenceId, attempt + 1, maxRetries);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
                continue; // Retry
            }
            catch (NpgsqlException ex) when (ex.InnerException is EndOfStreamException && attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "QuestDB stream error for point {PointId}, retrying (attempt {Attempt}/{MaxRetries})", 
                    pointSequenceId, attempt + 1, maxRetries);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
                continue; // Retry
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading last value from QuestDB for point {PointId}: {Message}", pointSequenceId, ex.Message);
                throw;
            }
        }
        
        return null;
    }
    
    public async Task<AggregatedData?> GetAggregatedAsync(
        long pointSequenceId,
        DateTime startTime,
        DateTime endTime,
        AggregationPeriod period,
        CancellationToken cancellationToken = default)
    {
        var ds = GetDataSource();
        
        // QuestDB doesn't support parameterized queries well with Npgsql - use string interpolation
        var sql = $@"
            SELECT 
                min(value) as min_value,
                max(value) as max_value,
                avg(value) as avg_value,
                stddev_samp(value) as std_dev,
                count(*) as sample_count
            FROM {_options.TableName}
            WHERE point_id = {pointSequenceId}
              AND timestamp >= '{startTime:yyyy-MM-ddTHH:mm:ss.ffffffZ}'
              AND timestamp <= '{endTime:yyyy-MM-ddTHH:mm:ss.ffffffZ}'";
        
        await using var cmd = ds.CreateCommand(sql);
        
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            
            if (await reader.ReadAsync(cancellationToken))
            {
                var sampleCount = reader.GetInt64(4);
                if (sampleCount == 0)
                    return null;
                    
                return new AggregatedData
                {
                    PointSequenceId = pointSequenceId,
                    PeriodStart = startTime,
                    PeriodEnd = endTime,
                    MinValue = reader.GetDouble(0),
                    MaxValue = reader.GetDouble(1),
                    AvgValue = reader.GetDouble(2),
                    StdDev = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                    SampleCount = sampleCount
                };
            }
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("does not exist"))
        {
            _logger.LogDebug("Table {Table} does not exist yet", _options.TableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading aggregated data from QuestDB for point {PointId}: {Message}", pointSequenceId, ex.Message);
            throw;
        }
        
        return null;
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        if (_dataSource != null)
        {
            await _dataSource.DisposeAsync();
            _dataSource = null;
        }
        
        _logger.LogInformation("QuestDB reader disposed");
    }
}
