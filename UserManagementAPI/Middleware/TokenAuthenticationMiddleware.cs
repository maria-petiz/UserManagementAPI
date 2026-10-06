using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace UserManagementAPI.Middleware;

// Requires "Authorization: Bearer <token>" on every /api request; anything else gets 401.
public partial class TokenAuthenticationMiddleware
{
    private const string BearerScheme = "Bearer";

    private readonly RequestDelegate _next;
    private readonly ILogger<TokenAuthenticationMiddleware> _logger;
    private readonly (byte[] TokenHash, string Client)[] _clients;

    public TokenAuthenticationMiddleware(RequestDelegate next, IOptions<TokenAuthenticationOptions> options, ILogger<TokenAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        // Only hashes are kept in memory; comparing fixed-length hashes also hides the token length.
        _clients = options.Value.ApiTokens.Select(kv => (Hash(kv.Value), kv.Key)).ToArray();

        if (_clients.Length == 0)
            LogNoTokensConfigured(_logger);
    }

    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problemDetailsService)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            await RejectAsync(context, problemDetailsService, "Missing bearer token.", "Bearer");
            return;
        }

        // "<scheme> <token>"; a bare "Bearer" (no token) is an invalid token, not a wrong scheme.
        var separator = header.IndexOf(' ');
        var scheme = separator < 0 ? header : header[..separator];
        if (!scheme.Equals(BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(context, problemDetailsService, "Authorization header must use the Bearer scheme.", "Bearer");
            return;
        }

        var client = FindClient(separator < 0 ? string.Empty : header[(separator + 1)..].Trim());
        if (client is null)
        {
            await RejectAsync(context, problemDetailsService, "Invalid bearer token.", "Bearer error=\"invalid_token\"");
            return;
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, client)], authenticationType: "Bearer"));
        await _next(context);
    }

    private string? FindClient(string token)
    {
        if (token.Length == 0)
            return null;

        var hash = Hash(token);
        string? match = null;
        // Check every entry (no early exit) with a constant-time comparison so timing reveals nothing.
        foreach (var (tokenHash, client) in _clients)
        {
            if (CryptographicOperations.FixedTimeEquals(hash, tokenHash))
                match = client;
        }
        return match;
    }

    private async Task RejectAsync(HttpContext context, IProblemDetailsService problemDetailsService, string reason, string challenge)
    {
        // Rejected requests never reach the request-logging middleware, so they are audited here.
        LogRejected(_logger, context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress?.ToString(), reason);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = challenge;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized.",
                Detail = reason
            }
        });
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected {Method} {Path} from {RemoteIp} with 401: {Reason}")]
    private static partial void LogRejected(ILogger logger, string method, string path, string? remoteIp, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No API tokens are configured under 'Authentication:ApiTokens'; every /api request will be rejected.")]
    private static partial void LogNoTokensConfigured(ILogger logger);
}
