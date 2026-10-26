using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ResortWise.LoanBalance.Api.Audit;
using ResortWise.LoanBalance.Api.Errors;

namespace ResortWise.LoanBalance.UnitTests.Errors;

public sealed class ErrorWriterTests
{
    public static TheoryData<string, int, string, string?> Codes => new()
    {
        { ErrorCodes.TenantRequired, 400, "The X-Tenant-Id header is required.", "X-Tenant-Id" },
        { ErrorCodes.Unauthenticated, 401, "A valid caller credential is required.", null },
        { ErrorCodes.TenantForbidden, 403, "The caller is not allowed to access this tenant.", null },
        { ErrorCodes.LoanIdInvalid, 400, "The loan ID is not valid.", "loanId" },
        { ErrorCodes.LoanNotFound, 404, "The loan was not found.", null },
        { ErrorCodes.TenantUnavailable, 503, "The tenant's data is temporarily unavailable.", null },
        { ErrorCodes.InternalError, 500, "An unexpected error occurred.", null },
    };

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task Each_code_has_its_status_constant_message_and_field(string code, int status, string message, string? field)
    {
        var requestContext = new RequestContext();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(requestContext).BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();

        await ErrorWriter.WriteAsync(context, code);

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        var root = body.RootElement;

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.Equal(message, root.GetProperty("message").GetString());
        Assert.Equal(field, root.TryGetProperty("field", out var value) ? value.GetString() : null);
        Assert.Equal(code, requestContext.ErrorCode);
    }

    [Fact]
    public void Every_code_is_covered()
    {
        Assert.Equal(ErrorCodes.All.Order(), Codes.Select(row => (string)row[0]).Order());
    }
}
