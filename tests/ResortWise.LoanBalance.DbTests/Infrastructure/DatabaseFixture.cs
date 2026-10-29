using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using ResortWise.LoanBalance.Api.Tenancy;

namespace ResortWise.LoanBalance.DbTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}

/// <summary>
/// Shared setup for every database test (quickstart step 7). Reads four connection strings from
/// environment variables, proves each one points at the expected database and login before any
/// test runs, and starts the API with tenants A and B on the <c>rw_api</c> connections. Caller
/// keys are generated per run.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string TenantA = "tenant-a";
    public const string TenantB = "tenant-b";
    public const string DatabaseA = "AlphaPMS_ResortWise";
    public const string DatabaseB = "AlphaPMS_ResortWise_B";

    public const string SkipReason =
        "Database test: set RW_DB_TESTS=1 and RW_DB_A, RW_DB_B, RW_REF_A, RW_REF_B (quickstart step 7). Runs locally only.";

    private DbTestApp? app;

    public static bool Enabled => Environment.GetEnvironmentVariable("RW_DB_TESTS") == "1";

    /// <summary>Tenant A's API connection (<c>rw_api</c>). Only used to configure the API and for permission tests.</summary>
    public string ApiA { get; private set; } = "";

    /// <summary>Tenant B's API connection (<c>rw_api</c>).</summary>
    public string ApiB { get; private set; } = "";

    /// <summary>Reference reads on <c>AlphaPMS_ResortWise</c> (<c>rw_parity_ref</c>, read-only).</summary>
    public string RefA { get; private set; } = "";

    /// <summary>Reference reads on <c>AlphaPMS_ResortWise_B</c> (<c>rw_parity_ref</c>, read-only).</summary>
    public string RefB { get; private set; } = "";

    public string BothTenantsKey { get; } = NewKey();

    public string TenantAOnlyKey { get; } = NewKey();

    public DbTestApp App => app ?? throw new InvalidOperationException(SkipReason);

    public async Task InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        ApiA = Required("RW_DB_A");
        ApiB = Required("RW_DB_B");
        RefA = Required("RW_REF_A");
        RefB = Required("RW_REF_B");

        await AssertConnection("RW_DB_A", ApiA, DatabaseA, "rw_api");
        await AssertConnection("RW_DB_B", ApiB, DatabaseB, "rw_api");
        await AssertConnection("RW_REF_A", RefA, DatabaseA, "rw_parity_ref");
        await AssertConnection("RW_REF_B", RefB, DatabaseB, "rw_parity_ref");

        app = new DbTestApp(this);
    }

    public Task DisposeAsync()
    {
        app?.Dispose();
        RunSummary.Write();
        return Task.CompletedTask;
    }

    public HttpClient CreateClient(string? key, string? tenantId)
    {
        var client = App.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        if (tenantId is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(TenantAccessMiddleware.TenantHeader, tenantId);
        }

        return client;
    }

    public static async Task<SqlConnection> OpenAsync(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    internal static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is not set. {SkipReason}");

    private static async Task AssertConnection(string name, string connectionString, string database, string login)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new SqlCommand("SELECT DB_NAME(), SUSER_SNAME();", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        if (reader.GetString(0) != database || reader.GetString(1) != login)
        {
            // Names only; the connection string is never printed.
            throw new InvalidOperationException(
                $"{name} must connect to database {database} as login {login}. Fix the variable before running database tests.");
        }
    }

    private static string NewKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>The real API, configured for the two local tenant databases.</summary>
public sealed class DbTestApp(DatabaseFixture fixture) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Callers:0:Id"] = "db-test-both",
            ["Callers:0:KeyHash"] = DatabaseFixture.Hash(fixture.BothTenantsKey),
            ["Callers:0:Tenants:0"] = DatabaseFixture.TenantA,
            ["Callers:0:Tenants:1"] = DatabaseFixture.TenantB,
            ["Callers:1:Id"] = "db-test-a-only",
            ["Callers:1:KeyHash"] = DatabaseFixture.Hash(fixture.TenantAOnlyKey),
            ["Callers:1:Tenants:0"] = DatabaseFixture.TenantA,
            [$"Tenants:{DatabaseFixture.TenantA}:ConnectionStringName"] = "DbTestA",
            [$"Tenants:{DatabaseFixture.TenantB}:ConnectionStringName"] = "DbTestB",
            ["ConnectionStrings:DbTestA"] = fixture.ApiA,
            ["ConnectionStrings:DbTestB"] = fixture.ApiB,
        }));
    }
}
