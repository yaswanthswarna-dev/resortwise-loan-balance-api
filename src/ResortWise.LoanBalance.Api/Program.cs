using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ResortWise.LoanBalance.Api.Audit;
using ResortWise.LoanBalance.Api.Auth;
using ResortWise.LoanBalance.Api.Errors;
using ResortWise.LoanBalance.Api.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// Configuration: committed appsettings.json (placeholders only), then the optional git-ignored
// appsettings.Local.json, then environment variables, which win. Tests run as "Testing" and
// never read a developer's local file.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
}

builder.Configuration.AddEnvironmentVariables();

builder.Services.AddOptions<RegistryOptions>()
    .Bind(builder.Configuration)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<RegistryOptions>, TenantRegistryValidator>();
builder.Services.AddSingleton<TenantRegistry>();

builder.Services.AddScoped<RequestContext>();
builder.Services.AddScoped<ITenantConnectionFactory, TenantConnectionFactory>();

builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

var app = builder.Build();

// Fixed order: audit, exception mapping, authentication, tenant access, endpoints.
app.UseMiddleware<AuditMiddleware>();
app.UseMiddleware<ExceptionMappingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TenantAccessMiddleware>();

// The balance summary is the only endpoint. The OpenAPI contract is not served (research R11).

app.Run();

public partial class Program;
