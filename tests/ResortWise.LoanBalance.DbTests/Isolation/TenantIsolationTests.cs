using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ResortWise.LoanBalance.DbTests.Infrastructure;

namespace ResortWise.LoanBalance.DbTests.Isolation;

/// <summary>
/// Tenant A (<c>AlphaPMS_ResortWise</c>) and tenant B (<c>AlphaPMS_ResortWise_B</c> after T045)
/// each return only their own data (SC-002, SC-003, SC-005). Tenant B is checked by the setup
/// script's expected results, not by an SPI procedure. Loans are selected by the T045 rules,
/// never listed; failures name the loan ID and field only, in git-ignored <c>TestResults/</c>.
/// </summary>
[Trait("Category", "Database")]
[Collection(DatabaseCollection.Name)]
public sealed class TenantIsolationTests(DatabaseFixture fixture)
{
    [DatabaseFact]
    public async Task Changed_loans_return_each_tenants_own_status_and_principal()
    {
        await TenantBSetupCheck.EnsureAppliedAsync(fixture);

        var statusInA = await StatusNames(fixture.RefA, $"{TenantBSetupCheck.ChangedRule} AND l.LoanStatusId = 1");
        var loanIds = statusInA.Keys.ToList();
        var statusInB = await StatusNames(fixture.RefB, TenantBSetupCheck.ChangedRule);
        var reasonInA = await ExpectedO5Reasons(fixture.RefA, $"{TenantBSetupCheck.ChangedRule} AND l.LoanStatusId = 1");
        Assert.NotEmpty(loanIds);

        using var clientA = fixture.CreateClient(fixture.BothTenantsKey, DatabaseFixture.TenantA);
        using var clientB = fixture.CreateClient(fixture.BothTenantsKey, DatabaseFixture.TenantB);
        var failures = new List<string>();
        foreach (var loanId in loanIds)
        {
            var a = await Get(clientA, loanId);
            var b = await Get(clientB, loanId);
            if (a.Status != HttpStatusCode.OK || b.Status != HttpStatusCode.OK)
            {
                failures.Add($"{loanId}: status code");
                continue;
            }

            using var jsonA = JsonDocument.Parse(a.Body);
            using var jsonB = JsonDocument.Parse(b.Body);

            if (!string.Equals(jsonA.RootElement.GetProperty("loanStatus").GetString(), statusInA[loanId], StringComparison.Ordinal))
            {
                failures.Add($"{loanId}: tenant A loanStatus");
            }

            if (!statusInB.TryGetValue(loanId, out var expectedB)
                || !string.Equals(jsonB.RootElement.GetProperty("loanStatus").GetString(), expectedB, StringComparison.Ordinal))
            {
                failures.Add($"{loanId}: tenant B loanStatus");
            }

            // Tenant A: active. The principal is a number and available, unless an O5 rule applies:
            // then null, not available, with the O5 reason.
            if (reasonInA.GetValueOrDefault(loanId) is { } expectedReasonA)
            {
                if (jsonA.RootElement.GetProperty("principalBalance").ValueKind != JsonValueKind.Null
                    || jsonA.RootElement.GetProperty("principalBalanceAvailable").ValueKind != JsonValueKind.False
                    || !jsonA.RootElement.TryGetProperty("principalUnavailableReason", out var reasonA)
                    || reasonA.GetString() != expectedReasonA)
                {
                    failures.Add($"{loanId}: tenant A principal");
                }
            }
            else if (jsonA.RootElement.GetProperty("principalBalance").ValueKind != JsonValueKind.Number
                || jsonA.RootElement.GetProperty("principalBalanceAvailable").ValueKind != JsonValueKind.True)
            {
                failures.Add($"{loanId}: tenant A principal");
            }

            // Tenant B: made non-active by T045, so null, not available, LOAN_NOT_ACTIVE (O2).
            if (jsonB.RootElement.GetProperty("principalBalance").ValueKind != JsonValueKind.Null)
            {
                failures.Add($"{loanId}: tenant B principalBalance");
            }

            if (jsonB.RootElement.GetProperty("principalBalanceAvailable").ValueKind != JsonValueKind.False
                || !jsonB.RootElement.TryGetProperty("principalUnavailableReason", out var reasonB)
                || reasonB.GetString() != "LOAN_NOT_ACTIVE")
            {
                failures.Add($"{loanId}: tenant B principal availability");
            }

            if (a.Body == b.Body)
            {
                failures.Add($"{loanId}: responses do not differ");
            }
        }

        var failed = failures.Select(failure => failure.Split(':')[0]).Distinct().Count();
        RunSummary.Record("TenantIsolationTests (changed loans)", $"{DatabaseFixture.DatabaseA} and {DatabaseFixture.DatabaseB}", loanIds.Count, loanIds.Count - failed, failed);
        Assert.True(failures.Count == 0, $"{failed} loans failed; details in {RunSummary.WriteFailures("TenantIsolationTests", failures)}");
    }

