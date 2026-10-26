using System.Text.Json;
using Microsoft.Data.SqlClient;
using ResortWise.LoanBalance.DbTests.Infrastructure;

namespace ResortWise.LoanBalance.DbTests.Parity;

/// <summary>
/// Every non-active loan record in <c>AlphaPMS_ResortWise</c> returns its own status name and
/// principal <c>0.00</c> (FR-008a, O2). Failures name the loan ID and field only, in git-ignored
/// <c>TestResults/</c>.
/// </summary>
[Trait("Category", "Database")]
[Collection(DatabaseCollection.Name)]
public sealed class NonActiveLoanTests(DatabaseFixture fixture)
{
    [DatabaseFact]
    public async Task Every_non_active_loan_returns_its_status_and_zero_principal()
    {
        var loans = new List<(int LoanId, string? Status)>();
        await using (var db = await DatabaseFixture.OpenAsync(fixture.RefA))
        await using (var command = new SqlCommand(
            """
            SELECT l.ID, s.LoanStatusName
            FROM dbo.lrLoanReceivable AS l
            LEFT JOIN dbo.lrLoanStatus AS s ON s.ID = l.LoanStatusId
            WHERE l.LoanStatusId <> 1;
            """,
            db))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                loans.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
        }

        Assert.NotEmpty(loans);

        using var client = fixture.CreateClient(fixture.BothTenantsKey, DatabaseFixture.TenantA);
        var failures = new List<string>();
        foreach (var (loanId, status) in loans)
        {
            using var response = await client.GetAsync($"/v1/loans/{loanId}/balance-summary");
            if (!response.IsSuccessStatusCode)
            {
                failures.Add($"{loanId}: status code");
                continue;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!string.Equals(json.RootElement.GetProperty("loanStatus").GetString(), status, StringComparison.Ordinal))
            {
                failures.Add($"{loanId}: loanStatus");
            }

            if (json.RootElement.GetProperty("principalBalance").GetRawText() != "0.00")
            {
                failures.Add($"{loanId}: principalBalance");
            }
        }

        var failed = failures.Select(failure => failure.Split(':')[0]).Distinct().Count();
        RunSummary.Record(nameof(NonActiveLoanTests), DatabaseFixture.DatabaseA, loans.Count, loans.Count - failed, failed);
        Assert.True(failures.Count == 0, $"{failed} loans failed; details in {RunSummary.WriteFailures(nameof(NonActiveLoanTests), failures)}");
    }
}
