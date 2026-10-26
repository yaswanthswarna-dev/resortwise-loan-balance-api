using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ResortWise.LoanBalance.DbTests.Infrastructure;

namespace ResortWise.LoanBalance.DbTests.Parity;

/// <summary>
/// Every active loan record in <c>AlphaPMS_ResortWise</c> matches the SPI parity references
/// (SC-001, FR-021, research R4): status from <c>lrGetLoanDetails</c> and
/// <c>lrGetLoanStatusTypes</c>, as-of date from the server clock; and the principal is available
/// (a number, <c>principalBalanceAvailable</c> true, no <c>principalUnavailableReason</c>; O2). All reference calls are
/// read-only and use the reference login. Contract IDs are read at run time and never logged or
/// saved; failures name the loan ID and field only, in git-ignored <c>TestResults/</c>.
/// Principal is not compared automatically: its reference procedure needs another database the
/// reference login may not read, so principal parity is a documented deviation backed by the
/// manual screen check (plan, Complexity Tracking).
/// </summary>
[Trait("Category", "Database")]
[Collection(DatabaseCollection.Name)]
public sealed class ActiveLoanParityTests(DatabaseFixture fixture)
{
    private sealed record ActiveLoan(int LoanId, int SalesContractId);

    [DatabaseFact]
    public async Task Every_active_loan_matches_the_spi_references()
    {
        await using var reference = await DatabaseFixture.OpenAsync(fixture.RefA);
        var loans = await ActiveLoans(reference);
        var statusNames = await StatusNames(reference);
        Assert.NotEmpty(loans);

        using var client = fixture.CreateClient(fixture.BothTenantsKey, DatabaseFixture.TenantA);
        var failures = new List<string>();
        foreach (var loan in loans)
        {
            var before = await ServerDate(reference);
            using var response = await client.GetAsync($"/v1/loans/{loan.LoanId}/balance-summary");
            var after = await ServerDate(reference);

            if (!response.IsSuccessStatusCode)
            {
                failures.Add($"{loan.LoanId}: status code");
                continue;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var body = json.RootElement;

            var statusId = await LoanStatusId(reference, loan);
            if (statusId is null
                || !statusNames.TryGetValue(statusId.Value, out var expectedStatus)
                || !string.Equals(body.GetProperty("loanStatus").GetString(), expectedStatus, StringComparison.Ordinal))
            {
                failures.Add($"{loan.LoanId}: loanStatus");
            }

            var asOf = body.GetProperty("asOfDate").GetString();
            if (asOf != before && asOf != after)
            {
                failures.Add($"{loan.LoanId}: asOfDate");
            }

            // Active, so the principal is available: a number, never null, and no reason (FR-008a, O2).
            if (!body.TryGetProperty("principalBalanceAvailable", out var available)
                || available.ValueKind != JsonValueKind.True)
            {
                failures.Add($"{loan.LoanId}: principalBalanceAvailable");
            }

            if (body.TryGetProperty("principalUnavailableReason", out _))
            {
                failures.Add($"{loan.LoanId}: principalUnavailableReason");
            }

            if (!body.TryGetProperty("principalBalance", out var principal)
                || principal.ValueKind != JsonValueKind.Number)
            {
                failures.Add($"{loan.LoanId}: principalBalance");
            }
        }

        var failed = failures.Select(failure => failure.Split(':')[0]).Distinct().Count();
        RunSummary.Record(nameof(ActiveLoanParityTests), DatabaseFixture.DatabaseA, loans.Count, loans.Count - failed, failed);
        Assert.True(failures.Count == 0, $"{failed} loans failed; details in {RunSummary.WriteFailures(nameof(ActiveLoanParityTests), failures)}");
    }

    private static async Task<List<ActiveLoan>> ActiveLoans(SqlConnection reference)
    {
        await using var command = new SqlCommand(
            "SELECT l.ID, l.SalesContractId FROM dbo.lrLoanReceivable AS l WHERE l.LoanStatusId = 1;",
            reference);
        await using var reader = await command.ExecuteReaderAsync();
        var loans = new List<ActiveLoan>();
        while (await reader.ReadAsync())
        {
            loans.Add(new ActiveLoan(reader.GetInt32(0), reader.GetInt32(1)));
        }

        return loans;
    }

    private static async Task<Dictionary<int, string>> StatusNames(SqlConnection reference)
    {
        await using var command = new SqlCommand("EXEC dbo.lrGetLoanStatusTypes;", reference);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new Dictionary<int, string>();
        while (await reader.ReadAsync())
        {
            var id = int.Parse(reader.GetString(reader.GetOrdinal("LoanStatusId")).Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
            names[id] = reader.GetString(reader.GetOrdinal("LoanStatusName"));
        }

        return names;
    }

    private static async Task<int?> LoanStatusId(SqlConnection reference, ActiveLoan loan)
    {
        await using var command = new SqlCommand("EXEC dbo.lrGetLoanDetails @Loanid = @Loanid, @SalesContractId = @SalesContractId;", reference);
        command.Parameters.Add("@Loanid", SqlDbType.Int).Value = loan.LoanId;
        command.Parameters.Add("@SalesContractId", SqlDbType.Int).Value = loan.SalesContractId;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var ordinal = reader.GetOrdinal("LoanStatusId");
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static async Task<string> ServerDate(SqlConnection reference)
    {
        await using var command = new SqlCommand("SELECT CAST(GETDATE() AS date);", reference);
        var date = (DateTime)(await command.ExecuteScalarAsync())!;
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
