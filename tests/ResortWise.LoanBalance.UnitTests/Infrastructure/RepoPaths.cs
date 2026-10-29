namespace ResortWise.LoanBalance.UnitTests.Infrastructure;

/// <summary>Finds files in the repository from the test output directory.</summary>
public static class RepoPaths
{
    private static readonly Lazy<string> RootPath = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ResortWise.LoanBalance.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    });

    public static string Root => RootPath.Value;

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);
}
