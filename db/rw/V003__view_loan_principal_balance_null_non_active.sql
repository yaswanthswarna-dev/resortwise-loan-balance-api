-- rw.LoanPrincipalBalance, redefined (O2, option B, decided 2026-10-26). V002 is already applied
-- and is not edited; this script replaces its definition.
-- Active records (LoanStatusId = 1) take principalRemaining from dbo.lrFnGetBalanceSummary,
-- read-only, rounded to 2 places by the decimal(19,2) cast (half away from zero, as the screen
-- does; research R2). Any other status returns NULL and the function is not applied: the screen
-- shows such a record's own principal, which only the Excel engine calculates, so the API reports
-- it as not available (FR-008a).
-- OUTER APPLY keeps every loan record, so a record never disappears from the view.
CREATE OR ALTER VIEW rw.LoanPrincipalBalance
AS
SELECT
    l.ID AS LoanId,
    CAST(GETDATE() AS date) AS AsOfDate,
    CASE
        WHEN l.LoanStatusId = 1 THEN CAST(b.principalRemaining AS decimal(19,2))
        ELSE CAST(NULL AS decimal(19,2))
    END AS PrincipalBalance
FROM dbo.lrLoanReceivable AS l
OUTER APPLY
(
    SELECT f.principalRemaining
    FROM dbo.lrFnGetBalanceSummary(l.ID, GETDATE()) AS f
    WHERE l.LoanStatusId = 1
) AS b;
