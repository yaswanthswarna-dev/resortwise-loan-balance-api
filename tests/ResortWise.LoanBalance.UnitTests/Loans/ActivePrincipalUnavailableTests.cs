using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResortWise.LoanBalance.Api.Loans;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Loans;

/// <summary>
/// Active records with the payoff flag set have no principal available (FR-008a, O5): the flag
/// gives <c>PAID_OFF_FLAG_SET</c>, after "not active". All rows are synthetic.
/// </summary>
public sealed class ActivePrincipalUnavailableTests
{
    private const int LoanId = 100246;

    [Fact]
    public async Task Active_loan_with_the_payoff_flag_returns_null_principal_not_available_and_the_reason()
    {
        var repository = new FakeLoanBalanceRepository().AddRow(
            LoanId, FakeLoanBalanceRepository.ActiveStatusId, "Active", null, payOffFlagSet: true);

        var (status, body) = await Get(repository);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Active", json.RootElement.GetProperty("loanStatus").GetString());
        Assert.Equal("null", json.RootElement.GetProperty("principalBalance").GetRawText());
        Assert.False(json.RootElement.GetProperty("principalBalanceAvailable").GetBoolean());
        Assert.Equal("PAID_OFF_FLAG_SET", json.RootElement.GetProperty("principalUnavailableReason").GetString());
        Assert.DoesNotContain("\"principalBalance\":0", body, StringComparison.Ordinal);
    }

    // Status comes first: a non-active record keeps LOAN_NOT_ACTIVE whatever the flag says.
    [Fact]
    public async Task Non_active_loan_with_the_payoff_flag_returns_loan_not_active()
    {
        var repository = new FakeLoanBalanceRepository().AddRow(
            LoanId, FakeLoanBalanceRepository.NonActiveStatusId, "Rewritten", null, payOffFlagSet: true);

        var (status, body) = await Get(repository);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("null", json.RootElement.GetProperty("principalBalance").GetRawText());
        Assert.False(json.RootElement.GetProperty("principalBalanceAvailable").GetBoolean());
        Assert.Equal("LOAN_NOT_ACTIVE", json.RootElement.GetProperty("principalUnavailableReason").GetString());
    }

    [Fact]
    public async Task Active_loan_with_the_payoff_flag_and_a_principal_is_an_internal_error()
    {
        var repository = new FakeLoanBalanceRepository().AddRow(
            LoanId, FakeLoanBalanceRepository.ActiveStatusId, "Active", 12500.00m, payOffFlagSet: true);

        var (status, body) = await Get(repository);

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal("""{"code":"INTERNAL_ERROR","message":"An unexpected error occurred."}""", body);
    }

    [Fact]
    public async Task Active_loan_without_the_payoff_flag_keeps_its_principal()
    {
        var repository = new FakeLoanBalanceRepository().Add(LoanId, "Active", 12500.00m);

        var (status, body) = await Get(repository);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("12500.00", json.RootElement.GetProperty("principalBalance").GetRawText());
        Assert.True(json.RootElement.GetProperty("principalBalanceAvailable").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("principalUnavailableReason", out _));
    }

    [Fact]
    public async Task Active_loan_with_the_payoff_flag_has_exactly_the_fields_of_a_non_active_body()
    {
        var repository = new FakeLoanBalanceRepository().AddRow(
            LoanId, FakeLoanBalanceRepository.ActiveStatusId, "Active", null, payOffFlagSet: true);

        var (_, body) = await Get(repository);

        using var json = JsonDocument.Parse(body);
        var fields = json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
        Assert.Equal(
            ["asOfDate", "loanId", "loanStatus", "principalBalance", "principalBalanceAvailable", "principalUnavailableReason"],
            fields);
    }

    private static async Task<(HttpStatusCode Status, string Body)> Get(FakeLoanBalanceRepository repository)
    {
        using var app = new TestApp(services =>
        {
            services.RemoveAll<ILoanBalanceRepository>();
            services.AddSingleton<ILoanBalanceRepository>(repository);
        });
        using var client = app.CreateClient(app.BothTenantsKey, TestApp.TenantA);
        using var response = await client.GetAsync($"/v1/loans/{LoanId}/balance-summary");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
