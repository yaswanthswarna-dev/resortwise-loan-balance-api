using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using ResortWise.LoanBalance.DbTests.Infrastructure;

namespace ResortWise.LoanBalance.DbTests.Parity;

/// <summary>
/// Every active loan record in <c>AlphaPMS_ResortWise</c> matches the SPI parity references
/// (SC-001, FR-021, research R4): status from <c>lrGetLoanDetails</c> and
/// <c>lrGetLoanStatusTypes</c>, as-of date from the server clock; and the principal is available
/// (a number, <c>principalBalanceAvailable</c> true, no <c>principalUnavailableReason</c>; O2) and
/// equals <c>dbo.lrFnGetBalanceSummary</c>'s <c>principalRemaining</c>, rounded here to 2 places
/// half away from zero (O4 revised). All reference calls are read-only and use the reference
/// login. Contract IDs are read at run time and never logged or saved; failures name the loan ID
/// and field only, in git-ignored <c>TestResults/</c>.
/// The principal reference is the same SPI function the <c>rw</c> view uses, so this catches
/// rounding, mapping, and null errors across all active loans but does not prove the value the
/// screen shows, which the Excel engine calculates; that rests on the manual screen check (plan,
/// Complexity Tracking).
/// O5 (proposed by Yaswanth 2026-10-27, agreed by Farzin pending his PR approval): an active
/// record whose payoff flag is set, or whose contract has a reversal on one loan record that
/// reverses a transaction on a different loan record, shows a screen value the function cannot
/// reproduce. This test finds those records with its own
/// read-only query on SPI tables, independent of the <c>rw</c> view, and checks that the API
/// returns principal null, not available, with <c>PAID_OFF_FLAG_SET</c> or
/// <c>CROSS_LOAN_REVERSAL</c>; principal is compared only for the other active records. The
/// per-reason counts go to git-ignored <c>TestResults/o5-checks.txt</c>, never to the run summary
/// that is pasted into the pull request.
/// </summary>
[Trait("Category", "Database")]
[Collection(DatabaseCollection.Name)]
public sealed class ActiveLoanParityTests(DatabaseFixture fixture)
{
    private const string PaidOffFlagSet = O5Rules.PaidOffFlagSet;
    private const string CrossLoanReversal = O5Rules.CrossLoanReversal;

    /// <summary>An active loan record and the O5 reason it is expected to return, or null when its principal is available.</summary>
    private sealed record ActiveLoan(int LoanId, int SalesContractId, string? ExpectedReason);

