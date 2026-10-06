using Microsoft.AspNetCore.Mvc;

namespace UserManagementAPI.Middleware;

// Last line of defence: turns any unhandled exception into a logged, consistent 500 response instead of a crash.
public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problemDetailsService)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected; nobody is left to receive a response.
            logger.LogInformation("Request {Method} {Path} was cancelled by the client.", context.Request.Method, context.Request.Path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                Exception = ex,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Internal server error.",
                    Detail = "The server could not complete the request. Quote the traceId when reporting this issue."
                }
            });
        }
    }
}
