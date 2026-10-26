using System.Net;
using ResortWise.LoanBalance.UnitTests.Infrastructure;
using static ResortWise.LoanBalance.UnitTests.Loans.AccessControlTests;

namespace ResortWise.LoanBalance.UnitTests.Loans;

/// <summary>
/// With several access problems at once, the first failure in the order credential (401) →
/// tenant header (400) → tenant authorization (403) wins (spec Edge Cases). The loan ID is valid
/// throughout; loan checks are in <c>LoanCheckOrderTests</c>.
/// </summary>
public sealed class AccessCheckOrderTests
{
    public static TheoryData<Key, string?, HttpStatusCode, string> Combinations => new()
    {
        { Key.None, null, HttpStatusCode.Unauthorized, UnauthenticatedBody },
        { Key.None, "tenant-unknown", HttpStatusCode.Unauthorized, UnauthenticatedBody },
        { Key.Invalid, null, HttpStatusCode.Unauthorized, UnauthenticatedBody },
        { Key.Invalid, TestApp.TenantB, HttpStatusCode.Unauthorized, UnauthenticatedBody },
        { Key.TenantAOnly, null, HttpStatusCode.BadRequest, TenantRequiredBody },
        { Key.TenantAOnly, " ", HttpStatusCode.BadRequest, TenantRequiredBody },
        { Key.TenantAOnly, TestApp.TenantB, HttpStatusCode.Forbidden, TenantForbiddenBody },
    };

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task First_failure_in_the_check_order_wins(Key key, string? tenantId, HttpStatusCode status, string body)
    {
        var repository = new FakeLoanBalanceRepository().Add(100245, "Active", 1m);
        using var app = CreateApp(repository);

        var (actualStatus, actualBody) = await Get(app, key, tenantId);

        Assert.Equal(status, actualStatus);
        Assert.Equal(body, actualBody);
        Assert.Equal(0, repository.Calls);
    }
}
