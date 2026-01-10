using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Naia.PatternEngine.Events;
using Naia.PatternEngine.Services;
using Npgsql;

namespace Naia.PatternEngine.Workers;

/// <summary>
/// Consumes PatternFeedback events (approvals, rejections, modifications) and 
/// updates pattern confidence. Creates new patterns from approved suggestions.
/// 
/// This is the learning component of the flywheel - user feedback improves
/// pattern matching accuracy over time.
/// </summary>
public sealed class PatternLearnerWorker : BaseKafkaConsumer<PatternFeedback>
{
    private readonly IPatternEventPublisher _eventPublisher;
    private readonly PatternLearningOptions _options;
    private readonly PatternKafkaOptions _kafkaOptions;
    private readonly string _postgresConnectionString;

    public PatternLearnerWorker(
        ILogger<PatternLearnerWorker> logger,
        IOptions<PatternFlywheelOptions> options,
        IPatternEventPublisher eventPublisher,
        string postgresConnectionString)
        : base(
            logger,
            options.Value.Kafka.BootstrapServers,
            options.Value.Kafka.PatternLearnerGroupId,
            options.Value.Kafka.PatternsFeedbackTopic)
    {
        _eventPublisher = eventPublisher;
        _options = options.Value.PatternLearning;
        _kafkaOptions = options.Value.Kafka;
        _postgresConnectionString = postgresConnectionString;
    }

    protected override async Task ProcessMessageAsync(
        PatternFeedback message,
        string key,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "Processing feedback for suggestion {SuggestionId}: {Action}",
            message.SuggestionId, message.Action);

