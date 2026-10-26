using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ResortWise.LoanBalance.Api.Tenancy;

/// <summary>
/// Validates the registry at startup (research R6). Failure messages name the rule and the
/// configuration key, never a value such as a hash or a connection string.
/// </summary>
public sealed partial class TenantRegistryValidator : IValidateOptions<RegistryOptions>
{
    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex KeyHashPattern();

    public ValidateOptionsResult Validate(string? name, RegistryOptions options)
    {
        var failures = new List<string>();
        ValidateTenants(options, failures);
        ValidateCallers(options, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateTenants(RegistryOptions options, List<string> failures)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var usedConnectionStrings = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (tenantId, tenant) in options.Tenants)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                failures.Add("A tenant ID under Tenants is blank. Tenant IDs must be non-blank.");
                continue;
            }

            var key = $"Tenants:{tenantId}:ConnectionStringName";
            if (string.IsNullOrWhiteSpace(tenant.ConnectionStringName))
            {
                failures.Add($"{key} is required.");
                continue;
            }

            if (!options.ConnectionStrings.TryGetValue(tenant.ConnectionStringName, out var connectionString)
                || string.IsNullOrWhiteSpace(connectionString))
            {
                failures.Add($"{key} must resolve to a non-empty ConnectionStrings entry.");
                continue;
            }

            // One database per tenant: two tenants never share a connection string.
            if (!usedNames.Add(tenant.ConnectionStringName))
            {
                failures.Add($"{key} is already used by another tenant. Each tenant needs its own connection string.");
            }
            else if (!usedConnectionStrings.Add(connectionString))
            {
                failures.Add($"{key} resolves to the same connection string as another tenant. Each tenant needs its own database.");
            }
        }
    }

    private static void ValidateCallers(RegistryOptions options, List<string> failures)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hashes = new HashSet<string>(StringComparer.Ordinal);

        for (var n = 0; n < options.Callers.Count; n++)
        {
            var caller = options.Callers[n];
            var prefix = $"Callers:{n}";

            if (string.IsNullOrWhiteSpace(caller.Id))
            {
                failures.Add($"{prefix}:Id is required.");
            }
            else if (!ids.Add(caller.Id))
            {
                failures.Add($"{prefix}:Id must be unique.");
            }

            if (string.IsNullOrEmpty(caller.KeyHash) || !KeyHashPattern().IsMatch(caller.KeyHash))
            {
                failures.Add($"{prefix}:KeyHash must be 64 lowercase hex characters (SHA-256 of the API key).");
            }
            else if (!hashes.Add(caller.KeyHash))
            {
                failures.Add($"{prefix}:KeyHash must be unique.");
            }

            if (caller.Tenants.Count == 0)
            {
                failures.Add($"{prefix}:Tenants must list at least one tenant.");
            }

            for (var m = 0; m < caller.Tenants.Count; m++)
            {
                if (!options.Tenants.ContainsKey(caller.Tenants[m]))
                {
                    failures.Add($"{prefix}:Tenants:{m} must name a tenant that exists in Tenants.");
                }
            }
        }
    }
}
