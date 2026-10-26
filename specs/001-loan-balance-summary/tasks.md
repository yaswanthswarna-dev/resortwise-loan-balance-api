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
the Excel engine is not used in any way. **O2**: non-active loan records return principal `0.00`.
**O3**: the test-only users and `EXECUTE` grants on read-only SPI procedures are allowed. No task
waits on an open decision.

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
- [X] T059 [P] Write `UnitTests/Scripts/TestCodeSpiCallTests.cs`: scan every `.cs` file under `tests/ResortWise.LoanBalance.DbTests`; fail if it names any `dbo.` procedure or function other than `lrGetLoanDetails`, `lrGetLoanStatusTypes`, and `lrGetTransactionActivity` (procedures), plus `lrFnGetBalanceSummary` only in T028's permission check. Selecting from SPI tables is allowed (research R4, R9; decided 2026-10-23). The SPI tables tests read are listed too (`lrLoanReceivable`, `lrLoanStatus`), because a static scan cannot tell a table from a procedure; adding one is a reviewed one-line change

**Checkpoint**: Foundation ready. The `rw` schema, journal, and `rw.Loan` apply cleanly to both
test databases, the API login is proven `rw`-only, and every request is authenticated,
tenant-checked, and audited. User story work can begin

---

## Phase 3: User Story 1 - Get a loan's balance summary (Priority: P1) 🎯 MVP

**Goal**: An authorized caller gets loan ID, loan status, as-of date, and principal balance for
one loan record, matching the SPI parity sources (FR-001–FR-003, FR-006–FR-008a).

**Independent Test**: With a caller allowed the tenant mapped to `AlphaPMS_ResortWise`, request
each active loan and compare every field with the parity references read automatically from SPI
(FR-021); every non-active record returns its own status and principal `0.00`.

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T029 [P] [US1] Write `UnitTests/Contract/ContractConsistencyTests.cs`: load `specs/001-loan-balance-summary/contracts/loan-balance-summary.openapi.yaml` (linked into the UnitTests project as a `None` item with `CopyToOutputDirectory`) with Microsoft.OpenApi.Readers; the app's routes equal the contract's paths; the `Error.code` enum equals the `ErrorCodes` constants; `LoanBalanceSummary` required fields are exactly `loanId`, `loanStatus`, `asOfDate`, `principalBalance` and match the response model
- [X] T030 [P] [US1] Write `UnitTests/Loans/BalanceSummaryEndpointTests.cs` with `WebApplicationFactory` and an in-memory fake `ILoanBalanceRepository`: 200 body has exactly the four fields; `principalBalance` is serialized with two decimal places, including `0.00` (FR-006); a negative principal is returned unchanged; the body contains no payoff field and none of the out-of-scope fields (FR-007)
- [ ] T031 [P] [US1] Write `DbTests/Parity/ActiveLoanParityTests.cs`: for every loan record with `LoanStatusId = 1` in `AlphaPMS_ResortWise` (read-only `SELECT` on `RW_REF_A`), call the API through tenant A and compare: `loanStatus` with `lrGetLoanDetails` (`@Loanid`, `@SalesContractId`; status ID) and `lrGetLoanStatusTypes` (no parameters; name) on `RW_REF_A`, exact ordinal string; `asOfDate` equals `CAST(GETDATE() AS date)` read on `RW_REF_A` just before or just after the request; `principalBalance`, exact `decimal` at 2 places, against `PrincipalRemaining` from the second result set of `lrGetTransactionActivity` (`@Ownerid`, `@SalesContractId`, called per contract) on `RW_REF_A`, after the same `CAST(... AS decimal(19,2))` (O1 option (b); the Excel engine is not used). Each loan's owner and contract IDs are read at run time on `RW_REF_A` and never logged or saved. All SPI calls are read-only. Failures name the loan ID and field only in `TestResults/`. Report the count to `RunSummary` (SC-001, FR-021, research R4). **Blocked (2026-10-26)**: the test is written, but `lrGetTransactionActivity` fails on `RW_REF_A` because `rw_parity_ref` cannot access the database `AlphaSales`, which the procedure reaches indirectly (no direct reference in `sys.sql_expression_dependencies`). Waiting for a decision on how the reference login may read it, or on another principal reference
- [X] T032 [P] [US1] Write `DbTests/Parity/NonActiveLoanTests.cs`: for every loan record with `LoanStatusId <> 1` in `AlphaPMS_ResortWise` (selected on `RW_REF_A`), the API returns 200, the record's own status name, and `principalBalance` `0.00` (FR-008a; plan, per-loan-record rule; O2)

### Implementation for User Story 1

