# Implementation Plan: Loan Balance Summary API

**Branch**: `001-loan-balance-summary` | **Date**: 2026-10-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-loan-balance-summary/spec.md`

**Status of this plan**: Design complete for the reduced scope (loan ID, loan status, as-of date,
principal balance). Farzin's decisions D1–D11 are closed (see [Decisions](#decisions)), and the
open points O1–O5 are decided (see [Open points for Farzin](#open-points-for-farzin)). No open
point blocks any task.

## Summary

A read-only .NET 8 minimal API returns one loan record's balance summary at
`GET /v1/loans/{loanId}/balance-summary`: loan ID, loan status, as-of date, principal balance,
and whether the principal is available (with a reason when it is not). Each request is authenticated with a per-caller API key (bearer token), authorized
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
| `principalBalance` | Principal Remaining (Balance Summary tab) | `rw.LoanPrincipalBalance.PrincipalBalance`: selects from `dbo.lrFnGetBalanceSummary` for active records whose payoff flag is not set, `NULL` otherwise, per the [loan record rule](#principal-balance-and-the-per-loan-record-rule) | Active and available: `dbo.lrFnGetBalanceSummary(@LoanId, GETDATE())`, `principalRemaining`, called read-only through `rw_parity_ref` and rounded to 2 places half away from zero in the test (O4; not independent of the view, see Complexity Tracking). `lrGetTransactionActivity` is not called (it reaches `AlphaSales`). Non-active, or active with the payoff flag set: none, the value is `null` (O2, O5) |
| `principalBalanceAvailable`, `principalUnavailableReason` | None (API fields) | `rw.Loan.LoanStatusId = 1` and the `V005` flag `PayOffFlagSet`; reason `LOAN_NOT_ACTIVE` or `PAID_OFF_FLAG_SET`, first match wins | Status from `lrGetLoanDetails` (as for `loanStatus`); the O5 rule evaluated by the test's own read-only query on SPI tables through `rw_parity_ref`, written independently of the view |

### Principal balance and the per-loan-record rule

The screen's SQL path is `lrGetTransactionActivity` → `dbo.lrFnGetBalanceSummary(@LoanId, today)`
→ `dbo.lrGetPrincipalRemaining(@SalesContractId, end of today)`. The function takes a loan ID but
resolves it to the loan's **contract** and returns one principal for the whole contract. When a
loan is rewritten, the old loan record and the new one share a contract, so the function returns
the same contract-level value for both. Returned unchanged for the old record, that value would
be the new loan's principal, which FR-008a forbids.

`rw.LoanPrincipalBalance` therefore applies this rule to every loan record `L`:

1. **`L` is active** (`LoanStatusId = 1`) **and its payoff flag is not set** (rule 3):
   `PrincipalBalance` is the function's value for `L`'s own loan ID. On a contract, the active
   record is the one that carries the contract's remaining principal, so the contract-level value
   is that record's own principal.
2. **`L` is not active** (an old record left behind by a rewrite, and also paid off, written off,
   or cancelled): the view does **not** call the function, and `PrincipalBalance` is `NULL`. The
   API response is decided in O2. Only the SPI Excel loan engine calculates such a record's
   screen value; no SPI function returns it per loan record (the per-loan-sounding functions
   also work per contract).
3. **`L` is active and its payoff flag is set**: the view does **not** call the function, and
   `PrincipalBalance` is `NULL`. The view exposes one detection flag, `PayOffFlagSet`
   (`lrLoanReceivable.IsPayOff = 1` on `L`); a non-active record returns `LOAN_NOT_ACTIVE`
   first. The API response is decided in O5. The function ignores the payoff flag. The flag only
   detects the condition from SPI tables: the view reads no amount column and calculates
   nothing, because rebuilding the screen's value would copy engine logic.

So an old rewritten loan ID returns its own record: its own non-active status, the as-of date,
and the O2 response. It never returns the new loan's principal,
and the API never looks up another record on the contract. The rule needs no rewritten-status
ID: it depends only on "active or not".

The current definition of `rw.LoanPrincipalBalance` is `V005`. Earlier definitions are applied
to both test databases and are never edited (data-model.md).

Two facts are confirmed locally in the first implementation task (counts only, nothing
committed). They are not decisions:

- No contract has more than one active loan record. If any does, the task stops and the finding
  goes to Farzin, because rule 1 would then give two records the same principal.
- What the SPI Application shows as Principal Remaining for a non-active record. This was
  checked by hand on the screen (T038): two old rewritten records, neither showing
  `0.00`; details went privately to Farzin, and no loan IDs or values are recorded in this
  repository. The SQL path cannot be the reference here, because it returns the contract-level
  value. A database test separately checks the O2 response for every non-active record.

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
| O1 | The Balance Summary tab's Principal Remaining is **shown from the Excel engine**: the screen loads the SQL value first and then overwrites it with the engine's value. The API reads the SQL function. Principle IV requires automated tests to prove parity with the value the screen **shows**, and a test against the SQL path cannot detect a case where the engine differs from SQL. | **Approved by Farzin on 2026-10-27.** The Excel loan engine is not used in any way, because ResortWise never changes or runs SPI Application code, and the constitution allows tests to call only SPI procedures and functions. The parity reference for principal is the SPI SQL function path. The gap is a documented deviation from Principle IV in [Complexity Tracking](#complexity-tracking), backed by the 5-loan manual screen check. |
| O4 | How the automated parity test (T031) checks principal, given that the screen's procedure `lrGetTransactionActivity` reaches the database `AlphaSales`, which `rw_parity_ref` is not given access to. | **Revised after Farzin's 2026-10-27 review; his approval comes with the PR.** T031 compares `principalBalance` for every active loan whose principal is available, automatically, with a read-only call to `dbo.lrFnGetBalanceSummary` through `rw_parity_ref`, applying the same rounding to 2 places, field mapping, and null rule (T066–T068). This catches rounding, mapping, and null errors across all active loans, but does not prove the screen value, which the Excel engine calculates. A manual screen check of about 20 active loans, including partly paid and delinquent ones, picked by rule, covers that gap; results are kept private (T069). |
| O2 | What principal a non-active loan record returns (rule 2 above, spec FR-008a). | **Approved by Farzin on 2026-10-27.** Every non-active record returns its status with principal `null`, `principalBalanceAvailable` `false`, and reason `LOAN_NOT_ACTIVE`, because the screen shows each record's own balance, which only the Excel engine calculates. This covers every non-active status, including paid-off and cancelled records, where the screen likely shows `0.00`; the engine feature must cover them (T070). Details are kept privately. |
| O5 | What an active loan record returns when its payoff flag (`lrLoanReceivable.IsPayOff`) is set: the SQL function ignores the flag, so its value can differ from the screen (rule 3 above). | **Approved by Farzin on 2026-10-28.** An active loan with `IsPayOff = 1` returns `principalBalance` `null`, `principalBalanceAvailable` `false`, and `principalUnavailableReason` `PAID_OFF_FLAG_SET`. A documented Principle IV deviation in [Complexity Tracking](#complexity-tracking). Loan IDs, values, and counts are kept privately. |
| O3 | The test setup creates database users in `AlphaPMS_ResortWise` and `AlphaPMS_ResortWise_B` and grants the test-only `rw_parity_ref` login `EXECUTE` on four read-only SPI procedures (quickstart step 1). This changes no data and no object definitions, but it adds permissions on SPI objects. | **Closed (2026-10-23): allowed.** The users and grants add permissions only and change no data or objects, which fits "exactly as restored" and Farzin's rule that tests may call SPI procedures read-only. `rw_parity_ref` is still never granted `EXECUTE` on any procedure that writes data. |

No engine-harness tasks are needed. T031 compares principal with the SPI SQL function
`dbo.lrFnGetBalanceSummary`, called read-only (O4; see tasks.md, T066–T068).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Checked against constitution v1.4.0.

| Principle | Check | Result |
| --- | --- | --- |
| I. API first | One public endpoint under `/v1`, documented in the committed OpenAPI contract. The contract is published with the repository, not served by the API, and a unit test checks that it matches the app. No internal endpoints. | PASS |
| II. Tenant isolation and authorization | API key → caller → allowed tenants; tenant → one connection string name. Checks in fixed order before any data read. Unknown tenant answers like a forbidden one. Isolation tests use both local databases. The API's database login has SELECT on schema `rw` only. The API has no anonymous endpoint (the contract is not served). | PASS |
| III. SPI schema read-only | New objects only in `rw`, by versioned scripts. Here "versioned" means committed to git and reviewed, so the bootstrap script counts even though it has no `V###` number. That includes the journal table `rw.SchemaVersions`, which the bootstrap script creates (not DbUp). The API login can read only `rw`, and a database test proves it cannot read SPI objects. `rw.LoanPrincipalBalance` selects from one SPI function, `dbo.lrFnGetBalanceSummary`, read-only. No `rw` object calls an SPI procedure or anything that writes data. Application code queries `rw` views only and never calls SPI procedures or functions itself. No SPI Application code or SPI database object is changed; the Excel engine is not used. Tests call SPI procedures read-only, locally, through a separate test-only reference login that cannot write to SPI tables directly; it inherits the restored database's `public` `EXECUTE` rights, so a unit test scans the test code and fails on any SPI call outside the approved R4 list (research R9); the users and `EXECUTE` grants that setup needs are allowed (O3, closed). The test-setup script changes data in `AlphaPMS_ResortWise_B` only. | PASS |
| IV. Parity with the SPI Application | Field-by-field parity test over every active loan in `AlphaPMS_ResortWise`. Status is checked against the screen's procedures. For principal, the shown value comes from the engine; per O1, the reference is the SPI SQL function path, and the gap is recorded openly as a deviation in [Complexity Tracking](#complexity-tracking). The screen's SQL procedure cannot run under the reference login (it reaches `AlphaSales`), so the automated test compares principal for every active loan with a read-only call to `dbo.lrFnGetBalanceSummary`, the function the view also uses: it catches rounding, mapping, and null errors, but does not prove the screen value, which rests on the manual screen check of about 20 active loans (second deviation, O4). For non-active records the screen shows a principal that only the Excel engine calculates; the API returns the O2 response instead (third deviation, O2). For active records with the payoff flag set, the screen shows a principal the SQL function cannot reproduce; the API returns the O5 response, and the parity test checks that for every such record instead of comparing a value (fourth deviation, O5). | PASS with documented deviations (O1, O2, O4, O5) |
| V. Public repo hygiene | No SPI source, function logic, formulas, or form paths committed. Config placeholders only. Synthetic fixtures. Parity values read at run time. Key hashes only, and only in local config. | PASS |
| VI. Stable error contract | Codes fixed in the contract; one error writer; messages are constants with no exception text. | PASS |
| VII. Tested against SPI data | Unit tests on GitHub (`Category!=Database`); database tests local only; the PR run summary reports counts only. Setup script checks `DB_NAME()` first and changes only `AlphaPMS_ResortWise_B`. Applying `rw` scripts to `AlphaPMS_ResortWise` adds views only and changes no data. | PASS |
| VIII. URL-path versioning | `/v1` prefix. The reduced field set, the nullable `principalBalance`, and the `principalBalanceAvailable` and `principalUnavailableReason` fields (O2) are all part of the first release, so no version change is needed. The contract tells clients to decide by `principalBalanceAvailable`, so a later engine feature that makes non-active principal available does not change a field's meaning. `principalUnavailableReason` codes are never renamed or repurposed; new codes may be added within `/v1`, and the contract tells clients to treat an unknown code as "not available", so adding one is not breaking. The O5 code `PAID_OFF_FLAG_SET`, and the fact that an active record may have no principal available, are also part of the first release. Adding the engine fields later as optional fields is not breaking. | PASS |

