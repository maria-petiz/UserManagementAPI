namespace UserManagementAPI.Middleware;

public class TokenAuthenticationOptions
{
    public const string SectionName = "Authentication";
    public const int MinimumTokenLength = 32;

    // Client name -> bearer token. The client name is recorded in the audit log; the token never is.
    public Dictionary<string, string> ApiTokens { get; set; } = new();
}
