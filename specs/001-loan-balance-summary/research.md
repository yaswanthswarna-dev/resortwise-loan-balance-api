# Research: Loan Balance Summary API

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-23

SPI objects are named here, but no SPI source code, formulas, workbook cell references, or form
file paths are recorded. These details stay in a local, uncommitted analysis (Principle V).

## R1. Where each returned value comes from

**Finding**: Loan ID and loan status come straight from SPI tables. Principal Remaining has an
SQL source: `lrGetTransactionActivity` (second result set) → `dbo.lrFnGetBalanceSummary(@LoanId,
today)` → `dbo.lrGetPrincipalRemaining(@SalesContractId, end of today)`. The value the Balance
Summary tab finally shows comes from the Excel engine (`SPILoanExcelEngine.GetLoanSummary`),
which overwrites the SQL value (open point O1 in the plan). The as-of date is the SQL Server's
current date (spec Assumptions). The per-field table is in [plan.md](plan.md#field-sources).

**Consequence**: Every field in this scope has a database source, so FR-003 ("MUST NOT
recompute") and FR-021 (parity from SPI procedures) hold without change. The fields the engine
calculates are out of scope (spec Out of Scope).

## R2. Principal balance data access

- **Decision**: `rw.LoanPrincipalBalance` selects from `dbo.lrFnGetBalanceSummary` read-only
  (former option P-A, now allowed by constitution v1.4.0, Principle III). The view applies the
  per-loan-record rule in [plan.md](plan.md#principal-balance-and-the-per-loan-record-rule):
  active records take the function's value; non-active records, including old rewritten records,
  return `NULL` without calling the function, and the API reports the principal as not available
  (reason `LOAN_NOT_ACTIVE`). This is defined by `V003` (O2, decided 2026-10-26); `V002`, already
  applied, returned `0.00` and is not edited.
- **Per-loan principal (checked 2026-10-26, O2)**: the screen shows an old rewritten record's own
  principal, which the SPI Excel loan engine calculates per loan record. No SPI function returns
  it: a read-only search of SPI function names and parameter types, and of the definitions of the
  two candidates that take a loan ID (`lrLoanToDatePrincipal`, `lrGetToDatePrincipal`), found
  that both resolve the loan to its contract and work per contract, not per loan record.
  Rebuilding the value from SPI tables would copy engine logic, so it
  waits for the engine feature.
- **Active records with SPI data issues (investigated 2026-10-27, O5)**: two active loans from the
  T069 screen check show principal `0.00` on the screen, while `dbo.lrFnGetBalanceSummary`
  returns a balance. All their transactions are posted. The function's principal comes from
  `dbo.lrGetPrincipalRemaining`, which works per contract and does not look at the transaction
  type, the payoff flag, or the payments remaining (its logic stays in the local analysis).
  `lrTransactionType` holds only three types: `1` REV (Payment Reversal), `4` PMT (Regular
  Payment), and `5` ADJ (Payment Adjustment); there is no payoff transaction type, and the only
  payoff marker is `lrLoanReceivable.IsPayOff` (`bit`). The two patterns found:
  - *Payoff flag*: the active record has `IsPayOff = 1`, but its posted payments do not pay off
    the principal, so the function still returns a balance.
  - *Cross-loan reversal*: after a rewrite, the new record's loan amount already excludes a
    payment made on the earlier record, and a reversal (`TransTypeid = 1`) posted on the new
    record has a `REVApplyID` pointing to that payment on the earlier record. The function
    counts the reversal against the contract, so it returns a balance that the screen does not
    show.

  Read-only counts found further active loans in `AlphaPMS_ResortWise` matching each pattern,
  and none matching both. Loan IDs, values, and counts are kept privately. **Option B**. O5 proposed by Yaswanth on 2026-10-27. Farzin agreed with the approach on 2026-10-27, pending his PR approval, on two conditions: both rules go into the engine feature's scope, and the per-pattern counts were sent to him privately.
  `V004` adds two detection flags to `rw.LoanPrincipalBalance` and
  returns `NULL` principal when either is set (data-model.md). The flags use only IDs, the
  contract ID, the transaction type, `REVApplyID`, and `IsPayOff`; no amount column is read.
  The reversal check does not filter on `Posted`, matching the counts; in the loans checked,
  every transaction is posted. **Reversal target (decided 2026-10-27, analyze A1)**: the check
  matches a reversal of **any** transaction on a different loan record of the same contract, not
  only a regular payment (`TransTypeid = 4`). Flagging more loans as not available is never wrong:
  it can only withhold a value, never return a wrong one. In the loans checked, the reversed
  transaction was a regular payment. *Alternatives considered*: returning the function's value
  (contradicts the screen); calculating a corrected value from SPI tables (copies engine logic,
  which the constitution exists to avoid).
- **Rationale**: Reuses SPI's own logic instead of rebuilding it, so the value follows future SPI
  changes to the function. The rule is needed because the function works per contract, while the
  API works per loan record (FR-008a).
- **Alternatives considered**: P-B, reimplementing the function from SPI tables. Dropped: it
  copies SPI logic, which the amended constitution exists to avoid. `dbo.lrGetPrincipalRemaining`
  called directly: it takes the contract, not the loan, so the view would need to resolve the
  contract itself. Calling `dbo.lrFnGetBalanceSummary` with the loan ID matches the screen's
  path exactly.
- **Confirmed locally (T007, 2026-10-23)**: `dbo.lrFnGetBalanceSummary` is a table-valued
  function with parameters `@Loanid int` and `@curdate datetime`. Its principal output column is
  `principalRemaining`, type `money` (precision 19, **scale 4**). Because the scale is greater
  than 2, the view rounds to 2 places as the screen does: `CAST(... AS decimal(19,2))`, which
  rounds half away from zero like the SPI Application's .NET money formatting (decided
  2026-10-23). A local count found 1 active loan in `AlphaPMS_ResortWise` whose principal has
  non-zero third or fourth decimals; the parity test confirms the rule. No contract has more than
  one active loan record (count 0).

## R3. Excel-engine values

Out of scope (spec Out of Scope). There is no engine calculator and there are no engine input
views. D2–D7 and D9 move to the engine feature, which starts from the local analysis.

## R4. Parity references for tests (FR-021)

Tests call these read-only against `AlphaPMS_ResortWise` only. All of them are SELECT-only
according to the screen analysis. `lrGetNonReversedPayments` (batch path, writes a session temp
table) is **not** used.

| Field | Reference |
| --- | --- |
| Active loan list | Read-only `SELECT` of loan IDs with `LoanStatusId = 1` (test code only) |
| `loanStatus` | `lrGetLoanDetails` (`@Loanid int`, `@SalesContractId int`) for the status ID; `lrGetLoanStatusTypes` (no parameters) for the name |
| `principalBalance` (active loans matching an O5 rule) | No value is compared. The test finds these loans with its own read-only `SELECT` on `lrLoanReceivable` and `lrTransaction` through `RW_REF_A`, written independently of `V004` (same rules, R2), and checks that the API returns principal `null`, `principalBalanceAvailable` `false`, and reason `PAID_OFF_FLAG_SET` (payoff flag set) or else `CROSS_LOAN_REVERSAL` (cross-loan reversal on the contract) for every one of them (O5) |
| `principalBalance` (active loans, principal available) | **`dbo.lrFnGetBalanceSummary(@Loanid, GETDATE())`, column `principalRemaining`** (`money`, scale 4), selected read-only on the reference login for every active loan that matches neither O5 rule (O4, revised by Farzin 2026-10-27; O5). The test rounds it itself to 2 places, half away from zero (`Math.Round(value, 2, MidpointRounding.AwayFromZero)`), as the screen does (R2), and requires that the function returns exactly one row with a non-null value and that the API returns a non-null number equal to it. This catches rounding, field-mapping, and null errors in `rw.LoanPrincipalBalance` and the API across all active loans. It is **not independent** of the view, which calls the same function, and it does **not** prove the value the screen shows, which the Excel engine calculates; that rests on the manual screen check of about 20 active loans (plan, Complexity Tracking). The screen's procedure `lrGetTransactionActivity` (`@Ownerid char(10)`, `@SalesContractId int`; second result set, `PrincipalRemaining`) is not called, because it reaches the database `AlphaSales` at run time and the reference login is not given access to it. `lrGetPrincipalRemaining` is not called either |
| `principalBalance` (non-active loans) | Not in the SC-001 parity scope. A database test checks the API returns principal `null`, `principalBalanceAvailable` `false`, and reason `LOAN_NOT_ACTIVE` for every non-active record (O2). The screen check for old rewritten records was manual (T038, done 2026-10-26, details kept privately). Paid-off and cancelled records get the same rule; the screen likely shows `0.00` for them, which is not verified here (those statuses are not in use in `AlphaPMS_ResortWise`), and the engine feature must cover them (Farzin, 2026-10-27) |
| `asOfDate` | `CAST(GETDATE() AS date)` on the reference connection, read before and after the request; the response must equal one of them (midnight-safe) |

**Connections**: every reference call above uses the test-only **reference login**, never the
API login (R9).

**Approved SPI calls in test code**: only `lrGetLoanDetails`, `lrGetLoanStatusTypes`, and
`lrGetTransactionActivity` (procedures); `lrFnGetBalanceSummary` (function, read-only `SELECT`),
only in the active-loan parity test (`ActiveLoanParityTests.cs`, through `RW_REF_A`) and in the
API login's permission check (`ApiLoginPermissionTests.cs`, which proves `rw_api` cannot call it);
plus read-only `SELECT`s on SPI tables (`lrLoanReceivable`, `lrLoanStatus`, and, since O5,
`lrTransaction`, read by the active-loan parity test to find the O5 loans). `lrFnGetBalanceSummary` was added on 2026-10-27 (O4
revised). `lrGetTransactionActivity` stays approved but is not called, because it reaches
`AlphaSales`. A unit test (T059) scans the test code and fails if it calls any other SPI
procedure or function, or names `lrFnGetBalanceSummary` in any other file (R9).

**Comparison rules**: exact `decimal` equality at 2 decimal places; strings exact, ordinal. A
failure message names the loan ID and field. The results file goes to git-ignored `TestResults/`.
The run summary prints counts only.

## R5. Caller credential

- **Decision**: One API key per caller, sent as `Authorization: Bearer <key>`. A key is at least
  32 random bytes, base64url encoded. Configuration stores only `SHA-256(key)` as hex, never the
  key itself. A custom `AuthenticationHandler` hashes the presented key and compares it with
  `CryptographicOperations.FixedTimeEquals` against every caller's hash.
- **Rationale**: Simple, standard, and cloud-agnostic. Keys are high-entropy random values, so an
  unsalted fast hash is enough (no password-style stretching needed), and a leaked config does not
  reveal usable keys.
- **Alternatives considered**: OAuth2/JWT from an identity provider (needs a vendor or extra
  service); mTLS (heavy for screens); PBKDF2 per key (unneeded for random keys and slow per
  request).
- **Key issuance**: a small documented command (`quickstart.md`) generates a key and its hash. The
  key goes to the caller; the hash goes to local config or an environment variable.

## R6. Tenant registry and configuration

- **Decision**: Bind from `IConfiguration` (environment variables, then optional git-ignored
  `appsettings.Local.json`; the committed `appsettings.json` has placeholders only):
  - `Callers:{n}:Id`, `Callers:{n}:KeyHash`, `Callers:{n}:Tenants:{m}`
  - `Tenants:{tenantId}:ConnectionStringName`
  - `ConnectionStrings:{name}`
- Validated at startup (`ValidateOnStart`): every caller tenant exists in `Tenants`; every
  `ConnectionStringName` resolves; hashes are 64 hex characters; no duplicate hashes. The service
  refuses to start on invalid config, and the startup error message never includes values.
- Tenant IDs compare ordinally (case-sensitive), exactly as sent, after the whitespace check.
- **Alternatives considered**: a registry table in a central database (an extra dependency for no
  gain at this scale).

## R7. Request pipeline and check order

Fixed order (spec Edge Cases): credential (401) → `X-Tenant-Id` present (400) → tenant allowed or
known (403) → loan ID format (400) → loan exists (404). The audit middleware wraps everything, so
every outcome produces exactly one entry. An exception handler maps unexpected errors to
`INTERNAL_ERROR`. `SqlException` connection or login failures, and timeouts opening the
connection, map to `TENANT_UNAVAILABLE`. Responses use the contract's `Error` schema, not
ProblemDetails.

## R8. Loan ID validation

Validate the raw route string with `^[1-9][0-9]*$`, then range-check against the SPI loan ID
column type. **Confirmed (T007)**: the loan record ID is `lrLoanReceivable.ID`, type `int`, so
the maximum is 2147483647 and the contract gets `maximum: 2147483647` (T052). That is allowed
before release. The `rw.Loan` view exposes the column as `LoanId`.

## R9. `rw` scripts and database permissions

- **Bootstrap script** `db/rw/bootstrap/create_schema_and_journal.sql`, committed and
  reviewed like every other `rw` script (Principle III: every `rw` object is created by a
  versioned script in this repository). It has two batches: a plain
  `CREATE SCHEMA rw AUTHORIZATION dbo`, then `CREATE TABLE rw.SchemaVersions` with the columns
  DbUp's SQL Server journal expects (checked against the DbUp version used). It has no
  existence checks. The migrator decides whether to run it: if schema `rw` does not exist, it
  runs the bootstrap script once; if schema `rw` exists but `rw.SchemaVersions` does not, it stops
  with an error and changes nothing.
- **Versioned scripts** `db/rw/V###__name.sql`, applied in order by the DbUp console tool after
  the bootstrap. DbUp journals to the existing `rw.SchemaVersions` and never creates it. Views use
  `CREATE OR ALTER VIEW rw.…`.
- `CREATE SCHEMA rw AUTHORIZATION dbo` lets ownership chaining give the API login access through
  `rw` views **without any permission on SPI objects**. Ownership chaining also covers the view's
  call to `dbo.lrFnGetBalanceSummary` (both owned by `dbo`), so the API login needs no permission
  on the SPI function either. This holds only if every SPI object the views reach is owned by
  `dbo`; T007 (g) confirms the owners (names only) before the views are written.
- **Logins per test database** (created locally, quickstart step 1; never committed, no
  passwords in the repository):
  - **API login** (`rw_api`): `SELECT ON SCHEMA::rw` only, plus `DENY SELECT, EXECUTE ON
    SCHEMA::dbo` so it inherits nothing from `public` or `guest` grants in the restored database
    (ownership chaining through `rw` views is not affected). The API and every API request in the
    database tests use it. A database test proves it cannot select from an SPI table, execute an
    SPI procedure, or call an SPI function directly, and that it can read the `rw` views. This
    enforces Principle III in the database, not only by convention.
  - **Reference login** (`rw_parity_ref`), test-only: `db_datareader` and `db_denydatawriter`, plus
    `EXECUTE` on the read-only SPI procedures listed in R4 and nothing else. Parity and isolation
    tests use it to read reference values. `db_datareader` grants `SELECT` at database level,
    which is expected to cover selecting from the table-valued function
    `dbo.lrFnGetBalanceSummary` (R4, O4 revised); T066 confirms this locally before T067, and if
    it does not, quickstart step 1 adds only `GRANT SELECT ON dbo.lrFnGetBalanceSummary TO
    rw_parity_ref` (a test-only grant of the kind O3 allows). Because a procedure's own writes would bypass
    `db_denydatawriter` through ownership chaining, `EXECUTE` is granted only on procedures that
    the screen analysis shows are SELECT-only.
  - Applying `rw` scripts and running the tenant B setup script use an owner-level SQL login on
    the (remote) test server, supplied only through the `SQLCMD*` environment variables (never
    Windows authentication), not either login above (quickstart steps 1 and 2).
- A unit test (GitHub) scans `db/rw`, including `db/rw/bootstrap`, and fails if any
  `CREATE`/`ALTER`/`DROP`/`INSERT`/`UPDATE`/`DELETE`/`MERGE` targets anything other than schema
  `rw`, if any script contains `EXEC`, if a view references an SPI function not on an allowlist,
  or if the bootstrap script creates anything other than schema `rw` and table
  `rw.SchemaVersions`. The allowlist holds only `dbo.lrFnGetBalanceSummary`; adding a function to
  it is a reviewed change. Since O5 (2026-10-27), the scanner also fails if any `rw` script names
  an SPI amount column of `lrLoanReceivable` or `lrTransaction` (a fixed list of the `money`
  columns, by name only) or uses an aggregate (`SUM`, `AVG`), so the views can detect conditions
  but never calculate an amount. The function's `principalRemaining` output is not on the list.
- **Confirmed (T007)**: the loan status type table is `dbo.lrLoanStatus` (`ID int`,
  `LoanStatusName varchar(25)`, nullable), joined from `lrLoanReceivable.LoanStatusId` (`int`, not
  null) by a foreign key. The contract column is `lrLoanReceivable.SalesContractId` (`int`, not
  null). Every SPI object the views reach is owned by `dbo`: `lrLoanReceivable`, `lrLoanStatus`,
  `lrFnGetBalanceSummary`, and the objects it references (`lrGetPrincipalRemaining`,
  `GetFinanceCharge`, `lrTransaction`, `lrFrequency`), so ownership chaining holds.
- **Inherited `public` grants (T007 f, decided 2026-10-23)**: in the restored
  `AlphaPMS_ResortWise`, `public`/`guest` hold `EXECUTE` on 52 SPI procedures outside the approved
  R4 list (object level; 0 schema- or database-level `EXECUTE` grants). `rw_api` keeps its
  explicit `DENY SELECT, EXECUTE ON SCHEMA::dbo`. For `rw_parity_ref`, no per-object `DENY`s are
  added on SPI objects; its inherited rights are accepted. Instead, a unit test scans the test
  code and fails if it calls any SPI procedure or function outside the approved R4 list, so tests
  never call a procedure that could write.

## R10. Audit logging

`Microsoft.Extensions.Logging` with the JSON console formatter writes to stdout. One entry per
request from a middleware's `finally` block, with fields: `timestamp`, `callerId` (null if
unauthenticated), `tenantId` (as sent, length-capped), `loanId` (as sent, length-capped),
`status`, `errorCode`, `correlationId`. The correlation ID is generated per request and returned
in the `X-Correlation-Id` response header. Request and response bodies, headers, and connection
details are never logged. A unit test asserts that no amount appears in audit output.

## R11. OpenAPI

Contract-first. The committed YAML in `specs/001-loan-balance-summary/contracts/` is the source of
truth and is published with the repository. The API does **not** serve it: every request must
carry a tenant and a valid credential (Principle II), and an anonymous endpoint for the contract
would break that rule. A unit test loads the contract with `Microsoft.OpenApi.Readers` and checks
that the app's routes, the error code enum, and the `ErrorCode` constants agree. No generated
document replaces it.

## R12. Tests

- **Unit (GitHub)**: auth handler, registry validation, check order, loan ID parser, error
  writer, audit fields, contract consistency, script scanner (including the SPI function allowlist),
  full pipeline with an in-memory fake repository (`WebApplicationFactory`).
- **Database (local only)**: `[Trait("Category","Database")]`. Each test skips with a clear
  message unless `RW_DB_TESTS=1`. CI runs `dotnet test --filter "Category!=Database"`.
  - *Parity*: every active loan in `AlphaPMS_ResortWise`, field by field, against the R4
    references, including principal against `dbo.lrFnGetBalanceSummary` (O4 revised) for every
    active loan whose principal is available; for every active loan matching an O5 rule,
    principal `null`, not available, reason `PAID_OFF_FLAG_SET` or `CROSS_LOAN_REVERSAL` (O5);
    plus, for every non-active record, principal `null`, not available, reason
    `LOAN_NOT_ACTIVE` (O2).
  - *Isolation*: tenant A → `AlphaPMS_ResortWise`, tenant B → `AlphaPMS_ResortWise_B`. The fixture
    asserts `DB_NAME()` on each tenant connection before any test runs. API requests use the API
    login; reference reads use the reference login (R9).
  - *Permissions*: the API login cannot read SPI objects (R9).
  - *End-to-end*: every error code through the real pipeline with real databases.
- **Test-setup script** `tests/db/setup/B001__diverge_tenant_b.sql`:
  - First statement: `IF DB_NAME() <> N'AlphaPMS_ResortWise_B' THROW 50001, 'Wrong database', 1;`
    before any change. `SET XACT_ABORT ON`, one transaction.
  - Selects rows **by rule** on loan IDs (decided 2026-10-26): every active record with
    `ID % 10 = 3` gets the lowest non-active status in use (one `UPDATE`; no amounts change), so
    tenant B returns that status with principal `null` and reason `LOAN_NOT_ACTIVE` (O2); exactly one record, the lowest ID with `ID % 10 = 7`,
    is removed to prove the 404. It contains no loan IDs or values. A rerun finds no active
    `ID % 10 = 3` record left and changes nothing. Tenant B is checked by these expected results,
    not by `lrGetTransactionActivity`.
  - Changes only SPI **data** (no DDL on SPI objects). Removal deletes dependent rows in
    foreign-key order. The child tables are confirmed from the local schema.
  - **Confirmed locally (T044, 2026-10-26)**: for principal, `dbo.lrFnGetBalanceSummary` reads
    `lrLoanReceivable` (including `LoanAmount`), `lrTransaction`, and `lrFrequency`, and calls
    `dbo.lrGetPrincipalRemaining` (which reads `lrLoanReceivable` and `lrTransaction`) and
    `dbo.GetFinanceCharge`. The only foreign key to `lrLoanReceivable` is `lrTransaction.LoanId`
    (no action on delete), and nothing references `lrTransaction`, so removal deletes
    `lrTransaction` rows first, then `lrLoanReceivable`. Other tables with a loan ID column but
    no foreign key (backup, report, and conversion tables) are not read by the `rw` views and are
    left unchanged. The audit triggers on both tables are disabled in the restored database.
  - Run by hand with an explicit `-d AlphaPMS_ResortWise_B` and the owner-level SQL login from the `SQLCMD*` variables
    (quickstart step 2). The test run does not execute it, so the database tests need no login
    that can write. Before the isolation tests, the fixture checks through the reference login on
    `AlphaPMS_ResortWise_B` that the rule's changes are present, and fails with "run quickstart
    step 2" if not.

## R13. Container and CI

Multi-stage Dockerfile: `sdk:8.0` build, `aspnet:8.0` runtime, non-root user, port 8080,
`ASPNETCORE_URLS` from the environment, no config files with secrets in the image (`.dockerignore`
excludes `appsettings.Local.json` and `*.Development.json`). CI: restore, build, unit tests,
`docker build`. No secrets and no database in CI.

## R14. Performance

One loan per request: one query joining `rw.Loan` and `rw.LoanPrincipalBalance` on the loan ID
(Dapper). Connection pooling per tenant connection string. The view must filter on loan ID before
the function runs, so the function is evaluated for one loan only; the first implementation task
checks the plan shape locally. The SC-006 target (p95 < 1 s at 20 req/s total) is checked locally
with a simple load script.

## Open questions

D1–D11 are closed (see [plan.md](plan.md#decisions)). O1–O3 were decided on 2026-10-23 and are
closed (see [plan.md](plan.md#open-points-for-farzin)); O1 is option (b), the SQL path as the
principal reference. O2 was reopened and decided by Farzin on 2026-10-26 (option B: non-active
principal `null`, reason `LOAN_NOT_ACTIVE`). O4 was revised in Farzin's pull request review on
2026-10-27: principal is compared automatically with `dbo.lrFnGetBalanceSummary` (R4). O5 is
option B (active records with the payoff flag set or a
cross-loan reversal return principal `null` with reason `PAID_OFF_FLAG_SET` or
`CROSS_LOAN_REVERSAL`; R2). O5 proposed by Yaswanth on 2026-10-27. Farzin agreed with the approach on 2026-10-27, pending his PR approval, on two conditions: both rules go into the engine feature's scope, and the per-pattern counts were sent to him privately. R2, R8, and R9 contain schema confirmations that the first
implementation task resolves locally.
