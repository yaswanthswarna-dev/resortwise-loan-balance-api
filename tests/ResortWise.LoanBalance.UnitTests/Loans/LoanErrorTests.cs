using System.Net;
using ResortWise.LoanBalance.UnitTests.Infrastructure;
using static ResortWise.LoanBalance.UnitTests.Loans.AccessControlTests;

namespace ResortWise.LoanBalance.UnitTests.Loans;

/// <summary>Loan input errors have stable codes and constant messages (FR-014–FR-017).</summary>
public sealed class LoanErrorTests
{
    public const string LoanIdInvalidBody = """{"code":"LOAN_ID_INVALID","message":"The loan ID is not valid.","field":"loanId"}""";
    public const string LoanNotFoundBody = """{"code":"LOAN_NOT_FOUND","message":"The loan was not found."}""";
    private const string InternalErrorBody = """{"code":"INTERNAL_ERROR","message":"An unexpected error occurred."}""";

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("007")]
    [InlineData("-5")]
    [InlineData("2147483648")]
    public async Task Invalid_loan_id_returns_400_and_reads_nothing(string loanId)
    {
        var repository = new FakeLoanBalanceRepository();
        using var app = CreateApp(repository);

        var (status, body) = await Get(app, Key.Both, TestApp.TenantA, Uri.EscapeDataString(loanId));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(LoanIdInvalidBody, body);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Unknown_loan_id_returns_404_without_field()
    {
        using var app = CreateApp(new FakeLoanBalanceRepository());

        var (status, body) = await Get(app, Key.Both, TestApp.TenantA, "424242");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(LoanNotFoundBody, body);
    }

    [Fact]
    public async Task Repository_failure_returns_500_with_no_exception_text()
    {
        var repository = new FakeLoanBalanceRepository
        {
            Throw = new InvalidOperationException("SELECT * FROM rw.Loan; Data Source=unit-test.invalid; at Some.Stack.Frame()"),
        };
        using var app = CreateApp(repository);

        var (status, body) = await Get(app, Key.Both, TestApp.TenantA, "1");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal(InternalErrorBody, body);
    }
}
