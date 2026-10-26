namespace ResortWise.LoanBalance.Api.Loans;

/// <summary>
/// Stable reasons for <c>principalUnavailableReason</c>, listed in the OpenAPI contract. Never
/// rename or repurpose one within /v1 (FR-008a, O2).
/// </summary>
public static class PrincipalUnavailableReasons
{
    /// <summary>The loan record's status is not Active.</summary>
    public const string LoanNotActive = "LOAN_NOT_ACTIVE";

    public static IReadOnlyList<string> All { get; } = [LoanNotActive];
}
