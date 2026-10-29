namespace ResortWise.LoanBalance.Api.Tenancy;

/// <summary>One caller, bound from <c>Callers:{n}</c>.</summary>
public sealed class CallerOptions
{
    /// <summary>Required, unique. Appears in audit entries.</summary>
    public string? Id { get; set; }

    /// <summary>Required, 64 lowercase hex characters (SHA-256 of the API key), unique. Never the plain key.</summary>
    public string? KeyHash { get; set; }

    /// <summary>Required, at least one. Each must exist in <c>Tenants</c>.</summary>
    public List<string> Tenants { get; set; } = [];
}
