using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Npgsql;

namespace Naia.PatternEngine.Jobs;

/// <summary>
/// Processes user feedback to update pattern confidence and create point bindings.
/// This is the learning component of the Pattern Flywheel - the system gets smarter
/// with each user interaction.
/// 
/// Actions:
/// - Approved: +5% confidence, bind points to pattern, create element
/// - Rejected: -3% confidence, log rejection reason for analysis
/// - Deferred: No change, logged for analysis
/// 
/// Also applies confidence decay (0.5% per day of inactivity).
/// Runs hourly via Hangfire scheduler.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 3300)]
public sealed class PatternLearningJob : IPatternLearningJob
{
    private readonly ILogger<PatternLearningJob> _logger;
    private readonly PatternFlywheelOptions _options;
    private readonly string _postgresConnectionString;

    private const double ApprovalBoost = 0.05;    // +5% for approval
    private const double RejectionPenalty = 0.03; // -3% for rejection
    private const double DailyDecay = 0.005;      // 0.5% decay per day
    private const double MinConfidence = 0.30;    // Floor at 30%
    private const double MaxConfidence = 1.00;    // Ceiling at 100%

    public PatternLearningJob(
        ILogger<PatternLearningJob> logger,
        IOptions<PatternFlywheelOptions> options,
        string postgresConnectionString)
    {
        _logger = logger;
        _options = options.Value;
        _postgresConnectionString = postgresConnectionString;
    }

