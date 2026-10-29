namespace ResortWise.LoanBalance.Api.Audit;

/// <summary>
/// Request-scoped values filled in as the pipeline advances and written once to the audit log.
/// Never holds amounts.
/// </summary>
public sealed class RequestContext
{
    public string CorrelationId { get; } = Guid.NewGuid().ToString("N");

    public string? CallerId { get; set; }

    public string? TenantId { get; set; }

    public string? LoanId { get; set; }

    public string? ErrorCode { get; set; }

    /// <summary>
    /// The connection string of the tenant the tenant check resolved, if any. Used only by the
    /// connection factory; never logged.
    /// </summary>
    internal string? TenantConnectionString { get; set; }
}