    [DatabaseFact]
    public async Task Removed_loan_is_not_found_in_tenant_b_and_still_found_in_tenant_a()
    {
        await TenantBSetupCheck.EnsureAppliedAsync(fixture);
        var removedId = await TenantBSetupCheck.RemovedLoanId(fixture);
        var unknownId = await UnknownLoanId();

        using var clientA = fixture.CreateClient(fixture.BothTenantsKey, DatabaseFixture.TenantA);
        using var clientB = fixture.CreateClient(fixture.BothTenantsKey, DatabaseFixture.TenantB);

        var inA = await Get(clientA, removedId);
        var inB = await Get(clientB, removedId);
        var nowhere = await Get(clientB, unknownId);

        Assert.Equal(HttpStatusCode.OK, inA.Status);
        Assert.Equal(HttpStatusCode.NotFound, inB.Status);
        Assert.Equal(HttpStatusCode.NotFound, nowhere.Status);
        Assert.Equal(nowhere.Body, inB.Body);
        RunSummary.Record("TenantIsolationTests (removed loan)", $"{DatabaseFixture.DatabaseA} and {DatabaseFixture.DatabaseB}", 1, 1, 0);
    }

    [DatabaseFact]
    public async Task Caller_allowed_only_tenant_a_is_forbidden_tenant_b()
    {
        var removedId = await TenantBSetupCheck.RemovedLoanId(fixture);
        using var client = fixture.CreateClient(fixture.TenantAOnlyKey, DatabaseFixture.TenantB);

        var response = await Get(client, removedId);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("""{"code":"TENANT_FORBIDDEN","message":"The caller is not allowed to access this tenant."}""", response.Body);
    }

    private static async Task<Dictionary<int, string?>> StatusNames(string connectionString, string where)
    {
        await using var db = await DatabaseFixture.OpenAsync(connectionString);
        await using var command = new SqlCommand(
            $"""
            SELECT l.ID, s.LoanStatusName
            FROM dbo.lrLoanReceivable AS l
            LEFT JOIN dbo.lrLoanStatus AS s ON s.ID = l.LoanStatusId
            WHERE {where};
            """,
            db);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new Dictionary<int, string?>();
        while (await reader.ReadAsync())
        {
            names[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return names;
    }

    // The O5 reason each matching record is expected to return; records whose principal is
    // available are left out (O5Rules).
    private static async Task<Dictionary<int, string>> ExpectedO5Reasons(string connectionString, string where)
    {
        await using var db = await DatabaseFixture.OpenAsync(connectionString);
        await using var command = new SqlCommand(
            $"""
            SELECT l.ID, {O5Rules.ExpectedReason}
            FROM dbo.lrLoanReceivable AS l
            WHERE {where};
            """,
            db);
        await using var reader = await command.ExecuteReaderAsync();
        var reasons = new Dictionary<int, string>();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(1))
            {
                reasons[reader.GetInt32(0)] = reader.GetString(1);
            }
        }

        return reasons;
    }

    // One above the highest loan record ID in tenant A; tenant B only has fewer records.
    private async Task<int> UnknownLoanId()
    {
        await using var reference = await DatabaseFixture.OpenAsync(fixture.RefA);
        await using var command = new SqlCommand("SELECT MAX(ID) + 1 FROM dbo.lrLoanReceivable;", reference);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<(HttpStatusCode Status, string Body)> Get(HttpClient client, int loanId)
    {
        using var response = await client.GetAsync($"/v1/loans/{loanId}/balance-summary");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
