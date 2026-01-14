using Microsoft.AspNetCore.Mvc;
using Naia.Api.Services;
using System.Text.Json;

namespace Naia.Api.Controllers;

/// <summary>
/// Debug Console API - Provides Claude AI-powered debugging assistance.
/// Only accessible with Master token authentication.
/// </summary>
[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly ILogger<DebugController> _logger;
    private readonly ClaudeDebugService _claudeService;

    public DebugController(
        ILogger<DebugController> logger,
        ClaudeDebugService claudeService)
    {
        _logger = logger;
        _claudeService = claudeService;
    }

    /// <summary>
    /// Check if current request has master access
    /// </summary>
    [HttpGet("access")]
    public IActionResult CheckAccess()
    {
        var hasMasterAccess = User.HasClaim("master_access", "true");
        return Ok(new { 
            hasMasterAccess,
            user = User.Identity?.Name,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Stream a chat response from Claude with NAIA debugging tools.
    /// Requires Master token authentication.
    /// </summary>
    [HttpPost("chat")]
    public async Task Chat([FromBody] DebugChatRequest request, CancellationToken ct)
    {
        // Require master access
        if (!User.HasClaim("master_access", "true"))
        {
            Response.StatusCode = 403;
            await Response.WriteAsync("Master access required");
            return;
        }

        _logger.LogInformation("Debug chat request from {User}: {Message}", 
            User.Identity?.Name ?? "Unknown",
            request.Message?.Substring(0, Math.Min(100, request.Message?.Length ?? 0)));

        // Set up SSE response
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            await foreach (var chunk in _claudeService.ChatStreamAsync(
                request.Message ?? "", 
                request.History?.Select(h => new ChatMessage 
                { 
                    Role = h.Role, 
                    Content = h.Content 
                }).ToList(),
                ct))
            {
                if (ct.IsCancellationRequested) break;

                // Send as SSE event
                var data = JsonSerializer.Serialize(new { text = chunk });
                await Response.WriteAsync($"data: {data}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            // Send done event
            await Response.WriteAsync("data: [DONE]\n\n", ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Debug chat cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in debug chat");
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { error = ex.Message })}\n\n", ct);
        }
    }

    /// <summary>
    /// Get system context for the debug console (files, status, etc.)
    /// </summary>
    [HttpGet("context")]
    public async Task<IActionResult> GetContext()
    {
        if (!User.HasClaim("master_access", "true"))
            return Forbid();

        // Return helpful context about the system
        return Ok(new
        {
            projectName = "NAIA Industrial Historian",
            version = "3.0.0",
            components = new[]
            {
                new { name = "Naia.Api", description = "REST API & SignalR hub", port = 5000 },
                new { name = "Naia.Web", description = "SvelteKit UI", port = 5173 },
                new { name = "PostgreSQL", description = "Metadata database", port = 5432 },
                new { name = "QuestDB", description = "Time-series database", port = 9000 },
                new { name = "Redis", description = "Current value cache", port = 6379 },
                new { name = "Kafka", description = "Message queue", port = 9092 }
            },
            suggestedQueries = new[]
            {
                "What's the current system status?",
                "Show me recent ingestion logs",
                "How many points were ingested in the last hour?",
                "Explain how the pattern engine works",
                "What connectors are configured?",
                "Show me the data flow from CSV to QuestDB"
            },
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Quick status check without Claude
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            enabled = true,
            claudeModel = "claude-sonnet-4-20250514",
            hasApiKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")),
            timestamp = DateTime.UtcNow
        });
    }
}

public class DebugChatRequest
{
    public string? Message { get; set; }
    public List<DebugChatMessage>? History { get; set; }
}

public class DebugChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}
