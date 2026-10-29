using System.Data.Common;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ResortWise.LoanBalance.Api.Tenancy;

namespace ResortWise.LoanBalance.UnitTests.Infrastructure;

/// <summary>
/// The real API pipeline with synthetic configuration: tenant <c>tenant-a</c> and
/// <c>tenant-b</c>, a caller allowed both, and a caller allowed only <c>tenant-a</c>. Keys are
/// generated per instance. No database is used; services can be replaced per test.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    public const string TenantA = "tenant-a";
    public const string TenantB = "tenant-b";

    private readonly Action<IServiceCollection>? configureServices;

    public TestApp(Action<IServiceCollection>? configureServices = null)
    {
        this.configureServices = configureServices;
    }

    public TestKeys.ApiKey BothTenantsKey { get; } = TestKeys.Create();

    public TestKeys.ApiKey TenantAOnlyKey { get; } = TestKeys.Create();

    public CapturingLoggerProvider Logs { get; } = new();

    public CountingConnectionFactory Connections { get; } = new();

    public HttpClient CreateClient(TestKeys.ApiKey? key, string? tenantId)
    {
        var client = CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key.Key);
        }

        if (tenantId is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(TenantAccessMiddleware.TenantHeader, tenantId);
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Callers:0:Id"] = "caller-both",
            ["Callers:0:KeyHash"] = BothTenantsKey.Hash,
            ["Callers:0:Tenants:0"] = TenantA,
            ["Callers:0:Tenants:1"] = TenantB,
            ["Callers:1:Id"] = "caller-a-only",
            ["Callers:1:KeyHash"] = TenantAOnlyKey.Hash,
            ["Callers:1:Tenants:0"] = TenantA,
            [$"Tenants:{TenantA}:ConnectionStringName"] = "UnitTestA",
            [$"Tenants:{TenantB}:ConnectionStringName"] = "UnitTestB",
            ["ConnectionStrings:UnitTestA"] = "Data Source=unit-test-a.invalid",
            ["ConnectionStrings:UnitTestB"] = "Data Source=unit-test-b.invalid",
        }));

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITenantConnectionFactory>();
            services.AddSingleton<ITenantConnectionFactory>(Connections);
            configureServices?.Invoke(services);
        });
    }
}

/// <summary>A connection factory that never connects and counts how often it was asked.</summary>
public sealed class CountingConnectionFactory : ITenantConnectionFactory
{
    private int calls;

    public int Calls => calls;

    public Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        throw new InvalidOperationException("Unit tests never open a database connection.");
    }
}
