using System.Data.Common;
using Microsoft.Data.SqlClient;
using ResortWise.LoanBalance.Api.Audit;
using ResortWise.LoanBalance.Api.Errors;

namespace ResortWise.LoanBalance.Api.Tenancy;

public interface ITenantConnectionFactory
{
    /// <summary>Opens a connection to the request's resolved tenant database, and no other.</summary>
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Opens a connection only for the tenant the tenant check resolved (FR-013). A login failure,
/// connection failure, or timeout while opening throws <see cref="TenantUnavailableException"/>.
/// </summary>
public sealed class TenantConnectionFactory(RequestContext requestContext) : ITenantConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = requestContext.TenantConnectionString
            ?? throw new InvalidOperationException("No tenant was resolved for this request.");

        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch (Exception exception) when (exception is SqlException or TimeoutException or InvalidOperationException)
        {
            await connection.DisposeAsync();
            throw new TenantUnavailableException(exception);
        }
    }
}
