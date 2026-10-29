namespace ResortWise.LoanBalance.DbTests.Infrastructure;

/// <summary>
/// A database theory: skipped with a clear message unless <c>RW_DB_TESTS=1</c>. Database tests
/// run locally only, never on GitHub (constitution Principle VII).
/// </summary>
public sealed class DatabaseTheoryAttribute : TheoryAttribute
{
    public DatabaseTheoryAttribute()
    {
        if (!DatabaseFixture.Enabled)
        {
            Skip = DatabaseFixture.SkipReason;
        }
    }
}
