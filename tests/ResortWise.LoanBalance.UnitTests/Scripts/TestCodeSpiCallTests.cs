using System.Text.RegularExpressions;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Scripts;

/// <summary>
/// The reference login inherits the restored database's <c>public</c> EXECUTE rights (research
/// R9), so this test is what keeps database tests on the approved SPI calls (research R4). Every
/// SPI name in the database test code must be approved; anything else fails, including names in
/// comments.
/// </summary>
public sealed class TestCodeSpiCallTests
{
    // Adding a name here is a reviewed change (research R4, R9).
    private static readonly HashSet<string> ApprovedProcedures = new(StringComparer.OrdinalIgnoreCase)
    {
        "lrGetLoanDetails",
        "lrGetLoanStatusTypes",
        "lrGetTransactionActivity",
    };

    // Tests may select from SPI tables; listing them keeps every other SPI name a failure.
    // lrTransaction is no longer approved: only the dropped O5 cross-loan rule read it (research R4).
    private static readonly HashSet<string> ApprovedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "lrLoanReceivable",
        "lrLoanStatus",
    };

    // The function may be named only in the API login's permission test (T028) and, as the
    // principal reference, in the active-loan parity test (O4 revised, research R4).
    private const string BalanceFunction = "lrFnGetBalanceSummary";
    private const string PermissionTestFile = "ApiLoginPermissionTests.cs";
    private const string ParityTestFile = "ActiveLoanParityTests.cs";

    private static readonly Regex DboName = new(@"\bdbo\]?\s*\.\s*\[?(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase);

    private static readonly Regex SpiName = new(@"\b(?<name>lr[A-Z][A-Za-z0-9_]*)\b");

    private static readonly Regex StoredProcedureCommand = new(@"\bCommandType\s*\.\s*StoredProcedure\b");

    public static IEnumerable<string> DbTestFiles()
    {
        var root = RepoPaths.Combine("tests", "ResortWise.LoanBalance.DbTests");
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(Path.GetRelativePath(root, path)))
            .Order(StringComparer.Ordinal);
    }

    [Fact]
    public void Database_test_code_calls_only_approved_spi_objects()
    {
        var files = DbTestFiles().ToList();
        Assert.NotEmpty(files);

        var failures = files.SelectMany(path => Check(Path.GetFileName(path), File.ReadAllText(path))).ToList();
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("Any.cs", "EXEC dbo.lrPostPayment @Loanid = 1;")]
    [InlineData("Any.cs", "SELECT * FROM dbo.lrSomeOtherTable;")]
    [InlineData("Any.cs", "SELECT t.LoanId, t.REVApplyID FROM dbo.lrTransaction AS t WHERE t.TransTypeid = 1;")]
    [InlineData("Any.cs", "EXEC lrRecalculateLoan 1;")]
    [InlineData("Any.cs", "SELECT * FROM [dbo].[lrOther];")]
    [InlineData("Any.cs", "SELECT * FROM dbo.Accounts;")]
    [InlineData("Any.cs", "SELECT * FROM dbo.lrFnGetBalanceSummary(1, GETDATE());")]
    [InlineData("NonActiveLoanTests.cs", "SELECT principalRemaining FROM dbo.lrFnGetBalanceSummary(@LoanId, GETDATE());")]
    [InlineData("Any.cs", "new SqlCommand(name, db) { CommandType = CommandType.StoredProcedure };")]
    public void Scanner_rejects_unapproved_spi_calls(string file, string code)
    {
        Assert.NotEmpty(Check(file, code));
    }

    [Theory]
    [InlineData("Any.cs", "EXEC dbo.lrGetLoanDetails @Loanid = 1, @SalesContractId = 2;")]
    [InlineData("Any.cs", "EXEC dbo.lrGetLoanStatusTypes;")]
    [InlineData("Any.cs", "EXEC [dbo].[lrGetTransactionActivity] @Ownerid = 'x', @SalesContractId = 2;")]
    [InlineData("Any.cs", "SELECT l.ID FROM dbo.lrLoanReceivable AS l JOIN dbo.lrLoanStatus AS s ON s.ID = l.LoanStatusId;")]
    [InlineData("Any.cs", "SELECT TOP (0) * FROM rw.Loan;")]
    [InlineData(PermissionTestFile, "SELECT TOP (0) * FROM dbo.lrFnGetBalanceSummary(0, GETDATE());")]
    [InlineData(ParityTestFile, "SELECT principalRemaining FROM dbo.lrFnGetBalanceSummary(@LoanId, GETDATE());")]
    public void Scanner_accepts_approved_spi_calls(string file, string code)
    {
        Assert.Empty(Check(file, code));
    }

    private static IEnumerable<string> Check(string file, string code)
    {
        var names = DboName.Matches(code).Select(match => match.Groups["name"].Value)
            .Concat(SpiName.Matches(code).Select(match => match.Groups["name"].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            if (!IsApproved(file, name))
            {
                yield return $"{file}: names dbo object or SPI name {name}, which is not an approved SPI call (research R4).";
            }
        }

        if (StoredProcedureCommand.IsMatch(code))
        {
            yield return $"{file}: uses CommandType.StoredProcedure; call approved procedures as EXEC dbo.<name> in the command text.";
        }
    }

    private static bool IsApproved(string file, string name) =>
        ApprovedProcedures.Contains(name)
        || ApprovedTables.Contains(name)
        || (string.Equals(name, BalanceFunction, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(file, PermissionTestFile, StringComparison.Ordinal)
                || string.Equals(file, ParityTestFile, StringComparison.Ordinal)));

    private static bool IsBuildOutput(string relativePath)
    {
        var first = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first is "bin" or "obj";
    }
}
