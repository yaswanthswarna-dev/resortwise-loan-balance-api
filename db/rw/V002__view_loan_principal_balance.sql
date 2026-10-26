-- rw.LoanPrincipalBalance: one row per SPI loan record, with today's principal balance.
-- Active records (LoanStatusId = 1) take principalRemaining from dbo.lrFnGetBalanceSummary,
-- read-only, rounded to 2 places by the decimal(19,2) cast (half away from zero, as the screen
-- does; research R2). Any other status returns 0.00 and the function is not applied (O2).
-- OUTER APPLY keeps every loan record, so a record never disappears from the view.
CREATE OR ALTER VIEW rw.LoanPrincipalBalance
AS
SELECT
    l.ID AS LoanId,
    CAST(GETDATE() AS date) AS AsOfDate,
    CASE
        WHEN l.LoanStatusId = 1 THEN CAST(b.principalRemaining AS decimal(19,2))
        ELSE CAST(0 AS decimal(19,2))
    END AS PrincipalBalance
FROM dbo.lrLoanReceivable AS l
OUTER APPLY
(
    SELECT f.principalRemaining
    FROM dbo.lrFnGetBalanceSummary(l.ID, GETDATE()) AS f
    WHERE l.LoanStatusId = 1
) AS b;
