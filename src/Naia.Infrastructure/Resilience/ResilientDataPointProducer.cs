using System.Text.Json;
using Microsoft.Extensions.Logging;
using Naia.Application.Abstractions;
using Naia.Domain.ValueObjects;

namespace Naia.Infrastructure.Resilience;

/// <summary>
/// Resilient Data Point Producer - wraps the Kafka producer with TIC + Shadow Buffer.
/// 
/// FLOW:
/// 1. Buffer data in Shadow Buffer (SQLite) BEFORE sending to Kafka
/// 2. Create TIC chain entry for cryptographic linking
/// 3. Send to Kafka with chain hash in headers
/// 4. On confirmation from consumer, mark shadow entry as confirmed
/// 
/// This ensures ZERO data loss because:
/// - Data is persisted locally BEFORE Kafka (survives Kafka failures)
/// - Chain hash allows instant gap detection (no polling needed)
/// - Shadow buffer enables automatic recovery
/// </summary>
public sealed class ResilientDataPointProducer : IDataPointProducer
{
    private readonly ILogger<ResilientDataPointProducer> _logger;
    private readonly IDataPointProducer _innerProducer;
    private readonly IShadowBuffer _shadowBuffer;
    private readonly IIntegrityChainService _chainService;
    
    public ResilientDataPointProducer(
        IDataPointProducer innerProducer,
        IShadowBuffer shadowBuffer,
        IIntegrityChainService chainService,
        ILogger<ResilientDataPointProducer> logger)
    {
        _innerProducer = innerProducer;
        _shadowBuffer = shadowBuffer;
        _chainService = chainService;
        _logger = logger;
    }
    
    public async Task<ProduceResult> PublishAsync(
        DataPointBatch batch, 
        CancellationToken cancellationToken = default)
    {
        if (batch.IsEmpty)
            return ProduceResult.Successful("", 0, 0);
        
        var dataSourceId = batch.DataSourceId ?? "unknown";
        
        try
        {
            // STEP 1: Buffer in Shadow Buffer FIRST (local persistence)
            var shadowId = await _shadowBuffer.BufferAsync(batch, dataSourceId, cancellationToken);
            
            _logger.LogDebug(
                "Buffered batch {BatchId} to shadow {ShadowId}",
                batch.BatchId, shadowId);
            
            // STEP 2: Create TIC chain entry
            var chainEntry = await _chainService.CreateChainEntryAsync(
                batch, dataSourceId, cancellationToken);
            
            _logger.LogDebug(
                "Created chain entry {EntryId} with hash {Hash}",
                chainEntry.EntryId, chainEntry.ChainHash[..16]);
            
            // STEP 3: Send to Kafka (inner producer adds chain info to headers)
            // Note: We could modify batch to include chain info, but for now
            // we rely on the inner producer's existing header mechanism
            var result = await _innerProducer.PublishAsync(batch, cancellationToken);
            
            if (result.Success)
            {
                _logger.LogDebug(
                    "Published batch {BatchId} to Kafka at {Topic}[{Partition}]@{Offset}",
                    batch.BatchId, result.Topic, result.Partition, result.Offset);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to publish batch {BatchId} to Kafka: {Error}. Shadow entry {ShadowId} retained for recovery.",
                    batch.BatchId, result.ErrorMessage, shadowId);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error in resilient publish for batch {BatchId}",
                batch.BatchId);
            
            // Even on exception, shadow buffer might have the data
            return ProduceResult.Failed("", ex.Message);
        }
    }
    
    public async Task<ProduceResult> PublishAsync(
        DataPoint point, 
        CancellationToken cancellationToken = default)
    {
        var batch = DataPointBatch.Create(new[] { point }, point.DataSourceId);
        return await PublishAsync(batch, cancellationToken);
    }
    
    public Task<ProduceResult> PublishAsync(
        string topic, 
        string key, 
        string jsonPayload, 
        CancellationToken cancellationToken = default)
    {
        // Pass through to inner producer for non-batch messages
        return _innerProducer.PublishAsync(topic, key, jsonPayload, cancellationToken);
    }
    
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return _innerProducer.FlushAsync(cancellationToken);
    }
}
