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
- **To confirm locally** (first implementation task): the function's type (inline table-valued
  or scalar) and output column; that no contract has more than one active loan record (count
  only). See the plan for what happens if it does.

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
| `loanStatus` | `lrGetLoanDetails` (`@LoanId`, `@SalesContractId`) for the status ID; `lrGetLoanStatusTypes` (through `lrGetLoanMasterInfo`) for the name |
| `principalBalance` (active loans) | `lrGetTransactionActivity`, second result set, `PrincipalRemaining`. This is the screen's SQL path; the shown value comes from the engine (O1) |
| `principalBalance` (non-active loans) | Not in the SC-001 parity scope. A database test checks the view returns `0.00` for every non-active record; the screen check for an old rewritten record is manual (plan) |
| `asOfDate` | `CAST(GETDATE() AS date)` on the same connection, read before and after the request; the response must equal one of them (midnight-safe) |

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
column type. The type is **to be confirmed** in the first implementation task by reading
`INFORMATION_SCHEMA` on the local database. The local server was not reachable during planning.
If it is `int`, the maximum is 2147483647 and the contract gets `maximum: 2147483647`. That is
allowed before release.

## R9. `rw` scripts and database permissions

- Scripts in `db/rw/V###__name.sql`, applied in order by a DbUp console tool. The journal table is
  `rw.SchemaVersions`, a table in `rw`, which is allowed. Views use `CREATE OR ALTER VIEW rw.…`.
- `CREATE SCHEMA rw AUTHORIZATION dbo` lets ownership chaining give the API login access through
  `rw` views **without any permission on SPI tables**. The API login is granted `SELECT ON
  SCHEMA::rw` only. This enforces Principle III in the database, not only by convention.
- Ownership chaining also covers the view's call to `dbo.lrFnGetBalanceSummary` (both owned by
  `dbo`), so the API login needs no permission on the SPI function either.
- A unit test (GitHub) scans `db/rw` and fails if any `CREATE`/`ALTER`/`DROP`/`INSERT`/`UPDATE`/
  `DELETE`/`MERGE` targets anything other than schema `rw`, if any script contains `EXEC`, or if
  a view references an SPI function not on an allowlist. The allowlist holds only
  `dbo.lrFnGetBalanceSummary`; adding a function to it is a reviewed change.
- Exact SPI table and column names for the views (status type table, contract column) are **to
  be confirmed** from the local schema and the read-only procedures'
  definitions in the first implementation task.

## R10. Audit logging

`Microsoft.Extensions.Logging` with the JSON console formatter writes to stdout. One entry per
request from a middleware's `finally` block, with fields: `timestamp`, `callerId` (null if
unauthenticated), `tenantId` (as sent, length-capped), `loanId` (as sent, length-capped),
`status`, `errorCode`, `correlationId`. The correlation ID is generated per request and returned
in the `X-Correlation-Id` response header. Request and response bodies, headers, and connection
details are never logged. A unit test asserts that no amount appears in audit output.

## R11. OpenAPI

Contract-first. The committed YAML is the source of truth. It is copied into the image and served
at `GET /v1/openapi.yaml` (anonymous, no tenant). A unit test loads the contract with
`Microsoft.OpenApi.Readers` and checks that the app's routes, the error code enum, and the
`ErrorCode` constants agree. No generated document replaces it.

> Serving the contract is a second endpoint. It must be listed in the contract itself
> (Principle I: an endpoint not in OpenAPI is not done), and it is exempt from tenant and
> credential checks because it returns no tenant data. If reviewers disagree with that exemption,
> drop the endpoint and publish the file with the repository instead.

## R12. Tests

- **Unit (GitHub)**: auth handler, registry validation, check order, loan ID parser, error
  writer, audit fields, contract consistency, script scanner (including the SPI function allowlist),
  full pipeline with an in-memory fake repository (`WebApplicationFactory`).
- **Database (local only)**: `[Trait("Category","Database")]`. Each test skips with a clear
  message unless `RW_DB_TESTS=1`. CI runs `dotnet test --filter "Category!=Database"`.
  - *Parity*: every active loan in `AlphaPMS_ResortWise`, field by field, against the R4
    references; plus `0.00` principal for every non-active record.
  - *Isolation*: tenant A → `AlphaPMS_ResortWise`, tenant B → `AlphaPMS_ResortWise_B`. The fixture
    asserts `DB_NAME()` on each tenant connection before any test runs.
  - *End-to-end*: every error code through the real pipeline with real databases.
- **Test-setup script** `tests/db/setup/B001__diverge_tenant_b.sql`:
  - First statement: `IF DB_NAME() <> N'AlphaPMS_ResortWise_B' THROW 50001, 'Wrong database', 1;`
    before any change. `SET XACT_ABORT ON`, one transaction.
  - Selects rows **by rule** on loan IDs (for example, `LoanId % 10 = 3` changed and
    `LoanId % 10 = 7` removed). Changed loans get data the principal function reads (for example
    principal paid) or their status changed, so the principal or status differs from tenant A. It contains no loan IDs or values. Changes are idempotent: they
    set values derived from the rule, never increments, so a rerun gives the same state.
  - Changes only SPI **data** (no DDL on SPI objects). Removal deletes dependent rows in
    foreign-key order. The child tables are confirmed from the local schema.
  - Run with an explicit `-d AlphaPMS_ResortWise_B`. The test fixture also refuses to run it
    unless its connection's `DB_NAME()` is `AlphaPMS_ResortWise_B`.

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

D1–D11 are closed (see [plan.md](plan.md#decisions)). O1, the principal parity reference, is open
for Farzin's confirmation and does not block tasks. R2, R8, and R9 contain schema confirmations
that the first implementation task resolves locally.