- [X] T033 [US1] Create `db/rw/V002__view_loan_principal_balance.sql`: `CREATE OR ALTER VIEW rw.LoanPrincipalBalance` with one row per loan record and columns `LoanId`, `AsOfDate` = `CAST(GETDATE() AS date)`, and `PrincipalBalance`: for `LoanStatusId = 1`, the principal from `dbo.lrFnGetBalanceSummary(LoanId, GETDATE())` (with `CROSS APPLY` or in the select list, per the type confirmed in T007); for any other status, `CAST(0 AS decimal(19,2))` without calling the function. T007 found `principalRemaining` is `money` (scale 4): use `CAST(principalRemaining AS decimal(19,2))`, which rounds half away from zero as the screen does (research R2), and the parity test T031 confirms it. No other SPI function and no procedure (data-model.md, `rw.LoanPrincipalBalance`)
- [X] T034 [US1] Apply `V002` to both local test databases with `tools/ResortWise.RwMigrator` (quickstart step 1) and confirm locally that a query filtered on one `LoanId` evaluates the function for that loan only (actual execution plan; research R14). Record nothing from the plan beyond "filter pushed down: yes/no" in the task notes. **Note (2026-10-26)**: filter pushed down: yes
- [X] T035 [P] [US1] Create `Api/Loans/Models/LoanBalanceSummaryResponse.cs` with exactly `loanId` (int64), `loanStatus` (string), `asOfDate` (date, `yyyy-MM-dd`), `principalBalance` (decimal, two places), and `Api/Loans/Models/LoanBalanceRow.cs` for the repository result
- [X] T036 [US1] Implement `Api/Loans/ILoanBalanceRepository.cs` and `Api/Loans/LoanBalanceRepository.cs`: one parameterized Dapper query joining `rw.Loan` and `rw.LoanPrincipalBalance` on `LoanId` with `WHERE LoanId = @LoanId`, through `TenantConnectionFactory`; it references `rw.` objects only and returns null when no row exists
- [X] T037 [US1] Implement `Api/Loans/BalanceSummaryEndpoint.cs` and map `GET /v1/loans/{loanId}/balance-summary` in `Api/Program.cs`: read through the repository and return the four fields; set `loanId` on the audit context; no recomputation or rounding beyond the view's (FR-003)
- [ ] T038 [US1] Manual check, not committed: in the SPI Application, open the Balance Summary tab for one loan record of **each** non-active status found in T007 (e) (one non-active status is in use in `AlphaPMS_ResortWise`: the status of old records left behind by a rewrite), in `AlphaPMS_ResortWise`, and note whether Principal Remaining shows 0. Keep the results private and send them to Farzin. O2 (`0.00`) is decided; any status that does not show 0 goes to Farzin before merge (plan). Record only "done, results sent privately" in the PR, with no loan IDs, statuses per loan, or values

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

- [ ] T043 [P] [US3] Write `DbTests/Isolation/TenantIsolationTests.cs`: for every loan the rule in T045 changes, tenant A and tenant B responses each match their own database, read on `RW_REF_A` and `RW_REF_B`, and differ (SC-002); for every loan the rule removes, tenant B returns 404 `LOAN_NOT_FOUND` byte-identical to a loan ID that exists in no tenant while tenant A still returns 200 (SC-005); a caller allowed only tenant A gets 403 `TENANT_FORBIDDEN` for `X-Tenant-Id: tenant-b` (SC-003). Loan IDs are selected by the same rule as T045, never listed

### Implementation for User Story 3

- [ ] T044 [US3] Confirm locally, from the schema and the read-only procedures' definitions, which SPI data `dbo.lrFnGetBalanceSummary` reads for principal and which child tables reference a loan record (foreign-key order for removal). Record table names only in `specs/001-loan-balance-summary/research.md` R12
- [ ] T045 [US3] Create `tests/db/setup/B001__diverge_tenant_b.sql`: first statement `IF DB_NAME() <> N'AlphaPMS_ResortWise_B' THROW 50001, 'Wrong database', 1;`; then `SET XACT_ABORT ON` and one transaction; change loans selected by rule (for example `LoanId % 10 = 3`) so their principal or status differs, by setting values derived from the rule (never increments, so a rerun gives the same state); remove loans selected by rule (for example `LoanId % 10 = 7`), deleting child rows first in the order found in T044. Data changes only: no `CREATE`, `ALTER`, or `DROP`. No loan IDs or values in the script (Test data rules). Run it by hand per quickstart step 2
- [ ] T046 [US3] Add `DbTests/Infrastructure/TenantBSetupCheck.cs`: before the isolation tests, check on `RW_REF_B` (read-only) that `DB_NAME()` is exactly `AlphaPMS_ResortWise_B` and that the rule's changes from T045 are present (for example, no loan record matching the removal rule remains); if not, fail with "run quickstart step 2". It never runs the setup script and needs no login that can write

**Checkpoint**: Isolation suite passes; tenant A data is unchanged

---

## Phase 6: User Story 4 - Clear, stable errors for bad input (Priority: P2)

**Goal**: Invalid and unknown loan IDs return stable codes, with the field name for input errors
(FR-014–FR-017).

**Independent Test**: Request a malformed loan ID and a well-formed unknown loan ID; confirm the
documented status, code, and field.

### Tests for User Story 4 ⚠️

