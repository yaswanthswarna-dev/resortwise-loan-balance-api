---

description: "Task list for the Loan Balance Summary API (reduced scope)"
---

# Tasks: Loan Balance Summary API

**Input**: Design documents from `specs/001-loan-balance-summary/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/loan-balance-summary.openapi.yaml),
[quickstart.md](quickstart.md)

**Tests**: Required. Constitution Principle VII requires automated tests for every endpoint
(success, `TENANT_REQUIRED`, `UNAUTHENTICATED`, `TENANT_FORBIDDEN`, tenant isolation, and its
error codes), and spec FR-021 requires the parity test. Write each test task before the
implementation it covers and confirm it fails first.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on
its own.

**Rules that apply to every task** (constitution v1.4.0):

- This repository is public. Never commit loan IDs, loan values, owner details, query results,
  connection strings, passwords, keys, or key hashes. Test output goes to git-ignored
  `TestResults/`.
- Application code queries `rw` views only and never calls SPI procedures or functions itself.
  The only SPI function a `rw` view may call is `dbo.lrFnGetBalanceSummary`, read-only. No `rw`
  object calls an SPI procedure.
- Tests may call SPI procedures and functions read-only, against the local test databases only,
  through the test-only reference login (`rw_parity_ref`). API requests use the API login
  (`rw_api`). `lrGetNonReversedPayments` is never called.
- Database tests carry `[Trait("Category","Database")]` and run locally only.
- Only `tests/db/setup/*.sql` may change data, only in `AlphaPMS_ResortWise_B`, and it is run by
  hand (quickstart step 2), never by the test run.

**Decisions O1–O3, closed 2026-10-23** (plan, Open points for Farzin): **O1**, option (b): the
principal parity reference is the SPI SQL function path, with a documented Principle IV deviation;
the Excel engine is not used in any way. **O3**: the test-only users and `EXECUTE` grants on
read-only SPI procedures are allowed. **O2, reopened and decided by Farzin 2026-10-26 (option
B)**: non-active loan records return their status with principal `null` (never `0.00`),
`principalBalanceAvailable` `false`, and `principalUnavailableReason` `LOAN_NOT_ACTIVE`; the
earlier `0.00` rule was wrong (T038). The view change is `V003` (T063), because `V002` is already
applied. **Farzin's pull request review, 2026-10-27**: O1 and O2 approved; **O4 revised**: T031
compares `principalBalance` for every active loan with a read-only call to
`dbo.lrFnGetBalanceSummary` through `rw_parity_ref` (rounding to 2 places, field mapping, and
nulls), which catches rounding, mapping, and null errors but does not prove the screen value
(the Excel engine calculates it); the manual screen check grows to about 20 active loans; and
the O2 null rule also covers paid-off and cancelled records, which the engine feature must
cover. These are T066–T072 (Phase 8). **O5 (option B)**: an
**active** record whose payoff flag is set returns principal `null`, `principalBalanceAvailable`
`false`, and reason `PAID_OFF_FLAG_SET`; non-active records keep `LOAN_NOT_ACTIVE` (first match
wins, in that order). The view change was `V004`, which only detects these cases from SPI tables
and never calculates amounts. The cross-loan rule (`CROSS_LOAN_REVERSAL`) was dropped on 2026-10-28 because it came from a misread screen value; O5 is now only the payoff flag rule, still proposed by Yaswanth and pending Farzin's PR approval. `V004` also had a cross-loan reversal flag;
`V005` removes it (T088–T097, Phase 11). A documented Principle IV deviation, signed off by Farzin's PR approval. O5 proposed by Yaswanth on 2026-10-27. Farzin agreed with the approach on 2026-10-27, pending his PR approval, on two conditions: both rules go into the engine feature's scope, and the per-pattern counts were sent to him privately.
These are T073–T083 (Phase 9). No task waits on an open decision.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1–US4)

## Path Conventions

Single API project plus a migration tool, as in [plan.md](plan.md#source-code-repository-root):
`src/ResortWise.LoanBalance.Api/`, `tools/ResortWise.RwMigrator/`, `db/rw/`,
`tests/ResortWise.LoanBalance.UnitTests/`, `tests/ResortWise.LoanBalance.DbTests/`,
`tests/db/setup/`. Below, `Api/` means `src/ResortWise.LoanBalance.Api/`, `UnitTests/` means
`tests/ResortWise.LoanBalance.UnitTests/`, and `DbTests/` means
`tests/ResortWise.LoanBalance.DbTests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution, projects, configuration placeholders, container, and CI

- [X] T001 Create `ResortWise.LoanBalance.sln` at the repository root with four .NET 8 projects: `src/ResortWise.LoanBalance.Api/ResortWise.LoanBalance.Api.csproj` (ASP.NET Core minimal API), `tools/ResortWise.RwMigrator/ResortWise.RwMigrator.csproj` (console), `tests/ResortWise.LoanBalance.UnitTests/ResortWise.LoanBalance.UnitTests.csproj` (xUnit, references Api), `tests/ResortWise.LoanBalance.DbTests/ResortWise.LoanBalance.DbTests.csproj` (xUnit, references Api)
- [X] T002 Add `Directory.Build.props` at the repository root (`net8.0`, `LangVersion` 12, `Nullable` enable, `TreatWarningsAsErrors` true) and the packages: Dapper and Microsoft.Data.SqlClient in Api; `dbup-sqlserver` and Microsoft.Data.SqlClient in RwMigrator; Microsoft.OpenApi.Readers and Microsoft.AspNetCore.Mvc.Testing in UnitTests; Microsoft.Data.SqlClient and Microsoft.AspNetCore.Mvc.Testing in DbTests
- [X] T003 [P] Check `.gitignore` covers `appsettings.Local.json`, `appsettings.Development.json`, `*.Development.json`, `TestResults/`, `*.env`, `*.bak`, `*.mdf`, `*.ldf`, `*.bacpac`, `bin/`, `obj/`; add any that are missing in `.gitignore`
- [X] T004 [P] Create `Api/appsettings.json` with placeholders only (empty `Callers`, empty `Tenants`, empty `ConnectionStrings`, JSON console logging) and `Api/appsettings.Local.json.example` showing the shape from data-model.md with synthetic placeholders (`tenant-a`, `tenant-b`, `<connection string as rw_api>`, `<64 hex characters>`), never real values
- [X] T005 [P] Create a multi-stage `Dockerfile` (`mcr.microsoft.com/dotnet/sdk:8.0` build, `mcr.microsoft.com/dotnet/aspnet:8.0` runtime, non-root user, port 8080, `ASPNETCORE_URLS` from the environment) and `.dockerignore` excluding `appsettings.Local.json`, `*.Development.json`, `TestResults/`, `bin/`, `obj/`, `.git/` (research R13)
- [X] T006 [P] Create `.github/workflows/ci.yml`: restore, build, `dotnet test --filter "Category!=Database"`, `docker build`. No secrets, no database, no service containers

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema confirmations, `rw` scripts and migrator, test logins, errors, configuration,
credential and tenant checks, connections, audit, and test fixtures. Every story needs all of
these, because no loan data may be read before the credential and tenant checks (FR-012).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T007 Confirm the SPI schema locally with read-only metadata queries against `AlphaPMS_ResortWise` (restore it first if missing; it was not restored on the local instances during planning): (a) the data type of `lrLoanReceivable.LoanId` (research R8); (b) the loan status type table and its name column, and the `SalesContractId` column (research R9); (c) the type of `dbo.lrFnGetBalanceSummary` (inline table-valued or scalar), its parameter types, its principal output column, and **that column's data type, including precision and scale** (research R2); (d) a **count only** of contracts with more than one `LoanStatusId = 1` loan record, which must be 0; (e) the list of distinct non-active loan status names (names only, no counts per loan); (f) a **count only** of permissions granted to `public` or `guest` on SPI objects (any count is covered by the `DENY` in quickstart step 1, and T028 proves it); (g) the owner of each SPI object the `rw` views use (the tables in T009 and `dbo.lrFnGetBalanceSummary`), owner names only, each of which must be `dbo` for ownership chaining (research R9). If (d) is not 0, stop and send the count privately to Farzin (plan, per-loan-record rule). If any owner in (g) is not `dbo`, stop and send the object and owner names to Farzin before T009. Keep the status list from (e) in a private local file outside the repository (for example under git-ignored `TestResults/`), not in `research.md`, unless the names are confirmed to be standard SPI names. Record the other findings as names and types only, never data or function logic, in `specs/001-loan-balance-summary/research.md` (R2, R8, R9), and remove the *(confirm)* marks in `specs/001-loan-balance-summary/data-model.md`
- [X] T008 Create the bootstrap script `db/rw/bootstrap/create_schema_and_journal.sql` with two batches: a plain `CREATE SCHEMA rw AUTHORIZATION dbo` (no existence check), then `CREATE TABLE rw.SchemaVersions` with the columns DbUp's SQL Server journal expects for the DbUp version in T002 (an identity primary key, `ScriptName nvarchar(255) NOT NULL`, `Applied datetime NOT NULL`; check the column names against that DbUp version). It creates nothing else (research R9)
- [X] T009 Create `db/rw/V001__view_loan.sql`: `CREATE OR ALTER VIEW rw.Loan` with one row per loan record and columns `LoanId`, `LoanStatusId`, `LoanStatusName`, `SalesContractId`, read from `lrLoanReceivable` and the status type table confirmed in T007. It reads SPI tables only and calls no SPI function or procedure (data-model.md, `rw.Loan`)
- [X] T010 Implement `tools/ResortWise.RwMigrator/Program.cs`: a console that takes `--connection-env <ENV VAR NAME>` and reads the connection string from that environment variable. Before DbUp: if `SCHEMA_ID('rw')` is null, run `db/rw/bootstrap/create_schema_and_journal.sql` (embedded resource, split on `GO`); if schema `rw` exists but `OBJECT_ID('rw.SchemaVersions')` is null, stop with an error and change nothing. Then run DbUp with `JournalToSqlTable("rw", "SchemaVersions")`, which finds the existing table and does not create it, applying `db/rw/V###__*.sql` (embedded resources) in order. The bootstrap script is never passed to DbUp. Print script names only, never the connection string
- [X] T011 Apply the bootstrap script and `V001` to both local test databases, and create the `rw_api` and `rw_parity_ref` logins and users, as in quickstart step 1. Passwords are typed at the prompt and never written to a file or committed
- [X] T012 [P] Write `UnitTests/Scripts/RwScriptScannerTests.cs`: scan every file in `db/rw`, including `db/rw/bootstrap`; fail if any `CREATE`/`ALTER`/`DROP`/`INSERT`/`UPDATE`/`DELETE`/`MERGE` targets anything other than schema `rw`, if any script contains `EXEC`, if any script references an SPI function not on the allowlist (only `dbo.lrFnGetBalanceSummary`), or if `db/rw/bootstrap/create_schema_and_journal.sql` creates anything other than schema `rw` and table `rw.SchemaVersions` (research R9)
- [X] T013 [P] Write `UnitTests/Scripts/SetupScriptGuardTests.cs`: for every file in `tests/db/setup`, the first executable statement is `IF DB_NAME() <> N'AlphaPMS_ResortWise_B' THROW 50001, 'Wrong database', 1;`, and the script contains no `CREATE`/`ALTER`/`DROP` (research R12)
- [X] T014 [P] Create `Api/Errors/ErrorCodes.cs` (constants `TENANT_REQUIRED`, `UNAUTHENTICATED`, `TENANT_FORBIDDEN`, `LOAN_ID_INVALID`, `LOAN_NOT_FOUND`, `TENANT_UNAVAILABLE`, `INTERNAL_ERROR`), `Api/Errors/ErrorResponse.cs` (`code`, `message`, optional `field`, omitted when null), and `Api/Errors/ErrorWriter.cs`, which writes the status, code, and one constant message per code (the messages in the contract examples). Messages never include exception text
- [X] T015 [P] Write `UnitTests/Errors/ErrorWriterTests.cs`: each code gives its status and constant message; `field` appears only for `TENANT_REQUIRED` (`X-Tenant-Id`) and `LOAN_ID_INVALID` (`loanId`)
- [X] T016 [P] Create `Api/Tenancy/CallerOptions.cs`, `Api/Tenancy/TenantOptions.cs`, and `Api/Tenancy/TenantRegistryValidator.cs` (bound from `Callers:{n}:Id`, `Callers:{n}:KeyHash`, `Callers:{n}:Tenants:{m}`, `Tenants:{tenantId}:ConnectionStringName`, `ConnectionStrings:{name}`; `ValidateOnStart`). Caller `Id`: "Required, unique. Appears in audit entries." `KeyHash`: "Required, 64 lowercase hex characters (SHA-256 of the API key), unique. Never the plain key." `Tenants`: "Required, at least one. Each must exist in `Tenants`." Tenant ID: "Non-blank. Compared ordinally." `ConnectionStringName`: "Required. Must resolve to `ConnectionStrings:{name}`. Exactly one per tenant." The registry is immutable after startup. Validation messages name the rule and the config key, never the value (research R6)
- [X] T017 [P] Write `UnitTests/Tenancy/TenantRegistryValidatorTests.cs`: each rule in T016 rejects bad config, valid config passes, and no failure message contains a hash or connection string
- [X] T018 Implement `Api/Auth/ApiKeyAuthenticationHandler.cs` (after T014, because it uses `ErrorWriter`): read `Authorization: Bearer <key>`, compute SHA-256 as lowercase hex, compare with `CryptographicOperations.FixedTimeEquals` against **every** caller's hash (no early exit), set the caller ID claim on success; on a missing or invalid credential, write 401 `UNAUTHENTICATED` through `ErrorWriter` (research R5)
- [X] T019 [P] Write `UnitTests/Auth/ApiKeyAuthenticationHandlerTests.cs`: missing header, wrong scheme, unknown key → 401 `UNAUTHENTICATED`; valid key → caller ID set. Keys and hashes are generated in the test, never hard-coded from real config
- [X] T020 Implement `Api/Tenancy/TenantAccessMiddleware.cs`: after authentication, a missing, empty, or whitespace-only `X-Tenant-Id` → 400 `TENANT_REQUIRED` with field `X-Tenant-Id`; a tenant the caller is not allowed **or** a tenant not in the registry → 403 `TENANT_FORBIDDEN` with an identical body; otherwise put the resolved tenant on the request context. Tenant IDs compare ordinally, exactly as sent (FR-010, FR-011, research R6, R7)
- [X] T021 [P] Implement `Api/Tenancy/TenantConnectionFactory.cs`: opens a `SqlConnection` only for the resolved tenant's connection string, never another; a login failure, connection failure, or timeout opening the connection throws `TenantUnavailableException` (FR-013, research R7)
- [X] T022 [P] Implement `Api/Audit/RequestContext.cs` and `Api/Audit/AuditMiddleware.cs`: one JSON entry per request, written from a `finally` block, with `timestamp`, `callerId` (null if unauthenticated), `tenantId` as sent (capped at 128 characters), `loanId` as sent (capped at 128 characters), `status`, `errorCode`, `correlationId`. Generate the correlation ID per request and return it in `X-Correlation-Id`. Never log bodies, headers, amounts, or connection details (FR-019, FR-020, research R10)
- [X] T023 [P] Write `UnitTests/Audit/AuditMiddlewareTests.cs`: exactly one entry for a success, for each refusal status (400, 401, 403, 404, 503, 500), and for an unhandled exception; all FR-019 fields present; no entry contains the principal amount, the bearer key, or a connection string (SC-007)
- [X] T024 Implement `Api/Errors/ExceptionMappingMiddleware.cs`: `TenantUnavailableException` and `SqlException` connection or login failures → 503 `TENANT_UNAVAILABLE`; anything else → 500 `INTERNAL_ERROR`; no exception text, SQL, or stack trace in the response (FR-017, research R7)
- [X] T025 Implement `Api/Program.cs`: configuration from environment variables plus optional git-ignored `appsettings.Local.json`; options validation on start; middleware order audit → exception mapping → authentication → tenant access → endpoints; expose `public partial class Program` for `WebApplicationFactory`. Map no endpoint other than the balance summary: the contract is not served (research R11)
- [X] T026 Create `DbTests/Infrastructure/DatabaseFixture.cs`: skip every test with a clear message unless `RW_DB_TESTS=1`; read four connection strings from `RW_DB_A` and `RW_DB_B` (the `rw_api` login, used only to configure the API's tenants A and B) and `RW_REF_A` and `RW_REF_B` (the `rw_parity_ref` login, used only for reference reads); before any test runs, assert `DB_NAME()` is exactly `AlphaPMS_ResortWise` on both A connections and `AlphaPMS_ResortWise_B` on both B connections, and that `SUSER_SNAME()` is `rw_api` on the `RW_DB_*` connections and `rw_parity_ref` on the `RW_REF_*` connections; start the API with `WebApplicationFactory`, configured with test callers whose keys are generated per run (quickstart step 7)
- [X] T027 Create `DbTests/Infrastructure/RunSummary.cs`: at the end of the run, print and write to git-ignored `TestResults/run-summary.txt` only the date, the test database name, the number of loans checked, and the passed and failed counts. No loan IDs or values (Principle VII)
- [X] T028 Write `DbTests/Security/ApiLoginPermissionTests.cs`: on `RW_DB_A` and `RW_DB_B` (the `rw_api` login), `SELECT TOP (0)` from `dbo.lrLoanReceivable`, executing `dbo.lrGetLoanDetails`, and selecting from `dbo.lrFnGetBalanceSummary` directly each fail with permission error 229; `SELECT TOP (0)` from `rw.Loan` succeeds. On `RW_REF_A` and `RW_REF_B` (the `rw_parity_ref` login), `HAS_PERMS_BY_NAME` reports no `INSERT`, `UPDATE`, or `DELETE` on `dbo.lrLoanReceivable` (inherited `public` `EXECUTE` rights are accepted; T059 covers them). The `rw_api` checks call the table-valued function as `SELECT TOP (0) * FROM dbo.lrFnGetBalanceSummary(0, GETDATE())` and execute `dbo.lrGetLoanDetails @Loanid = 0, @SalesContractId = 0`. Nothing is written in either case (research R9, Principle III)
- [X] T059 [P] Write `UnitTests/Scripts/TestCodeSpiCallTests.cs`: scan every `.cs` file under `tests/ResortWise.LoanBalance.DbTests`; fail if it names any `dbo.` procedure or function other than `lrGetLoanDetails`, `lrGetLoanStatusTypes`, and `lrGetTransactionActivity` (procedures), plus `lrFnGetBalanceSummary` only in T028's permission check. Selecting from SPI tables is allowed (research R4, R9; decided 2026-10-23). The SPI tables tests read are listed too (`lrLoanReceivable`, `lrLoanStatus`), because a static scan cannot tell a table from a procedure; adding one is a reviewed one-line change. **Note (2026-10-27)**: `lrFnGetBalanceSummary` is also allowed in the active-loan parity test (O4 revised); T068 updates the scanner

**Checkpoint**: Foundation ready. The `rw` schema, journal, and `rw.Loan` apply cleanly to both
test databases, the API login is proven `rw`-only, and every request is authenticated,
tenant-checked, and audited. User story work can begin

---

## Phase 3: User Story 1 - Get a loan's balance summary (Priority: P1) 🎯 MVP

**Goal**: An authorized caller gets loan ID, loan status, as-of date, and principal balance for
one loan record, matching the SPI parity sources (FR-001–FR-003, FR-006–FR-008a).

**Independent Test**: With a caller allowed the tenant mapped to `AlphaPMS_ResortWise`, request
each active loan and compare every field with the parity references read automatically from SPI
(FR-021); every non-active record returns its own status, principal `null`,
`principalBalanceAvailable` `false`, and reason `LOAN_NOT_ACTIVE` (O2); every active record
with the payoff flag set returns principal `null`, not available, and reason
`PAID_OFF_FLAG_SET` (O5).

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T029 [P] [US1] Write `UnitTests/Contract/ContractConsistencyTests.cs`: load `specs/001-loan-balance-summary/contracts/loan-balance-summary.openapi.yaml` (linked into the UnitTests project as a `None` item with `CopyToOutputDirectory`) with Microsoft.OpenApi.Readers; the app's routes equal the contract's paths; the `Error.code` enum equals the `ErrorCodes` constants; `LoanBalanceSummary` required fields are exactly `loanId`, `loanStatus`, `asOfDate`, `principalBalance` and match the response model. **Note (2026-10-26)**: the four-field check is superseded by T060 (O2)
- [X] T030 [P] [US1] Write `UnitTests/Loans/BalanceSummaryEndpointTests.cs` with `WebApplicationFactory` and an in-memory fake `ILoanBalanceRepository`: 200 body has exactly the four fields; `principalBalance` is serialized with two decimal places, including `0.00` (FR-006); a negative principal is returned unchanged; the body contains no payoff field and none of the out-of-scope fields (FR-007). **Note (2026-10-26)**: the four-field check is superseded by T062 (O2)
- [X] T031 [P] [US1] Write `DbTests/Parity/ActiveLoanParityTests.cs`: for every loan record with `LoanStatusId = 1` in `AlphaPMS_ResortWise` (read-only `SELECT` on `RW_REF_A`), call the API through tenant A and compare: `loanStatus` with `lrGetLoanDetails` (`@Loanid`, `@SalesContractId`; status ID) and `lrGetLoanStatusTypes` (no parameters; name) on `RW_REF_A`, exact ordinal string; `asOfDate` equals `CAST(GETDATE() AS date)` read on `RW_REF_A` just before or just after the request. The principal is available: `principalBalance` is a number (not `null`), `principalBalanceAvailable` is `true`, and `principalUnavailableReason` is absent (FR-008a, O2; added 2026-10-26, analyze G1). Each loan's contract ID is read at run time on `RW_REF_A` and never logged or saved. All SPI calls are read-only. Failures name the loan ID and field only in `TestResults/`. Report the count to `RunSummary` (SC-001, FR-021, research R4). **Principal is not compared (deviation, decided 2026-10-26)**: `lrGetTransactionActivity` reaches the database `AlphaSales`, which the reference login is not given access to, and `lrGetPrincipalRemaining` is not independent of the view. Principal parity rests on the 5-loan manual screen check, recorded in the plan's Complexity Tracking, with Farzin's PR approval as the sign-off. **Superseded 2026-10-27 (O4 revised in Farzin's PR review)**: principal is now compared for every active loan with a read-only call to `dbo.lrFnGetBalanceSummary` (T066–T068), and the manual screen check grows to about 20 active loans (T069). **Updated 2026-10-27 (O5)**: principal is compared only for active loans whose principal is available; for every active loan matching an O5 rule, the test checks principal `null`, `principalBalanceAvailable` `false`, and the right reason (`PAID_OFF_FLAG_SET`, else `CROSS_LOAN_REVERSAL`) instead. The change is T077. **Updated 2026-10-28 (cross-loan rule dropped)**: only the payoff flag rule is left (T091)
- [X] T032 [P] [US1] Write `DbTests/Parity/NonActiveLoanTests.cs`: for every loan record with `LoanStatusId <> 1` in `AlphaPMS_ResortWise` (selected on `RW_REF_A`), the API returns 200, the record's own status name, `principalBalance` as the JSON `null` token (never `0` or `0.00`), `principalBalanceAvailable` `false`, and `principalUnavailableReason` `LOAN_NOT_ACTIVE` (FR-006, FR-008a; plan, per-loan-record rule; O2). **Updated 2026-10-26 (O2 option B)**: the test written earlier expects `0.00` and must be changed; it fails until T063–T065 are done. **Note (2026-10-26)**: updated; compiles; not yet run against the databases (needs T064)

### Implementation for User Story 1

- [X] T033 [US1] Create `db/rw/V002__view_loan_principal_balance.sql`: `CREATE OR ALTER VIEW rw.LoanPrincipalBalance` with one row per loan record and columns `LoanId`, `AsOfDate` = `CAST(GETDATE() AS date)`, and `PrincipalBalance`: for `LoanStatusId = 1`, the principal from `dbo.lrFnGetBalanceSummary(LoanId, GETDATE())` (with `CROSS APPLY` or in the select list, per the type confirmed in T007); for any other status, `CAST(0 AS decimal(19,2))` without calling the function. T007 found `principalRemaining` is `money` (scale 4): use `CAST(principalRemaining AS decimal(19,2))`, which rounds half away from zero as the screen does (research R2), and the parity test T031 confirms it. No other SPI function and no procedure (data-model.md, `rw.LoanPrincipalBalance`). **Note (2026-10-26)**: `V002` is applied and is not edited; its non-active `0.00` branch is replaced by `V003` (T063, O2)
- [X] T034 [US1] Apply `V002` to both local test databases with `tools/ResortWise.RwMigrator` (quickstart step 1) and confirm locally that a query filtered on one `LoanId` evaluates the function for that loan only (actual execution plan; research R14). Record nothing from the plan beyond "filter pushed down: yes/no" in the task notes. **Note (2026-10-26)**: filter pushed down: yes
- [X] T035 [P] [US1] Create `Api/Loans/Models/LoanBalanceSummaryResponse.cs` with exactly `loanId` (int64), `loanStatus` (string), `asOfDate` (date, `yyyy-MM-dd`), `principalBalance` (decimal, two places), and `Api/Loans/Models/LoanBalanceRow.cs` for the repository result. **Note (2026-10-26)**: the model is extended by T065 (nullable principal, availability, reason; O2)
- [X] T036 [US1] Implement `Api/Loans/ILoanBalanceRepository.cs` and `Api/Loans/LoanBalanceRepository.cs`: one parameterized Dapper query joining `rw.Loan` and `rw.LoanPrincipalBalance` on `LoanId` with `WHERE LoanId = @LoanId`, through `TenantConnectionFactory`; it references `rw.` objects only and returns null when no row exists
- [X] T037 [US1] Implement `Api/Loans/BalanceSummaryEndpoint.cs` and map `GET /v1/loans/{loanId}/balance-summary` in `Api/Program.cs`: read through the repository and return the four fields; set `loanId` on the audit context; no recomputation or rounding beyond the view's (FR-003). **Note (2026-10-26)**: the response rule is extended by T065 (O2)
- [X] T038 [US1] Manual check, not committed: in the SPI Application, open the Balance Summary tab for one loan record of **each** non-active status found in T007 (e) (one non-active status is in use in `AlphaPMS_ResortWise`: the status of old records left behind by a rewrite), in `AlphaPMS_ResortWise`, and note whether Principal Remaining shows 0. Keep the results private and send them to Farzin. Any status that does not show 0 goes to Farzin before merge (plan). Record only "done, results sent privately" in the PR, with no loan IDs, statuses per loan, or values. **Done (2026-10-26)**: two rewritten loan records checked; the screen value is not `0.00` on either. Details are kept privately. This reopened O2, which Farzin decided as option B (T032, T043, T060–T065)
- [X] T060 [P] [US1] Update `UnitTests/Contract/ContractConsistencyTests.cs` (T029): `LoanBalanceSummary` required fields are exactly `loanId`, `loanStatus`, `asOfDate`, `principalBalance`, `principalBalanceAvailable`; `principalBalance` is nullable; `principalUnavailableReason` is optional and its enum equals the API's reason constants (only `LOAN_NOT_ACTIVE`); all of them match the response model (O2)
- [X] T061 [P] [US1] Write `UnitTests/Loans/NullPrincipalSerializationTests.cs` with `WebApplicationFactory` and the fake repository: **a null principal is never serialized as 0**. A non-active record gives `principalBalance` as the JSON `null` token, never `0`, `0.0`, or `0.00`, plus `principalBalanceAvailable` `false` and `principalUnavailableReason` `LOAN_NOT_ACTIVE`. Serializing the response model directly with a null principal also writes `null`. An active record with principal zero gives `0.00`, `principalBalanceAvailable` `true`, and no `principalUnavailableReason` property. An active record with a null principal and a non-active record with a non-null principal each give 500 `INTERNAL_ERROR` (FR-006, FR-008a; data-model.md)
- [X] T062 [P] [US1] Update `UnitTests/Loans/BalanceSummaryEndpointTests.cs` (T030): an active 200 body has exactly `loanId`, `loanStatus`, `asOfDate`, `principalBalance`, `principalBalanceAvailable`; a non-active 200 body has those plus `principalUnavailableReason`; the other T030 checks are unchanged
- [X] T063 [US1] Create `db/rw/V003__view_loan_principal_balance_null_non_active.sql`: `CREATE OR ALTER VIEW rw.LoanPrincipalBalance`, the same as `V002` except that a record with any status other than `LoanStatusId = 1` returns `PrincipalBalance` `CAST(NULL AS decimal(19,2))`, still without applying the function. `V002` is already applied and is not edited (O2). Still only `dbo.lrFnGetBalanceSummary`, read-only; the T012 scanner must pass. No loan IDs or values in the script. **Note (2026-10-26)**: script written and passes the T012 scanner; not applied to any database yet (T064)
- [X] T064 [US1] Apply `V003` to both local test databases with `tools/ResortWise.RwMigrator` (quickstart step 1) and re-check that a query filtered on one `LoanId` evaluates the function for that loan only (as in T034). Record only "filter pushed down: yes/no" in the task notes. **Note (2026-10-26)**: applied to both test databases; filter pushed down: yes
- [X] T065 [US1] Update `Api/Loans/Models/LoanBalanceRow.cs` (add `LoanStatusId`), `Api/Loans/LoanBalanceRepository.cs` (select `rw.Loan.LoanStatusId`), `Api/Loans/Models/LoanBalanceSummaryResponse.cs`, and `Api/Loans/BalanceSummaryEndpoint.cs`. `principalBalance` becomes nullable and is always written: as the JSON `null` token when null, never `0`, and with two places otherwise. Add `principalBalanceAvailable` (`LoanStatusId = 1`) and `principalUnavailableReason`, which is `LOAN_NOT_ACTIVE` when not available and omitted when available. Add a reason constant next to `Api/Errors/ErrorCodes.cs`. Keep the guard that an active record with a null principal is an internal error, and add one for a non-active record with a non-null principal (data-model.md). Make T032, T043, and T060–T062 pass. **Note (2026-10-26)**: reason constants are in `Api/Loans/PrincipalUnavailableReasons.cs`. The contract's `principalBalance` is now an inline nullable number, because OpenAPI 3.0.3 ignores `nullable` beside `allOf` (analyze D1); the unused `Amount` schema was removed

**Checkpoint**: User Story 1 is fully functional; parity and non-active tests pass locally

---

## Phase 4: User Story 2 - Access is limited to authorized callers and their tenants (Priority: P1)

**Goal**: Prove that every refused request gets the documented status and code, in the fixed
check order, before any loan data is read (FR-009–FR-012).

**Independent Test**: Send requests with a missing credential, an invalid credential, a missing
or blank tenant header, a tenant the caller is not allowed, and an unknown tenant; each returns
the documented status and code, and no loan data is read.

### Tests for User Story 2 ⚠️

- [X] T039 [P] [US2] Write `UnitTests/Loans/AccessControlTests.cs` with `WebApplicationFactory` and a fake repository and connection factory that count calls: no credential and invalid credential → 401 `UNAUTHENTICATED`; missing, empty, and whitespace-only `X-Tenant-Id` → 400 `TENANT_REQUIRED` with field `X-Tenant-Id`; tenant not allowed → 403 `TENANT_FORBIDDEN`; unknown tenant → 403 with a byte-identical body; in every case the repository and connection factory are called 0 times (FR-012)
- [X] T040 [P] [US2] Write `UnitTests/Loans/AccessCheckOrderTests.cs`: requests with several access problems at once return the first failure in the order credential (401) → tenant header (400) → tenant authorization (403), using a valid loan ID so no loan check is involved (spec Edge Cases). The loan ID and 404 cases are in T049
- [X] T041 [P] [US2] Write `DbTests/EndToEnd/AccessEndToEndTests.cs`: the same 401, 400, and 403 cases through the real pipeline and real tenant databases, and a 503 `TENANT_UNAVAILABLE` case using a tenant whose connection string points to an unreachable server (a synthetic `localhost` port), with no fallback to another tenant

### Implementation for User Story 2

- [X] T042 [US2] Fix any failures from T039–T041 in `Api/Tenancy/TenantAccessMiddleware.cs`, `Api/Auth/ApiKeyAuthenticationHandler.cs`, or the middleware order in `Api/Program.cs`; the refusal bodies match the contract's `Unauthenticated`, `TenantForbidden`, and `400` examples exactly. **Note (2026-10-26)**: T039–T041 passed without changes

**Checkpoint**: User Stories 1 and 2 both work; no refused request touches a tenant database

---

## Phase 5: User Story 3 - Tenants never see each other's loans (Priority: P1)

**Goal**: Two tenants mapped to different databases return only their own data; a loan that
exists only in another tenant looks exactly like a loan that does not exist (FR-013, FR-014).

**Independent Test**: Tenant A → `AlphaPMS_ResortWise`, tenant B → `AlphaPMS_ResortWise_B` after
the test-setup script; the same loan IDs return each database's own values, and removed loans
return `LOAN_NOT_FOUND` through B only.

### Tests for User Story 3 ⚠️

- [X] T043 [P] [US3] Write `DbTests/Isolation/TenantIsolationTests.cs`. Loans are selected by the T045 rules on `RW_REF_A` (read-only), never listed. For every loan record that is active with `ID % 10 = 3` in `AlphaPMS_ResortWise`: tenant A returns its own status, a non-null principal, and `principalBalanceAvailable` `true`; tenant B returns the non-active status set by T045 (its name read on `RW_REF_B` from `lrLoanReceivable` and `lrLoanStatus`), `principalBalance` as the JSON `null` token (never `0` or `0.00`), `principalBalanceAvailable` `false`, and `principalUnavailableReason` `LOAN_NOT_ACTIVE`, so the responses differ (SC-002; O2). **Updated 2026-10-26 (O2 option B)**: the test written earlier expects `0.00` for tenant B and must be changed; it fails until T063–T065 are done. For the removed loan (lowest ID with `ID % 10 = 7` in `AlphaPMS_ResortWise`): tenant B returns 404 `LOAN_NOT_FOUND`, byte-identical to a loan ID that exists in no tenant, while tenant A returns 200 (SC-005). A caller allowed only tenant A gets 403 `TENANT_FORBIDDEN` for `X-Tenant-Id: tenant-b` (SC-003). Tenant B is checked by these expected results only; `lrGetTransactionActivity` is not called for tenant B (decided 2026-10-26). **Note (2026-10-26)**: updated; also checks tenant A's principal is a number and available; compiles; not yet run against the databases (needs T064)

### Implementation for User Story 3

- [X] T044 [US3] Confirm locally, from the schema and the read-only procedures' definitions, which SPI data `dbo.lrFnGetBalanceSummary` reads for principal and which child tables reference a loan record (foreign-key order for removal). Record table names only in `specs/001-loan-balance-summary/research.md` R12
- [X] T045 [US3] Create `tests/db/setup/B001__diverge_tenant_b.sql`: first statement `IF DB_NAME() <> N'AlphaPMS_ResortWise_B' THROW 50001, 'Wrong database', 1;`; then `SET XACT_ABORT ON`; if no active loan record with `ID % 10 = 3` remains, stop without changes (already applied); otherwise, in one transaction, one `UPDATE` on `lrLoanReceivable` sets every active record with `ID % 10 = 3` to the lowest non-active status in use, and exactly one loan record is removed (the lowest ID with `ID % 10 = 7`), deleting its `lrTransaction` rows first (T044). `LoanAmount` and other amounts are not changed. Orphan rows in backup, report, and conversion tables without a foreign key are accepted. Data changes only: no `CREATE`, `ALTER`, or `DROP`. No loan IDs or values in the script (Test data rules). Run it by hand per quickstart step 2 (decided 2026-10-26)
- [X] T046 [US3] Add `DbTests/Infrastructure/TenantBSetupCheck.cs`: before the isolation tests, check on `RW_REF_B` (read-only) that `DB_NAME()` is exactly `AlphaPMS_ResortWise_B` and that the T045 changes are present: no active loan record with `ID % 10 = 3` remains, and the lowest loan ID with `ID % 10 = 7` in `AlphaPMS_ResortWise` (read on `RW_REF_A`) does not exist in tenant B; if not, fail with "run quickstart step 2". It never runs the setup script and needs no login that can write

**Checkpoint**: Isolation suite passes; tenant A data is unchanged

---

## Phase 6: User Story 4 - Clear, stable errors for bad input (Priority: P2)

**Goal**: Invalid and unknown loan IDs return stable codes, with the field name for input errors
(FR-014–FR-017).

**Independent Test**: Request a malformed loan ID and a well-formed unknown loan ID; confirm the
documented status, code, and field.

### Tests for User Story 4 ⚠️

- [X] T047 [P] [US4] Write `UnitTests/Loans/LoanIdParserTests.cs`: `abc`, `-5`, `0`, `007`, ` 12`, `12 `, `1.0`, `+1`, and a value one above the maximum confirmed in T007 → invalid; `1` and the maximum → valid
- [X] T048 [P] [US4] Write `UnitTests/Loans/LoanErrorTests.cs`: an invalid loan ID → 400 `LOAN_ID_INVALID` with field `loanId`, and the repository is not called; an unknown loan ID → 404 `LOAN_NOT_FOUND` with no `field`; no error message contains SQL, a stack trace, or exception text, including when the fake repository throws (FR-017)
- [X] T049 [P] [US4] Write `UnitTests/Loans/LoanCheckOrderTests.cs` (moved from the US2 check-order test): a forbidden tenant with an invalid loan ID → 403 `TENANT_FORBIDDEN` (tenant authorization before loan ID format); an allowed tenant with an invalid loan ID → 400 `LOAN_ID_INVALID` and the repository is called 0 times (format before existence); an allowed tenant with a valid unknown loan ID → 404 `LOAN_NOT_FOUND` (spec Edge Cases)
- [X] T050 [P] [US4] Write `DbTests/EndToEnd/LoanErrorEndToEndTests.cs`: `LOAN_ID_INVALID` and `LOAN_NOT_FOUND` through the real pipeline against tenant A

### Implementation for User Story 4

- [X] T051 [US4] Implement `Api/Loans/LoanIdParser.cs`: match the raw route string against `^[1-9][0-9]*$`, then range-check against the SPI loan ID type confirmed in T007; no normalization (FR-016, research R8). Call it from `Api/Loans/BalanceSummaryEndpoint.cs` before the repository, returning 400 `LOAN_ID_INVALID` with field `loanId`, and return 404 `LOAN_NOT_FOUND` when the repository returns null
- [X] T052 [US4] T007 found the loan ID type is `int`: add `maximum: 2147483647` to the `loanId` path parameter and the `loanId` response property in `specs/001-loan-balance-summary/contracts/loan-balance-summary.openapi.yaml` (allowed before first release; research R8)

**Checkpoint**: All user stories work independently

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Performance, documentation, local test run, decisions, and PR readiness

- [X] T053 [P] Create `tools/load-check.ps1`: a local script that sends a sustained 10 requests/second per tenant to both tenants (20 requests/second total) for 60 seconds and prints the p95 latency and error count only, no response bodies (SC-006, research R14). **Note (2026-10-26)**: script written. **Run (2026-10-26)**: API run locally in Release against both test databases (`rw_api`), with two test keys kept only in git-ignored local files. 10 requests/second per tenant, 2 tenants, 60 seconds: 1200 requests, 0 errors, p95 94 ms; SC-006 met. Quickstart smoke checks (step 6) also passed
- [X] T054 [P] Update `specs/001-loan-balance-summary/checklists/requirements.md` for the reduced scope: remove items about the out-of-scope fields and Other bucket names
- [X] T055 Walk through `specs/001-loan-balance-summary/quickstart.md` end to end on the local databases (apply scripts and create logins, prepare tenant B, create keys, configure, run, smoke checks, tests) and fix any step that is wrong in `specs/001-loan-balance-summary/quickstart.md`. **Note (2026-10-26)**: the database steps (prerequisites, steps 1, 2, and 7) now match the real setup: a remote SQL Server reached with an owner-level SQL login through `SQLCMDSERVER`, `SQLCMDUSER`, and `SQLCMDPASSWORD` (`sqlcmd -d` only, never `-S`, `-U`, `-P`, or `-E`; no Windows authentication), and `RW_MIGRATE_A`/`RW_MIGRATE_B` built in the session only; research R9 and R12 match. The migrator loop is the one used for T064; the step 1 check command was run on both databases. The one-time login step and B001 were not rerun (already applied). Steps 3 to 6 are exercised by T053
- [X] T056 Run the full local test suite with `RW_DB_TESTS=1` and the quickstart step 7 variables (unit and database) and `tools/load-check.ps1`; all tests pass with 0 parity mismatches and p95 under 1 s. Keep `TestResults/run-summary.txt` for the PR (date, database name, loans checked, passed and failed counts only). **Run (2026-10-26)**: unit 148 passed, database 25 passed, 0 failed, 0 skipped; parity 0 mismatches over 400 active loans; load check p95 94 ms. `TestResults/run-summary.txt` (git-ignored, counts only) kept for the PR
- [X] T057 Before the PR, record the decisions in `specs/001-loan-balance-summary/plan.md` and `specs/001-loan-balance-summary/spec.md`: O1 option (b) with the Principle IV deviation in Complexity Tracking, O2 confirmed, O3 allowed, and O1–O3 closed in the spec's Open Questions (done 2026-10-23). If T038 finds a status that does not show 0, update O2 here after Farzin's answer. **Note (2026-10-26)**: T038 found that; O2 was reopened and Farzin's option B is recorded in the spec, contract, plan, research, data-model, and these tasks. Run `git diff --cached` for secrets and real data before every commit
- [ ] T058 Open the pull request to `main` with the run summary from T056 and the T038 note ("manual per-status check done, results sent privately"), with no loan values or client data. The PR description names all four Principle IV deviations in Complexity Tracking (O1 option (b); O2, non-active principal not available; the T031 principal reference, O4; and O5, active records with SPI data issues); the PR review approval is Farzin's sign-off on all four. After merge, set **Status** to `Done` in `specs/001-loan-balance-summary/spec.md`. **Note (2026-10-27)**: the PR is open and Farzin reviewed it (O1 and O2 approved, O4 revised); T072 updates its description, and this task stays open until merge

---

## Phase 8: Pull request review changes (Farzin, 2026-10-27)

**Purpose**: O4 revised (automated principal comparison in T031), a wider manual screen check,
and the O2 note on paid-off and cancelled records. No loan IDs, amounts, or other client data in
the repository, the PR, or test output outside git-ignored `TestResults/`.

- [X] T066 [US1] Confirm locally that `rw_parity_ref` can select from `dbo.lrFnGetBalanceSummary` on `RW_REF_A` and `RW_REF_B`: run `SELECT TOP (0) principalRemaining FROM dbo.lrFnGetBalanceSummary(0, GETDATE());` as `rw_parity_ref` (reads and writes nothing). `db_datareader`'s database-level `SELECT` is expected to cover it, and the call must not reach `AlphaSales`. If it fails with permission error 229, add only `GRANT SELECT ON dbo.lrFnGetBalanceSummary TO rw_parity_ref;` to `specs/001-loan-balance-summary/quickstart.md` step 1 and apply it to both test databases (test-only grant, as O3 allows). Record only "inherited" or "granted" in the task note (research R4, R9). **Note (2026-10-27)**: inherited on both databases (`HAS_PERMS_BY_NAME` 1, `SELECT TOP (0)` succeeds); no grant needed, quickstart unchanged
- [X] T067 [US1] Update `DbTests/Parity/ActiveLoanParityTests.cs` (T031) to compare principal for every loan record with `LoanStatusId = 1` in `AlphaPMS_ResortWise`, automatically (O4 revised): right after the API request, select `principalRemaining` from `dbo.lrFnGetBalanceSummary(@LoanId, GETDATE())` on `RW_REF_A` (read-only, parameterized). **Rounding**: the test rounds the reference itself with `Math.Round(value, 2, MidpointRounding.AwayFromZero)`, never by reusing the view's `CAST`, and compares it with the API's `principalBalance` by exact `decimal` equality, including that the JSON number has exactly two decimal places. **Mapping**: the reference is called with the same loan ID the API was asked for and its `principalRemaining` column is compared with `principalBalance`. **Nulls**: the function must return exactly one row with a non-null value, and the API must return a number (not the JSON `null` token) with `principalBalanceAvailable` `true`; any other case is a mismatch. If `CAST(GETDATE() AS date)` read before and after differs (midnight), repeat that loan once. Failures name the loan ID and field only, in `TestResults/`; no amounts in the run summary. Report the number of principal comparisons to `RunSummary`. A comment in the test states that this catches rounding, mapping, and null errors across all active loans but does not prove the screen value, which the Excel engine calculates (plan, Complexity Tracking). After T066 and T068 (SC-001, FR-021, research R4). **Note (2026-10-27)**: done; run locally: 400 active loans, principal compared on all 400, 0 mismatches
- [X] T068 [P] [US1] Update `UnitTests/Scripts/TestCodeSpiCallTests.cs` (T059): allow `lrFnGetBalanceSummary` in exactly two files, `ApiLoginPermissionTests.cs` (T028) and `ActiveLoanParityTests.cs` (T067), and keep failing on it in any other file; add accept cases for both files and a reject case for another file. The approved procedures and tables are unchanged. Update the class comment to cite O4 revised (research R4, R9). **Note (2026-10-27)**: done; the allowlist comment cites O4 revised
- [X] T069 [US1] Manual check, not committed: in the SPI Application, compare Principal Remaining on the Balance Summary tab with the API's `principalBalance` (local API, tenant A) for 20 active loans in `AlphaPMS_ResortWise`. Pick the loans **by rule, never by listing** (rule updated 2026-10-27). Candidates are active loan records with `ID % 10` not 3 or 7 (the residues B001 uses). *Delinquent*: `lrLoanReceivable.NextDueDate` before today, and equal to the `NextDueDate` of the latest posted `lrTransaction` where one exists (confirmed by counts to agree on almost every loan with posted transactions). *Partly paid*: at least one posted `lrTransaction` with `prnAmountPaid > 0`, and principal from `rw.LoanPrincipalBalance` above zero and below `LoanAmount`. Groups: (1) 10 delinquent loans, the lowest IDs with `ID % 20 = 9`; (2) every partly paid loan that is not delinquent, up to 10, in residue order starting at 9 (`((ID % 20) - 9 + 20) % 20`), then ID; only 9 qualify in `AlphaPMS_ResortWise`; (3) 1 loan that is both partly paid and delinquent, not already in group 1, in the same residue order, making 20. The selection query runs read-only through `rw_parity_ref` and is kept in a private file outside the repository; the API principal is read with the API repository's query through `rw_api`. The list (owner ID, contract ID, loan ID, type, API principal, screen principal) is kept only in git-ignored `TestResults/manual-check-20.txt`, never committed. Keep loan IDs and values private and send them to Farzin. Any mismatch goes to Farzin before merge (Principle IV: record the loan and both values privately and escalate; never pick one). Record only "done, N loans checked, M matched, results sent privately" in the PR. **Updated 2026-10-27 (O5)**: the check found active loans whose screen principal differs from the function's value, which led to O5. Candidates now also exclude active loans matching an O5 rule (their principal is `null`, so there is nothing to compare). Rerun the selection by the same rule after T081; loans that drop out are reported privately as "covered by O5", and the mismatches already found go to Farzin as found. **Note (2026-10-27)**: selection rerun read-only with O5 loans excluded; group 3 now fills the list to 20, because fewer partly paid loans qualify once O5 loans are excluded. The list is in git-ignored `TestResults/manual-check-20.txt`; screen values already found are kept for loans still in the list. Screen check of the new loans still to do. **Updated 2026-10-28**: the cross-loan rule was dropped, so rerun the selection with only payoff-flag loans excluded after T095 (T096). **Done (2026-10-28)**: 20 loans checked on the SPI screen, 17 matched; the 3 that differed all have the payoff flag set and now return not available (`PAID_OFF_FLAG_SET`). Results sent privately
- [X] T070 [P] [US1] Update `UnitTests/Loans/NullPrincipalSerializationTests.cs` (T061) with synthetic paid-off and cancelled records (synthetic status names and non-active status IDs, no SPI data): each returns `principalBalance` as the JSON `null` token (never `0.00`), `principalBalanceAvailable` `false`, and `principalUnavailableReason` `LOAN_NOT_ACTIVE` (FR-008a, O2 note from Farzin, 2026-10-27). The requirement that the engine feature covers paid-off and cancelled records (the screen likely shows `0.00`) is recorded in the spec's Out of Scope and must be carried into that feature's spec. **Note (2026-10-27)**: test written and builds with 0 errors; **not yet run**, because a Windows Application Control policy blocks loading the built test assemblies on this machine (0x800711C7). **Verified (2026-10-27)**: passed on GitHub Actions (ci run #2, commit `e31f105`), which runs the unit tests; not run locally because Application Control blocks the test DLLs
- [X] T071 Rerun the full local test suite as in T056 (unit and database, `RW_DB_TESTS=1`, quickstart step 7 variables) after T066–T068 and T070; all tests pass with 0 parity mismatches, now including principal. Replace `TestResults/run-summary.txt` (date, database name, loans checked, passed and failed counts only). Record counts only in the task note. **Run (2026-10-27)**: unit 150 passed, database 25 passed, 0 failed, 0 skipped; parity 0 mismatches over 400 active loans, principal included; the load check was not rerun (no API code changed since T056)
- [X] T072 Update the open pull request description: O1 and O2 approved, O4 revised (principal compared automatically with `dbo.lrFnGetBalanceSummary`; catches rounding, mapping, and null errors but does not prove the screen value), the O2 note on paid-off and cancelled records, O5 (the fourth Principle IV deviation: the payoff flag reason code and why, and that the cross-loan rule was dropped on 2026-10-28 (T097), signed off by Farzin's approval; attribute O5 exactly as the spec's O5 entry does, never as Farzin's decision), the T069 note, and the T082 run summary (it replaces T071's). No loan IDs, amounts, counts of O5 loans, or other client data. After T069 and T082. **Note (2026-10-28)**: superseded by T097, which updates the same description after the cross-loan rule was dropped

---

## Phase 9: Active loans with SPI data issues (O5, 2026-10-27)

**Purpose**: Implement O5. An active record with the payoff flag set, or on a contract with a
cross-loan reversal, returns principal `null`, not available, with reason `PAID_OFF_FLAG_SET` or
`CROSS_LOAN_REVERSAL` (spec FR-008a; data-model.md). O5 proposed by Yaswanth on 2026-10-27. Farzin agreed with the approach on 2026-10-27, pending his PR approval, on two conditions: both rules go into the engine feature's scope, and the per-pattern counts were sent to him privately. The cross-loan rule was
dropped on 2026-10-28 (Phase 11). No loan IDs, amounts, or counts of O5 loans
in the repository, the PR, or test output outside git-ignored `TestResults/`. Tests T073–T078
first; each fails until T079–T081 are done.

- [X] T073 [P] [US1] Update `UnitTests/Contract/ContractConsistencyTests.cs` (T060): the `principalUnavailableReason` enum is exactly `LOAN_NOT_ACTIVE`, `PAID_OFF_FLAG_SET`, `CROSS_LOAN_REVERSAL` and equals the constants in `Api/Loans/PrincipalUnavailableReasons.cs`; the other T060 checks are unchanged (O5). **Note (2026-10-27)**: done; unit tests pass locally
- [X] T074 [P] [US1] Write `UnitTests/Loans/ActivePrincipalUnavailableTests.cs` with `WebApplicationFactory` and the fake repository, using synthetic rows only (synthetic loan IDs and status names, no SPI data): (a) an active row with `PayOffFlagSet` gives 200, `principalBalance` as the JSON `null` token (never `0` or `0.00`), `principalBalanceAvailable` `false`, and `principalUnavailableReason` `PAID_OFF_FLAG_SET`; (b) an active row with `CrossLoanReversal` gives the same with `CROSS_LOAN_REVERSAL`; (c) an active row with both flags gives `PAID_OFF_FLAG_SET`; (d) a non-active row with either or both flags gives `LOAN_NOT_ACTIVE`; (e) an active row with either flag and a non-null principal gives 500 `INTERNAL_ERROR`; (f) an active row with neither flag and a non-null principal is unchanged (available, no reason); (g) the 200 body for (a) and (b) has exactly the fields of a non-active body (T062). Covers both reason codes (FR-008a, O5; data-model.md guards). **Note (2026-10-27)**: done; unit tests pass locally
- [X] T075 [P] [US1] Update `UnitTests/Scripts/RwScriptScannerTests.cs` (T012): also fail if any `db/rw` script names an amount column of `lrLoanReceivable` or `lrTransaction` (a fixed list in the test of those tables' `money` column names, names only, no data) or uses `SUM(` or `AVG(`, so `rw` views can detect conditions but never calculate an amount (O5; research R9). Add an accept case for a synthetic flag-only view and reject cases for one that names `prnAmountPaid` and one that uses `SUM(`. `V001`–`V003` must still pass. **Note (2026-10-27)**: done; `V001`–`V004` pass; unit tests pass locally
- [X] T076 [P] [US1] Update `UnitTests/Scripts/TestCodeSpiCallTests.cs` (T068): allow the SPI table `lrTransaction` in test code (read-only `SELECT`, used by T077 to find the O5 loans); the approved procedures, the function rules, and the other tables are unchanged (research R4). **Note (2026-10-27)**: done; unit tests pass locally
- [X] T077 [US1] Update `DbTests/Parity/ActiveLoanParityTests.cs` (T031, T067) for O5. With one read-only, parameter-free `SELECT` on `RW_REF_A` over `lrLoanReceivable` and `lrTransaction`, written in the test and independent of `V004` (never reading `rw` views for the expectation), classify every active loan record: `PAID_OFF_FLAG_SET` when `IsPayOff = 1`; else `CROSS_LOAN_REVERSAL` when a `TransTypeid = 1` transaction on any loan record of its contract has a `REVApplyID` pointing to a transaction on a different loan record of the same contract; else available. Loans are selected by these rules, never listed. For **available** loans: compare principal exactly as in T067. For **every O5 loan**: `principalBalance` is the JSON `null` token, `principalBalanceAvailable` is `false`, and `principalUnavailableReason` is the expected code; the function is not called for them. `loanStatus` and `asOfDate` are still compared for every active loan. `RunSummary` (which is pasted into the PR) prints only the total loans checked and the passed and failed counts; the number of principal comparisons and the number of O5 checks per reason go only to a separate git-ignored file, `TestResults/o5-checks.txt`, never committed and never pasted into the PR (analyze I2). Fail if either reason count is 0 in `AlphaPMS_ResortWise`, because the rule would then go untested. Failures name the loan ID and field only. Update the test comment to cite O5 (SC-001, FR-008a). **Note (2026-10-27)**: written (the O5 rule is shared with T078 in `DbTests/Infrastructure/O5Rules.cs`); builds; not yet run against the databases (needs T080)
- [X] T078 [US3] Update `DbTests/Isolation/TenantIsolationTests.cs` (T043): for the `ID % 10 = 3` active records, tenant A's expectation follows the O5 rules (classified as in T077 on `RW_REF_A`): available records return a non-null principal and `principalBalanceAvailable` `true`; O5 records return `null` with their O5 reason. Tenant B still returns the T045 non-active status with `LOAN_NOT_ACTIVE` for all of them (status comes first), so the responses still differ (SC-002). The 404 and 403 checks are unchanged. **Note (2026-10-27)**: written; builds; not yet run against the databases (needs T080)
- [X] T079 [US1] Create `db/rw/V004__view_loan_principal_balance_o5_flags.sql`: `CREATE OR ALTER VIEW rw.LoanPrincipalBalance` with columns `LoanId`, `AsOfDate`, `PayOffFlagSet` (`bit`, not null), `CrossLoanReversal` (`bit`, not null), and `PrincipalBalance`, exactly as in data-model.md. `PrincipalBalance` is the `V003` value for an active record with both flags `0`, and `CAST(NULL AS decimal(19,2))` otherwise, without applying the function. The flags read only `lrLoanReceivable.ID`, `SalesContractId`, `IsPayOff` and `lrTransaction.ID`, `LoanId`, `TransTypeid`, `REVApplyID`; no amount column, no aggregate, no calculation. Still only `dbo.lrFnGetBalanceSummary`, read-only. `V003` is already applied and is not edited. The T012 and T075 scanners must pass. No loan IDs or values in the script (O5). **Note (2026-10-27)**: script written and passes the T012 and T075 scanners; not applied to any database yet (T080)
- [X] T080 [US1] Apply `V004` to both local test databases with `tools/ResortWise.RwMigrator` (quickstart step 1) and check, as in T034, that a query filtered on one `LoanId` evaluates the function for that loan only and the reversal check only for that loan's contract (actual execution plan). Record only "filter pushed down: yes/no" in the task note. **Note (2026-10-27)**: migrator rebuilt; `V004` applied to both test databases (journal now V001–V004); filter pushed down: yes
- [X] T081 [US1] Update `Api/Loans/Models/LoanBalanceRow.cs` (add `PayOffFlagSet`, `CrossLoanReversal`), `Api/Loans/LoanBalanceRepository.cs` (select both from `rw.LoanPrincipalBalance`), `Api/Loans/PrincipalUnavailableReasons.cs` (add `PAID_OFF_FLAG_SET` and `CROSS_LOAN_REVERSAL`), and `Api/Loans/BalanceSummaryEndpoint.cs`: pick the reason in the order not active → `PAID_OFF_FLAG_SET` → `CROSS_LOAN_REVERSAL`; `principalBalanceAvailable` is `true` only when none applies. Guards: a null principal when available, or a non-null principal when not available, is 500 `INTERNAL_ERROR` (data-model.md). The API calculates nothing and reads only `rw` views. Make T073, T074, T077, and T078 pass. **Note (2026-10-27)**: done; unit tests pass locally (170 passed). The repository now selects the `V004` columns, so the API needs `V004` applied (T080) before it runs against a database
- [X] T082 Rerun the full local test suite as in T071 (unit and database, `RW_DB_TESTS=1`, quickstart step 7 variables) after T073–T081; all tests pass with 0 parity mismatches and 0 O5 failures. If Application Control still blocks the unit test assemblies locally, record that and verify the unit tests on GitHub CI, as for T070. Replace `TestResults/run-summary.txt` (date, database name, loans checked, passed and failed counts only). Record counts only in the task note, with no count of O5 loans. Rerun `tools/load-check.ps1`, because `V004` adds a check per request; p95 must stay under 1 s. **Run (2026-10-27)**: unit 170 passed, database 25 passed, 0 failed, 0 skipped (unit tests ran locally; Application Control did not block them); parity 0 mismatches over 400 active loans; load check 1200 requests, 0 errors, p95 100 ms. `TestResults/run-summary.txt` replaced (counts only)
- [X] T083 [P] Update `specs/001-loan-balance-summary/quickstart.md`: add the two O5 reasons to the expected smoke results and to the test description (an active record with the payoff flag set or a cross-loan reversal returns principal `null` with `PAID_OFF_FLAG_SET` or `CROSS_LOAN_REVERSAL`), with no loan IDs or values. **Note (2026-10-27)**: done

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; can start immediately
- **Foundational (Phase 2)**: Depends on Setup; BLOCKS all user stories. T007 needs
  `AlphaPMS_ResortWise` restored locally, and T009, T033, T038, T047, T051, and T052 depend on
  its findings. T011 needs T008–T010. T026 and T028 need T011. O3 is closed, so T011 and every
  database task after it wait on no decision
- **User Stories (Phases 3–6)**: All depend on Foundational
- **Polish (Phase 7)**: Depends on all user stories
- **PR review changes (Phase 8)**: After Phase 7 up to T057. T066 → T067; T068 and T070 in
  parallel with T066; T069 any time (manual; its O5 rerun after T081); T071 after T066–T068 and
  T070; T072 after T069 and T082; T058 (merge) after T072
- **O5 (Phase 9)**: After T071. Tests T073–T078 first, in parallel except T077 and T078 after
  T076 (the test-code scanner must allow `lrTransaction`); then T079 → T080 (apply `V004`) and
  T081; T082 after T073–T081; T083 any time

### User Story Dependencies

- **US1 (P1)**: After Foundational. No dependency on other stories. The principal comparison in
  T031 uses the SPI SQL function (O1 option (b); O4 revised, T067), so no engine-harness tasks
  are needed
- **US2 (P1)**: After Foundational. Its tests use the fake repository and valid loan IDs, so it
  does not need US1 or US4
- **US3 (P1)**: After Foundational and US1 (it compares real balance summaries across tenants)
- **US4 (P2)**: After Foundational. T050 needs US1's endpoint

### Within Each User Story

- Tests are written first and fail before implementation
- `rw` scripts before the repository, the repository before the endpoint
- T038 (manual per-status check) and T057 (decisions recorded) must be done before the PR (T058)
- T032, T043, and T060–T065 follow O2 (option B, decided 2026-10-26: non-active principal
  `null`, reason `LOAN_NOT_ACTIVE`). Tests T032, T043, and T060–T062 first; then T063 → T064
  (apply `V003`) and T065. T043 also needs T063–T065 before it passes
- T073–T083 follow O5 (option B, proposed 2026-10-27: active records with the payoff flag set or
  a cross-loan reversal return principal `null`, reason `PAID_OFF_FLAG_SET` or
  `CROSS_LOAN_REVERSAL`). T079 needs T075, because the new scanner rule must already pass on
  `V004`
- T088–T097 drop the cross-loan rule (2026-10-28). Tests T089–T092 first; then T093 (`V005`) and
  T094 (API). T095 (apply `V005`) waits for the user's approval; T096 needs T095; T097 needs T096
- T011 and T028 follow O3 (test-only users and grants, allowed)

### Parallel Opportunities

- Setup: T003–T006 in parallel after T001–T002
- Foundational: T012–T017, T019, and T021–T023 in parallel after T007–T011; T018 after T014;
  T020 after T014 and T016; T024 after T021; T025 after T018–T024; T026–T028 after T011 and T025; T059 any time after T026
- US1: T029–T032 in parallel; T035 in parallel with T033; T060–T062 in parallel with T032
- US2: T039–T041 in parallel
- US4: T047–T050 in parallel
- US2 and US4 can run in parallel with US1 once Foundational is done

---

## Parallel Example: User Story 1

```text
# Tests for User Story 1 together:
Task: "T029 Contract consistency tests in tests/ResortWise.LoanBalance.UnitTests/Contract/ContractConsistencyTests.cs"
Task: "T030 Endpoint tests in tests/ResortWise.LoanBalance.UnitTests/Loans/BalanceSummaryEndpointTests.cs"
Task: "T031 Active loan parity tests in tests/ResortWise.LoanBalance.DbTests/Parity/ActiveLoanParityTests.cs"
Task: "T032 Non-active loan tests in tests/ResortWise.LoanBalance.DbTests/Parity/NonActiveLoanTests.cs"

# Then the view and the model together:
Task: "T033 rw.LoanPrincipalBalance in db/rw/V002__view_loan_principal_balance.sql"
Task: "T035 Response model in src/ResortWise.LoanBalance.Api/Loans/Models/LoanBalanceSummaryResponse.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (includes credential and tenant checks and the `rw`-only API
   login, which no story may skip)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run the US1 parity and non-active tests locally, and do the T038
   per-status screen check
5. Not releasable yet: the constitution requires the isolation and error-code tests (US2–US4)
   before merge, and the Principle IV deviation (O1) needs Farzin's PR approval

### Incremental Delivery

1. Setup + Foundational → requests are authenticated, tenant-checked, and audited
2. US1 → balance summaries with parity (MVP)
3. US2 → refusal paths proven
4. US3 → tenant isolation proven
5. US4 → input errors proven
6. Polish → local run summary, decisions recorded (T057, done), PR

---

## Notes

- [P] tasks touch different files and have no dependencies on incomplete tasks
- Commit after each task or logical group, checking `git diff --cached` first
- Out of scope (separate engine feature): interest, fees, unapplied, Others 1 to 5, total
  balance, past due, payoff. Do not add tasks or fields for them
- The API serves no OpenAPI endpoint; the contract is published with the repository (research
  R11)

---

## Phase 10: Convergence

- [X] T084 Change the cross-loan reversal wording from "reverses a payment" to "reverses a transaction of any type" in `specs/001-loan-balance-summary/spec.md` (the O5 clarification, US1 scenario 6, Edge Cases, FR-008a, O5), `specs/001-loan-balance-summary/contracts/loan-balance-summary.openapi.yaml` (operation description and `principalUnavailableReason`), and the O5 note at the top of `specs/001-loan-balance-summary/tasks.md`, to match `V004`, `O5Rules.cs`, and the plan's A1 decision per FR-008a (contradicts). **Note (2026-10-27)**: done (wording only)
- [X] T085 State in `specs/001-loan-balance-summary/data-model.md` (`CrossLoanReversal`) that the reversal check counts transactions whether posted or not, as `V004` does and research R2 records, per plan: per-loan-record rule 3 (partial). **Note (2026-10-27)**: done (wording only)
- [X] T086 Add the O5 reason codes `PAID_OFF_FLAG_SET` and `CROSS_LOAN_REVERSAL` to the `principalUnavailableReason` item in `specs/001-loan-balance-summary/checklists/requirements.md` per FR-002 and FR-008a (partial). **Note (2026-10-27)**: done (wording only)
- [X] T087 Correct the O5 attribution in `specs/001-loan-balance-summary/spec.md`, `plan.md`, `research.md`, `data-model.md`, these tasks, and the PR description text (T072): O5 was proposed by Yaswanth on 2026-10-27, and Farzin agreed with the approach, pending his PR approval, on two conditions (both rules go into the engine feature's scope; the per-pattern counts were sent to him privately). He did not decide it. `V004`'s header comment has the same wrong wording; `V004` is applied and never edited, so the correction is recorded in the plan. Add the spec section "Handed to the engine feature" (both O5 rules, to be replaced by the engine's own logic, and the O2 paid-off and cancelled note). **Note (2026-10-27)**: done (wording only)

---

## Phase 11: Drop the cross-loan rule (O5, 2026-10-28)

**Purpose**: The cross-loan rule (`CROSS_LOAN_REVERSAL`) was dropped on 2026-10-28 because it came from a misread screen value; O5 is now only the payoff flag rule, still proposed by Yaswanth and pending Farzin's PR approval. A screen check found that loans the rule flagged show the same
principal on the screen as `dbo.lrFnGetBalanceSummary`, so the rule had no confirmed case and hid
correct values. Keep `PAID_OFF_FLAG_SET`. Remove `CROSS_LOAN_REVERSAL` from the reason codes, the
contract, and the API constants before first release (plan, Principle VIII). The view change is a
new script, `V005`; `V004` is applied and is not edited. No loan IDs, amounts, or counts in the
repository, the PR, or test output outside git-ignored `TestResults/`.

- [X] T088 Update `specs/001-loan-balance-summary/spec.md`, `contracts/loan-balance-summary.openapi.yaml`, `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, `checklists/requirements.md`, and these tasks: O5 is only the payoff flag rule, still proposed by Yaswanth and pending Farzin's PR approval; record that the cross-loan rule was dropped on 2026-10-28 because it came from a misread screen value; remove `CROSS_LOAN_REVERSAL` from the reason codes (contract enum, description, and example); the spec's "Handed to the engine feature" lists only the payoff flag rule. **Note (2026-10-28)**: done (wording only)
- [X] T089 [P] [US1] Update `UnitTests/Contract/ContractConsistencyTests.cs` (T073): the `principalUnavailableReason` enum is exactly `LOAN_NOT_ACTIVE`, `PAID_OFF_FLAG_SET` and equals `PrincipalUnavailableReasons.All`. **Note (2026-10-28)**: done; unit tests pass locally (164 passed)
- [X] T090 [P] [US1] Update `UnitTests/Loans/ActivePrincipalUnavailableTests.cs` (T074) and `UnitTests/Infrastructure/FakeLoanBalanceRepository.cs` for a row with only `PayOffFlagSet`: remove the cross-loan cases; keep (a) payoff flag → `PAID_OFF_FLAG_SET`, (d) non-active with the flag → `LOAN_NOT_ACTIVE`, (e) active with the flag and a non-null principal → 500, (f) active without the flag → available, and (g) the body shape. Synthetic rows only. **Note (2026-10-28)**: done; unit tests pass locally (164 passed)
- [X] T091 [US1] Update `DbTests/Infrastructure/O5Rules.cs`, `DbTests/Parity/ActiveLoanParityTests.cs` (T077), and `DbTests/Isolation/TenantIsolationTests.cs` (T078): classify active records only by `lrLoanReceivable.IsPayOff = 1` (`PAID_OFF_FLAG_SET`), with no read of `lrTransaction`; every other active record is available and its principal is compared with `dbo.lrFnGetBalanceSummary` as in T067. Fail if the `PAID_OFF_FLAG_SET` count is 0. `TestResults/o5-checks.txt` keeps only the principal-compared and `PAID_OFF_FLAG_SET` counts. **Note (2026-10-28)**: written; builds; not yet run against the databases (needs T095)
- [X] T092 [P] Update `UnitTests/Scripts/TestCodeSpiCallTests.cs` (T076): `lrTransaction` is no longer allowed in test code, because no test reads it after T091 (research R4). **Note (2026-10-28)**: done; unit tests pass locally (164 passed)
- [X] T093 [US1] Create `db/rw/V005__view_loan_principal_balance_drop_cross_loan.sql`: `CREATE OR ALTER VIEW rw.LoanPrincipalBalance` with columns `LoanId`, `AsOfDate`, `PayOffFlagSet`, and `PrincipalBalance`, exactly as in data-model.md. `PrincipalBalance` is the function's value for an active record with `PayOffFlagSet = 0`, and `NULL` otherwise, without applying the function. Reads only `lrLoanReceivable` and `dbo.lrFnGetBalanceSummary`; no `lrTransaction`, amount column, aggregate, or calculation. `V004` is not edited. The T012 and T075 scanners must pass. No loan IDs or values. **Note (2026-10-28)**: script written and passes the T012 and T075 scanners; not applied to any database yet (T095)
- [X] T094 [US1] Update `Api/Loans/Models/LoanBalanceRow.cs`, `Api/Loans/LoanBalanceRepository.cs`, `Api/Loans/PrincipalUnavailableReasons.cs`, and `Api/Loans/BalanceSummaryEndpoint.cs`: remove `CrossLoanReversal` and `CROSS_LOAN_REVERSAL`; the reason order is not active → `PAID_OFF_FLAG_SET`. The repository then needs `V005` applied (T095) before it runs against a database. Make T089 and T090 pass. **Note (2026-10-28)**: done; unit tests pass locally (164 passed). The repository no longer selects `CrossLoanReversal`, so the API needs `V005` applied (T095) before it runs against a database
- [X] T095 [US1] Apply `V005` to both local test databases with `tools/ResortWise.RwMigrator` (quickstart step 1), after the user approves, and check as in T080 that a query filtered on one `LoanId` evaluates the function for that loan only. Record only "filter pushed down: yes/no". **Note (2026-10-28)**: approved; `V005` applied to both test databases (journal now V001–V005); filter pushed down: yes (both databases)
- [X] T096 Rerun the full local test suite and `tools/load-check.ps1` as in T082 after T089–T095; replace `TestResults/run-summary.txt` (counts only). Rerun the T069 selection with only payoff-flag loans excluded; loans that come back are rechecked on the screen privately. **Run (2026-10-28)**: unit 164 passed, database 25 passed, 0 failed, 0 skipped; parity 0 mismatches over 400 active loans; load check 1200 requests, 0 errors, p95 98 ms. `TestResults/run-summary.txt` replaced (counts only). T069 selection rerun read-only into git-ignored `TestResults/manual-check-20-rerun.txt` (the screen-checked list in `manual-check-20.txt` is kept); details kept privately
- [X] T097 Update the PR description text (T072): O5 is only the payoff flag rule; the cross-loan rule was dropped on 2026-10-28 because it came from a misread screen value; the T096 run summary. No loan IDs, amounts, or counts of O5 loans. **Note (2026-10-28)**: text written with the T096 run summary and the T069 result (20 checked, 17 matched, the 3 that differed have the payoff flag set and now return not available, results sent privately); it is pasted into the PR on GitHub by hand, because `gh` is not installed here
