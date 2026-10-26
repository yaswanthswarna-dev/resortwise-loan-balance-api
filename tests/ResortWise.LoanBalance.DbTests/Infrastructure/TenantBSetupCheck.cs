using Microsoft.Data.SqlClient;

namespace ResortWise.LoanBalance.DbTests.Infrastructure;

/// <summary>
/// Confirms, read-only, that the T045 setup script was applied to <c>AlphaPMS_ResortWise_B</c>
/// before the isolation tests run. It never runs the script and needs no login that can write.
/// Loans are selected by the script's rules, never listed.
/// </summary>
public static class TenantBSetupCheck
{
    private const string Fix = "Tenant B is not prepared: run quickstart step 2 (tests/db/setup/B001__diverge_tenant_b.sql).";

    /// <summary>Changed rule: active records with this remainder in tenant A become non-active in tenant B.</summary>
    public const string ChangedRule = "l.ID % 10 = 3";

    /// <summary>Removed rule: the lowest record ID with this remainder in tenant A is removed from tenant B.</summary>
    public const string RemovedRule = "l.ID % 10 = 7";

    public static async Task<int> RemovedLoanId(DatabaseFixture fixture)
    {
        await using var reference = await DatabaseFixture.OpenAsync(fixture.RefA);
        await using var command = new SqlCommand($"SELECT MIN(l.ID) FROM dbo.lrLoanReceivable AS l WHERE {RemovedRule};", reference);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    public static async Task EnsureAppliedAsync(DatabaseFixture fixture)
    {
        await using var reference = await DatabaseFixture.OpenAsync(fixture.RefB);
        await using var command = new SqlCommand(
            $"""
            SELECT
                DB_NAME(),
                (SELECT COUNT(*) FROM dbo.lrLoanReceivable AS l WHERE {ChangedRule} AND l.LoanStatusId = 1),
                (SELECT COUNT(*) FROM dbo.lrLoanReceivable WHERE ID = @RemovedLoanId);
            """,
            reference);
        command.Parameters.AddWithValue("@RemovedLoanId", await RemovedLoanId(fixture));
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        if (reader.GetString(0) != DatabaseFixture.DatabaseB || reader.GetInt32(1) != 0 || reader.GetInt32(2) != 0)
        {
            throw new InvalidOperationException(Fix);
        }
    }
}
