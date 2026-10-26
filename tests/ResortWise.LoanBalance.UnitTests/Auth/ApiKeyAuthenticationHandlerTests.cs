using System.Net;
using System.Text.Json;
using ResortWise.LoanBalance.Api.Errors;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Auth;

public sealed class ApiKeyAuthenticationHandlerTests : IDisposable
{
    private const string Path = "/v1/loans/1/balance-summary";

    private readonly TestApp app = new();

    public void Dispose() => app.Dispose();

    public static TheoryData<string?> BadAuthorizationHeaders => new()
    {
        null,
        "Bearer",
        "Bearer ",
        "bearer {key}",
        "Basic {key}",
        "Bearer not-a-known-key",
        "Bearer {key}x",
    };

    [Theory]
    [MemberData(nameof(BadAuthorizationHeaders))]
    public async Task Missing_or_invalid_credential_returns_401(string? header)
    {
        var client = app.CreateClient(null, TestApp.TenantA);
        if (header is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header.Replace("{key}", app.BothTenantsKey.Key));
        }

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.Unauthenticated, body.RootElement.GetProperty("code").GetString());
        Assert.Null(Assert.Single(app.Logs.AuditEntries).State["callerId"]);
    }

    [Fact]
    public async Task Valid_key_sets_the_caller_id()
    {
        // No tenant header, so the request stops at the next check (400) after authentication.
        var response = await app.CreateClient(app.TenantAOnlyKey, null).GetAsync(Path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("caller-a-only", Assert.Single(app.Logs.AuditEntries).State["callerId"]);
    }
}
