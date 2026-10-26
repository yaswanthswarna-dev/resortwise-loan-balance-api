namespace ResortWise.LoanBalance.Api.Loans.Models;

/// <summary>One row from <c>rw.Loan</c> joined with <c>rw.LoanPrincipalBalance</c>.</summary>
public sealed class LoanBalanceRow
{
    public int LoanId { get; init; }

    public int LoanStatusId { get; init; }

    public string? LoanStatusName { get; init; }

    public DateTime AsOfDate { get; init; }

    public decimal? PrincipalBalance { get; init; }
}
