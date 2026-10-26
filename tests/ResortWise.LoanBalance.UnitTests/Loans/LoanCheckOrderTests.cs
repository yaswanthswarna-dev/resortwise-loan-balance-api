using System.Net;
using ResortWise.LoanBalance.UnitTests.Infrastructure;
using static ResortWise.LoanBalance.UnitTests.Loans.AccessControlTests;
using static ResortWise.LoanBalance.UnitTests.Loans.LoanErrorTests;

namespace ResortWise.LoanBalance.UnitTests.Loans;

/// <summary>
/// Tenant authorization (403) comes before the loan ID format (400), which comes before the loan
/// lookup (404) (spec Edge Cases).
/// </summary>
public sealed class LoanCheckOrderTests
{
    [Fact]
    public async Task Forbidden_tenant_with_invalid_loan_id_returns_403()
    {
        var repository = new FakeLoanBalanceRepository();
        using var app = CreateApp(repository);

        var (status, body) = await Get(app, Key.TenantAOnly, TestApp.TenantB, "abc");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(TenantForbiddenBody, body);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Allowed_tenant_with_invalid_loan_id_returns_400_before_any_lookup()
    {
        var repository = new FakeLoanBalanceRepository();
        using var app = CreateApp(repository);

        var (status, body) = await Get(app, Key.TenantAOnly, TestApp.TenantA, "007");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(LoanIdInvalidBody, body);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Allowed_tenant_with_valid_unknown_loan_id_returns_404()
    {
        var repository = new FakeLoanBalanceRepository();
        using var app = CreateApp(repository);

        var (status, body) = await Get(app, Key.TenantAOnly, TestApp.TenantA, "7");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(LoanNotFoundBody, body);
        Assert.Equal(1, repository.Calls);
    }
}
