# Research: Loan Balance Summary API

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-23

SPI objects are named here, but no SPI source code, formulas, workbook cell references, or form
file paths are recorded. These details stay in a local, uncommitted analysis (Principle V).

## R1. Where each returned value comes from

**Finding**: Of the returned fields, only loan status and the Other bucket names come straight
from SPI tables. Principal Remaining has an SQL source (`dbo.lrGetPrincipalRemaining`, reached
through `lrGetTransactionActivity` → `dbo.lrFnGetBalanceSummary`), but the value the screen
finally shows comes from the Excel engine (`SPILoanExcelEngine.GetLoanSummary`). Every Current
Payment Information amount comes from the Excel engine, which reads its inputs through
`lrExcelGetLoanById`, `lrExcelGetTransactions`, `lrExcelGetControlSettings`, and
`lrExcelGetContractPaidTotals`. The full per-field table is in
[plan.md](plan.md#field-sources-design-question).

**Consequence**: FR-003 ("MUST NOT recompute") and FR-021 ("call the same SPI stored procedures or
queries") assume the values exist in the database. For most fields they do not. See D2 and D3 in
the plan.

## R2. Principal balance data access

- **Decision (recommended, pending D1)**: P-B. Reimplement the contract-level principal
  calculation in `rw.LoanPrincipalRemaining`, reading SPI tables directly.
- **Rationale**: Small aggregation. Keeps application access free of SPI functions
  (Principle III, "directly or indirectly"). Parity tests call the SPI function read-only as an
  independent check.
- **Alternatives considered**: P-A, a view selecting from `dbo.lrGetPrincipalRemaining`. It gives
  exact SQL-path parity, but it is an indirect call to an SPI function from application code. It
  is not allowed under constitution v1.3.0 and would need an amendment. Farzin decides (D1).

## R3. Excel-engine values

- **Decision (recommended, pending D2)**: E-B. `rw` input views read the same data the four
  `lrExcelGet*` procedures read, straight from SPI tables, and a pure C# `LoanSummaryCalculator`
  reproduces the engine's Current Payment calculations.
- **Rationale**: Inside Principle III; unit-testable on GitHub with synthetic data; runs in a
  Linux container; one round trip to the database per request.
- **Implementation notes** (no formulas recorded here):
  - Port from the local engine and workbook. The implementer reads them locally and does not
    commit them.
  - Reproduce the spreadsheet's arithmetic: keep calculations in `double` where the workbook does,
    and round with `MidpointRounding.AwayFromZero` where the workbook rounds, because the
    spreadsheet's ROUND differs from .NET's default banker's rounding. Convert to `decimal(…,2)`
    only at the output edge. Parity tests decide any doubt.
  - "As of today" values use the as-of date passed in (D10). Never read the container clock
    inside the calculator, so unit tests are deterministic.
  - Paid-off and written-off loans return 0 for every due and balance field, as the SPI
    Application does (D8).
- **Alternatives considered**: E-A (T-SQL reimplementation; untestable on GitHub, hard to
  maintain), E-C (run SPI engine in the service; breaks the container rule and Principle III's
  intent), E-D (SPI exposes the values; outside our control, worth raising in parallel).

## R4. Parity references for tests (FR-021)

Tests call these read-only against `AlphaPMS_ResortWise` only. All of them are SELECT-only
according to the screen analysis. `lrGetNonReversedPayments` (batch path, writes a session temp
table) is **not** used.

| Field | Reference |
| --- | --- |
| Active loan list | Read-only `SELECT` of loan IDs with `LoanStatusId = 1` (test code only) |
| `loanStatus` | `lrGetLoanDetails` (`@LoanId`, `@SalesContractId`) for the status ID; `lrGetLoanStatusTypes` (through `lrGetLoanMasterInfo`) for the name |
| `principalBalance` | Engine value (D3/D8), plus `lrGetTransactionActivity` second result set `PrincipalRemaining` as a cross-check |
| `others[n].name` | `lrGetControlByResort` (`@ResortId` = contract's resort) |
| Engine fields | `SPILoanExcelEngine.GetLoanSummary(LoanId, ValidThrough)` from a local SPI install, **only if D3 is approved**. The fields used are as of today, so ValidThrough does not affect them. |

If D3 is not approved, no automated parity reference exists for the engine fields. Then
FR-021/SC-001 cannot be met for them, and the spec must change.

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
- A unit test (GitHub) scans `db/rw` and fails if any `CREATE`/`ALTER`/`DROP`/`INSERT`/`UPDATE`/
  `DELETE`/`MERGE`/`EXEC` targets anything other than schema `rw`, or if a view references an SPI
  function or procedure.
- Exact SPI table and column names for the views (status type table, transaction table, contract
  and resort columns) are **to be confirmed** from the local schema and the read-only procedures'
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
  writer, audit fields, calculator with synthetic fixtures, contract consistency, script scanner,
  full pipeline with an in-memory fake repository (`WebApplicationFactory`).
- **Database (local only)**: `[Trait("Category","Database")]`. Each test skips with a clear
  message unless `RW_DB_TESTS=1`. CI runs `dotnet test --filter "Category!=Database"`.
  - *Parity*: every active loan in `AlphaPMS_ResortWise`, field by field, against the R4
    references.
  - *Isolation*: tenant A → `AlphaPMS_ResortWise`, tenant B → `AlphaPMS_ResortWise_B`. The fixture
    asserts `DB_NAME()` on each tenant connection before any test runs.
  - *End-to-end*: every error code through the real pipeline with real databases.
- **Test-setup script** `tests/db/setup/B001__diverge_tenant_b.sql`:
  - First statement: `IF DB_NAME() <> N'AlphaPMS_ResortWise_B' THROW 50001, 'Wrong database', 1;`
    before any change. `SET XACT_ABORT ON`, one transaction.
  - Selects rows **by rule** on loan IDs (for example, `LoanId % 10 = 3` changed and
    `LoanId % 10 = 7` removed). It contains no loan IDs or values. Changes are idempotent: they
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

One loan per request: one round trip that returns the loan, names, principal, and engine-input
row sets (Dapper `QueryMultiple`), then an in-memory calculation. Connection pooling per tenant
connection string. The SC-006 target (p95 < 1 s at 20 req/s total) is checked locally with a
simple load script. Transaction volume per loan is small, so this is not a risk. The views must
filter on loan ID so the predicate is pushed into the SPI tables' indexes.

## Open questions

D1–D11 in [plan.md](plan.md#decisions-needed-from-farzin) are open. Each is for Farzin, except
D10 and D11, which are minor confirmations. R8 and R9 contain two schema confirmations that the
first implementation task resolves locally.
