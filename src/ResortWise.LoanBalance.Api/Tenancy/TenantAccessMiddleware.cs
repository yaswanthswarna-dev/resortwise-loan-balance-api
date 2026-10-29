using Microsoft.AspNetCore.Authentication;
using ResortWise.LoanBalance.Api.Audit;
using ResortWise.LoanBalance.Api.Auth;
using ResortWise.LoanBalance.Api.Errors;

namespace ResortWise.LoanBalance.Api.Tenancy;

/// <summary>
/// Runs after authentication and before any endpoint, in the fixed check order: credential
/// (401), then <c>X-Tenant-Id</c> present (400), then tenant allowed and known (403). An unknown
/// tenant gets the same 403 body as a tenant the caller is not allowed (FR-010, FR-011).
/// </summary>
public sealed class TenantAccessMiddleware(RequestDelegate next, TenantRegistry registry)
{
    public const string TenantHeader = "X-Tenant-Id";

    public async Task InvokeAsync(HttpContext context, RequestContext requestContext)
    {
        var callerId = context.User.FindFirst(ApiKeyAuthenticationHandler.CallerIdClaim)?.Value;
        if (callerId is null)
        {
            await context.ChallengeAsync(ApiKeyAuthenticationHandler.SchemeName);
            return;
        }

        requestContext.CallerId = callerId;

        var tenantId = context.Request.Headers[TenantHeader].ToString();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await ErrorWriter.WriteAsync(context, ErrorCodes.TenantRequired);
            return;
        }

        // Tenant IDs compare ordinally, exactly as sent.
        var caller = registry.FindCaller(callerId);
        if (caller is null
            || !caller.Tenants.Contains(tenantId)
            || !registry.ConnectionStrings.TryGetValue(tenantId, out var connectionString))
        {
            await ErrorWriter.WriteAsync(context, ErrorCodes.TenantForbidden);
            return;
        }

        requestContext.TenantId = tenantId;
        requestContext.TenantConnectionString = connectionString;
        await next(context);
    }
}
