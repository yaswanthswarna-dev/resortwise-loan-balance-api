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
  return `0.00` without calling the function.
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
| `principalBalance` (active loans) | **Not compared automatically (deviation, decided 2026-10-26).** The planned reference, `lrGetTransactionActivity` (`@Ownerid char(10)`, `@SalesContractId int`; second result set, `PrincipalRemaining`), reaches the database `AlphaSales` at run time, and the reference login is not given access to it. `lrGetPrincipalRemaining` was rejected as a substitute because `dbo.lrFnGetBalanceSummary`, which `rw.LoanPrincipalBalance` uses, calls it, so it would not be an independent reference. Principal parity rests on the 5-loan manual screen check (plan, Complexity Tracking) |
| `principalBalance` (non-active loans) | Not in the SC-001 parity scope. A database test checks the view returns `0.00` for every non-active record; the screen check for an old rewritten record is manual (plan) |
| `asOfDate` | `CAST(GETDATE() AS date)` on the reference connection, read before and after the request; the response must equal one of them (midnight-safe) |

**Connections**: every reference call above uses the test-only **reference login**, never the
API login (R9).

**Approved SPI calls in test code**: only `lrGetLoanDetails`, `lrGetLoanStatusTypes`, and
`lrGetTransactionActivity` (procedures), plus read-only `SELECT`s on SPI tables.
`lrGetTransactionActivity` stays approved but is not called while the principal deviation stands. A unit test scans
the test code and fails if it calls any other SPI procedure or function (R9).

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
    tests use it to read reference values. Because a procedure's own writes would bypass
    `db_denydatawriter` through ownership chaining, `EXECUTE` is granted only on procedures that
    the screen analysis shows are SELECT-only.
  - Applying `rw` scripts and running the tenant B setup script use a local owner-level
    connection (Windows authentication), not either login.
- A unit test (GitHub) scans `db/rw`, including `db/rw/bootstrap`, and fails if any
  `CREATE`/`ALTER`/`DROP`/`INSERT`/`UPDATE`/`DELETE`/`MERGE` targets anything other than schema
  `rw`, if any script contains `EXEC`, if a view references an SPI function not on an allowlist,
  or if the bootstrap script creates anything other than schema `rw` and table
  `rw.SchemaVersions`. The allowlist holds only `dbo.lrFnGetBalanceSummary`; adding a function to
  it is a reviewed change.
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
    references; plus `0.00` principal for every non-active record.
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
    tenant B returns that status and `0.00`; exactly one record, the lowest ID with `ID % 10 = 7`,
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
  - Run by hand with an explicit `-d AlphaPMS_ResortWise_B` and an owner-level local connection
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
principal reference. R2, R8, and R9 contain schema confirmations
that the first implementation task resolves locally.
