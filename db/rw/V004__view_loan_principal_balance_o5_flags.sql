-- rw.LoanPrincipalBalance, redefined (O5, option B, decided by Farzin 2026-10-27). V003 is
-- already applied and is not edited; this script replaces its definition.
-- Two detection flags, set for every loan record (data-model.md):
--   PayOffFlagSet: the record's own payoff flag (lrLoanReceivable.IsPayOff = 1); NULL counts as 0.
--   CrossLoanReversal: a reversal (TransTypeid = 1, REV) on any loan record of the record's
--   contract whose REVApplyID points to a transaction on a different loan record of the same
--   contract.
-- The flags only detect these conditions from SPI tables: no amount column, no aggregate, and no
-- calculation (the T012 scanner enforces this). The API maps them to PAID_OFF_FLAG_SET and
-- CROSS_LOAN_REVERSAL (spec FR-008a).
-- PrincipalBalance: for an active record (LoanStatusId = 1) with both flags 0, principalRemaining
-- from dbo.lrFnGetBalanceSummary, read-only, rounded to 2 places by the decimal(19,2) cast (half
-- away from zero, as the screen does; research R2), exactly as in V003. Otherwise NULL, and the
-- function is not applied: for a non-active record (O2), and for an active record with either flag,
-- whose screen value the function cannot reproduce (O5).
-- OUTER APPLY keeps every loan record, so a record never disappears from the view.
CREATE OR ALTER VIEW rw.LoanPrincipalBalance
AS
SELECT
    l.ID AS LoanId,
    CAST(GETDATE() AS date) AS AsOfDate,
    o5.PayOffFlagSet,
    o5.CrossLoanReversal,
    CASE
        WHEN l.LoanStatusId = 1 AND o5.PayOffFlagSet = 0 AND o5.CrossLoanReversal = 0
            THEN CAST(b.principalRemaining AS decimal(19,2))
        ELSE CAST(NULL AS decimal(19,2))
    END AS PrincipalBalance
FROM dbo.lrLoanReceivable AS l
CROSS APPLY
(
    SELECT
        ISNULL(CAST(CASE WHEN l.IsPayOff = 1 THEN 1 ELSE 0 END AS bit), CAST(0 AS bit)) AS PayOffFlagSet,
        ISNULL(CAST(CASE WHEN EXISTS
        (
            SELECT 1
            FROM dbo.lrTransaction AS rv
            JOIN dbo.lrLoanReceivable AS rvl ON rvl.ID = rv.LoanId
            JOIN dbo.lrTransaction AS p ON p.ID = rv.REVApplyID
            JOIN dbo.lrLoanReceivable AS pl ON pl.ID = p.LoanId
            WHERE rv.TransTypeid = 1
              AND rvl.SalesContractId = l.SalesContractId
              AND pl.SalesContractId = l.SalesContractId
              AND p.LoanId <> rv.LoanId
        ) THEN 1 ELSE 0 END AS bit), CAST(0 AS bit)) AS CrossLoanReversal
) AS o5
OUTER APPLY
(
    SELECT f.principalRemaining
    FROM dbo.lrFnGetBalanceSummary(l.ID, GETDATE()) AS f
    WHERE l.LoanStatusId = 1 AND o5.PayOffFlagSet = 0 AND o5.CrossLoanReversal = 0
) AS b;
