using System.Collections.Frozen;
using Microsoft.Extensions.Options;

namespace ResortWise.LoanBalance.Api.Tenancy;

/// <summary>An immutable view of the validated registry, built once at startup.</summary>
public sealed class TenantRegistry
{
    public sealed record Caller(string Id, byte[] KeyHash, FrozenSet<string> Tenants);

    public TenantRegistry(IOptions<RegistryOptions> options)
    {
        var value = options.Value;

        Callers = value.Callers
            .Select(caller => new Caller(
                caller.Id!,
                Convert.FromHexString(caller.KeyHash!),
                caller.Tenants.ToFrozenSet(StringComparer.Ordinal)))
            .ToArray();

        ConnectionStrings = value.Tenants.ToFrozenDictionary(
            tenant => tenant.Key,
            tenant => value.ConnectionStrings[tenant.Value.ConnectionStringName!],
            StringComparer.Ordinal);
    }

    public IReadOnlyList<Caller> Callers { get; }

    /// <summary>Tenant ID to that tenant's one connection string. Keys compare ordinally.</summary>
    public FrozenDictionary<string, string> ConnectionStrings { get; }

    public Caller? FindCaller(string id) => Callers.FirstOrDefault(caller => caller.Id == id);
}
