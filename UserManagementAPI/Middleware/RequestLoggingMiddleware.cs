using System.Diagnostics;

namespace UserManagementAPI.Middleware;

// Audit log: one line per request with method, path, status code, duration and the authenticated client.
// Bodies and headers are deliberately not logged, because they can contain personal data and tokens.
public partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private const int ClientClosedRequest = 499;

    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
            Log(context, context.Response.StatusCode, start);
        }
        catch
        {
            // ErrorHandlingMiddleware (further out) turns this exception into a 500; record what the client will get.
            Log(context, context.RequestAborted.IsCancellationRequested ? ClientClosedRequest : StatusCodes.Status500InternalServerError, start);
            throw;
        }
    }

    private void Log(HttpContext context, int statusCode, long start)
    {
        var level = statusCode switch
        {
            >= 500 => LogLevel.Error,
            >= 400 => LogLevel.Warning,
            _ => LogLevel.Information
        };

        LogRequest(logger, level, context.Request.Method, context.Request.Path, statusCode,
            Stopwatch.GetElapsedTime(start).TotalMilliseconds, context.User.Identity?.Name ?? "anonymous");
    }

    [LoggerMessage(Message = "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms (client: {Client})")]
    private static partial void LogRequest(ILogger logger, LogLevel level, string method, string path, int statusCode, double elapsedMs, string client);
}
