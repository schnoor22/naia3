using System.Security.Claims;

namespace Naia.Api.Middleware;

/// <summary>
/// Master access middleware - provides system-wide override authentication.
/// All requests with valid master token bypass normal authentication.
/// </summary>
public class MasterAccessMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<MasterAccessMiddleware> _logger;
    private readonly string _masterToken;

    public MasterAccessMiddleware(RequestDelegate next, ILogger<MasterAccessMiddleware> logger, IConfiguration config)
    {
        _next = next;
        _logger = logger;
        
        // Read master token from environment variable or config
        // Set via: export NAIA_MASTER_TOKEN="your-secret-master-key"
        _masterToken = Environment.GetEnvironmentVariable("NAIA_MASTER_TOKEN") 
            ?? config["Security:MasterToken"] 
            ?? throw new InvalidOperationException("NAIA_MASTER_TOKEN environment variable not set");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check for master token in Authorization header or X-Master-Token header
        var masterToken = ExtractMasterToken(context);
        
        if (!string.IsNullOrEmpty(masterToken) && masterToken == _masterToken)
        {
            // Valid master token - set special principal
            var masterIdentity = new ClaimsIdentity(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "MasterUser"),
                new Claim("master_access", "true"),
                new Claim(ClaimTypes.Role, "Master")
            }, "MasterToken");
            
            context.User = new ClaimsPrincipal(masterIdentity);
            _logger.LogWarning("Master access granted from {RemoteIp}", context.Connection.RemoteIpAddress);
        }

        await _next(context);
    }

    private string? ExtractMasterToken(HttpContext context)
    {
        // Check X-Master-Token header first
        if (context.Request.Headers.TryGetValue("X-Master-Token", out var headerToken))
        {
            return headerToken.ToString();
        }

        // Check Authorization header (Bearer token)
        var authHeader = context.Request.Headers["Authorization"].ToString();
        if (authHeader.StartsWith("Bearer "))
        {
            return authHeader.Substring("Bearer ".Length);
        }

        return null;
    }
}

/// <summary>
/// Extension methods for master access middleware
/// </summary>
public static class MasterAccessMiddlewareExtensions
{
    public static IApplicationBuilder UseMasterAccess(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<MasterAccessMiddleware>();
    }
}