    private static readonly Regex TwoPlaces = new(@"^-?[0-9]+\.[0-9]{2}$");

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
            // Null means the server date changed during the check (midnight), so the principal
            // reference may be for another day: check the loan once more.
            var loanFailures = await CheckLoan(client, reference, statusNames, loan)
                ?? await CheckLoan(client, reference, statusNames, loan)
                ?? [$"{loan.LoanId}: asOfDate"];
            failures.AddRange(loanFailures);
        }

        var failed = failures.Select(failure => failure.Split(':')[0]).Distinct().Count();
        RunSummary.Record(nameof(ActiveLoanParityTests), DatabaseFixture.DatabaseA, loans.Count, loans.Count - failed, failed);

        var paidOff = loans.Count(loan => loan.ExpectedReason == PaidOffFlagSet);
        var crossLoan = loans.Count(loan => loan.ExpectedReason == CrossLoanReversal);
        RunSummary.WritePrivateCounts("o5-checks.txt",
        [
            $"Principal compared: {loans.Count - paidOff - crossLoan}",
            $"{PaidOffFlagSet} checked: {paidOff}",
            $"{CrossLoanReversal} checked: {crossLoan}",
        ]);

        Assert.True(failures.Count == 0, $"{failed} loans failed; details in {RunSummary.WriteFailures(nameof(ActiveLoanParityTests), failures)}");

        // AlphaPMS_ResortWise stays exactly as restored, so both O5 rules have records to check;
        // a count of 0 would leave a rule untested.
        Assert.True(paidOff > 0, $"No active record matches {PaidOffFlagSet}, so the rule is untested.");
        Assert.True(crossLoan > 0, $"No active record matches {CrossLoanReversal}, so the rule is untested.");
    }

    /// <summary>
    /// Checks one loan and returns its failures, or null when the server date read before the
    /// request differs from the one read after the principal reference.
    /// </summary>
    private static async Task<List<string>?> CheckLoan(
        HttpClient client, SqlConnection reference, Dictionary<int, string> statusNames, ActiveLoan loan)
    {
        var before = await ServerDate(reference);
        using var response = await client.GetAsync($"/v1/loans/{loan.LoanId}/balance-summary");
        var expectedPrincipal = loan.ExpectedReason is null ? await ReferencePrincipal(reference, loan.LoanId) : null;
        var after = await ServerDate(reference);
        if (before != after)
        {
            return null;
        }

        var failures = new List<string>();
        if (!response.IsSuccessStatusCode)
        {
            failures.Add($"{loan.LoanId}: status code");
            return failures;
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

        if (body.GetProperty("asOfDate").GetString() != before)
        {
            failures.Add($"{loan.LoanId}: asOfDate");
        }

        if (loan.ExpectedReason is not null)
        {
            // O5: null principal (the JSON null token), not available, and the expected reason.
            // The function is not called, because its value is not the screen's for this record.
            if (!body.TryGetProperty("principalBalance", out var unavailable)
                || unavailable.ValueKind != JsonValueKind.Null)
            {
                failures.Add($"{loan.LoanId}: principalBalance");
            }

            if (!body.TryGetProperty("principalBalanceAvailable", out var notAvailable)
                || notAvailable.ValueKind != JsonValueKind.False)
            {
                failures.Add($"{loan.LoanId}: principalBalanceAvailable");
            }

            if (!body.TryGetProperty("principalUnavailableReason", out var reason)
                || !string.Equals(reason.GetString(), loan.ExpectedReason, StringComparison.Ordinal))
            {
                failures.Add($"{loan.LoanId}: principalUnavailableReason");
            }

            return failures;
        }

        // Active and no O5 rule applies, so the principal is available: a number, never null,
        // and no reason (FR-008a, O2, O5).
        if (!body.TryGetProperty("principalBalanceAvailable", out var available)
            || available.ValueKind != JsonValueKind.True)
        {
            failures.Add($"{loan.LoanId}: principalBalanceAvailable");
        }

        if (body.TryGetProperty("principalUnavailableReason", out _))
        {
            failures.Add($"{loan.LoanId}: principalUnavailableReason");
        }

        // The principal equals the function's value for the same loan ID, rounded here (not by
        // the view's CAST) to 2 places half away from zero, as the screen does (research R2, R4).
        // A missing or null reference value, or a null API value, is a mismatch.
        if (expectedPrincipal is null
            || !body.TryGetProperty("principalBalance", out var principal)
            || principal.ValueKind != JsonValueKind.Number
            || !TwoPlaces.IsMatch(principal.GetRawText())
            || principal.GetDecimal() != Math.Round(expectedPrincipal.Value, 2, MidpointRounding.AwayFromZero))
        {
            failures.Add($"{loan.LoanId}: principalBalance");
        }

        return failures;
    }

    /// <summary>
    /// Every active loan record with its expected O5 reason (<see cref="O5Rules"/>), by rule,
    /// never a list of loans, read-only.
    /// </summary>
    private static async Task<List<ActiveLoan>> ActiveLoans(SqlConnection reference)
    {
        await using var command = new SqlCommand(
            $"""
            SELECT l.ID, l.SalesContractId, {O5Rules.ExpectedReason}
            FROM dbo.lrLoanReceivable AS l
            WHERE l.LoanStatusId = 1;
            """,
            reference);
        await using var reader = await command.ExecuteReaderAsync();
        var loans = new List<ActiveLoan>();
        while (await reader.ReadAsync())
        {
            loans.Add(new ActiveLoan(reader.GetInt32(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
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

    /// <summary>
    /// The function's principal for the loan (read-only), or null unless it returns exactly one
    /// row with a non-null value.
    /// </summary>
    private static async Task<decimal?> ReferencePrincipal(SqlConnection reference, int loanId)
    {
        await using var command = new SqlCommand(
            "SELECT principalRemaining FROM dbo.lrFnGetBalanceSummary(@LoanId, GETDATE());",
            reference);
        command.Parameters.Add("@LoanId", SqlDbType.Int).Value = loanId;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.IsDBNull(0))
        {
            return null;
        }

        var value = reader.GetDecimal(0);
        return await reader.ReadAsync() ? null : value;
    }

    private static async Task<string> ServerDate(SqlConnection reference)
    {
        await using var command = new SqlCommand("SELECT CAST(GETDATE() AS date);", reference);
        var date = (DateTime)(await command.ExecuteScalarAsync())!;
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
