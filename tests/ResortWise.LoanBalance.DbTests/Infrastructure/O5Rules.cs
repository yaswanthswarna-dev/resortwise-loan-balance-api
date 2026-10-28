namespace ResortWise.LoanBalance.DbTests.Infrastructure;

/// <summary>
/// The O5 rules (proposed by Yaswanth 2026-10-27, agreed by Farzin pending his PR approval; spec
/// FR-008a) written for the tests, independently of the <c>rw</c> view, so the view is checked
/// against its own expectation. Rows are selected by rule, never listed, and read-only.
/// </summary>
public static class O5Rules
{
    public const string PaidOffFlagSet = "PAID_OFF_FLAG_SET";
    public const string CrossLoanReversal = "CROSS_LOAN_REVERSAL";

    /// <summary>
    /// A SQL expression over <c>dbo.lrLoanReceivable AS l</c>: the O5 reason an active record is
    /// expected to return, or NULL when its principal is available. First match wins: the payoff
    /// flag, then a reversal (<c>TransTypeid = 1</c>) on any loan record of the contract whose
    /// <c>REVApplyID</c> points to a transaction on a different loan record of the same contract.
    /// </summary>
    public const string ExpectedReason = $"""
        CASE
            WHEN l.IsPayOff = 1 THEN '{PaidOffFlagSet}'
            WHEN EXISTS
            (
                SELECT 1
                FROM dbo.lrTransaction AS rv
                JOIN dbo.lrLoanReceivable AS rvl ON rvl.ID = rv.LoanId
                JOIN dbo.lrTransaction AS p ON p.ID = rv.REVApplyID
                JOIN dbo.lrLoanReceivable AS pl ON pl.ID = p.LoanId
                WHERE rv.TransTypeid = 1
                  AND rvl.SalesContractId = l.SalesContractId
                  AND pl.SalesContractId = l.SalesContractId
                  AND p.LoanId <> rv.LoanId
            ) THEN '{CrossLoanReversal}'
        END
        """;
}
