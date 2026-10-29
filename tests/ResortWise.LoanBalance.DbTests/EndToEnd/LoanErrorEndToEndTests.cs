using System.Net;
using Microsoft.Data.SqlClient;
using ResortWise.LoanBalance.DbTests.Infrastructure;

namespace ResortWise.LoanBalance.DbTests.EndToEnd;

/// <summary><c>LOAN_ID_INVALID</c> and <c>LOAN_NOT_FOUND</c> through the real pipeline against tenant A.</summary>
[Trait("Category", "Database")]
[Collection(DatabaseCollection.Name)]
public sealed class LoanErrorEndToEndTests(DatabaseFixture fixture)
{
    [DatabaseTheory]
    [InlineData("abc")]
    [InlineData("007")]
    [InlineData("2147483648")]
    public async Task Invalid_loan_id_returns_400(string loanId)
    {
        using var client = fixture.CreateClient(fixture.BothTenantsKey, DatabaseFixture.TenantA);
        using var response = await client.GetAsync($"/v1/loans/{Uri.EscapeDataString(loanId)}/balance-summary");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"code":"LOAN_ID_INVALID","message":"The loan ID is not valid.","field":"loanId"}""",
            await response.Content.ReadAsStringAsync());
    }

    [DatabaseFact]
    public async Task Unknown_loan_id_returns_404()
    {
        // One above the highest loan record ID: selected by rule, never listed.
        await using var reference = await DatabaseFixture.OpenAsync(fixture.RefA);
        await using var command = new SqlCommand("SELECT MAX(ID) + 1 FROM dbo.lrLoanReceivable;", reference);
        var unknownId = (int)(await command.ExecuteScalarAsync())!;

        using var client = fixture.CreateClient(fixture.BothTenantsKey, DatabaseFixture.TenantA);
        using var response = await client.GetAsync($"/v1/loans/{unknownId}/balance-summary");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("""{"code":"LOAN_NOT_FOUND","message":"The loan was not found."}""", await response.Content.ReadAsStringAsync());
    }
}
