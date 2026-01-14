using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Naia.Api.Services;

namespace Naia.Api.Controllers;

/// <summary>
/// Coral AI Assistant API - The nurturing guide to NAIA's data ocean
/// </summary>
[ApiController]
[Route("api/coral")]
public class CoralController : ControllerBase
{
    private readonly CoralAssistantService _coral;
    private readonly ILogger<CoralController> _logger;

    public CoralController(CoralAssistantService coral, ILogger<CoralController> logger)
    {
        _coral = coral;
        _logger = logger;
    }

    /// <summary>
    /// Chat with Coral - streaming SSE response
    /// </summary>
    [HttpPost("chat")]
    public async Task Chat([FromBody] CoralChatRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        _logger.LogInformation("Coral chat request: {Message}", request.Message?.Substring(0, Math.Min(100, request.Message?.Length ?? 0)));

        try
        {
            await foreach (var evt in _coral.ChatAsync(request.Message ?? "", request.Context, cancellationToken))
            {
                if (evt.Text != null)
                {
                    await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { text = evt.Text })}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
                if (evt.Tool != null)
                {
                    await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { tool = evt.Tool })}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }

            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Coral chat cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Coral chat");
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { text = "I encountered an issue. Please try again." })}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Quick query endpoint for non-streaming responses
    /// </summary>
    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] CoralChatRequest request, CancellationToken cancellationToken)
    {
        var response = new System.Text.StringBuilder();
        
        await foreach (var evt in _coral.ChatAsync(request.Message ?? "", request.Context, cancellationToken))
        {
            if (evt.Text != null)
                response.Append(evt.Text);
        }

        return Ok(new { response = response.ToString() });
    }
}

public class CoralChatRequest
{
    public string? Message { get; set; }
    public CoralContext? Context { get; set; }
}
