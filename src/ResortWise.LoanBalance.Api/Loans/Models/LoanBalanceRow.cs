namespace ResortWise.LoanBalance.Api.Loans.Models;

/// <summary>One row from <c>rw.Loan</c> joined with <c>rw.LoanPrincipalBalance</c>.</summary>
public sealed class LoanBalanceRow
{
    public int LoanId { get; init; }

    public int LoanStatusId { get; init; }

    public string? LoanStatusName { get; init; }

    public DateTime AsOfDate { get; init; }

    public decimal? PrincipalBalance { get; init; }

    /// <summary>V004: the record's payoff flag is set in SPI (O5).</summary>
    public bool PayOffFlagSet { get; init; }

    /// <summary>V004: the contract has a reversal of a transaction on a different loan record (O5).</summary>
    public bool CrossLoanReversal { get; init; }
}
