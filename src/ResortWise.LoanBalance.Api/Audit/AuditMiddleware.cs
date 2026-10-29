using ResortWise.LoanBalance.Api.Auth;
using ResortWise.LoanBalance.Api.Tenancy;

namespace ResortWise.LoanBalance.Api.Audit;

/// <summary>
/// Outermost middleware. Returns a per-request correlation ID in <c>X-Correlation-Id</c> and
/// writes exactly one audit entry per request, from a <c>finally</c> block (FR-019, FR-020).
/// Never logs bodies, headers other than the tenant ID as sent, amounts, or connection details.
/// </summary>
public sealed class AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const int MaxEchoedLength = 128;

    public async Task InvokeAsync(HttpContext context, RequestContext requestContext)
    {
        context.Response.Headers[CorrelationHeader] = requestContext.CorrelationId;
        var status = StatusCodes.Status500InternalServerError;

        try
        {
            await next(context);
            status = context.Response.StatusCode;
        }
        finally
        {
            logger.LogInformation(
                "audit {timestamp} {callerId} {tenantId} {loanId} {status} {errorCode} {correlationId}",
                DateTimeOffset.UtcNow.ToString("O"),
                requestContext.CallerId ?? context.User.FindFirst(ApiKeyAuthenticationHandler.CallerIdClaim)?.Value,
                Cap(context.Request.Headers[TenantAccessMiddleware.TenantHeader].ToString()),
                Cap(requestContext.LoanId ?? context.Request.RouteValues["loanId"]?.ToString()),
                status,
                requestContext.ErrorCode,
                requestContext.CorrelationId);
        }
    }

    private static string? Cap(string? value) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= MaxEchoedLength ? value : value[..MaxEchoedLength];
}
