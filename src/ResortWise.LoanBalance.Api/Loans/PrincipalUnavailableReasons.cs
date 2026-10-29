namespace ResortWise.LoanBalance.Api.Loans;

/// <summary>
/// Stable reasons for <c>principalUnavailableReason</c>, listed in the OpenAPI contract. Never
/// rename or repurpose one within /v1 (FR-008a, O2, O5). CROSS_LOAN_REVERSAL was dropped before
/// first release (2026-10-28) and is not reused.
/// </summary>
public static class PrincipalUnavailableReasons
{
    /// <summary>The loan record's status is not Active.</summary>
    public const string LoanNotActive = "LOAN_NOT_ACTIVE";

    /// <summary>The record is Active, but its payoff flag is set in SPI (O5).</summary>
    public const string PaidOffFlagSet = "PAID_OFF_FLAG_SET";

    public static IReadOnlyList<string> All { get; } = [LoanNotActive, PaidOffFlagSet];
}
