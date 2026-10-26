-- rw.Loan: one row per SPI loan record, with its status name and contract.
-- Reads SPI tables only (read-only); calls no SPI function or procedure.
-- LEFT JOIN so a loan record is never hidden if its status row is missing.
CREATE OR ALTER VIEW rw.Loan
AS
SELECT
    l.ID AS LoanId,
    l.LoanStatusId,
    s.LoanStatusName,
    l.SalesContractId
FROM dbo.lrLoanReceivable AS l
LEFT JOIN dbo.lrLoanStatus AS s ON s.ID = l.LoanStatusId;
