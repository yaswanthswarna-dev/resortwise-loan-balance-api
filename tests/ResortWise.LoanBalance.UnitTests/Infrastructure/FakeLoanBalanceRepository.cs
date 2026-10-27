using ResortWise.LoanBalance.Api.Loans;
using ResortWise.LoanBalance.Api.Loans.Models;

namespace ResortWise.LoanBalance.UnitTests.Infrastructure;

/// <summary>An in-memory repository with synthetic rows that counts how often it was asked.</summary>
public sealed class FakeLoanBalanceRepository : ILoanBalanceRepository
{
    private readonly Dictionary<int, LoanBalanceRow> rows = [];
    private int calls;

    public int Calls => calls;

    public Exception? Throw { get; set; }

    /// <summary>An active record (status ID 1), as <c>rw.Loan</c> and V003 return it.</summary>
    public FakeLoanBalanceRepository Add(int loanId, string status, decimal principal, DateTime? asOf = null) =>
        AddRow(loanId, ActiveStatusId, status, principal, asOf);

    /// <summary>A non-active record: V003 returns a NULL principal for it (O2).</summary>
    public FakeLoanBalanceRepository AddNonActive(int loanId, string status) =>
        AddRow(loanId, NonActiveStatusId, status, null);

    /// <summary>Any row, including ones the view should never return, to test the endpoint's guards.</summary>
    public FakeLoanBalanceRepository AddRow(
        int loanId,
        int statusId,
        string status,
        decimal? principal,
        DateTime? asOf = null,
        bool payOffFlagSet = false,
        bool crossLoanReversal = false)
    {
        rows[loanId] = new LoanBalanceRow
        {
            LoanId = loanId,
            LoanStatusId = statusId,
            LoanStatusName = status,
            AsOfDate = asOf ?? new DateTime(2026, 10, 22),
            PrincipalBalance = principal,
            PayOffFlagSet = payOffFlagSet,
            CrossLoanReversal = crossLoanReversal,
        };
        return this;
    }

    public const int ActiveStatusId = 1;

    // Synthetic: any status ID other than 1 is non-active.
    public const int NonActiveStatusId = 2;

    public Task<LoanBalanceRow?> GetAsync(int loanId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        if (Throw is not null)
        {
            throw Throw;
        }

        return Task.FromResult(rows.GetValueOrDefault(loanId));
    }
}
