using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ResortWise.LoanBalance.DbTests.Infrastructure;

namespace ResortWise.LoanBalance.DbTests.Parity;

/// <summary>
/// Every active loan record in <c>AlphaPMS_ResortWise</c> matches the SPI parity references
/// (SC-001, FR-021, research R4): status from <c>lrGetLoanDetails</c> and
/// <c>lrGetLoanStatusTypes</c>, principal from the second result set of
/// <c>lrGetTransactionActivity</c>, as-of date from the server clock. All reference calls are
/// read-only and use the reference login. Owner and contract IDs are read at run time and never
/// logged or saved; failures name the loan ID and field only, in git-ignored <c>TestResults/</c>.
/// </summary>
[Trait("Category", "Database")]
[Collection(DatabaseCollection.Name)]
public sealed class ActiveLoanParityTests(DatabaseFixture fixture)
{
    private sealed record ActiveLoan(int LoanId, int SalesContractId, string OwnerId);

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

            var expectedPrincipal = await PrincipalRemaining(reference, loan);
            if (expectedPrincipal is null || body.GetProperty("principalBalance").GetDecimal() != expectedPrincipal.Value)
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
            "SELECT l.ID, l.SalesContractId, l.OwnerId FROM dbo.lrLoanReceivable AS l WHERE l.LoanStatusId = 1;",
            reference);
        await using var reader = await command.ExecuteReaderAsync();
        var loans = new List<ActiveLoan>();
        while (await reader.ReadAsync())
        {
            loans.Add(new ActiveLoan(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
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

    private static async Task<decimal?> PrincipalRemaining(SqlConnection reference, ActiveLoan loan)
    {
        await using var command = new SqlCommand("EXEC dbo.lrGetTransactionActivity @Ownerid = @Ownerid, @SalesContractId = @SalesContractId;", reference);
        command.Parameters.Add("@Ownerid", SqlDbType.Char, 10).Value = loan.OwnerId;
        command.Parameters.Add("@SalesContractId", SqlDbType.Int).Value = loan.SalesContractId;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.NextResultAsync() || !await reader.ReadAsync())
        {
            return null;
        }

        var ordinal = reader.GetOrdinal("PrincipalRemaining");
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        // Same rounding as the view's CAST(... AS decimal(19,2)): half away from zero (research R2).
        return Math.Round(reader.GetDecimal(ordinal), 2, MidpointRounding.AwayFromZero);
    }

    private static async Task<string> ServerDate(SqlConnection reference)
    {
        await using var command = new SqlCommand("SELECT CAST(GETDATE() AS date);", reference);
        var date = (DateTime)(await command.ExecuteScalarAsync())!;
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
