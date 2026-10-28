namespace ResortWise.LoanBalance.DbTests.Infrastructure;

/// <summary>
/// The O5 rule (proposed by Yaswanth 2026-10-27, agreed by Farzin pending his PR approval; spec
/// FR-008a) written for the tests, independently of the <c>rw</c> view, so the view is checked
/// against its own expectation. Rows are selected by rule, never listed, and read-only. The
/// cross-loan rule was dropped on 2026-10-28; only the payoff flag rule is left.
/// </summary>
public static class O5Rules
{
    public const string PaidOffFlagSet = "PAID_OFF_FLAG_SET";

    /// <summary>
    /// A SQL expression over <c>dbo.lrLoanReceivable AS l</c>: the O5 reason an active record is
    /// expected to return, or NULL when its principal is available: the payoff flag.
    /// </summary>
    public const string ExpectedReason = $"""
        CASE WHEN l.IsPayOff = 1 THEN '{PaidOffFlagSet}' END
        """;
}
