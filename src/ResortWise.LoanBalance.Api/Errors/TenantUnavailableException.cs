namespace ResortWise.LoanBalance.Api.Errors;

/// <summary>The tenant's database could not be reached. Mapped to 503 TENANT_UNAVAILABLE.</summary>
public sealed class TenantUnavailableException(Exception innerException)
    : Exception("The tenant's data source is unavailable.", innerException);
