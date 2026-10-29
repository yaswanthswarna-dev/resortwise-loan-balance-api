-- rw.LoanPrincipalBalance, redefined: the O5 cross-loan rule is dropped (2026-10-28). It came
-- from a misread screen value, had no confirmed case, and hid correct values. O5 is now only the
-- payoff flag rule, proposed by Yaswanth on 2026-10-27 and pending Farzin's PR approval (spec O5).
-- V004 is already applied and is not edited; this script replaces its definition without the
-- CrossLoanReversal column, so it no longer reads lrTransaction.
-- One detection flag, set for every loan record (data-model.md):
--   PayOffFlagSet: the record's own payoff flag (lrLoanReceivable.IsPayOff = 1); NULL counts as 0.
-- The flag only detects a condition from SPI tables: no amount column, no aggregate, and no
-- calculation (the T012 scanner enforces this). The API maps it to PAID_OFF_FLAG_SET (spec FR-008a).
-- PrincipalBalance: for an active record (LoanStatusId = 1) with the flag 0, principalRemaining
-- from dbo.lrFnGetBalanceSummary, read-only, rounded to 2 places by the decimal(19,2) cast (half
-- away from zero, as the screen does; research R2), exactly as in V003. Otherwise NULL, and the
-- function is not applied: for a non-active record (O2), and for an active record with the flag
-- set, whose screen value the function cannot reproduce (O5).
-- OUTER APPLY keeps every loan record, so a record never disappears from the view.
CREATE OR ALTER VIEW rw.LoanPrincipalBalance
AS
SELECT
    l.ID AS LoanId,
    CAST(GETDATE() AS date) AS AsOfDate,
    o5.PayOffFlagSet,
    CASE
        WHEN l.LoanStatusId = 1 AND o5.PayOffFlagSet = 0
            THEN CAST(b.principalRemaining AS decimal(19,2))
        ELSE CAST(NULL AS decimal(19,2))
    END AS PrincipalBalance
FROM dbo.lrLoanReceivable AS l
CROSS APPLY
(
    SELECT ISNULL(CAST(CASE WHEN l.IsPayOff = 1 THEN 1 ELSE 0 END AS bit), CAST(0 AS bit)) AS PayOffFlagSet
) AS o5
OUTER APPLY
(
    SELECT f.principalRemaining
    FROM dbo.lrFnGetBalanceSummary(l.ID, GETDATE()) AS f
    WHERE l.LoanStatusId = 1 AND o5.PayOffFlagSet = 0
) AS b;
