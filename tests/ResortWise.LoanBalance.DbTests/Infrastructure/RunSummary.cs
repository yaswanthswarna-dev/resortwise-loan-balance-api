using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace ResortWise.LoanBalance.DbTests.Infrastructure;

/// <summary>
/// The local run summary for the pull request (Principle VII): date, test database name, number
/// of loans checked, and passed and failed counts. Never loan IDs or values. Written at the end
/// of the run to git-ignored <c>TestResults/run-summary.txt</c>.
/// </summary>
public static class RunSummary
{
    private sealed record Check(string Name, string Database, int LoansChecked, int Passed, int Failed);

    private static readonly ConcurrentQueue<Check> Checks = new();

    public static void Record(string name, string database, int loansChecked, int passed, int failed) =>
        Checks.Enqueue(new Check(name, database, loansChecked, passed, failed));

    public static void Write()
    {
        if (Checks.IsEmpty)
        {
            return;
        }

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Local database test run: {DateTime.Now:yyyy-MM-dd}");
        foreach (var check in Checks.OrderBy(check => check.Name, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"- {check.Name} on {check.Database}: loans checked {check.LoansChecked}, passed {check.Passed}, failed {check.Failed}");
        }

        Console.WriteLine(text.ToString());
        var directory = Directory.CreateDirectory(Path.Combine(ResultsDirectory(), "TestResults"));
        File.WriteAllText(Path.Combine(directory.FullName, "run-summary.txt"), text.ToString());
    }

    /// <summary>
    /// Writes loan-level failure details (loan ID and field only, never values) to git-ignored
    /// <c>TestResults/</c>, and returns the file path for the assertion message.
    /// </summary>
    public static string WriteFailures(string name, IEnumerable<string> lines)
    {
        var directory = Directory.CreateDirectory(Path.Combine(ResultsDirectory(), "TestResults"));
        var path = Path.Combine(directory.FullName, $"{name}-failures.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string ResultsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ResortWise.LoanBalance.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