        switch (message.Action)
        {
            case FeedbackAction.Approved:
                await HandleApprovalAsync(message, cancellationToken);
                break;
            case FeedbackAction.Rejected:
                await HandleRejectionAsync(message, cancellationToken);
                break;
            case FeedbackAction.Deferred:
                // No action needed for deferred
                Logger.LogInformation("Suggestion {SuggestionId} deferred", message.SuggestionId);
                break;
            default:
                Logger.LogWarning("Unknown feedback action: {Action}", message.Action);
                break;
        }
    }

    private async Task HandleApprovalAsync(PatternFeedback feedback, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            // Load the suggestion
            var suggestion = await LoadSuggestionAsync(conn, feedback.SuggestionId, cancellationToken);
            if (suggestion == null)
            {
                Logger.LogWarning("Suggestion {SuggestionId} not found", feedback.SuggestionId);
                return;
            }

            // Update pattern confidence (increase)
            var newConfidence = await UpdatePatternConfidenceAsync(
                conn, 
                suggestion.PatternId, 
                _options.ConfidenceIncreasePerApproval, 
                cancellationToken);

            // Record the approval in audit log
            await RecordFeedbackAsync(conn, feedback, suggestion, cancellationToken);

            // Mark suggestion as applied
            await MarkSuggestionAppliedAsync(conn, feedback.SuggestionId, cancellationToken);

            // Bind points according to the suggestion
            await BindPointsAsync(conn, suggestion, feedback.UserId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            // Publish pattern updated event
            var approvalCount = await GetApprovalCountAsync(conn, suggestion.PatternId, cancellationToken);
            var evt = new PatternUpdated
            {
                PatternId = suggestion.PatternId,
                PatternName = suggestion.PatternName,
                UpdateType = PatternUpdateType.ConfidenceIncreased,
                OldConfidence = feedback.ConfidenceAtAction,
                NewConfidence = newConfidence,
                ExampleCount = approvalCount,
                SignatureCount = 0, // TODO: Calculate actual signature count
                CreatedAt = DateTime.UtcNow
            };

            await _eventPublisher.PublishAsync(
                _kafkaOptions.PatternsUpdatedTopic,
                suggestion.PatternId.ToString(),
                evt,
                cancellationToken);

            Logger.LogInformation(
                "Pattern {PatternName} confidence increased to {Confidence:P1}",
                suggestion.PatternName, newConfidence);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            Logger.LogError(ex, "Failed to process approval for {SuggestionId}", feedback.SuggestionId);
            throw;
        }
    }

    private async Task HandleRejectionAsync(PatternFeedback feedback, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            var suggestion = await LoadSuggestionAsync(conn, feedback.SuggestionId, cancellationToken);
            if (suggestion == null)
            {
                Logger.LogWarning("Suggestion {SuggestionId} not found", feedback.SuggestionId);
                return;
            }

            // Update pattern confidence (decrease)
            var newConfidence = await UpdatePatternConfidenceAsync(
                conn,
                suggestion.PatternId,
                -_options.ConfidenceDecreasePerRejection,
                cancellationToken);

            // Record the rejection
            await RecordFeedbackAsync(conn, feedback, suggestion, cancellationToken);

            // Mark suggestion as rejected
            await MarkSuggestionRejectedAsync(conn, feedback.SuggestionId, feedback.RejectionReason, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            // Publish pattern updated event
            var rejectionCount = await GetRejectionCountAsync(conn, suggestion.PatternId, cancellationToken);
            var evt = new PatternUpdated
            {
                PatternId = suggestion.PatternId,
                PatternName = suggestion.PatternName,
                UpdateType = PatternUpdateType.ConfidenceDecreased,
                OldConfidence = feedback.ConfidenceAtAction,
                NewConfidence = newConfidence,
                ExampleCount = rejectionCount,
                SignatureCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            await _eventPublisher.PublishAsync(
                _kafkaOptions.PatternsUpdatedTopic,
                suggestion.PatternId.ToString(),
                evt,
                cancellationToken);

            Logger.LogInformation(
                "Pattern {PatternName} confidence decreased to {Confidence:P1}",
                suggestion.PatternName, newConfidence);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            Logger.LogError(ex, "Failed to process rejection for {SuggestionId}", feedback.SuggestionId);
            throw;
        }
    }

    private async Task<SuggestionInfo?> LoadSuggestionAsync(
        NpgsqlConnection conn, Guid suggestionId, CancellationToken cancellationToken)
    {
        var sql = @"
            SELECT s.id, s.cluster_id, s.pattern_id, p.name as pattern_name,
                   s.overall_confidence, s.matched_point_ids, s.role_assignments
            FROM pattern_suggestions s
            JOIN patterns p ON p.id = s.pattern_id
            WHERE s.id = $1
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(suggestionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new SuggestionInfo
            {
                SuggestionId = reader.GetGuid(0),
                ClusterId = reader.GetGuid(1),
                PatternId = reader.GetGuid(2),
                PatternName = reader.GetString(3),
                OverallConfidence = reader.GetDouble(4),
                MatchedPointIds = reader.IsDBNull(5) ? new List<Guid>() : 
                    ParseGuidArray(reader.GetString(5)),
                RoleAssignments = reader.IsDBNull(6) ? new Dictionary<string, string>() :
                    System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(6)) 
                    ?? new Dictionary<string, string>()
            };
        }

        return null;
    }

    private List<Guid> ParseGuidArray(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>();
        }
        catch
        {
            return new List<Guid>();
        }
    }

    private async Task<double> UpdatePatternConfidenceAsync(
        NpgsqlConnection conn, Guid patternId, double delta, CancellationToken cancellationToken)
    {
        var sql = @"
            UPDATE patterns
            SET confidence = GREATEST($1, LEAST(1.0, confidence + $2)),
                updated_at = NOW()
            WHERE id = $3
            RETURNING confidence
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(_options.MinConfidenceFloor);
        cmd.Parameters.AddWithValue(delta);
        cmd.Parameters.AddWithValue(patternId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null ? (double)result : _options.InitialPatternConfidence;
    }

    private async Task RecordFeedbackAsync(
        NpgsqlConnection conn, PatternFeedback feedback, SuggestionInfo suggestion, CancellationToken cancellationToken)
    {
        var sql = @"
            INSERT INTO pattern_feedback_log 
                (suggestion_id, pattern_id, action, user_id, rejection_reason, created_at)
            VALUES ($1, $2, $3, $4, $5, NOW())
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(feedback.SuggestionId);
        cmd.Parameters.AddWithValue(suggestion.PatternId);
        cmd.Parameters.AddWithValue(feedback.Action.ToString());
        cmd.Parameters.AddWithValue(feedback.UserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(feedback.RejectionReason ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkSuggestionAppliedAsync(
        NpgsqlConnection conn, Guid suggestionId, CancellationToken cancellationToken)
    {
        var sql = @"UPDATE pattern_suggestions SET status = 'applied', applied_at = NOW() WHERE id = $1";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(suggestionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkSuggestionRejectedAsync(
        NpgsqlConnection conn, Guid suggestionId, string? reason, CancellationToken cancellationToken)
    {
        var sql = @"UPDATE pattern_suggestions SET status = 'rejected', rejection_reason = $2 WHERE id = $1";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(suggestionId);
        cmd.Parameters.AddWithValue(reason ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task BindPointsAsync(
        NpgsqlConnection conn, SuggestionInfo suggestion, string? userId, CancellationToken cancellationToken)
    {
        foreach (var (pointIdStr, roleIdStr) in suggestion.RoleAssignments)
        {
            if (!Guid.TryParse(pointIdStr, out var pointId) || !Guid.TryParse(roleIdStr, out var roleId))
                continue;

            var sql = @"
                INSERT INTO point_pattern_bindings (point_id, pattern_id, role_id, created_by, created_at)
                VALUES ($1, $2, $3, $4, NOW())
                ON CONFLICT (point_id, pattern_id) 
                DO UPDATE SET role_id = $3, updated_at = NOW()
            ";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue(pointId);
            cmd.Parameters.AddWithValue(suggestion.PatternId);
            cmd.Parameters.AddWithValue(roleId);
            cmd.Parameters.AddWithValue(userId ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<int> GetApprovalCountAsync(
        NpgsqlConnection conn, Guid patternId, CancellationToken cancellationToken)
    {
        var sql = @"SELECT COUNT(*) FROM pattern_feedback_log WHERE pattern_id = $1 AND action = 'Approved'";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(patternId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null ? Convert.ToInt32(result) : 0;
    }

    private async Task<int> GetRejectionCountAsync(
        NpgsqlConnection conn, Guid patternId, CancellationToken cancellationToken)
    {
        var sql = @"SELECT COUNT(*) FROM pattern_feedback_log WHERE pattern_id = $1 AND action = 'Rejected'";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(patternId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null ? Convert.ToInt32(result) : 0;
    }
}

internal sealed class SuggestionInfo
{
    public Guid SuggestionId { get; set; }
    public Guid ClusterId { get; set; }
    public Guid PatternId { get; set; }
    public string PatternName { get; set; } = string.Empty;
    public double OverallConfidence { get; set; }
    public List<Guid> MatchedPointIds { get; set; } = new();
    public Dictionary<string, string> RoleAssignments { get; set; } = new();
}