    public async Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken)
    {
        context?.WriteLine("Starting pattern learning job...");
        _logger.LogInformation("Starting pattern learning job");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var approvalsProcessed = 0;
        var rejectionsProcessed = 0;
        var patternsDecayed = 0;

        try
        {
            // Process approved suggestions
            approvalsProcessed = await ProcessApprovedSuggestionsAsync(context, cancellationToken);
            context?.WriteLine($"Processed {approvalsProcessed} approvals");

            // Process rejected suggestions
            rejectionsProcessed = await ProcessRejectedSuggestionsAsync(context, cancellationToken);
            context?.WriteLine($"Processed {rejectionsProcessed} rejections");

            // Apply confidence decay to inactive patterns
            patternsDecayed = await ApplyConfidenceDecayAsync(cancellationToken);
            context?.WriteLine($"Applied decay to {patternsDecayed} patterns");

            // Cleanup expired suggestions
            var expired = await CleanupExpiredSuggestionsAsync(cancellationToken);
            context?.WriteLine($"Cleaned up {expired} expired suggestions");

            stopwatch.Stop();
            context?.WriteLine($"Completed: {approvalsProcessed} approved, {rejectionsProcessed} rejected, {patternsDecayed} decayed, {stopwatch.ElapsedMilliseconds}ms");

            _logger.LogInformation(
                "Pattern learning complete: {Approved} approved, {Rejected} rejected, {Decayed} decayed, {Duration}ms",
                approvalsProcessed, rejectionsProcessed, patternsDecayed, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pattern learning job failed");
            context?.WriteLine($"ERROR: {ex.Message}");
            throw;
        }
    }

    private async Task<int> ProcessApprovedSuggestionsAsync(
        PerformContext? context,
        CancellationToken cancellationToken)
    {
        var processed = 0;

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        // Get approved suggestions that haven't been processed yet
        var selectSql = @"
            SELECT ps.id, ps.cluster_id, ps.pattern_id, ps.overall_confidence,
                   ps.reviewed_at, ps.reviewed_by
            FROM pattern_suggestions ps
            WHERE ps.status = 'approved'
            ORDER BY ps.created_at ASC
            LIMIT 100
        ";

        var suggestions = new List<ApprovedSuggestion>();
        await using (var selectCmd = new NpgsqlCommand(selectSql, conn))
        await using (var reader = await selectCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                suggestions.Add(new ApprovedSuggestion
                {
                    Id = reader.GetGuid(0),
                    ClusterId = reader.GetGuid(1),
                    PatternId = reader.GetGuid(2),
                    Confidence = reader.GetDouble(3),
                    ReviewedBy = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ReviewedAt = reader.IsDBNull(4) ? DateTime.UtcNow : reader.GetDateTime(4)
                });
            }
        }

        foreach (var suggestion in suggestions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Get current pattern confidence
                var currentConfidence = await GetPatternConfidenceAsync(conn, suggestion.PatternId, cancellationToken);

                // Boost confidence
                var newConfidence = Math.Min(currentConfidence + ApprovalBoost, MaxConfidence);
                await UpdatePatternConfidenceAsync(conn, suggestion.PatternId, newConfidence, cancellationToken);

                // Create point bindings
                await CreatePointBindingsAsync(conn, suggestion, cancellationToken);

                // Log feedback
                await LogFeedbackAsync(conn, suggestion, "approved", currentConfidence, newConfidence, null, cancellationToken);

                processed++;

                _logger.LogDebug(
                    "Processed approval for suggestion {SuggestionId}, pattern {PatternId} confidence: {Old:P0} → {New:P0}",
                    suggestion.Id, suggestion.PatternId, currentConfidence, newConfidence);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process approval for suggestion {SuggestionId}", suggestion.Id);
            }
        }

        return processed;
    }

    private async Task<int> ProcessRejectedSuggestionsAsync(
        PerformContext? context,
        CancellationToken cancellationToken)
    {
        var processed = 0;

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        // Get rejected suggestions that haven't been processed yet
        var selectSql = @"
            SELECT ps.id, ps.cluster_id, ps.pattern_id, ps.overall_confidence,
                   ps.rejection_reason
            FROM pattern_suggestions ps
            WHERE ps.status = 'rejected'
            ORDER BY ps.created_at ASC
            LIMIT 100
        ";

        var suggestions = new List<RejectedSuggestion>();
        await using (var selectCmd = new NpgsqlCommand(selectSql, conn))
        await using (var reader = await selectCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                suggestions.Add(new RejectedSuggestion
                {
                    Id = reader.GetGuid(0),
                    ClusterId = reader.GetGuid(1),
                    PatternId = reader.GetGuid(2),
                    Confidence = reader.GetDouble(3),
                    RejectionReason = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
        }

        foreach (var suggestion in suggestions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Get current pattern confidence
                var currentConfidence = await GetPatternConfidenceAsync(conn, suggestion.PatternId, cancellationToken);

                // Apply rejection penalty
                var newConfidence = Math.Max(currentConfidence - RejectionPenalty, MinConfidence);
                await UpdatePatternConfidenceAsync(conn, suggestion.PatternId, newConfidence, cancellationToken);

                // Log feedback with rejection reason
                await LogFeedbackAsync(conn, suggestion, "rejected", currentConfidence, newConfidence, 
                    suggestion.RejectionReason, cancellationToken);

                processed++;

                _logger.LogDebug(
                    "Processed rejection for suggestion {SuggestionId}, pattern {PatternId} confidence: {Old:P0} → {New:P0}",
                    suggestion.Id, suggestion.PatternId, currentConfidence, newConfidence);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process rejection for suggestion {SuggestionId}", suggestion.Id);
            }
        }

        return processed;
    }

    private async Task<double> GetPatternConfidenceAsync(
        NpgsqlConnection conn,
        Guid patternId,
        CancellationToken cancellationToken)
    {
        var sql = "SELECT confidence FROM patterns WHERE id = @Id";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", patternId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is double d ? d : 0.5;
    }

    private async Task UpdatePatternConfidenceAsync(
        NpgsqlConnection conn,
        Guid patternId,
        double newConfidence,
        CancellationToken cancellationToken)
    {
        var sql = @"
            UPDATE patterns 
            SET confidence = @Confidence, 
                last_matched_at = NOW(),
                example_count = example_count + 1
            WHERE id = @Id
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Confidence", newConfidence);
        cmd.Parameters.AddWithValue("@Id", patternId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task CreatePointBindingsAsync(
        NpgsqlConnection conn,
        ApprovedSuggestion suggestion,
        CancellationToken cancellationToken)
    {
        // Get cluster points
        var clusterSql = "SELECT point_ids FROM behavioral_clusters WHERE id = @ClusterId";
        await using var clusterCmd = new NpgsqlCommand(clusterSql, conn);
        clusterCmd.Parameters.AddWithValue("@ClusterId", suggestion.ClusterId);
        var pointIds = await clusterCmd.ExecuteScalarAsync(cancellationToken) as Guid[];

        if (pointIds == null || pointIds.Length == 0)
            return;

        // Insert point bindings
        var insertSql = @"
            INSERT INTO point_pattern_bindings (point_id, pattern_id, bound_by, confidence_at_binding)
            VALUES (@PointId, @PatternId, @BoundBy, @Confidence)
            ON CONFLICT (point_id, pattern_id) DO UPDATE SET
                bound_at = NOW(),
                confidence_at_binding = EXCLUDED.confidence_at_binding
        ";

        foreach (var pointId in pointIds)
        {
            await using var cmd = new NpgsqlCommand(insertSql, conn);
            cmd.Parameters.AddWithValue("@PointId", pointId);
            cmd.Parameters.AddWithValue("@PatternId", suggestion.PatternId);
            cmd.Parameters.AddWithValue("@BoundBy", suggestion.ReviewedBy ?? "system");
            cmd.Parameters.AddWithValue("@Confidence", suggestion.Confidence);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogDebug("Created {Count} point bindings for pattern {PatternId}", 
            pointIds.Length, suggestion.PatternId);
    }

    private async Task LogFeedbackAsync(
        NpgsqlConnection conn,
        dynamic suggestion,
        string action,
        double confidenceBefore,
        double confidenceAfter,
        string? rejectionReason,
        CancellationToken cancellationToken)
    {
        var sql = @"
            INSERT INTO pattern_feedback_log (
                suggestion_id, pattern_id, cluster_id, action, user_id,
                confidence_before, confidence_after, rejection_reason
            ) VALUES (
                @SuggestionId, @PatternId, @ClusterId, @Action, @UserId,
                @ConfidenceBefore, @ConfidenceAfter, @RejectionReason
            )
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SuggestionId", suggestion.Id);
        cmd.Parameters.AddWithValue("@PatternId", suggestion.PatternId);
        cmd.Parameters.AddWithValue("@ClusterId", suggestion.ClusterId);
        cmd.Parameters.AddWithValue("@Action", action);
        cmd.Parameters.AddWithValue("@UserId", suggestion.ReviewedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ConfidenceBefore", confidenceBefore);
        cmd.Parameters.AddWithValue("@ConfidenceAfter", confidenceAfter);
        cmd.Parameters.AddWithValue("@RejectionReason", rejectionReason ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> ApplyConfidenceDecayAsync(CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        // Apply decay to patterns that haven't been updated in 24+ hours
        // Decay formula: confidence = MAX(min_confidence, confidence * (1 - daily_decay * days_since_update))
        var sql = @"
            UPDATE patterns
            SET confidence = GREATEST(@MinConfidence, 
                confidence * (1 - @DailyDecay * EXTRACT(EPOCH FROM (NOW() - COALESCE(updated_at, created_at))) / 86400))
            WHERE is_active = true
              AND (updated_at IS NULL OR updated_at < NOW() - INTERVAL '24 hours')
              AND confidence > @MinConfidence
            RETURNING id
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MinConfidence", MinConfidence);
        cmd.Parameters.AddWithValue("@DailyDecay", DailyDecay);

        var count = 0;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            count++;

        return count;
    }

    private async Task<int> CleanupExpiredSuggestionsAsync(CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            UPDATE pattern_suggestions
            SET status = 'expired'
            WHERE status = 'pending'
              AND expires_at < NOW()
            RETURNING id
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        var count = 0;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            count++;

        return count;
    }
}

internal sealed record ApprovedSuggestion
{
    public Guid Id { get; init; }
    public Guid ClusterId { get; init; }
    public Guid PatternId { get; init; }
    public double Confidence { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime ReviewedAt { get; init; }
}

internal sealed record RejectedSuggestion
{
    public Guid Id { get; init; }
    public Guid ClusterId { get; init; }
    public Guid PatternId { get; init; }
    public double Confidence { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime ReviewedAt { get; init; }
    public string? RejectionReason { get; init; }
}
