using System.Globalization;
using ResortWise.LoanBalance.Api.Audit;
using ResortWise.LoanBalance.Api.Errors;
using ResortWise.LoanBalance.Api.Loans.Models;

namespace ResortWise.LoanBalance.Api.Loans;

/// <summary>
/// <c>GET /v1/loans/{loanId}/balance-summary</c>. Returns the view's values as they are: no
/// recomputation or rounding here (FR-003).
/// </summary>
public static class BalanceSummaryEndpoint
{
    public const string Route = "/v1/loans/{loanId}/balance-summary";

    public static IEndpointRouteBuilder MapBalanceSummary(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, HandleAsync);
        return endpoints;
    }

    private static async Task HandleAsync(
        string loanId,
        HttpContext context,
        RequestContext requestContext,
        ILoanBalanceRepository repository)
    {
        requestContext.LoanId = loanId;

        if (!int.TryParse(loanId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < 1)
        {
            await ErrorWriter.WriteAsync(context, ErrorCodes.LoanIdInvalid);
            return;
        }

        var row = await repository.GetAsync(id, context.RequestAborted);
        if (row is null)
        {
            await ErrorWriter.WriteAsync(context, ErrorCodes.LoanNotFound);
            return;
        }

        await context.Response.WriteAsJsonAsync(ToResponse(row), context.RequestAborted);
    }

    private static LoanBalanceSummaryResponse ToResponse(LoanBalanceRow row) => new(
        row.LoanId,
        row.LoanStatusName ?? throw new InvalidOperationException("A loan record has no status name."),
        DateOnly.FromDateTime(row.AsOfDate),
        row.PrincipalBalance ?? throw new InvalidOperationException("A loan record has no principal balance."));
}
