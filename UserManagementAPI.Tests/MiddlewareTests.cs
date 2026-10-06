using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using UserManagementAPI.Middleware;

namespace UserManagementAPI.Tests;

public class MiddlewareTests : IDisposable
{
    private readonly CapturingLoggerProvider _logs = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public MiddlewareTests()
    {
        _factory = TestApp.Create(b => b.ConfigureLogging(logging => logging.AddProvider(_logs)));
        _client = _factory.CreateClient(); // no token by default
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static HttpRequestMessage Get(string url, string? authorization = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return request;
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // ---------- Authentication ----------

    [Theory]
    [InlineData(null, "Missing bearer token.")]
    [InlineData("", "Missing bearer token.")]
    [InlineData("Bearer wrong-token", "Invalid bearer token.")]
    [InlineData("Bearer ", "Invalid bearer token.")]
    [InlineData("Basic dXNlcjpwYXNz", "Authorization header must use the Bearer scheme.")]
    [InlineData(TestApp.Token, "Authorization header must use the Bearer scheme.")]
    [InlineData("Bearer " + TestApp.Token + "x", "Invalid bearer token.")]
    public async Task MissingOrInvalidToken_Returns401(string? authorization, string expectedDetail)
    {
        var response = await _client.SendAsync(Get("/api/users", authorization));
        var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Unauthorized.", body.GetProperty("error").GetString());
        Assert.Equal(expectedDetail, body.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("Bearer " + TestApp.Token)]
    [InlineData("bearer " + TestApp.Token)]          // scheme is case-insensitive (RFC 7235)
    [InlineData("Bearer   " + TestApp.Token + "  ")] // surrounding whitespace tolerated
    public async Task ValidToken_IsAccepted(string authorization)
    {
        var response = await _client.SendAsync(Get("/api/users", authorization));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task EveryApiMethod_RequiresToken(string method)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/users/1"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequest_NeverReachesTheController()
    {
        var response = await _client.DeleteAsync("/api/users/1");
        using var authed = _factory.CreateAuthenticatedClient();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await authed.GetAsync("/api/users/1")).StatusCode);
    }

    [Fact]
    public async Task NonApiPaths_DoNotRequireToken()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Startup_RejectsTokensShorterThanMinimum()
    {
        using var factory = TestApp.Create(b => b.UseSetting("Authentication:ApiTokens:weak-client", "short"));
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains($"at least {TokenAuthenticationOptions.MinimumTokenLength} characters", ex.ToString());
    }

    // ---------- Error handling ----------

    [Fact]
    public async Task UnhandledException_Returns500WithStandardJson()
    {
        using var authed = _factory.CreateAuthenticatedClient();
        var response = await authed.GetAsync("/api/debug/throw");
        var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Internal server error.", body.GetProperty("error").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("Simulated failure", body.GetRawText());
    }

    [Fact]
    public async Task ExceptionEndpoint_StillRequiresToken()
    {
        var response = await _client.GetAsync("/api/debug/throw");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EveryErrorStatus_HasTheSameShape()
    {
        using var authed = _factory.CreateAuthenticatedClient();
        var responses = new[]
        {
            await authed.PostAsJsonAsync("/api/users", new { firstName = "", lastName = "X", email = "bad", department = "IT" }), // 400
            await _client.GetAsync("/api/users"),                                                                               // 401
            await authed.GetAsync("/api/users/99"),                                                                             // 404
            await authed.GetAsync("/api/users/abc"),                                                                            // 404 (routing)
            await authed.PostAsJsonAsync("/api/users", new { firstName = "A", lastName = "B", email = "alice.johnson@techhive.com", department = "IT" }), // 409
            await authed.GetAsync("/api/debug/throw"),                                                                          // 500
        };

        Assert.Equal(new[] { 400, 401, 404, 404, 409, 500 }, responses.Select(r => (int)r.StatusCode));
        foreach (var response in responses)
        {
            var body = await ReadJson(response);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
            Assert.Equal((int)response.StatusCode, body.GetProperty("status").GetInt32());
            Assert.True(body.TryGetProperty("traceId", out _));
        }
    }

    // ---------- Logging ----------

    [Fact]
    public async Task SuccessfulRequest_IsLoggedWithMethodPathStatusAndClient()
    {
        using var authed = _factory.CreateAuthenticatedClient();
        await (await authed.GetAsync("/api/users/1")).Content.ReadAsStringAsync();

        var entry = Assert.Single(_logs.For<RequestLoggingMiddleware>());
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Matches(@"^HTTP GET /api/users/1 responded 200 in \d+\.\d ms \(client: test-client\)$", entry.Message);
    }

    [Fact]
    public async Task ClientErrors_AreLoggedAsWarnings()
    {
        using var authed = _factory.CreateAuthenticatedClient();
        await (await authed.DeleteAsync("/api/users/99")).Content.ReadAsStringAsync();

        var entry = Assert.Single(_logs.For<RequestLoggingMiddleware>());
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("HTTP DELETE /api/users/99 responded 404", entry.Message);
    }

    [Fact]
    public async Task Exception_IsLoggedAs500_WithFullDetailsInTheErrorLog()
    {
        using var authed = _factory.CreateAuthenticatedClient();
        await (await authed.GetAsync("/api/debug/throw")).Content.ReadAsStringAsync();

        var request = Assert.Single(_logs.For<RequestLoggingMiddleware>());
        Assert.Equal(LogLevel.Error, request.Level);
        Assert.StartsWith("HTTP GET /api/debug/throw responded 500", request.Message);

        // The stack trace goes to the server log, not to the client.
        var error = Assert.Single(_logs.For<ErrorHandlingMiddleware>());
        Assert.Contains("Simulated failure", error.Exception?.Message);
    }

    [Fact]
    public async Task RejectedRequest_IsAudited_WithoutLoggingTheToken()
    {
        await (await _client.SendAsync(Get("/api/users", "Bearer leaked-secret-value"))).Content.ReadAsStringAsync();

        var entry = Assert.Single(_logs.For<TokenAuthenticationMiddleware>());
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Rejected GET /api/users", entry.Message);
        Assert.Contains("Invalid bearer token.", entry.Message);
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("leaked-secret-value"));
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains(TestApp.Token));
    }

    // ---------- Test logger ----------

    public sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public IEnumerable<LogEntry> For<T>() => Entries.Where(e => e.Category == typeof(T).FullName);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose() { }

        private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }
}