**Gate result**: PASS for Principles I–III and V–VIII. Principle IV passes with the documented
deviations in Complexity Tracking (O1, O2, O4, and O5); the sign-off for each is in
[Open points for Farzin](#open-points-for-farzin).

**Post-design re-check (after Phase 1)**: same result. data-model.md and the contract match the
reduced scope. The only SPI call inside a `rw` view is the read-only function call above, and the
script scanner test (research R9) enforces it. `V005` adds only reads of SPI tables (Principle
III allows them), and the scanner also fails on any SPI amount column in a `rw` script, so the
O5 flag cannot turn into a calculation. The API login test enforces the `rw`-only
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
    ├── V002__view_loan_principal_balance.sql  # Selects from dbo.lrFnGetBalanceSummary (applied; not edited)
    ├── V003__view_loan_principal_balance_null_non_active.sql  # Earlier definition of the view (applied, not edited)
    ├── V004__view_loan_principal_balance_o5_flags.sql         # Earlier definition of the view (applied, not edited)
    └── V005__view_loan_principal_balance_drop_cross_loan.sql  # Current definition: NULL for non-active (O2) and payoff flag set (O5)

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

Four deviations are justified in this plan (O1, O2, O4, and O5); the sign-off for each is in
[Open points for Farzin](#open-points-for-farzin):

| Deviation | Why needed | Evidence | Sign-off |
| --- | --- | --- | --- |
| Principle IV: the principal parity reference is the SPI SQL function path (`lrGetTransactionActivity`, second result set), not the Excel engine value the screen shows | The Excel loan engine is SPI Application code. ResortWise never changes or runs SPI Application code, and the constitution allows tests to call only SPI procedures and functions, so the engine cannot be the reference. Reusing the SQL function also keeps the API in the container and inside `rw` views | 5-loan manual screen check on 2026-10-22: engine and SQL values matched (details kept privately) | Approved by Farzin on 2026-10-27 |
| Principle IV: the automated parity test (T031) compares principal with a read-only call to `dbo.lrFnGetBalanceSummary`, the same SPI function the `rw` view uses, so the reference is not independent of the view and does not prove the value the screen shows | The screen's SQL procedure `lrGetTransactionActivity` reaches the database `AlphaSales` at run time, and the test-only reference login is not given access to it. The screen's shown value comes from the Excel engine, which ResortWise never runs. The function call, with the test's own rounding to 2 places (half away from zero), field mapping, and null checks, catches rounding, mapping, and null errors in the view and the API for every active loan (O4). The non-active rule (O2) is still tested automatically for every non-active record (T032) | 5-loan manual screen check on 2026-10-22: Principal Remaining matched on all 5 loans. Manual screen check of about 20 active loans, including partly paid and delinquent ones, picked by rule (T069). Details of both are kept privately | Revised after Farzin's 2026-10-27 review; his approval comes with the PR. |
| Principle IV: for non-active loan records (only Rewritten is in use), the API returns the O2 response, while the screen shows the record's own principal | Only the SPI Excel loan engine calculates that per-record value. No SPI function returns it (the candidates work per contract), the engine is SPI Application code that ResortWise never runs, and rebuilding it from SPI tables would copy engine logic. Returning `0.00` or the contract-level value would contradict the screen, so the API reports the value as not available until the engine feature covers it (O2) | T038 manual screen check on 2026-10-26: two rewritten loan records, neither showing `0.00` (details kept privately). Paid-off and cancelled records, where the screen likely shows `0.00`, get the same rule; not verified, because those statuses are not in use in `AlphaPMS_ResortWise`, and the engine feature must cover them | Approved by Farzin on 2026-10-27 |
| Principle IV: for active loan records with the payoff flag set, the API returns the O5 response, while the screen shows a value | These loans show a different value on the screen because of an SPI data issue the SQL function cannot see: it ignores the payoff flag. Returning the function's value would contradict the screen, and reproducing the screen's value would copy engine logic. The view only detects the flag from SPI tables and calculates no amount (O5) | T069 manual screen check and a read-only investigation: active loans with the payoff flag set where the screen shows `0.00` and the function does not; read-only counts found further active loans with the flag set (loan IDs, values, and counts kept privately) | Approved by Farzin on 2026-10-28 |
