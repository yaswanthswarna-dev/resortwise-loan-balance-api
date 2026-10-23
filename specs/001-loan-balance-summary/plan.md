# Implementation Plan: Loan Balance Summary API

**Branch**: `001-loan-balance-summary` | **Date**: 2026-10-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-loan-balance-summary/spec.md`

**Status of this plan**: Design complete for the reduced scope (loan ID, loan status, as-of date,
principal balance). Farzin's decisions D1–D11 are closed (see [Decisions](#decisions)). One open
point for Farzin, O1, is recorded; it does not block tasks.

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
- The SPI Application shows 0 Principal Remaining for an old rewritten loan record. This is
  checked by hand on the screen for at least one such record, and the result goes privately to
  Farzin. The SQL path cannot be the reference here, because it returns the contract-level value.
  If the screen shows anything other than 0, the rule goes to Farzin before merge. A database
  test separately checks that the view returns `0.00` for every non-active record.

The function's type (inline table-valued or scalar) and its exact output column are confirmed
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

### Open point for Farzin

| ID | Point | Proposed handling |
| --- | --- | --- |
| O1 | The Balance Summary tab's Principal Remaining is **shown from the Excel engine**: the screen loads the SQL value first and then overwrites it with the engine's value. The API and the parity test both use the SQL function, not the engine. | The parity test uses the SQL path (`lrGetTransactionActivity`, second result set) as the reference. The 5-loan manual check on 2026-10-22 showed the engine and SQL values matched. Farzin to confirm that this reference is acceptable. Note: because the view and the reference reach the same SPI function, the test proves the view returns the SQL path's value; it cannot detect a case where the engine differs from SQL. That evidence comes only from the manual check, and the engine feature can add an engine-based check later. |

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Checked against constitution v1.4.0.

| Principle | Check | Result |
| --- | --- | --- |
| I. API first | One public endpoint under `/v1`, documented in the committed OpenAPI contract. The contract file is served by the API and checked by a unit test. No internal endpoints. | PASS |
| II. Tenant isolation and authorization | API key → caller → allowed tenants; tenant → one connection string name. Checks in fixed order before any data read. Unknown tenant answers like a forbidden one. Isolation tests use both local databases. The database login has SELECT on schema `rw` only. | PASS |
| III. SPI schema read-only | New objects only in `rw`, by versioned scripts. `rw.LoanPrincipalBalance` selects from one SPI function, `dbo.lrFnGetBalanceSummary`, read-only. No `rw` object calls an SPI procedure or anything that writes data. Application code queries `rw` views only and never calls SPI procedures or functions itself. No SPI Application code or SPI database object is changed; the Excel engine is not used. Tests call SPI procedures read-only, locally. The test-setup script changes data in `AlphaPMS_ResortWise_B` only. | PASS |
| IV. Parity with the SPI Application | Field-by-field parity test over every active loan in `AlphaPMS_ResortWise`: status against the screen's procedures, principal against the screen's SQL path. The shown value comes from the engine; that gap is recorded openly as O1, not chosen silently. | PASS, with O1 open for Farzin's confirmation |
| V. Public repo hygiene | No SPI source, function logic, formulas, or form paths committed. Config placeholders only. Synthetic fixtures. Parity values read at run time. Key hashes only, and only in local config. | PASS |
| VI. Stable error contract | Codes fixed in the contract; one error writer; messages are constants with no exception text. | PASS |
| VII. Tested against SPI data | Unit tests on GitHub (`Category!=Database`); database tests local only; the PR run summary reports counts only. Setup script checks `DB_NAME()` first and changes only `AlphaPMS_ResortWise_B`. Applying `rw` scripts to `AlphaPMS_ResortWise` adds views only and changes no data. | PASS |
| VIII. URL-path versioning | `/v1` prefix. The reduced field set is fixed before first release. Adding the engine fields later as optional fields is not breaking. | PASS |

**Gate result**: PASS. No principle violation is being justified, so Complexity Tracking is
empty.

**Post-design re-check (after Phase 1)**: PASS. data-model.md and the contract match the reduced
scope. The only SPI call inside a `rw` view is the read-only function call above, and the script
scanner test (research R9) enforces it.

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
    ├── openapi/                   # Copy of the contract, served at /v1/openapi.yaml
    ├── appsettings.json           # Placeholders only
    └── appsettings.Local.json.example

db/
└── rw/
    ├── V001__create_schema_rw.sql
    ├── V002__view_loan.sql                    # Loan record and status, from SPI tables
    └── V003__view_loan_principal_balance.sql  # Selects from dbo.lrFnGetBalanceSummary

tools/
└── ResortWise.RwMigrator/         # DbUp console; applies db/rw scripts; journal in rw schema

tests/
├── ResortWise.LoanBalance.UnitTests/    # No database; runs on GitHub
│   ├── Auth/ Tenancy/ Errors/ Audit/ Loans/
│   ├── Contract/                        # Contract and routes, error codes agree
│   └── Scripts/                         # db/rw targets rw only; SPI calls on the allowlist only
├── ResortWise.LoanBalance.DbTests/      # [Trait("Category","Database")]; local only
│   ├── Parity/                          # vs SPI procedures, read-only
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

No constitution violations are justified in this plan.
