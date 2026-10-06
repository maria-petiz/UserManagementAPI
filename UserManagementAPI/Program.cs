using UserManagementAPI.Middleware;
using UserManagementAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// One error shape for every endpoint and status (400/401/404/409/500): RFC 9457 problem details
// plus an "error" field, e.g. { "error": "Internal server error.", "status": 500, "traceId": "..." }.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions.TryAdd("error", ctx.ProblemDetails.Title));

builder.Services.AddOptions<TokenAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(TokenAuthenticationOptions.SectionName))
    .Validate(o => o.ApiTokens.Values.All(t => t.Length >= TokenAuthenticationOptions.MinimumTokenLength),
        $"Every API token must be at least {TokenAuthenticationOptions.MinimumTokenLength} characters.")
    .ValidateOnStart();

// In-memory store shared across requests (swap for a database-backed implementation later).
builder.Services.AddSingleton<IUserService, InMemoryUserService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
// 1. Error handling first: outermost, so it catches exceptions from every later component.
app.UseMiddleware<ErrorHandlingMiddleware>();
// Gives body-less error responses (e.g. 404 for /api/users/abc) the same problem-details body.
app.UseStatusCodePages();
app.UseHttpsRedirection();

// 2. Authentication next: rejects bad tokens before any logging, routing or model-binding work is spent on them.
app.UseMiddleware<TokenAuthenticationMiddleware>();

// 3. Logging last: runs only for authenticated requests, so it can record which client made each call.
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Lets testers trigger an unhandled exception on purpose (requires a valid token like any /api route).
    app.MapGet("/api/debug/throw", () => { throw new InvalidOperationException("Simulated failure for testing."); })
        .ExcludeFromDescription();
}

app.MapControllers();

app.Run();

// Exposes Program to the integration tests (WebApplicationFactory<Program>).
public partial class Program;
