using ResortWise.LoanBalance.Api.Tenancy;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Tenancy;

public sealed class TenantRegistryValidatorTests
{
    private const string ConnectionA = "Data Source=validator-a.invalid";
    private const string ConnectionB = "Data Source=validator-b.invalid";

    private static readonly string HashA = TestKeys.Create().Hash;
    private static readonly string HashB = TestKeys.Create().Hash;

    [Fact]
    public void Valid_configuration_passes()
    {
        Assert.True(Validate(Valid()).Succeeded);
    }

    public static TheoryData<string, Action<RegistryOptions>> BadConfigurations => new()
    {
        { "Callers:0:Id is required", options => options.Callers[0].Id = " " },
        { "Callers:1:Id must be unique", options => options.Callers[1].Id = options.Callers[0].Id },
        { "Callers:0:KeyHash must be 64 lowercase hex", options => options.Callers[0].KeyHash = null },
        { "Callers:0:KeyHash must be 64 lowercase hex", options => options.Callers[0].KeyHash = HashA.ToUpperInvariant() },
        { "Callers:0:KeyHash must be 64 lowercase hex", options => options.Callers[0].KeyHash = HashA[..63] },
        { "Callers:1:KeyHash must be unique", options => options.Callers[1].KeyHash = options.Callers[0].KeyHash },
        { "Callers:0:Tenants must list at least one", options => options.Callers[0].Tenants.Clear() },
        { "Callers:0:Tenants:1 must name a tenant that exists", options => options.Callers[0].Tenants[1] = "tenant-c" },
        { "Callers:0:Tenants:0 must name a tenant that exists", options => options.Callers[0].Tenants[0] = "Tenant-A" },
        { "tenant ID under Tenants is blank", options => options.Tenants[" "] = new TenantOptions { ConnectionStringName = "A" } },
        { "Tenants:tenant-a:ConnectionStringName is required", options => options.Tenants["tenant-a"].ConnectionStringName = "" },
        { "Tenants:tenant-a:ConnectionStringName must resolve", options => options.Tenants["tenant-a"].ConnectionStringName = "Missing" },
        { "Tenants:tenant-a:ConnectionStringName must resolve", options => options.ConnectionStrings["A"] = " " },
        { "Tenants:tenant-b:ConnectionStringName is already used", options => options.Tenants["tenant-b"].ConnectionStringName = "A" },
        { "Tenants:tenant-b:ConnectionStringName resolves to the same connection string", options => options.ConnectionStrings["B"] = ConnectionA },
    };

    [Theory]
    [MemberData(nameof(BadConfigurations))]
    public void Each_rule_rejects_bad_configuration_without_leaking_values(string expected, Action<RegistryOptions> breakIt)
    {
        var options = Valid();
        breakIt(options);

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains(expected, StringComparison.Ordinal));
        foreach (var failure in result.Failures!)
        {
            Assert.DoesNotContain(HashA, failure, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(HashB, failure, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Data Source", failure, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(RegistryOptions options) =>
        new TenantRegistryValidator().Validate(null, options);

    private static RegistryOptions Valid() => new()
    {
        Callers =
        [
            new CallerOptions { Id = "caller-both", KeyHash = HashA, Tenants = ["tenant-a", "tenant-b"] },
            new CallerOptions { Id = "caller-a-only", KeyHash = HashB, Tenants = ["tenant-a"] },
        ],
        Tenants = new(StringComparer.Ordinal)
        {
            ["tenant-a"] = new TenantOptions { ConnectionStringName = "A" },
            ["tenant-b"] = new TenantOptions { ConnectionStringName = "B" },
        },
        ConnectionStrings = new(StringComparer.Ordinal)
        {
            ["A"] = ConnectionA,
            ["B"] = ConnectionB,
        },
    };
}
