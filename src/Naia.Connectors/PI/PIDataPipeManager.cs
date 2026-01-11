using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Naia.Connectors.Abstractions;
using OSIsoft.AF;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;
using OSIsoft.AF.Time;

namespace Naia.Connectors.PI;

/// <summary>
/// Manages snapshot polling for real-time PI data streaming.
/// 
/// Uses periodic snapshot polling instead of AFDataPipe because:
/// - AFDataPipe requires AF (Asset Framework) attributes, not direct PI Points
/// - For PI Data Archive points, snapshot polling is simpler and reliable
/// - Can poll 1M+ points efficiently with batch CurrentValue() calls
/// 
/// Flow:
///   PI Archive → Snapshot Poll Timer → Channel (buffer) → Kafka Producer
/// 
/// Backpressure handling:
/// - Bounded channel prevents memory overflow
/// - If full, drops oldest values
/// </summary>
public sealed class PIDataPipeManager : IDisposable
{
    private readonly PIAfSdkConnector _connector;
    private readonly ILogger<PIDataPipeManager> _logger;
    private readonly Channel<DataPointUpdate> _updateChannel;
    private readonly TimeSpan _pollInterval;
    
    private Timer? _pollTimer;
    private readonly List<PIPoint> _subscribedPoints = new();
    private readonly Dictionary<int, string> _pointIdToAddress = new();
    private readonly Dictionary<int, AFValue> _lastValues = new();
    private bool _isSubscribed;
    
    public PIDataPipeManager(
        PIAfSdkConnector connector,
        ILogger<PIDataPipeManager> logger,
        int channelCapacity = 100000,
        TimeSpan? pollInterval = null)
    {
        _connector = connector;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1); // Default 1 second polling
        
        // Create bounded channel for backpressure
        _updateChannel = Channel.CreateBounded<DataPointUpdate>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // Drop oldest when full
            SingleReader = false,
            SingleWriter = true
        });
    }
    
    /// <summary>
    /// Subscribe to real-time updates for the given points using snapshot polling.
    /// </summary>
    public async Task SubscribeAsync(IEnumerable<string> sourceAddresses, CancellationToken ct = default)
    {
        var piServer = _connector.GetPIServer();
        if (piServer == null)
        {
            throw new InvalidOperationException("PI Server not initialized");
        }
        
        // Clear existing subscriptions
        if (_isSubscribed && _pollTimer != null)
        {
            await _pollTimer.DisposeAsync();
            _subscribedPoints.Clear();
            _pointIdToAddress.Clear();
            _lastValues.Clear();
            _isSubscribed = false;
        }
        
        // Get PIPoint objects
        var points = new List<PIPoint>();
        foreach (var address in sourceAddresses)
        {
            try
            {
                var point = await Task.Run(() => PIPoint.FindPIPoint(piServer, address), ct);
                if (point != null)
                {
                    points.Add(point);
                    _pointIdToAddress[point.ID] = address;
                }
                else
                {
                    _logger.LogWarning("PI Point not found: {Address}", address);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve PI Point: {Address}", address);
            }
        }
        
        if (points.Count == 0)
        {
            _logger.LogWarning("No valid PI Points to subscribe to");
            return;
        }
        
        _subscribedPoints.AddRange(points);
        _isSubscribed = true;
        
        // Start polling timer
        _pollTimer = new Timer(
            callback: _ => PollSnapshots(),
            state: null,
            dueTime: TimeSpan.Zero, // Start immediately
            period: _pollInterval);
        
        _logger.LogInformation("Subscribed to {Count} PI Points with {Interval}s polling", 
            points.Count, _pollInterval.TotalSeconds);
    }
    
    /// <summary>
    /// Poll all subscribed points for current values.
    /// </summary>
    private void PollSnapshots()
    {
        if (!_isSubscribed || _subscribedPoints.Count == 0)
        {
            return;
        }
        
        try
        {
            // Batch read current values
            var values = new AFValues();
            
            foreach (var point in _subscribedPoints)
            {
                try
                {
                    var value = point.CurrentValue();
                    
                    // Check if value changed from last poll
                    if (_lastValues.TryGetValue(point.ID, out var lastValue))
                    {
                        if (value.Timestamp == lastValue.Timestamp && 
                            Equals(value.Value, lastValue.Value))
                        {
                            continue; // No change, skip
                        }
                    }
                    
                    _lastValues[point.ID] = value;
                    
                    if (!_pointIdToAddress.TryGetValue(point.ID, out var address))
                    {
                        continue;
                    }
                    
                    var update = new DataPointUpdate
                    {
                        SourceAddress = address,
                        PointName = point.Name,
                        Value = value.Value,
                        Timestamp = value.Timestamp.UtcTime,
                        Quality = value.IsGood ? DataQuality.Good : DataQuality.Bad,
                        Units = value.UOM?.Abbreviation,
                        IsSnapshot = true,
                        ReceivedAt = DateTime.UtcNow
                    };
                    
                    // Try write to channel (non-blocking)
                    if (!_updateChannel.Writer.TryWrite(update))
                    {
                        _logger.LogWarning("Channel full, dropping update for {Point}", address);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read snapshot for {Point}", point.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling snapshots");
        }
    }
    
    /// <summary>
    /// Get the channel reader for consuming updates.
    /// </summary>
    public ChannelReader<DataPointUpdate> GetUpdateReader() => _updateChannel.Reader;
    
    /// <summary>
    /// Get subscription statistics.
    /// </summary>
    public SubscriptionStats GetStats()
    {
        return new SubscriptionStats
        {
            SubscribedPointCount = _subscribedPoints.Count,
            IsSubscribed = _isSubscribed,
            ChannelCount = _updateChannel.Reader.Count
        };
    }
    
    public void Dispose()
    {
        try
        {
            _pollTimer?.Dispose();
            _subscribedPoints.Clear();
            _pointIdToAddress.Clear();
            _lastValues.Clear();
            _updateChannel.Writer.Complete();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing PIDataPipeManager");
        }
    }
}

/// <summary>
/// Represents a data point update from AFDataPipe.
/// </summary>
public sealed class DataPointUpdate
{
    public required string SourceAddress { get; init; }
    public required string PointName { get; init; }
    public object? Value { get; init; }
    public DateTime Timestamp { get; init; }
    public DataQuality Quality { get; init; }
    public string? Units { get; init; }
    public bool IsSnapshot { get; init; }
    public DateTime ReceivedAt { get; init; }
}

/// <summary>
/// Subscription statistics.
/// </summary>
public sealed class SubscriptionStats
{
    public int SubscribedPointCount { get; init; }
    public bool IsSubscribed { get; init; }
    public int ChannelCount { get; init; }
}
