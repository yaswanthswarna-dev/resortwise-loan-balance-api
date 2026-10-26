# Implementation Plan: Loan Balance Summary API

**Branch**: `001-loan-balance-summary` | **Date**: 2026-10-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-loan-balance-summary/spec.md`

**Status of this plan**: Design complete for the reduced scope (loan ID, loan status, as-of date,
principal balance). Farzin's decisions D1–D11 are closed (see [Decisions](#decisions)). O1, O2,
and O3 were decided on 2026-10-23 and are closed (see
[Open points for Farzin](#open-points-for-farzin)): O1 is option (b), the SQL function as the
principal parity reference with a documented Principle IV deviation; O2 confirms principal `0.00`
for non-active records; O3 allows the test-only users and grants. No open point blocks any task.

## Summary

A read-only .NET 8 minimal API returns one loan record's balance summary at
`GET /v1/loans/{loanId}/balance-summary`: loan ID, loan status, as-of date, and principal
balance. Each request is authenticated with a per-caller API key (bearer token), authorized
against a tenant registry held in configuration, and served only from the database mapped to the
requested tenant. The API reads SPI data only through views in the `rw` schema, created by
versioned scripts in this repository.

Principal balance comes from a `rw` view that selects, read-only, from the SPI function the
screen's SQL path uses (`dbo.lrFnGetBalanceSummary`), instead of rebuilding its logic. Constitution
v1.4.0 allows this (Principle III). Every other field the SPI Excel loan engine calculates is out
of scope and will be a separate feature, so this plan has no engine calculator and no engine
input views.

## Technical Context

**Language/Version**: C# 12 on .NET 8 (LTS)

**Primary Dependencies**: ASP.NET Core minimal API; Dapper; Microsoft.Data.SqlClient;
Microsoft.OpenApi.Readers (contract tests only); DbUp (`dbup-sqlserver`) for applying `rw` scripts

**Storage**: Microsoft SQL Server, one SPI database per tenant; read-only through `rw` views

**Testing**: xUnit. Unit tests (no database) run on GitHub. Database tests (parity, isolation,
end-to-end) run locally only, against `AlphaPMS_ResortWise` and `AlphaPMS_ResortWise_B`

**Target Platform**: Linux container (`mcr.microsoft.com/dotnet/aspnet:8.0`), cloud-agnostic

**Project Type**: Web service (single API project plus a migration tool)

**Performance Goals**: p95 under 1 s at 10 requests/second per tenant, 2 tenants (SC-006)

**Constraints**: Read-only; configuration only from environment variables or git-ignored
`appsettings.Local.json`; application code queries `rw` views only and never calls SPI
procedures or functions itself; `rw` views call SPI functions read-only and never call SPI
procedures; no SPI object is changed; no client data or secrets in the repository

**Scale/Scope**: One endpoint; tenant count in the low tens; one loan per request

## Field sources

The screen sources were read from a local, uncommitted analysis of the SPI Application. Only SPI
object names and screen labels are given here. The function's logic is never copied into this
repository.

| API field | Screen field (parity source) | Source used by the API | Parity reference (tests) |
| --- | --- | --- | --- |
| `loanId` | Selected loan | `rw.Loan.LoanId` (the loan record exists in the tenant) | Request value |
| `loanStatus` | Loan Status (Loan Summary Information) | `rw.Loan.LoanStatusName`, read from SPI tables | `lrGetLoanDetails` (status ID) and `lrGetLoanStatusTypes` (name) |
| `asOfDate` | Today | `rw.LoanPrincipalBalance.AsOfDate`: the SQL Server's current date, taken in the same statement as the principal | Not compared with a screen; checked against the server date (spec Assumptions) |
| `principalBalance` | Principal Remaining (Balance Summary tab) | `rw.LoanPrincipalBalance.PrincipalBalance`: selects from `dbo.lrFnGetBalanceSummary`, per the [loan record rule](#principal-balance-and-the-per-loan-record-rule) | `lrGetTransactionActivity`, second result set, `PrincipalRemaining` (see O1) |

### Principal balance and the per-loan-record rule

The screen's SQL path is `lrGetTransactionActivity` → `dbo.lrFnGetBalanceSummary(@LoanId, today)`
→ `dbo.lrGetPrincipalRemaining(@SalesContractId, end of today)`. The function takes a loan ID but
resolves it to the loan's **contract** and returns one principal for the whole contract. When a
loan is rewritten, the old loan record and the new one share a contract, so the function returns
the same contract-level value for both. Returned unchanged for the old record, that value would
be the new loan's principal, which FR-008a forbids.

`rw.LoanPrincipalBalance` therefore applies this rule to every loan record `L`:

1. **`L` is active** (`LoanStatusId = 1`): `PrincipalBalance` is the function's value for `L`'s
   own loan ID. On a contract, the active record is the one that carries the contract's
   remaining principal, so the contract-level value is that record's own principal.
2. **`L` is not active** (an old record left behind by a rewrite, and also paid off, written off,
   or cancelled): the view does **not** call the function, and `PrincipalBalance` is `0.00`.
   Decided in O2 (spec FR-008a).
   The old record's principal was moved to the new loan when the contract was rewritten, so it
   has no principal of its own left. This matches the SPI Application, which shows 0 for every
   balance field of paid-off and written-off loans.

So an old rewritten loan ID returns its own record: its own non-active status, the as-of date,
and principal `0.00`. It never returns the new loan's principal, and the API never looks up
another record on the contract. The rule needs no rewritten-status ID: it depends only on
"active or not".

Two facts are confirmed locally in the first implementation task (counts only, nothing
committed). They are not decisions:

- No contract has more than one active loan record. If any does, the task stops and the finding
  goes to Farzin, because rule 1 would then give two records the same principal.
- The SPI Application shows 0 Principal Remaining for every non-active status. This is checked
  by hand on the screen for one record of **each** non-active status found in
  `AlphaPMS_ResortWise` (including an old rewritten record), and the results go privately to
  Farzin; no loan IDs or values are recorded in this repository. The SQL path cannot be the
  reference here, because it returns the contract-level value. Any status that does not show 0
  goes to Farzin before merge. A database test separately checks that the view returns `0.00` for
  every non-active record.

The function's type (inline table-valued or scalar), its exact output column, and that column's
data type are confirmed
from the local definition. The view calls it with `CROSS APPLY` or in the select list to match.

## Decisions

All of Farzin's decisions from the previous plan revision are closed.

| ID | Decision | Outcome |
| --- | --- | --- |
| D1 | May a `rw` view select from an SPI function? | **Closed: yes.** Constitution v1.4.0, Principle III. The view selects from `dbo.lrFnGetBalanceSummary` read-only (the former option P-A). The table-reimplementation option P-B is dropped. |
| D2 | Reimplement the Excel engine's calculations | **Closed: moved to the engine feature.** No field in this scope needs the engine. FR-003's "MUST NOT recompute" stands unchanged. |
| D3 | Parity reference for engine values | **Closed: moved to the engine feature.** |
| D4 | `interest` and `serviceFees` as of today | **Closed: moved to the engine feature.** |
| D5 | `daysPastDue` has no screen source | **Closed: moved to the engine feature.** |
| D6 | `totalBalance` and `pastDueAmount` mapping | **Closed: moved to the engine feature.** |
| D7 | `unappliedAmount` source and sign | **Closed: moved to the engine feature.** |
| D9 | Other bucket names are per resort | **Closed: moved to the engine feature.** |
| D10 | As-of date time zone | **Closed: the SQL Server's current date**, recorded as a spec assumption. A hosted server may run in a different time zone than the resort. |
| D11 | Adding `rw` views to `AlphaPMS_ResortWise` | **Closed: allowed.** Constitution v1.4.0: "stays exactly as restored" means no data changes. |

D8 (which principal value is the reference) is answered by the rule above and by O1.

### Open points for Farzin

| ID | Point | Status |
| --- | --- | --- |
| O1 | The Balance Summary tab's Principal Remaining is **shown from the Excel engine**: the screen loads the SQL value first and then overwrites it with the engine's value. The API reads the SQL function. Principle IV requires automated tests to prove parity with the value the screen **shows**. Because the view and an SQL-path reference reach the same SPI function, a test against the SQL path cannot detect a case where the engine differs from SQL. The 5-loan manual check on 2026-10-22 showed the engine and SQL values matched. | **Closed (2026-10-23): option (b).** The Excel loan engine is not used in any way, because ResortWise never changes or runs SPI Application code, and the constitution allows tests to call only SPI procedures and functions. The parity test uses the SPI SQL function path as the principal reference. The gap is a documented deviation from Principle IV in [Complexity Tracking](#complexity-tracking), backed by the 5-loan manual screen check, with Farzin's pull request approval as the sign-off. |
| O2 | Non-active loan records return principal `0.00` (rule 2 above, spec FR-008a). | **Closed (2026-10-23): confirmed.** `0.00` matches what the screen shows. T038 checks the other non-active statuses privately before merge; any status that does not show 0 goes to Farzin. |
| O3 | The test setup creates database users in `AlphaPMS_ResortWise` and `AlphaPMS_ResortWise_B` and grants the test-only `rw_parity_ref` login `EXECUTE` on four read-only SPI procedures (quickstart step 1). This changes no data and no object definitions, but it adds permissions on SPI objects. | **Closed (2026-10-23): allowed.** The users and grants add permissions only and change no data or objects, which fits "exactly as restored" and Farzin's rule that tests may call SPI procedures read-only. `rw_parity_ref` is still never granted `EXECUTE` on any procedure that writes data. |

**O1 options** (option (b) chosen):

- **(a) Engine reference in local-only parity tests.** *Not chosen: ResortWise never runs SPI
  Application code, and the engine is not an SPI procedure or function.* The local parity test runs the SPI Excel
  loan engine (`SPILoanExcelEngine.GetLoanSummary`) read-only, from a local, uncommitted SPI
  Application install whose path comes from an environment variable, to get the value the screen
  shows, and compares it with the API. The engine is never modified, and it never runs in the
  API or on GitHub. This gives an automated reference that is independent of the API's SQL path,
  so Principle IV is met without a deviation. Costs: a Windows machine with the SPI install and
  its workbook for every local run, and new tasks for the engine harness. Principle III's test
  exception names SPI procedures and functions only, so Farzin should also say whether running
  the engine in tests needs a constitution note.
- **(b) SQL function as the reference, with a documented deviation.** *Chosen.* The parity test keeps the
  SQL path (`lrGetTransactionActivity`, second result set) as the reference. The gap between the
  shown engine value and the SQL value is recorded as a deviation from Principle IV in
  [Complexity Tracking](#complexity-tracking), backed by the 5-loan manual check, with Farzin's
  approval in pull request review as the sign-off. Principle IV is not NON-NEGOTIABLE, so it can
  be justified this way. The engine feature can add an engine-based check later.

With option (b), no engine-harness tasks are needed; T031 compares principal with the SQL path
(see tasks.md).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Checked against constitution v1.4.0.

| Principle | Check | Result |
| --- | --- | --- |
| I. API first | One public endpoint under `/v1`, documented in the committed OpenAPI contract. The contract is published with the repository, not served by the API, and a unit test checks that it matches the app. No internal endpoints. | PASS |
| II. Tenant isolation and authorization | API key → caller → allowed tenants; tenant → one connection string name. Checks in fixed order before any data read. Unknown tenant answers like a forbidden one. Isolation tests use both local databases. The API's database login has SELECT on schema `rw` only. The API has no anonymous endpoint (the contract is not served). | PASS |
| III. SPI schema read-only | New objects only in `rw`, by versioned scripts. Here "versioned" means committed to git and reviewed, so the bootstrap script counts even though it has no `V###` number. That includes the journal table `rw.SchemaVersions`, which the bootstrap script creates (not DbUp). The API login can read only `rw`, and a database test proves it cannot read SPI objects. `rw.LoanPrincipalBalance` selects from one SPI function, `dbo.lrFnGetBalanceSummary`, read-only. No `rw` object calls an SPI procedure or anything that writes data. Application code queries `rw` views only and never calls SPI procedures or functions itself. No SPI Application code or SPI database object is changed; the Excel engine is not used. Tests call SPI procedures read-only, locally, through a separate test-only reference login that cannot write to SPI tables directly; it inherits the restored database's `public` `EXECUTE` rights, so a unit test scans the test code and fails on any SPI call outside the approved R4 list (research R9); the users and `EXECUTE` grants that setup needs are allowed (O3, closed). The test-setup script changes data in `AlphaPMS_ResortWise_B` only. | PASS |
| IV. Parity with the SPI Application | Field-by-field parity test over every active loan in `AlphaPMS_ResortWise`. Status is checked against the screen's procedures. For principal, the shown value comes from the engine; per O1 option (b), the reference is the SPI SQL function path, and the gap is recorded openly as a deviation in [Complexity Tracking](#complexity-tracking). That SQL reference cannot run under the reference login (it reaches `AlphaSales`), so the automated test compares status and as-of date only, and principal parity rests on the manual screen check (second deviation, 2026-10-26). | PASS with documented deviations (O1, T031 principal) |
| V. Public repo hygiene | No SPI source, function logic, formulas, or form paths committed. Config placeholders only. Synthetic fixtures. Parity values read at run time. Key hashes only, and only in local config. | PASS |
| VI. Stable error contract | Codes fixed in the contract; one error writer; messages are constants with no exception text. | PASS |
| VII. Tested against SPI data | Unit tests on GitHub (`Category!=Database`); database tests local only; the PR run summary reports counts only. Setup script checks `DB_NAME()` first and changes only `AlphaPMS_ResortWise_B`. Applying `rw` scripts to `AlphaPMS_ResortWise` adds views only and changes no data. | PASS |
| VIII. URL-path versioning | `/v1` prefix. The reduced field set is fixed before first release. Adding the engine fields later as optional fields is not breaking. | PASS |

