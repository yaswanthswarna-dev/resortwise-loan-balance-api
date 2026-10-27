using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResortWise.LoanBalance.Api.Loans;
using ResortWise.LoanBalance.Api.Loans.Models;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Loans;

/// <summary>
/// A null principal is never serialized as 0 (FR-006, FR-008a, O2). All rows are synthetic.
/// </summary>
public sealed class NullPrincipalSerializationTests
{
    [Fact]
    public async Task Non_active_loan_returns_null_principal_not_available_and_the_reason()
    {
        var (status, body) = await Get(new FakeLoanBalanceRepository().AddNonActive(100244, "Rewritten"), 100244);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        var principal = json.RootElement.GetProperty("principalBalance");
        Assert.Equal(JsonValueKind.Null, principal.ValueKind);
        Assert.Equal("null", principal.GetRawText());
        Assert.False(json.RootElement.GetProperty("principalBalanceAvailable").GetBoolean());
        Assert.Equal("LOAN_NOT_ACTIVE", json.RootElement.GetProperty("principalUnavailableReason").GetString());
        Assert.Contains("\"principalBalance\":null", body, StringComparison.Ordinal);
    }

    // Farzin's O2 note (2026-10-27): paid-off and cancelled records get the same rule, although
    // the screen likely shows 0.00 for them; the engine feature must cover them. Synthetic statuses.
    [Theory]
    [InlineData(3, "Paid Off")]
    [InlineData(4, "Cancelled")]
    public async Task Paid_off_and_cancelled_loans_return_null_principal_not_available_and_the_reason(int statusId, string statusName)
    {
        var repository = new FakeLoanBalanceRepository().AddRow(100246, statusId, statusName, null);

        var (status, body) = await Get(repository, 100246);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(statusName, json.RootElement.GetProperty("loanStatus").GetString());
        Assert.Equal("null", json.RootElement.GetProperty("principalBalance").GetRawText());
        Assert.False(json.RootElement.GetProperty("principalBalanceAvailable").GetBoolean());
        Assert.Equal("LOAN_NOT_ACTIVE", json.RootElement.GetProperty("principalUnavailableReason").GetString());
        Assert.DoesNotContain("\"principalBalance\":0", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"principalBalance\":0")]
    [InlineData("\"principalBalance\":0.0")]
    [InlineData("\"principalBalance\":0.00")]
    [InlineData("\"principalBalance\":\"")]
    public async Task Non_active_loan_never_writes_zero_or_text_for_the_principal(string forbidden)
    {
        var (_, body) = await Get(new FakeLoanBalanceRepository().AddNonActive(100244, "Rewritten"), 100244);

        Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
    }

    [Fact]
    public void Response_model_with_a_null_principal_serializes_null()
    {
        var response = new LoanBalanceSummaryResponse(100244, "Rewritten", new DateOnly(2026, 10, 22), null, false, PrincipalUnavailableReasons.LoanNotActive);

        var body = JsonSerializer.Serialize(response);

        Assert.Contains("\"principalBalance\":null", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"principalBalance\":0", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_loan_with_zero_principal_returns_zero_available_and_no_reason()
    {
        var (status, body) = await Get(new FakeLoanBalanceRepository().Add(100245, "Active", 0m), 100245);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("0.00", json.RootElement.GetProperty("principalBalance").GetRawText());
        Assert.True(json.RootElement.GetProperty("principalBalanceAvailable").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("principalUnavailableReason", out _));
    }

    [Fact]
    public async Task Active_loan_with_a_null_principal_is_an_internal_error()
    {
        var repository = new FakeLoanBalanceRepository().AddRow(100245, FakeLoanBalanceRepository.ActiveStatusId, "Active", null);

        var (status, body) = await Get(repository, 100245);

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal("""{"code":"INTERNAL_ERROR","message":"An unexpected error occurred."}""", body);
    }

    [Fact]
    public async Task Non_active_loan_with_a_principal_is_an_internal_error()
    {
        var repository = new FakeLoanBalanceRepository().AddRow(100244, FakeLoanBalanceRepository.NonActiveStatusId, "Rewritten", 12500.00m);

        var (status, body) = await Get(repository, 100244);

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal("""{"code":"INTERNAL_ERROR","message":"An unexpected error occurred."}""", body);
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
