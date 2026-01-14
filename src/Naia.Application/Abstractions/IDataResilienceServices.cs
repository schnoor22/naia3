using Naia.Domain.ValueObjects;

namespace Naia.Application.Abstractions;

/// <summary>
/// Temporal Integrity Chain (TIC) - Blockchain-inspired cryptographic fingerprinting
/// for instant gap detection in data pipelines. Each batch is cryptographically linked
/// to the previous batch, forming an unbreakable chain that reveals any gaps within seconds.
/// </summary>
public interface IIntegrityChainService
{
    /// <summary>
    /// Generate a chain entry for a new batch, linking it to the previous batch.
    /// The chain hash includes: previous_hash + batch_id + point_count + timestamp + data_hash
    /// </summary>
    Task<ChainEntry> CreateChainEntryAsync(
        DataPointBatch batch,
        string dataSourceId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validate a received batch against the chain. Returns validation result with gap info.
    /// </summary>
    Task<ChainValidationResult> ValidateChainAsync(
        ChainEntry entry,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the current chain state for a data source (last known good entry).
    /// </summary>
    Task<ChainEntry?> GetLastEntryAsync(
        string dataSourceId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Record a checkpoint in the chain (for recovery purposes).
    /// </summary>
    Task CreateCheckpointAsync(
        string dataSourceId,
        string reason,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Detect gaps in the chain for a data source within a time range.
    /// </summary>
    Task<IReadOnlyList<ChainGap>> DetectGapsAsync(
        string dataSourceId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Shadow Historian - Local SQLite buffer that runs alongside connectors.
/// Captures ALL data before it enters Kafka, providing a 7-day rolling safety net.
/// </summary>
public interface IShadowBuffer
{
    /// <summary>
    /// Buffer a batch BEFORE sending to Kafka. Returns shadow ID for reconciliation.
    /// </summary>
    Task<string> BufferAsync(
        DataPointBatch batch,
        string dataSourceId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Mark a batch as confirmed (successfully stored in historian).
    /// </summary>
    Task ConfirmAsync(
        string shadowId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all unconfirmed batches (potential gaps) for a data source.
    /// </summary>
    Task<IReadOnlyList<ShadowEntry>> GetUnconfirmedAsync(
        string dataSourceId,
        DateTime since,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get buffered data for recovery/replay.
    /// </summary>
    Task<IReadOnlyList<ShadowEntry>> GetForRecoveryAsync(
        string dataSourceId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Purge confirmed entries older than retention period (default 7 days).
    /// </summary>
    Task PurgeExpiredAsync(
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get buffer statistics.
    /// </summary>
    Task<ShadowBufferStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Gap Detection & Recovery Service - Coordinates TIC + Shadow Buffer for automatic healing.
/// </summary>
public interface IGapRecoveryService
{
    /// <summary>
    /// Scan for gaps and initiate recovery from shadow buffer.
    /// </summary>
    Task<GapRecoveryResult> ScanAndRecoverAsync(
        string? dataSourceId = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Request backfill for a detected gap.
    /// </summary>
    Task RequestBackfillAsync(
        ChainGap gap,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get recovery status for a data source.
    /// </summary>
    Task<RecoveryStatus> GetStatusAsync(
        string dataSourceId,
        CancellationToken cancellationToken = default);
}

#region DTOs for Temporal Integrity Chain

/// <summary>
/// A single entry in the temporal integrity chain.
/// </summary>
public sealed record ChainEntry
{
    /// <summary>Unique ID for this chain entry</summary>
    public required string EntryId { get; init; }
    
    /// <summary>Data source this entry belongs to</summary>
    public required string DataSourceId { get; init; }
    
    /// <summary>Sequence number in the chain (monotonically increasing per source)</summary>
    public required long SequenceNumber { get; init; }
    
    /// <summary>Batch ID from the data point batch</summary>
    public required string BatchId { get; init; }
    
    /// <summary>Number of points in the batch</summary>
    public required int PointCount { get; init; }
    
    /// <summary>Earliest timestamp in the batch</summary>
    public required DateTime MinTimestamp { get; init; }
    
    /// <summary>Latest timestamp in the batch</summary>
    public required DateTime MaxTimestamp { get; init; }
    
    /// <summary>When this entry was created</summary>
    public required DateTime CreatedAt { get; init; }
    
    /// <summary>Hash of the previous entry (SHA-256)</summary>
    public required string PreviousHash { get; init; }
    
    /// <summary>Hash of the data content (SHA-256 of serialized points)</summary>
    public required string DataHash { get; init; }
    
    /// <summary>
    /// Chain hash = SHA256(PreviousHash + BatchId + PointCount + Timestamps + DataHash)
    /// This creates the unbreakable link to the previous entry.
    /// </summary>
    public required string ChainHash { get; init; }
    
    /// <summary>Is this a checkpoint entry (marks a known-good state)?</summary>
    public bool IsCheckpoint { get; init; }
    
    /// <summary>Reason for checkpoint (if applicable)</summary>
    public string? CheckpointReason { get; init; }
}

/// <summary>
/// Result of validating a chain entry.
/// </summary>
public sealed record ChainValidationResult
{
    public required bool IsValid { get; init; }
    public required string EntryId { get; init; }
    public required long ExpectedSequence { get; init; }
    public required long ActualSequence { get; init; }
    public ChainGap? DetectedGap { get; init; }
    public string? ErrorMessage { get; init; }
    
    public static ChainValidationResult Valid(string entryId, long sequence) => new()
    {
        IsValid = true,
        EntryId = entryId,
        ExpectedSequence = sequence,
        ActualSequence = sequence
    };
    
    public static ChainValidationResult Invalid(
        string entryId,
        long expected,
        long actual,
        ChainGap gap,
        string error) => new()
    {
        IsValid = false,
        EntryId = entryId,
        ExpectedSequence = expected,
        ActualSequence = actual,
        DetectedGap = gap,
        ErrorMessage = error
    };
}

/// <summary>
/// Represents a detected gap in the integrity chain.
/// </summary>
public sealed record ChainGap
{
    /// <summary>Unique ID for this gap</summary>
    public required string GapId { get; init; }
    
    /// <summary>Data source where gap was detected</summary>
    public required string DataSourceId { get; init; }
    
    /// <summary>Last known good sequence number before the gap</summary>
    public required long LastGoodSequence { get; init; }
    
    /// <summary>First sequence number after the gap</summary>
    public required long FirstBadSequence { get; init; }
    
    /// <summary>Number of missing entries</summary>
    public required int MissingCount { get; init; }
    
    /// <summary>Estimated start time of the gap</summary>
    public required DateTime GapStartTime { get; init; }
    
    /// <summary>Estimated end time of the gap</summary>
    public required DateTime GapEndTime { get; init; }
    
    /// <summary>When this gap was detected</summary>
    public required DateTime DetectedAt { get; init; }
    
    /// <summary>Current status of gap recovery</summary>
    public GapStatus Status { get; init; } = GapStatus.Detected;
    
    /// <summary>Recovery attempts made</summary>
    public int RecoveryAttempts { get; init; }
    
    /// <summary>Last recovery error (if any)</summary>
    public string? LastRecoveryError { get; init; }
}

/// <summary>
/// Gap recovery status.
/// </summary>
public enum GapStatus
{
    Detected = 0,
    RecoveryInProgress = 1,
    Recovered = 2,
    RecoveryFailed = 3,
    Abandoned = 4
}

#endregion

#region DTOs for Shadow Buffer

/// <summary>
/// A single entry in the shadow buffer.
/// </summary>
public sealed record ShadowEntry
{
    /// <summary>Unique ID for this shadow entry</summary>
    public required string ShadowId { get; init; }
    
    /// <summary>Data source this entry belongs to</summary>
    public required string DataSourceId { get; init; }
    
    /// <summary>Original batch ID</summary>
    public required string BatchId { get; init; }
    
    /// <summary>Chain entry ID (for linking to TIC)</summary>
    public string? ChainEntryId { get; init; }
    
    /// <summary>Number of points in the batch</summary>
    public required int PointCount { get; init; }
    
    /// <summary>Serialized batch data (JSON)</summary>
    public required string BatchJson { get; init; }
    
    /// <summary>Compressed batch data (for efficiency)</summary>
    public byte[]? CompressedData { get; init; }
    
    /// <summary>When this entry was buffered</summary>
    public required DateTime BufferedAt { get; init; }
    
    /// <summary>When this entry was confirmed as stored in historian</summary>
    public DateTime? ConfirmedAt { get; init; }
    
    /// <summary>Is this entry confirmed?</summary>
    public bool IsConfirmed => ConfirmedAt.HasValue;
    
    /// <summary>Original timestamps range</summary>
    public required DateTime MinTimestamp { get; init; }
    
    /// <summary>Original timestamps range</summary>
    public required DateTime MaxTimestamp { get; init; }
}

/// <summary>
/// Shadow buffer statistics.
/// </summary>
public sealed record ShadowBufferStats
{
    public required long TotalEntries { get; init; }
    public required long ConfirmedEntries { get; init; }
    public required long UnconfirmedEntries { get; init; }
    public required long TotalPoints { get; init; }
    public required long DatabaseSizeBytes { get; init; }
    public required DateTime OldestEntry { get; init; }
    public required DateTime NewestEntry { get; init; }
    public required Dictionary<string, long> EntriesPerDataSource { get; init; }
}

#endregion

#region DTOs for Gap Recovery

/// <summary>
/// Result of gap recovery scan.
/// </summary>
public sealed record GapRecoveryResult
{
    public required int GapsDetected { get; init; }
    public required int GapsRecovered { get; init; }
    public required int GapsFailed { get; init; }
    public required long PointsRecovered { get; init; }
    public required TimeSpan Duration { get; init; }
    public required IReadOnlyList<ChainGap> ActiveGaps { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Current recovery status for a data source.
/// </summary>
public sealed record RecoveryStatus
{
    public required string DataSourceId { get; init; }
    public required int PendingGaps { get; init; }
    public required int RecoveringGaps { get; init; }
    public required long UnconfirmedInShadow { get; init; }
    public required DateTime LastChainValidation { get; init; }
    public required bool IsHealthy { get; init; }
    public string? HealthMessage { get; init; }
}

#endregion
