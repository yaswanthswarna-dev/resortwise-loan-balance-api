using Microsoft.Data.SqlClient;
using ResortWise.LoanBalance.DbTests.Infrastructure;

namespace ResortWise.LoanBalance.DbTests.Security;

/// <summary>
/// Proves the API login can read only through rw views, and the reference login cannot write to
/// SPI tables directly (research R9, Principle III). Every statement here is read-only; a denied
/// statement never runs, and an allowed one writes nothing.
/// </summary>
[Trait("Category", "Database")]
[Collection(DatabaseCollection.Name)]
public sealed class ApiLoginPermissionTests(DatabaseFixture fixture)
{
    private const int PermissionDenied = 229;

    public static TheoryData<string> ApiConnections => new() { "RW_DB_A", "RW_DB_B" };

    public static TheoryData<string> ReferenceConnections => new() { "RW_REF_A", "RW_REF_B" };

    public static TheoryData<string, string> DeniedStatements => new()
    {
        { "RW_DB_A", "SELECT TOP (0) * FROM dbo.lrLoanReceivable;" },
        { "RW_DB_B", "SELECT TOP (0) * FROM dbo.lrLoanReceivable;" },
        { "RW_DB_A", "EXEC dbo.lrGetLoanDetails @Loanid = 0, @SalesContractId = 0;" },
        { "RW_DB_B", "EXEC dbo.lrGetLoanDetails @Loanid = 0, @SalesContractId = 0;" },
        { "RW_DB_A", "SELECT TOP (0) * FROM dbo.lrFnGetBalanceSummary(0, GETDATE());" },
        { "RW_DB_B", "SELECT TOP (0) * FROM dbo.lrFnGetBalanceSummary(0, GETDATE());" },
    };

    [DatabaseTheory]
    [MemberData(nameof(DeniedStatements))]
    public async Task Api_login_cannot_read_spi_objects_directly(string connection, string sql)
    {
        await using var db = await DatabaseFixture.OpenAsync(ConnectionString(connection));
        await using var command = new SqlCommand(sql, db);

        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PermissionDenied, error.Number);
    }

    [DatabaseTheory]
    [MemberData(nameof(ApiConnections))]
    public async Task Api_login_can_read_rw_views(string connection)
    {
        await using var db = await DatabaseFixture.OpenAsync(ConnectionString(connection));
        await using var command = new SqlCommand("SELECT TOP (0) * FROM rw.Loan;", db);

        await command.ExecuteNonQueryAsync();
    }

    [DatabaseTheory]
    [MemberData(nameof(ReferenceConnections))]
    public async Task Reference_login_cannot_write_spi_tables(string connection)
    {
        await using var db = await DatabaseFixture.OpenAsync(ConnectionString(connection));
        await using var command = new SqlCommand(
            """
            SELECT
                HAS_PERMS_BY_NAME(N'dbo.lrLoanReceivable', N'OBJECT', N'INSERT'),
                HAS_PERMS_BY_NAME(N'dbo.lrLoanReceivable', N'OBJECT', N'UPDATE'),
                HAS_PERMS_BY_NAME(N'dbo.lrLoanReceivable', N'OBJECT', N'DELETE');
            """,
            db);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
    }

    private string ConnectionString(string name) => name switch
    {
        "RW_DB_A" => fixture.ApiA,
        "RW_DB_B" => fixture.ApiB,
        "RW_REF_A" => fixture.RefA,
        "RW_REF_B" => fixture.RefB,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
