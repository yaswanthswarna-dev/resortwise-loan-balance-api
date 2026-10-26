using ResortWise.LoanBalance.Api.Loans.Models;

namespace ResortWise.LoanBalance.Api.Loans;

public interface ILoanBalanceRepository
{
    /// <summary>Reads one loan record from the request's tenant database, or null if it does not exist there.</summary>
    Task<LoanBalanceRow?> GetAsync(int loanId, CancellationToken cancellationToken);
}
