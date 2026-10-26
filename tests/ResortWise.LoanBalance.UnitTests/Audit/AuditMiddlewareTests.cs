using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ResortWise.LoanBalance.Api.Audit;
using ResortWise.LoanBalance.Api.Errors;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Audit;

public sealed class AuditMiddlewareTests
{
    private const string Amount = "98765.43";
    private const string ConnectionString = "Data Source=audit-test.invalid;Password=not-a-real-password";

    private static readonly string[] Fields = ["timestamp", "callerId", "tenantId", "loanId", "status", "errorCode", "correlationId"];

    public static TheoryData<int, string?> Outcomes => new()
    {
        { 200, null },
        { 400, ErrorCodes.TenantRequired },
        { 401, ErrorCodes.Unauthenticated },
        { 403, ErrorCodes.TenantForbidden },
        { 404, ErrorCodes.LoanNotFound },
        { 503, ErrorCodes.TenantUnavailable },
        { 500, ErrorCodes.InternalError },
    };

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task Writes_exactly_one_entry_with_every_field(int status, string? errorCode)
    {
        var (logs, context, requestContext, key) = Setup();

        await Run(logs, context, requestContext, async ctx =>
        {
            requestContext.CallerId = "caller-1";
            requestContext.LoanId = "42";
            if (errorCode is null)
            {
                ctx.Response.StatusCode = status;
                await ctx.Response.WriteAsync($$"""{"principalBalance":{{Amount}}}""");
            }
            else
            {
                await ErrorWriter.WriteAsync(ctx, errorCode);
            }
        });

        var entry = Assert.Single(logs.AuditEntries);
        Assert.All(Fields, field => Assert.True(entry.State.ContainsKey(field), $"missing {field}"));
        Assert.Equal(status, entry.State["status"]);
        Assert.Equal(errorCode, entry.State["errorCode"]);
        Assert.Equal("caller-1", entry.State["callerId"]);
        Assert.Equal("tenant-a", entry.State["tenantId"]);
        Assert.Equal("42", entry.State["loanId"]);
        Assert.Equal(requestContext.CorrelationId, entry.State["correlationId"]);
        Assert.Equal(requestContext.CorrelationId, context.Response.Headers[AuditMiddleware.CorrelationHeader].ToString());
        AssertNoSecrets(entry, key);
    }

    [Fact]
    public async Task Unhandled_exception_still_writes_one_entry_with_status_500()
    {
        var (logs, context, requestContext, key) = Setup();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Run(logs, context, requestContext, _ => throw new InvalidOperationException($"boom {Amount} {ConnectionString}")));

        var entry = Assert.Single(logs.AuditEntries);
        Assert.Equal(500, entry.State["status"]);
        AssertNoSecrets(entry, key);
    }

    [Fact]
    public async Task Long_tenant_and_loan_ids_are_capped()
    {
        var (logs, context, requestContext, _) = Setup();
        context.Request.Headers["X-Tenant-Id"] = new string('t', 500);

        await Run(logs, context, requestContext, _ =>
        {
            requestContext.LoanId = new string('9', 500);
            return Task.CompletedTask;
        });

        var entry = Assert.Single(logs.AuditEntries);
        Assert.Equal(AuditMiddleware.MaxEchoedLength, ((string)entry.State["tenantId"]!).Length);
        Assert.Equal(AuditMiddleware.MaxEchoedLength, ((string)entry.State["loanId"]!).Length);
    }

    private static (CapturingLoggerProvider Logs, DefaultHttpContext Context, RequestContext RequestContext, string Key) Setup()
    {
        var requestContext = new RequestContext { TenantConnectionString = ConnectionString };
        var key = TestKeys.Create().Key;
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(requestContext).BuildServiceProvider(),
        };
        context.Request.Headers.Authorization = $"Bearer {key}";
        context.Request.Headers["X-Tenant-Id"] = "tenant-a";
        context.Response.Body = new MemoryStream();
        return (new CapturingLoggerProvider(), context, requestContext, key);
    }

    private static Task Run(CapturingLoggerProvider logs, HttpContext context, RequestContext requestContext, RequestDelegate next) =>
        new AuditMiddleware(next, new CapturingLogger<AuditMiddleware>(logs)).InvokeAsync(context, requestContext);

    private static void AssertNoSecrets(CapturingLoggerProvider.Entry entry, string key)
    {
        var rendered = CapturingLoggerProvider.Render(entry);
        Assert.DoesNotContain(Amount, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(key, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Data Source", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", rendered, StringComparison.OrdinalIgnoreCase);
    }
}