**Gate result**: PASS for Principles I–III and V–VIII. Principle IV passes with the documented
deviations in Complexity Tracking (O1 option (b), and the T031 principal reference); Farzin's
approval in PR review is the sign-off for both.

**Post-design re-check (after Phase 1)**: same result. data-model.md and the contract match the
reduced scope. The only SPI call inside a `rw` view is the read-only function call above, and the
script scanner test (research R9) enforces it. The API login test enforces the `rw`-only
boundary in the database.

## Project Structure

### Documentation (this feature)

```text
specs/001-loan-balance-summary/
├── plan.md              # This file
├── research.md          # Phase 0: decisions and findings
├── data-model.md        # Phase 1: entities, rw views, configuration model
├── quickstart.md        # Phase 1: how to run and validate locally
├── contracts/
│   └── loan-balance-summary.openapi.yaml
└── tasks.md             # Phase 2 (/speckit-tasks; not created here)
```

### Source Code (repository root)

```text
src/
└── ResortWise.LoanBalance.Api/
    ├── Program.cs                 # Composition, middleware order, endpoint mapping
    ├── Auth/                      # API key authentication handler (bearer), key hashing
    ├── Tenancy/                   # Tenant registry, options binding, connection factory
    ├── Errors/                    # Error codes, error writer, exception → code mapping
    ├── Audit/                     # One structured audit entry per request (stdout)
    ├── Loans/
    │   ├── BalanceSummaryEndpoint.cs
    │   ├── LoanIdParser.cs
    │   ├── LoanBalanceRepository.cs     # Dapper; queries rw views only
    │   └── Models/
    ├── appsettings.json           # Placeholders only
    └── appsettings.Local.json.example

db/
└── rw/
    ├── bootstrap/
    │   └── create_schema_and_journal.sql      # CREATE SCHEMA rw; rw.SchemaVersions (run before DbUp)
    ├── V001__view_loan.sql                    # Loan record and status, from SPI tables
    └── V002__view_loan_principal_balance.sql  # Selects from dbo.lrFnGetBalanceSummary

tools/
└── ResortWise.RwMigrator/         # Runs the bootstrap script, then DbUp; journal rw.SchemaVersions

tests/
├── ResortWise.LoanBalance.UnitTests/    # No database; runs on GitHub
│   ├── Auth/ Tenancy/ Errors/ Audit/ Loans/
│   ├── Contract/                        # Contract and routes, error codes agree
│   └── Scripts/                         # db/rw targets rw only; SPI calls on the allowlist only
├── ResortWise.LoanBalance.DbTests/      # [Trait("Category","Database")]; local only
│   ├── Parity/                          # vs SPI procedures, read-only (reference login)
│   ├── Security/                        # API login cannot read SPI objects
│   ├── Isolation/                       # tenant A vs tenant B
│   └── EndToEnd/                        # Full HTTP pipeline, every error code
└── db/
    └── setup/
        └── B001__diverge_tenant_b.sql   # Checks DB_NAME() first; changes B only

Dockerfile
.dockerignore
.github/workflows/ci.yml           # Build, unit tests (Category!=Database), docker build
```

