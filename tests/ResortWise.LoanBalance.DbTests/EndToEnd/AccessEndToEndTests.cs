using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using ResortWise.LoanBalance.Api.Tenancy;
using ResortWise.LoanBalance.DbTests.Infrastructure;

namespace ResortWise.LoanBalance.DbTests.EndToEnd;

/// <summary>
/// The access refusals through the real pipeline and the real tenant databases, plus 503 for a
/// tenant whose database cannot be reached, with no fallback to another tenant (FR-009–FR-013).
/// </summary>
[Trait("Category", "Database")]
[Collection(DatabaseCollection.Name)]
public sealed class AccessEndToEndTests(DatabaseFixture fixture)
{
    private const string TenantDown = "tenant-down";

    // A synthetic, unreachable server: nothing listens on port 1 of the local machine.
    private const string UnreachableConnection = "Data Source=tcp:127.0.0.1,1;Initial Catalog=Unreachable;Connect Timeout=3;Encrypt=False";

    // Any well-formed ID: every refusal happens before a loan is read.
    private const string LoanPath = "/v1/loans/1/balance-summary";

    [DatabaseFact]
    public async Task Missing_credential_returns_401()
    {
        using var client = fixture.CreateClient(null, DatabaseFixture.TenantA);
        await AssertError(client, HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [DatabaseFact]
    public async Task Invalid_credential_returns_401()
    {
        using var client = fixture.CreateClient("not-a-valid-key", DatabaseFixture.TenantA);
        await AssertError(client, HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [DatabaseFact]
    public async Task Missing_tenant_header_returns_400()
    {
        using var client = fixture.CreateClient(fixture.BothTenantsKey, null);
        await AssertError(client, HttpStatusCode.BadRequest, "TENANT_REQUIRED");
    }

    [DatabaseFact]
    public async Task Tenant_not_allowed_returns_403()
    {
        using var client = fixture.CreateClient(fixture.TenantAOnlyKey, DatabaseFixture.TenantB);
        await AssertError(client, HttpStatusCode.Forbidden, "TENANT_FORBIDDEN");
    }

    [DatabaseFact]
    public async Task Unknown_tenant_returns_403()
    {
        using var client = fixture.CreateClient(fixture.BothTenantsKey, "tenant-unknown");
        await AssertError(client, HttpStatusCode.Forbidden, "TENANT_FORBIDDEN");
    }

    [DatabaseFact]
    public async Task Unreachable_tenant_returns_503_without_fallback()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var app = fixture.App.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Callers:2:Id"] = "db-test-down",
                ["Callers:2:KeyHash"] = DatabaseFixture.Hash(key),
                ["Callers:2:Tenants:0"] = TenantDown,
                [$"Tenants:{TenantDown}:ConnectionStringName"] = "DbTestDown",
                ["ConnectionStrings:DbTestDown"] = UnreachableConnection,
            })));

        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        client.DefaultRequestHeaders.TryAddWithoutValidation(TenantAccessMiddleware.TenantHeader, TenantDown);

        await AssertError(client, HttpStatusCode.ServiceUnavailable, "TENANT_UNAVAILABLE");
    }

    private static async Task AssertError(HttpClient client, HttpStatusCode status, string code)
    {
        using var response = await client.GetAsync(LoanPath);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(status, response.StatusCode);
        Assert.Contains($"\"code\":\"{code}\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("loanStatus", body, StringComparison.Ordinal);
        Assert.DoesNotContain("principalBalance", body, StringComparison.Ordinal);
    }
}
