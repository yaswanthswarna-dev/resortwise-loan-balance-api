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

        // Check order: loan ID format (400) before existence (404); the repository is not called for a bad ID.
        if (!LoanIdParser.TryParse(loanId, out var id))
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

    // Principal availability depends only on status (FR-008a, O2): active records carry the view's
    // principal; any other status has none, and V003 returns NULL for it. A row that breaks this
    // rule is an internal error, so the view and the API never disagree silently.
    private static LoanBalanceSummaryResponse ToResponse(LoanBalanceRow row)
    {
        var status = row.LoanStatusName ?? throw new InvalidOperationException("A loan record has no status name.");
        var asOfDate = DateOnly.FromDateTime(row.AsOfDate);

        if (row.LoanStatusId == ActiveStatusId)
        {
            var principal = row.PrincipalBalance ?? throw new InvalidOperationException("An active loan record has no principal balance.");
            return new(row.LoanId, status, asOfDate, principal, PrincipalBalanceAvailable: true, PrincipalUnavailableReason: null);
        }

        if (row.PrincipalBalance is not null)
        {
            throw new InvalidOperationException("A non-active loan record has a principal balance.");
        }

        return new(row.LoanId, status, asOfDate, PrincipalBalance: null, PrincipalBalanceAvailable: false, PrincipalUnavailableReasons.LoanNotActive);
    }

    // SPI's Active status (LoanStatusId = 1), the same rule as rw.LoanPrincipalBalance.
    private const int ActiveStatusId = 1;
}
