-- B001: make tenant B differ from tenant A for the tenant isolation tests (T043).
-- Run by hand against AlphaPMS_ResortWise_B only (quickstart step 2), as a single batch with
-- sqlcmd -b. Never run it against AlphaPMS_ResortWise or a client database.
-- Rows are selected by rule on the loan record ID. The script lists no loan IDs or values.
--   Changed: every active loan record with ID % 10 = 3 gets the non-active status already in
--            use in this database (lowest non-active status ID), in one UPDATE. Tenant B then
--            returns that status and principal 0.00 for these records.
--   Removed: exactly one loan record, the lowest ID with ID % 10 = 7, to prove a 404 for tenant
--            B. Its lrTransaction rows (the only foreign key) are deleted first. Orphan rows in
--            backup, report, and conversion tables without a foreign key are accepted.
-- Idempotent: if no active record with ID % 10 = 3 remains, the script was already applied and
-- changes nothing. Both changes are in one transaction, so they are applied together or not at
-- all. Data changes only; no SPI object is created or changed.
IF DB_NAME() <> N'AlphaPMS_ResortWise_B' THROW 50001, 'Wrong database', 1;

SET XACT_ABORT ON;
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.lrLoanReceivable WHERE ID % 10 = 3 AND LoanStatusId = 1)
BEGIN
    SELECT 'Already applied; nothing changed.' AS Result;
    RETURN;
END;

DECLARE @NonActiveStatusId int =
    (SELECT MIN(LoanStatusId) FROM dbo.lrLoanReceivable WHERE LoanStatusId <> 1);
IF @NonActiveStatusId IS NULL THROW 50002, 'No non-active loan status is in use', 1;

DECLARE @RemovedLoanId int = (SELECT MIN(ID) FROM dbo.lrLoanReceivable WHERE ID % 10 = 7);
IF @RemovedLoanId IS NULL THROW 50003, 'No loan record matches the removal rule', 1;

DECLARE @StatusChanged int, @TransactionsRemoved int, @LoansRemoved int;

BEGIN TRANSACTION;

UPDATE dbo.lrLoanReceivable
SET LoanStatusId = @NonActiveStatusId
WHERE ID % 10 = 3 AND LoanStatusId = 1;
SET @StatusChanged = @@ROWCOUNT;

DELETE FROM dbo.lrTransaction
WHERE LoanId = @RemovedLoanId;
SET @TransactionsRemoved = @@ROWCOUNT;

DELETE FROM dbo.lrLoanReceivable
WHERE ID = @RemovedLoanId;
SET @LoansRemoved = @@ROWCOUNT;

-- XACT_ABORT rolls everything back if this fires.
IF @LoansRemoved <> 1 THROW 50004, 'Expected to remove exactly one loan record', 1;

COMMIT TRANSACTION;

-- Counts only.
SELECT
    @StatusChanged AS LoansWithStatusChanged,
    @TransactionsRemoved AS TransactionRowsRemoved,
    @LoansRemoved AS LoansRemoved;
