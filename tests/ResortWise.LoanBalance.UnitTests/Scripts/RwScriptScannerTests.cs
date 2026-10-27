using System.Text.RegularExpressions;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Scripts;

/// <summary>
/// Enforces Principle III on every committed rw script (research R9): changes target schema rw
/// only, nothing is executed, the only SPI function a script may call is on the allowlist, and no
/// script names an SPI amount column or aggregates, so views detect conditions but never
/// calculate amounts (O5).
/// </summary>
public sealed class RwScriptScannerTests
{
    // Adding a function here is a reviewed change (research R9).
    private static readonly HashSet<string> SpiFunctionAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "dbo.lrFnGetBalanceSummary",
    };

    // The money columns of lrLoanReceivable and lrTransaction (names only). rw views may detect
    // conditions from SPI tables but never calculate an amount (O5, research R9), so none of
    // these may appear in a script. The function's principalRemaining output is not listed.
    private static readonly string[] SpiAmountColumns =
    [
        "ExchangeRate", "PaymentAmount", "LoanAmount", "FinanceCharge", "SFAmount", "PrnBalanceSnapShot",
        "IntBalanceSnapShot", "PrnPaidSnapshot", "IntPaidSnapshot", "IntYtd", "PrnYtd", "PmtRemaining",
        "prnAmount", "prnAmountPaid", "intAmount", "intAmountPaid", "lfAmount", "lfPaid", "sfPaid",
        "othAmount1", "oth1Paid", "othAmount2", "othAmount3", "othAmount4", "othAmount5", "Unapplied",
        "AmountPaid", "prnBalance",
    ];

    private static readonly Regex AmountColumn = new(
        $@"\b(?<name>{string.Join("|", SpiAmountColumns)})\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex Aggregate = new(@"\b(?<name>SUM|AVG)\s*\(", RegexOptions.IgnoreCase);

    private const string Name = @"\[?(?<{0}>[A-Za-z_][A-Za-z0-9_]*)\]?";

    private static readonly Regex CreateAlterDrop = new(
        $@"\b(?<verb>CREATE|ALTER|DROP)\s+(?:OR\s+ALTER\s+)?(?<type>[A-Za-z]+)\s+(?:{string.Format(Name, "schema")}\s*\.\s*)?{string.Format(Name, "name")}",
        RegexOptions.IgnoreCase);

    private static readonly Regex DataChange = new(
        $@"\b(?<verb>INSERT(?:\s+INTO)?|UPDATE|DELETE(?:\s+FROM)?|MERGE(?:\s+INTO)?)\s+(?:{string.Format(Name, "schema")}\s*\.\s*)?{string.Format(Name, "name")}",
        RegexOptions.IgnoreCase);

    private static readonly Regex Execute = new(@"\bEXEC(?:UTE)?\b", RegexOptions.IgnoreCase);

    private static readonly Regex QualifiedCall = new(
        $@"{string.Format(Name, "schema")}\s*\.\s*{string.Format(Name, "name")}\s*\(",
        RegexOptions.IgnoreCase);

    private static readonly Regex UnqualifiedTableFunction = new(
        $@"\b(?:FROM|JOIN|APPLY)\s+{string.Format(Name, "name")}\s*\(",
        RegexOptions.IgnoreCase);

    private static readonly Regex ThreePartName = new(
        @"\[?[A-Za-z_][A-Za-z0-9_]*\]?\s*\.\s*\[?[A-Za-z_][A-Za-z0-9_]*\]?\s*\.\s*\[?[A-Za-z_][A-Za-z0-9_]*\]?",
        RegexOptions.IgnoreCase);

    public static IEnumerable<string> RwScripts() =>
        Directory.EnumerateFiles(RepoPaths.Combine("db", "rw"), "*.sql", SearchOption.AllDirectories).Order(StringComparer.Ordinal);

    [Fact]
    public void Bootstrap_script_exists()
    {
        Assert.True(File.Exists(BootstrapPath));
    }

    [Fact]
    public void Every_rw_script_follows_the_rules()
    {
        var failures = RwScripts().SelectMany(path => Check(path, File.ReadAllText(path))).ToList();
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Bootstrap_creates_only_schema_rw_and_the_journal()
    {
        var sql = SqlText.StripCommentsAndStrings(File.ReadAllText(BootstrapPath));
        var created = CreateAlterDrop.Matches(sql)
            .Select(match => $"{match.Groups["verb"].Value} {match.Groups["type"].Value} {QualifiedName(match)}".ToUpperInvariant())
            .ToList();

        Assert.Equal(["CREATE SCHEMA RW", "CREATE TABLE RW.SCHEMAVERSIONS"], created);
        Assert.Empty(DataChange.Matches(sql));
    }

    [Theory]
    [InlineData("CREATE VIEW dbo.Loan AS SELECT 1 AS X;")]
    [InlineData("ALTER TABLE dbo.lrLoanReceivable ADD X int;")]
    [InlineData("DROP PROCEDURE dbo.lrGetLoanDetails;")]
    [InlineData("CREATE OR ALTER VIEW Loan AS SELECT 1 AS X;")]
    [InlineData("CREATE SCHEMA other;")]
    [InlineData("CREATE PROCEDURE rw.DoThing AS SELECT 1;")]
    [InlineData("UPDATE dbo.lrLoanReceivable SET LoanStatusId = 1;")]
    [InlineData("INSERT INTO dbo.lrLoanReceivable (LoanId) VALUES (1);")]
    [InlineData("DELETE FROM dbo.lrLoanReceivable;")]
    [InlineData("MERGE dbo.lrLoanReceivable AS t USING rw.Loan AS s ON 1 = 0 WHEN NOT MATCHED THEN DELETE;")]
    [InlineData("CREATE VIEW rw.X AS SELECT 1 AS X; EXEC dbo.lrGetLoanDetails 1, 1;")]
    [InlineData("CREATE VIEW rw.X AS SELECT dbo.lrSomeOtherFunction(1) AS X;")]
    [InlineData("CREATE VIEW rw.X AS SELECT f.X FROM lrFnGetBalanceSummary(1, GETDATE()) AS f;")]
    [InlineData("CREATE VIEW rw.X AS SELECT l.LoanId FROM OtherDb.dbo.lrLoanReceivable AS l;")]
    [InlineData("CREATE VIEW rw.X AS SELECT t.LoanId, t.prnAmountPaid FROM dbo.lrTransaction AS t;")]
    [InlineData("CREATE VIEW rw.X AS SELECT l.ID, l.LoanAmount AS A FROM dbo.lrLoanReceivable AS l;")]
    [InlineData("CREATE VIEW rw.X AS SELECT t.LoanId, SUM(t.ID) AS S FROM dbo.lrTransaction AS t GROUP BY t.LoanId;")]
    [InlineData("CREATE VIEW rw.X AS SELECT t.LoanId, AVG (t.ID) AS S FROM dbo.lrTransaction AS t GROUP BY t.LoanId;")]
    public void Scanner_rejects_forbidden_statements(string sql)
    {
        Assert.NotEmpty(Check("synthetic.sql", sql));
    }

    [Theory]
    [InlineData("CREATE OR ALTER VIEW rw.Loan AS SELECT l.LoanId FROM dbo.lrLoanReceivable AS l;")]
    [InlineData("CREATE OR ALTER VIEW [rw].[X] AS SELECT b.P FROM dbo.lrLoanReceivable AS l CROSS APPLY dbo.lrFnGetBalanceSummary(l.LoanId, GETDATE()) AS b;")]
    [InlineData("-- EXEC dbo.lrGetLoanDetails in a comment is ignored\nCREATE VIEW rw.X AS SELECT 'DROP TABLE dbo.Y' AS Text;")]
    [InlineData("CREATE OR ALTER VIEW rw.X AS SELECT l.ID, CAST(CASE WHEN l.IsPayOff = 1 THEN 1 ELSE 0 END AS bit) AS F, CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.lrTransaction AS t WHERE t.LoanId = l.ID AND t.TransTypeid = 1) THEN 1 ELSE 0 END AS bit) AS R FROM dbo.lrLoanReceivable AS l;")]
    public void Scanner_accepts_allowed_statements(string sql)
    {
        Assert.Empty(Check("synthetic.sql", sql));
    }

    private static string BootstrapPath => RepoPaths.Combine("db", "rw", "bootstrap", "create_schema_and_journal.sql");

    private static IEnumerable<string> Check(string path, string script)
    {
        var file = Path.GetFileName(path);
        var sql = SqlText.StripCommentsAndStrings(script);

        foreach (Match match in CreateAlterDrop.Matches(sql))
        {
            var type = match.Groups["type"].Value.ToUpperInvariant();
            var allowed = type switch
            {
                "SCHEMA" => !match.Groups["schema"].Success && Is(match.Groups["name"].Value, "rw"),
                "VIEW" or "TABLE" => Is(match.Groups["schema"].Value, "rw"),
                _ => false,
            };

            if (!allowed)
            {
                yield return $"{file}: {match.Groups["verb"].Value} {type} {QualifiedName(match)} does not target schema rw (only rw views and tables are allowed).";
            }
        }

        foreach (Match match in DataChange.Matches(sql))
        {
            if (!Is(match.Groups["schema"].Value, "rw"))
            {
                yield return $"{file}: {match.Groups["verb"].Value} {QualifiedName(match)} changes data outside schema rw.";
            }
        }

        if (Execute.IsMatch(sql))
        {
            yield return $"{file}: contains EXEC; no rw script may call a procedure.";
        }

        foreach (Match match in QualifiedCall.Matches(sql))
        {
            var name = QualifiedName(match);
            if (!Is(match.Groups["schema"].Value, "rw") && !SpiFunctionAllowlist.Contains(name))
            {
                yield return $"{file}: calls {name}, which is not on the SPI function allowlist.";
            }
        }

        foreach (Match match in UnqualifiedTableFunction.Matches(sql))
        {
            yield return $"{file}: calls {match.Groups["name"].Value} without a schema; qualify every function call.";
        }

        foreach (Match match in ThreePartName.Matches(sql))
        {
            yield return $"{file}: uses the three-part name {match.Value}; rw scripts never reference another database.";
        }

        foreach (Match match in AmountColumn.Matches(sql))
        {
            yield return $"{file}: names the SPI amount column {match.Groups["name"].Value}; rw views never calculate amounts (O5).";
        }

        foreach (Match match in Aggregate.Matches(sql))
        {
            yield return $"{file}: uses {match.Groups["name"].Value.ToUpperInvariant()}; rw views never calculate amounts (O5).";
        }
    }

    private static string QualifiedName(Match match) =>
        match.Groups["schema"].Success ? $"{match.Groups["schema"].Value}.{match.Groups["name"].Value}" : match.Groups["name"].Value;

    private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