**Structure Decision**: Single API project with feature folders, a separate DbUp migration tool,
and two test projects split by whether they need a database. The split is how GitHub CI excludes
database tests (Principle VII). `db/rw` holds every versioned `rw` script (Principle III).

## Complexity Tracking

Two deviations are justified in this plan (O1 option (b), decided 2026-10-23, and the T031
principal reference, decided 2026-10-26):

| Deviation | Why needed | Evidence | Sign-off |
| --- | --- | --- | --- |
| Principle IV: the principal parity reference is the SPI SQL function path (`lrGetTransactionActivity`, second result set), not the Excel engine value the screen shows | The Excel loan engine is SPI Application code. ResortWise never changes or runs SPI Application code, and the constitution allows tests to call only SPI procedures and functions, so the engine cannot be the reference. Reusing the SQL function also keeps the API in the container and inside `rw` views | 5-loan manual screen check on 2026-10-22: engine and SQL values matched (details kept privately) | Farzin's approval of the pull request |
| Principle IV: the automated parity test (T031) does not compare principal; it compares status and as-of date only | The SQL reference `lrGetTransactionActivity` reaches the database `AlphaSales` at run time, and the test-only reference login is not given access to it (decided 2026-10-26). `lrGetPrincipalRemaining` is not independent: the function the `rw` view uses calls it. The non-active rule (`0.00`) is still tested automatically for every non-active record (T032) | 5-loan manual screen check on 2026-10-22: Principal Remaining matched on all 5 loans (details kept privately) | Farzin's approval of the pull request |
