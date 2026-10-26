namespace ResortWise.LoanBalance.Api.Tenancy;

/// <summary>
/// One tenant, bound from <c>Tenants:{tenantId}</c>. The tenant ID is the key: non-blank,
/// compared ordinally.
/// </summary>
public sealed class TenantOptions
{
    /// <summary>Required. Must resolve to <c>ConnectionStrings:{name}</c>. Exactly one per tenant.</summary>
    public string? ConnectionStringName { get; set; }
}

/// <summary>Callers, tenants, and connection strings, bound from the configuration root.</summary>
public sealed class RegistryOptions
{
    public List<CallerOptions> Callers { get; set; } = [];

    public Dictionary<string, TenantOptions> Tenants { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> ConnectionStrings { get; set; } = new(StringComparer.Ordinal);
}
