using System.Text.Json;
using ResortWise.LoanBalance.Api.Audit;

namespace ResortWise.LoanBalance.Api.Errors;

/// <summary>
/// Writes every error response. Each code has one status, one constant message (the contract
/// examples), and a fixed field for input errors. Messages never include exception text.
/// </summary>
public static class ErrorWriter
{
    private sealed record ErrorDefinition(int Status, string Message, string? Field);

    private static readonly Dictionary<string, ErrorDefinition> Definitions = new(StringComparer.Ordinal)
    {
        [ErrorCodes.TenantRequired] = new(StatusCodes.Status400BadRequest, "The X-Tenant-Id header is required.", "X-Tenant-Id"),
        [ErrorCodes.Unauthenticated] = new(StatusCodes.Status401Unauthorized, "A valid caller credential is required.", null),
        [ErrorCodes.TenantForbidden] = new(StatusCodes.Status403Forbidden, "The caller is not allowed to access this tenant.", null),
        [ErrorCodes.LoanIdInvalid] = new(StatusCodes.Status400BadRequest, "The loan ID is not valid.", "loanId"),
        [ErrorCodes.LoanNotFound] = new(StatusCodes.Status404NotFound, "The loan was not found.", null),
        [ErrorCodes.TenantUnavailable] = new(StatusCodes.Status503ServiceUnavailable, "The tenant's data is temporarily unavailable.", null),
        [ErrorCodes.InternalError] = new(StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null),
    };

    public static int StatusFor(string code) => Get(code).Status;

    public static ErrorResponse ResponseFor(string code)
    {
        var definition = Get(code);
        return new ErrorResponse(code, definition.Message, definition.Field);
    }

    public static async Task WriteAsync(HttpContext context, string code)
    {
        var definition = Get(code);

        if (context.RequestServices.GetService<RequestContext>() is { } requestContext)
        {
            requestContext.ErrorCode = code;
        }

        context.Response.StatusCode = definition.Status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(ResponseFor(code)));
    }

    private static ErrorDefinition Get(string code) =>
        Definitions.TryGetValue(code, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(code), "Unknown error code.");
}
