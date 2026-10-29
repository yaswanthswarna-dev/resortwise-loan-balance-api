using Microsoft.Data.SqlClient;

namespace ResortWise.LoanBalance.Api.Errors;

/// <summary>
/// Maps unhandled exceptions to the error contract: connection and login failures to 503
/// TENANT_UNAVAILABLE, anything else to 500 INTERNAL_ERROR. No exception text, SQL, or stack
/// trace reaches the response, and only the exception type is logged.
/// </summary>
public sealed class ExceptionMappingMiddleware(RequestDelegate next, ILogger<ExceptionMappingMiddleware> logger)
{
    // SQL Server error numbers for connection and login failures (network, login, database not
    // available). Errors raised while a query runs, such as permission errors, are not in here.
    private static readonly HashSet<int> ConnectionErrorNumbers =
        [2, 40, 53, 121, 233, 4060, 4064, 10053, 10054, 10060, 10061, 11001, 18452, 18456, 18487, 18488];

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var code = IsTenantUnavailable(exception) ? ErrorCodes.TenantUnavailable : ErrorCodes.InternalError;
            logger.LogError("Request failed with {ExceptionType}; returned {ErrorCode}.", exception.GetType().Name, code);

            context.Response.Clear();
            await ErrorWriter.WriteAsync(context, code);
        }
    }

    private static bool IsTenantUnavailable(Exception exception) => exception switch
    {
        TenantUnavailableException => true,
        SqlException sql => sql.Errors.Cast<SqlError>().Any(error => ConnectionErrorNumbers.Contains(error.Number)),
        _ => false,
    };
}
