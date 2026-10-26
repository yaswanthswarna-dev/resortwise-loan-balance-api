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

    public FakeLoanBalanceRepository Add(int loanId, string status, decimal principal, DateTime? asOf = null)
    {
        rows[loanId] = new LoanBalanceRow
        {
            LoanId = loanId,
            LoanStatusName = status,
            AsOfDate = asOf ?? new DateTime(2026, 10, 22),
            PrincipalBalance = principal,
        };
        return this;
    }

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
