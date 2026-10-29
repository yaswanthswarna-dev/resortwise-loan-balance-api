using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ResortWise.LoanBalance.Api.Errors;
using ResortWise.LoanBalance.Api.Tenancy;

namespace ResortWise.LoanBalance.Api.Auth;

/// <summary>
/// Authenticates <c>Authorization: Bearer &lt;key&gt;</c> by comparing SHA-256 of the key with
/// every caller's hash in fixed time, with no early exit (research R5). A missing or invalid
/// credential gets 401 UNAUTHENTICATED.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TenantRegistry registry)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string CallerIdClaim = "caller_id";

    private const string BearerPrefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!header.StartsWith(BearerPrefix, StringComparison.Ordinal) || header.Length == BearerPrefix.Length)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unsupported credential."));
        }

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(header[BearerPrefix.Length..]));

        string? callerId = null;
        foreach (var caller in registry.Callers)
        {
            // Check every caller, so the time taken does not depend on which caller matched.
            if (CryptographicOperations.FixedTimeEquals(presented, caller.KeyHash))
            {
                callerId = caller.Id;
            }
        }

        if (callerId is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown credential."));
        }

        var identity = new ClaimsIdentity([new Claim(CallerIdClaim, callerId)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ErrorWriter.WriteAsync(Context, ErrorCodes.Unauthenticated);
}
