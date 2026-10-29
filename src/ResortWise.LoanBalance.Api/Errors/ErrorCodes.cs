namespace ResortWise.LoanBalance.Api.Errors;

/// <summary>
/// Stable error codes, listed in the OpenAPI contract. Never rename or repurpose one within /v1.
/// </summary>
public static class ErrorCodes
{
    public const string TenantRequired = "TENANT_REQUIRED";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string TenantForbidden = "TENANT_FORBIDDEN";
    public const string LoanIdInvalid = "LOAN_ID_INVALID";
    public const string LoanNotFound = "LOAN_NOT_FOUND";
    public const string TenantUnavailable = "TENANT_UNAVAILABLE";
    public const string InternalError = "INTERNAL_ERROR";

    public static IReadOnlyList<string> All { get; } =
    [
        TenantRequired,
        Unauthenticated,
        TenantForbidden,
        LoanIdInvalid,
        LoanNotFound,
        TenantUnavailable,
        InternalError,
    ];
}
