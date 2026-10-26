using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResortWise.LoanBalance.Api.Loans;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Loans;

public sealed class BalanceSummaryEndpointTests
{
    private static readonly string[] ActiveFields = ["loanId", "loanStatus", "asOfDate", "principalBalance", "principalBalanceAvailable"];
    private static readonly string[] NonActiveFields = [.. ActiveFields, "principalUnavailableReason"];

    [Fact]
    public async Task Active_loan_returns_exactly_the_active_fields()
    {
        var (status, body) = await Get(new FakeLoanBalanceRepository().Add(100245, "Active", 12500.00m), 100245);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(ActiveFields, json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(100245, json.RootElement.GetProperty("loanId").GetInt64());
        Assert.Equal("Active", json.RootElement.GetProperty("loanStatus").GetString());
        Assert.Equal("2026-10-22", json.RootElement.GetProperty("asOfDate").GetString());
    }

    [Fact]
    public async Task Non_active_loan_returns_exactly_the_non_active_fields()
    {
        var (status, body) = await Get(new FakeLoanBalanceRepository().AddNonActive(100244, "Rewritten"), 100244);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(NonActiveFields, json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Rewritten", json.RootElement.GetProperty("loanStatus").GetString());
    }

    [Theory]
    [InlineData("0", "0.00")]
    [InlineData("0.00", "0.00")]
    [InlineData("12.5", "12.50")]
    [InlineData("12500.00", "12500.00")]
    [InlineData("-15.25", "-15.25")]
    public async Task Principal_has_exactly_two_decimal_places_and_is_not_changed(string principal, string expected)
    {
        var (_, body) = await Get(new FakeLoanBalanceRepository().Add(7, "Active", decimal.Parse(principal, System.Globalization.CultureInfo.InvariantCulture)), 7);

        using var json = JsonDocument.Parse(body);
        Assert.Equal(expected, json.RootElement.GetProperty("principalBalance").GetRawText());
    }

    [Fact]
    public async Task Body_has_no_payoff_or_out_of_scope_fields()
    {
        var (_, body) = await Get(new FakeLoanBalanceRepository().Add(7, "Active", 1m), 7);

        foreach (var word in new[] { "payoff", "interest", "fee", "unapplied", "other", "total", "pastDue" })
        {
            Assert.DoesNotContain(word, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<(HttpStatusCode Status, string Body)> Get(FakeLoanBalanceRepository repository, int loanId)
    {
        using var app = new TestApp(services =>
        {
            services.RemoveAll<ILoanBalanceRepository>();
            services.AddSingleton<ILoanBalanceRepository>(repository);
        });
        using var client = app.CreateClient(app.BothTenantsKey, TestApp.TenantA);
        using var response = await client.GetAsync($"/v1/loans/{loanId}/balance-summary");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