- [ ] T047 [P] [US4] Write `UnitTests/Loans/LoanIdParserTests.cs`: `abc`, `-5`, `0`, `007`, ` 12`, `12 `, `1.0`, `+1`, and a value one above the maximum confirmed in T007 → invalid; `1` and the maximum → valid
- [ ] T048 [P] [US4] Write `UnitTests/Loans/LoanErrorTests.cs`: an invalid loan ID → 400 `LOAN_ID_INVALID` with field `loanId`, and the repository is not called; an unknown loan ID → 404 `LOAN_NOT_FOUND` with no `field`; no error message contains SQL, a stack trace, or exception text, including when the fake repository throws (FR-017)
- [ ] T049 [P] [US4] Write `UnitTests/Loans/LoanCheckOrderTests.cs` (moved from the US2 check-order test): a forbidden tenant with an invalid loan ID → 403 `TENANT_FORBIDDEN` (tenant authorization before loan ID format); an allowed tenant with an invalid loan ID → 400 `LOAN_ID_INVALID` and the repository is called 0 times (format before existence); an allowed tenant with a valid unknown loan ID → 404 `LOAN_NOT_FOUND` (spec Edge Cases)
- [ ] T050 [P] [US4] Write `DbTests/EndToEnd/LoanErrorEndToEndTests.cs`: `LOAN_ID_INVALID` and `LOAN_NOT_FOUND` through the real pipeline against tenant A

### Implementation for User Story 4

- [ ] T051 [US4] Implement `Api/Loans/LoanIdParser.cs`: match the raw route string against `^[1-9][0-9]*$`, then range-check against the SPI loan ID type confirmed in T007; no normalization (FR-016, research R8). Call it from `Api/Loans/BalanceSummaryEndpoint.cs` before the repository, returning 400 `LOAN_ID_INVALID` with field `loanId`, and return 404 `LOAN_NOT_FOUND` when the repository returns null
- [X] T052 [US4] T007 found the loan ID type is `int`: add `maximum: 2147483647` to the `loanId` path parameter and the `loanId` response property in `specs/001-loan-balance-summary/contracts/loan-balance-summary.openapi.yaml` (allowed before first release; research R8)

**Checkpoint**: All user stories work independently

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Performance, documentation, local test run, decisions, and PR readiness

- [ ] T053 [P] Create `tools/load-check.ps1`: a local script that sends a sustained 10 requests/second per tenant to both tenants (20 requests/second total) for 60 seconds and prints the p95 latency and error count only, no response bodies (SC-006, research R14)
- [ ] T054 [P] Update `specs/001-loan-balance-summary/checklists/requirements.md` for the reduced scope: remove items about the out-of-scope fields and Other bucket names
- [ ] T055 Walk through `specs/001-loan-balance-summary/quickstart.md` end to end on the local databases (apply scripts and create logins, prepare tenant B, create keys, configure, run, smoke checks, tests) and fix any step that is wrong in `specs/001-loan-balance-summary/quickstart.md`
- [ ] T056 Run the full local test suite with `RW_DB_TESTS=1` and the quickstart step 7 variables (unit and database) and `tools/load-check.ps1`; all tests pass with 0 parity mismatches and p95 under 1 s. Keep `TestResults/run-summary.txt` for the PR (date, database name, loans checked, passed and failed counts only)
- [X] T057 Before the PR, record the decisions in `specs/001-loan-balance-summary/plan.md` and `specs/001-loan-balance-summary/spec.md`: O1 option (b) with the Principle IV deviation in Complexity Tracking, O2 confirmed, O3 allowed, and O1–O3 closed in the spec's Open Questions (done 2026-10-23). If T038 finds a status that does not show 0, update O2 here after Farzin's answer. Run `git diff --cached` for secrets and real data before every commit
- [ ] T058 Open the pull request to `main` with the run summary from T056 and the T038 note ("manual per-status check done, results sent privately"), with no loan values or client data. With O1 option (b), the PR review approval is Farzin's sign-off on the deviation. After merge, set **Status** to `Done` in `specs/001-loan-balance-summary/spec.md`

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

### User Story Dependencies

- **US1 (P1)**: After Foundational. No dependency on other stories. The principal comparison in
  T031 uses the SQL path (O1 option (b)), so no engine-harness tasks are needed
- **US2 (P1)**: After Foundational. Its tests use the fake repository and valid loan IDs, so it
  does not need US1 or US4
- **US3 (P1)**: After Foundational and US1 (it compares real balance summaries across tenants)
- **US4 (P2)**: After Foundational. T050 needs US1's endpoint

### Within Each User Story

- Tests are written first and fail before implementation
- `rw` scripts before the repository, the repository before the endpoint
- T038 (manual per-status check) and T057 (decisions recorded) must be done before the PR (T058)
- T032 and T033 follow O2 (non-active principal `0.00`, decided). If T038 finds a status that
  does not show 0 and Farzin changes O2, update T032 and T033
- T011 and T028 follow O3 (test-only users and grants, allowed)

### Parallel Opportunities

- Setup: T003–T006 in parallel after T001–T002
- Foundational: T012–T017, T019, and T021–T023 in parallel after T007–T011; T018 after T014;
  T020 after T014 and T016; T024 after T021; T025 after T018–T024; T026–T028 after T011 and T025; T059 any time after T026
- US1: T029–T032 in parallel; T035 in parallel with T033
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
