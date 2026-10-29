using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResortWise.LoanBalance.Api.Loans;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Loans;

/// <summary>
/// Every refused request gets the contract's status and exact body, and no loan data is read:
/// the repository and the connection factory are never called (FR-009–FR-012).
/// </summary>
public sealed class AccessControlTests
{
    public const string UnauthenticatedBody = """{"code":"UNAUTHENTICATED","message":"A valid caller credential is required."}""";
    public const string TenantRequiredBody = """{"code":"TENANT_REQUIRED","message":"The X-Tenant-Id header is required.","field":"X-Tenant-Id"}""";
    public const string TenantForbiddenBody = """{"code":"TENANT_FORBIDDEN","message":"The caller is not allowed to access this tenant."}""";

    private const int LoanId = 100245;

    public enum Key
    {
        None,
        Invalid,
        Both,
        TenantAOnly,
    }

    public static TheoryData<Key, string?, HttpStatusCode, string> Refusals => new()
    {
        { Key.None, TestApp.TenantA, HttpStatusCode.Unauthorized, UnauthenticatedBody },
        { Key.Invalid, TestApp.TenantA, HttpStatusCode.Unauthorized, UnauthenticatedBody },
        { Key.Both, null, HttpStatusCode.BadRequest, TenantRequiredBody },
        { Key.Both, "", HttpStatusCode.BadRequest, TenantRequiredBody },
        { Key.Both, "   ", HttpStatusCode.BadRequest, TenantRequiredBody },
        { Key.TenantAOnly, TestApp.TenantB, HttpStatusCode.Forbidden, TenantForbiddenBody },
        { Key.Both, "tenant-unknown", HttpStatusCode.Forbidden, TenantForbiddenBody },
        { Key.Both, "TENANT-A", HttpStatusCode.Forbidden, TenantForbiddenBody },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Refused_requests_get_the_contract_body_and_read_no_data(Key key, string? tenantId, HttpStatusCode status, string body)
    {
        var repository = new FakeLoanBalanceRepository().Add(LoanId, "Active", 1m);
        using var app = CreateApp(repository);

        var (actualStatus, actualBody) = await Get(app, key, tenantId);

        Assert.Equal(status, actualStatus);
        Assert.Equal(body, actualBody);
        Assert.Equal(0, repository.Calls);
        Assert.Equal(0, app.Connections.Calls);
    }

    [Fact]
    public async Task Unknown_tenant_body_is_byte_identical_to_a_forbidden_tenant()
    {
        using var app = CreateApp(new FakeLoanBalanceRepository());

        var (_, forbidden) = await Get(app, Key.TenantAOnly, TestApp.TenantB);
        var (_, unknown) = await Get(app, Key.TenantAOnly, "tenant-unknown");

        Assert.Equal(forbidden, unknown);
    }

    [Fact]
    public async Task Allowed_request_reads_the_loan()
    {
        var repository = new FakeLoanBalanceRepository().Add(LoanId, "Active", 1m);
        using var app = CreateApp(repository);

        var (status, _) = await Get(app, Key.TenantAOnly, TestApp.TenantA);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, repository.Calls);
    }

    internal static TestApp CreateApp(FakeLoanBalanceRepository repository) => new(services =>
    {
        services.RemoveAll<ILoanBalanceRepository>();
        services.AddSingleton<ILoanBalanceRepository>(repository);
    });

    internal static async Task<(HttpStatusCode Status, string Body)> Get(TestApp app, Key key, string? tenantId, string loanId = "100245")
    {
        var apiKey = key switch
        {
            Key.Invalid => TestKeys.Create(),
            Key.Both => app.BothTenantsKey,
            Key.TenantAOnly => app.TenantAOnlyKey,
            _ => null,
        };

        using var client = app.CreateClient(apiKey, tenantId);
        using var response = await client.GetAsync($"/v1/loans/{loanId}/balance-summary");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
