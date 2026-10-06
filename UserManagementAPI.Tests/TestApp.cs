using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UserManagementAPI.Tests;

internal static class TestApp
{
    public const string ClientName = "test-client";
    public const string Token = "test-token-0123456789-abcdefghijklmnop";

    // A fresh app with one known API token, so tests never depend on appsettings.Development.json.
    public static WebApplicationFactory<Program> Create(Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"Authentication:ApiTokens:{ClientName}", Token);
            configure?.Invoke(builder);
        });

    public static HttpClient CreateAuthenticatedClient(this WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }
}
