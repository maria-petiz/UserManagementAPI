using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using UserManagementAPI.Models;
using UserManagementAPI.Services;

namespace UserManagementAPI.Tests;

// xUnit creates a new instance per test, so each test gets a fresh app and a fresh in-memory store.
public class UsersApiTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = TestApp.Create();
    private readonly HttpClient _client;

    public UsersApiTests() => _client = _factory.CreateAuthenticatedClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static object NewUser(string email = "carol.diaz@techhive.com", string firstName = "Carol", string lastName = "Diaz", string department = "Finance") =>
        new { firstName, lastName, email, department };

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // ---------- Validation ----------

    public static TheoryData<string, string> InvalidPayloads => new()
    {
        { """{"firstName":"","lastName":"Diaz","email":"c@techhive.com","department":"IT"}""", "FirstName" },
        { """{"firstName":"   ","lastName":"Diaz","email":"c@techhive.com","department":"IT"}""", "FirstName" },
        { """{"firstName":"<script>","lastName":"Diaz","email":"c@techhive.com","department":"IT"}""", "FirstName" },
        { """{"firstName":"Carol","lastName":"123","email":"c@techhive.com","department":"IT"}""", "LastName" },
        { """{"firstName":"Carol","lastName":"Diaz","email":"a@b","department":"IT"}""", "Email" },
        { """{"firstName":"Carol","lastName":"Diaz","email":"x y@z.com","department":"IT"}""", "Email" },
        { """{"firstName":"Carol","lastName":"Diaz","email":"no-at-sign.com","department":"IT"}""", "Email" },
        { """{"firstName":"Carol","lastName":"Diaz","email":"c@techhive.com","department":"!!!"}""", "Department" },
        { """{"firstName":"Carol","lastName":"Diaz","email":"c@techhive.com"}""", "Department" },
        { """{"firstName":null,"lastName":"Diaz","email":"c@techhive.com","department":"IT"}""", "FirstName" },
        { $$"""{"firstName":"{{new string('a', 51)}}","lastName":"Diaz","email":"c@techhive.com","department":"IT"}""", "FirstName" },
    };

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task Post_InvalidPayload_Returns400WithFieldError(string json, string field)
    {
        var response = await _client.PostAsync("/api/users", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJson(response);
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"Expected a validation error for {field}.");
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task Put_InvalidPayload_Returns400(string json, string _)
    {
        var response = await _client.PutAsync("/api/users/1", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{\"firstName\":")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{\"firstName\":123,\"lastName\":\"Diaz\",\"email\":\"c@techhive.com\",\"department\":\"IT\"}")]
    public async Task Post_MalformedBody_Returns400(string json)
    {
        var response = await _client.PostAsync("/api/users", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("O'Brien", "Mary-Jane", "Research & Development")]
    [InlineData("José", "Müller", "IT/Ops")]
    [InlineData("J. R.", "Smith", "HR")]
    public async Task Post_RealWorldNames_AreAccepted(string firstName, string lastName, string department)
    {
        var response = await _client.PostAsJsonAsync("/api/users", NewUser(firstName: firstName, lastName: lastName, department: department));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_TrimsWhitespaceAroundValues()
    {
        var response = await _client.PostAsJsonAsync("/api/users", NewUser(email: "  carol@techhive.com  ", lastName: "Diaz  "));
        var user = await response.Content.ReadFromJsonAsync<User>();

        Assert.Equal("carol@techhive.com", user!.Email);
        Assert.Equal("Diaz", user.LastName);
    }

    // ---------- Non-existent IDs ----------

    [Theory]
    [InlineData("GET", "/api/users/99")]
    [InlineData("GET", "/api/users/0")]
    [InlineData("GET", "/api/users/-1")]
    [InlineData("DELETE", "/api/users/99")]
    public async Task MissingUser_Returns404ProblemDetails(string method, string url)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("User not found.", (await ReadJson(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Put_MissingUser_Returns404()
    {
        var response = await _client.PutAsJsonAsync("/api/users/99", NewUser());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/users/abc")]
    [InlineData("/api/users/99999999999")]
    public async Task NonNumericOrOverflowingId_Returns404WithBody(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await ReadJson(response)).GetProperty("status").GetInt32());
    }

    // ---------- Email uniqueness ----------

    [Fact]
    public async Task Post_DuplicateEmail_IgnoringCase_Returns409()
    {
        var response = await _client.PostAsJsonAsync("/api/users", NewUser(email: "ALICE.johnson@techhive.com"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_KeepingOwnEmail_Succeeds_ButTakingAnothersEmail_Returns409()
    {
        var keep = await _client.PutAsJsonAsync("/api/users/1", NewUser(email: "alice.johnson@techhive.com", firstName: "Alicia"));
        var steal = await _client.PutAsJsonAsync("/api/users/1", NewUser(email: "bob.smith@techhive.com"));

        Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, steal.StatusCode);
    }

    [Fact]
    public async Task EmailBecomesAvailable_AfterUserChangesItOrIsDeleted()
    {
        await _client.PutAsJsonAsync("/api/users/1", NewUser(email: "alice.new@techhive.com"));
        var reuseOld = await _client.PostAsJsonAsync("/api/users", NewUser(email: "alice.johnson@techhive.com"));

        await _client.DeleteAsync("/api/users/2");
        var reuseDeleted = await _client.PostAsJsonAsync("/api/users", NewUser(email: "bob.smith@techhive.com"));

        Assert.Equal(HttpStatusCode.Created, reuseOld.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reuseDeleted.StatusCode);
    }

    [Fact]
    public async Task ConcurrentPosts_WithSameEmail_CreateExactlyOneUser()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => _client.PostAsJsonAsync("/api/users", NewUser(email: "race@techhive.com"))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(49, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public void Service_ConcurrentCreates_ProduceUniqueIds()
    {
        var service = new InMemoryUserService();
        var ids = new System.Collections.Concurrent.ConcurrentBag<int>();

        Parallel.For(0, 1000, i => ids.Add(service.Create(new UserRequest
        {
            FirstName = "User", LastName = "Load", Email = $"user{i}@techhive.com", Department = "IT"
        }).User!.Id));

        Assert.Equal(1000, ids.Distinct().Count());
        Assert.Equal(1002, service.GetPage(1, int.MaxValue).TotalCount);
    }

    // ---------- Paging ----------

    [Fact]
    public async Task GetUsers_Paged_ReturnsRequestedSliceAndTotalHeader()
    {
        var response = await _client.GetAsync("/api/users?page=2&pageSize=1");
        var users = await response.Content.ReadFromJsonAsync<List<User>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", response.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal(2, Assert.Single(users!).Id);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=abc")]
    public async Task GetUsers_InvalidPaging_Returns400(string query)
    {
        var response = await _client.GetAsync($"/api/users?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_PageBeyondEnd_ReturnsEmptyListWithoutOverflow()
    {
        var response = await _client.GetAsync($"/api/users?page={int.MaxValue}&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<User>>())!);
    }

    // ---------- Unhandled exceptions ----------

    [Fact]
    public async Task UnhandledException_Returns500ProblemDetails_WithoutLeakingInternals()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddSingleton<IUserService, ThrowingUserService>()));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/users/1");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("traceId", raw);
        Assert.DoesNotContain("Simulated database failure", raw);

        // The app keeps serving requests after the failure.
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync("/api/users")).StatusCode);
    }

    private sealed class ThrowingUserService : IUserService
    {
        private static Exception Fail() => new InvalidOperationException("Simulated database failure");
        public (IReadOnlyList<User> Items, int TotalCount) GetPage(int page, int pageSize) => throw Fail();
        public User? GetById(int id) => throw Fail();
        public UserOperationResult Create(UserRequest request) => throw Fail();
        public UserOperationResult Update(int id, UserRequest request) => throw Fail();
        public bool Delete(int id) => throw Fail();
    }
}
