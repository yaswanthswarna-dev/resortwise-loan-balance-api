using Dapper;
using ResortWise.LoanBalance.Api.Loans.Models;
using ResortWise.LoanBalance.Api.Tenancy;

namespace ResortWise.LoanBalance.Api.Loans;

/// <summary>
/// Reads through rw views only (Principle III): one parameterized query per request, on the
/// resolved tenant's connection (research R14).
/// </summary>
public sealed class LoanBalanceRepository(ITenantConnectionFactory connections) : ILoanBalanceRepository
{
    private const string Sql = """
        SELECT l.LoanId, l.LoanStatusId, l.LoanStatusName, p.AsOfDate, p.PrincipalBalance
        FROM rw.Loan AS l
        JOIN rw.LoanPrincipalBalance AS p ON p.LoanId = l.LoanId
        WHERE l.LoanId = @LoanId;
        """;

    public async Task<LoanBalanceRow?> GetAsync(int loanId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<LoanBalanceRow>(
            new CommandDefinition(Sql, new { LoanId = loanId }, cancellationToken: cancellationToken));
    }
}
