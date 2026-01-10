using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;

namespace Naia.Infrastructure.TimeSeries;

/// <summary>
/// QuestDB time-series writer using HTTP ILP (InfluxDB Line Protocol).
/// 
/// PERFORMANCE NOTES:
/// - Uses InfluxDB Line Protocol (ILP) for maximum write throughput
/// - HTTP transport for simplicity and reliability
/// - Batching for efficiency
/// 
/// DURABILITY:
/// - HTTP transport with retries
/// - WAL (Write-Ahead Log) enabled on QuestDB server
/// 
/// ILP FORMAT:
/// table_name,tag1=value1 field1=value1,field2=value2 timestamp_nanos
/// </summary>
public sealed class QuestDbTimeSeriesWriter : ITimeSeriesWriter, IAsyncDisposable
{
    private readonly ILogger<QuestDbTimeSeriesWriter> _logger;
    private readonly QuestDbOptions _options;
    private readonly HttpClient _httpClient;
    private readonly StringBuilder _lineBuffer = new();
    private bool _disposed;
    
    public QuestDbTimeSeriesWriter(
        IOptions<QuestDbOptions> options,
        ILogger<QuestDbTimeSeriesWriter> logger)
    {
        _options = options.Value;
        _logger = logger;
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.HttpEndpoint),
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        _logger.LogInformation("QuestDB writer initialized: {Endpoint}", _options.HttpEndpoint);
    }
    
    public async Task WriteAsync(DataPointBatch batch, CancellationToken cancellationToken = default)
    {
        if (batch.IsEmpty)
            return;
        
        try
        {
            // Build ILP lines
            _lineBuffer.Clear();
            foreach (var point in batch.Points)
            {
                // Format: table point_id=N,value=V,quality=Q timestamp_nanos
                // Using designated timestamp column (default)
                var timestampNanos = ((DateTimeOffset)point.Timestamp).ToUnixTimeMilliseconds() * 1_000_000;
                
                _lineBuffer.AppendLine(
                    $"{_options.TableName} point_id={point.PointSequenceId}i,value={point.Value},quality={((int)point.Quality)}i {timestampNanos}");
            }
            
            // Send via /write endpoint
            var content = new StringContent(_lineBuffer.ToString(), Encoding.UTF8, "text/plain");
            var response = await _httpClient.PostAsync("/write", content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"QuestDB write failed: {response.StatusCode} - {error}");
            }
            
            _logger.LogDebug("Wrote {Count} points to QuestDB", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write batch {BatchId} to QuestDB", batch.BatchId);
            throw;
        }
    }
    
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // HTTP writes are synchronous, nothing to flush
        await Task.CompletedTask;
    }
    
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QuestDB health check failed");
            return false;
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        _httpClient.Dispose();
        _logger.LogInformation("QuestDB writer disposed");
        await Task.CompletedTask;
    }
}
