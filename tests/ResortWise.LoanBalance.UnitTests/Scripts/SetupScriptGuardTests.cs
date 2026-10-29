using System.Text.RegularExpressions;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Scripts;

/// <summary>
/// Checks every test-setup script without running it (research R12): the database-name guard
/// comes first, and the script changes data only, never objects.
/// </summary>
public sealed class SetupScriptGuardTests
{
    public const string Guard = "IF DB_NAME() <> N'AlphaPMS_ResortWise_B' THROW 50001, 'Wrong database', 1;";

    private static readonly Regex Ddl = new(@"\b(CREATE|ALTER|DROP)\b", RegexOptions.IgnoreCase);

    public static IEnumerable<string> SetupScripts()
    {
        var directory = RepoPaths.Combine("tests", "db", "setup");
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            : [];
    }

    [Fact]
    public void Every_setup_script_starts_with_the_database_guard()
    {
        foreach (var path in SetupScripts())
        {
            Assert.True(StartsWithGuard(File.ReadAllText(path)), $"{Path.GetFileName(path)}: the first statement must be the AlphaPMS_ResortWise_B guard.");
        }
    }

    [Fact]
    public void No_setup_script_creates_alters_or_drops_anything()
    {
        foreach (var path in SetupScripts())
        {
            Assert.False(Ddl.IsMatch(SqlText.StripCommentsAndStrings(File.ReadAllText(path))), $"{Path.GetFileName(path)}: contains CREATE, ALTER, or DROP.");
        }
    }

    [Theory]
    [InlineData("-- header comment\n/* block */\n" + Guard + "\nSET XACT_ABORT ON;", true)]
    [InlineData("SET XACT_ABORT ON;\n" + Guard, false)]
    [InlineData("IF DB_NAME() <> N'AlphaPMS_ResortWise' THROW 50001, 'Wrong database', 1;", false)]
    [InlineData("UPDATE dbo.X SET Y = 1;", false)]
    public void Guard_check_finds_the_first_statement(string script, bool expected)
    {
        Assert.Equal(expected, StartsWithGuard(script));
    }

    private static bool StartsWithGuard(string script) =>
        SqlText.StripComments(script).TrimStart().StartsWith(Guard, StringComparison.Ordinal);
}
