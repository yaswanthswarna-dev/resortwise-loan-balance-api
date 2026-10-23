# Implementation Plan: Loan Balance Summary API

**Branch**: `001-loan-balance-summary` | **Date**: 2026-10-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-loan-balance-summary/spec.md`

**Status of this plan**: Design complete for authentication, tenancy, errors, audit, data access,
and test infrastructure. The value calculations are **blocked on decisions from Farzin** (see
[Decisions needed from Farzin](#decisions-needed-from-farzin)). `/speckit-tasks` may generate
tasks for the unblocked parts, and must mark tasks that depend on an open decision as blocked.

## Summary

A read-only .NET 8 minimal API returns one loan record's balance summary at
`GET /v1/loans/{loanId}/balance-summary`. Each request is authenticated with a per-caller API key
(bearer token), authorized against a tenant registry held in configuration, and served only from
the database mapped to the requested tenant. The API reads SPI data only through views in the `rw`
schema, created by versioned scripts in this repository.

The main design finding: **most returned values are not stored in SPI tables and are not computed
by any SPI database object.** On the Loan Summary Information tab, Current Payment Information
section, they are computed in the SPI Application itself by its Excel-based loan engine
(`SPILoanExcelEngine.GetLoanSummary`). Only loan status, the Other bucket names, and (through an
SPI function) principal remaining have a database source. The per-field answer, the options, and
the recommendation are in [Field sources](#field-sources-design-question) and
[research.md](research.md).

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
`appsettings.Local.json`; no SPI object is created, altered, dropped, written, or called by
application code; no client data or secrets in the repository

**Scale/Scope**: One endpoint; tenant count in the low tens; one loan per request

## Field sources (design question)

The screen sources were read from a local, uncommitted analysis of the SPI Application. Only SPI
object names and screen labels are given here. The exact formulas are read locally during
implementation and are never copied into this repository.

Three kinds of source:

- **Table**: an `rw` view can read the value straight from SPI tables.
- **SPI SQL logic**: the screen gets the value from an SPI stored procedure or function that
  contains logic beyond a plain read.
- **SPI application logic**: the screen gets the value from the SPI Application's Excel loan
  engine (`SPILoanExcelEngine.GetLoanSummary`). No SPI database object computes it. The engine
  only reads its inputs through `lrExcelGetLoanById`, `lrExcelGetTransactions`,
  `lrExcelGetControlSettings`, and `lrExcelGetContractPaidTotals`.

| API field | Screen field (parity source) | Screen gets it from | Kind | Open decision |
| --- | --- | --- | --- | --- |
| `loanId` | Selected loan | `lrGetLoanDetails` | Table | — |
| `loanStatus` | Loan Status (Loan Summary Information) | `lrGetLoanDetails` (status ID), `lrGetLoanMasterInfo` → `lrGetLoanStatusTypes` (name) | Table | — |
| `asOfDate` | Today on the SPI client machine | Engine uses today's date | Table (server date) | D10 |
| `principalBalance` | Principal Remaining (Balance Summary tab) | Shown value comes from the Excel engine; the SQL value from `lrGetTransactionActivity` → `dbo.lrFnGetBalanceSummary` → `dbo.lrGetPrincipalRemaining` is loaded first, then overwritten | SPI SQL logic and SPI application logic | D1, D8 |
| `lateFees` | Accrued Late Fees (Current Payment) | Excel engine | SPI application logic | D2 |
| `others[n].amount` | Other 1–5 Due amounts (Current Payment) | Excel engine | SPI application logic | D2 |
| `others[n].name` | Other bucket labels | `lrGetControlByResort` (reads `lrControl` for the contract's resort) | Table | D9 |
| `totalBalance` | Total Amount Due (Current Payment) | Excel engine | SPI application logic | D2, D6 |
| `pastDueAmount` | Schedule Due (Current Payment), proposed | Excel engine | SPI application logic | D2, D6 |
| `daysPastDue` | **No such field on the screen.** The nearest is "# Pmts Due", a count of payments | Excel engine | No parity source | D5 |
| `interest` | **Not shown as of today.** Current Payment has no interest box; the engine computes today's accrued interest but the screen skips it. Payoff shows interest as of Valid Through | Excel engine | SPI application logic, not displayed | D4 |
| `serviceFees` | **Not shown as of today.** Same as interest | Excel engine | SPI application logic, not displayed | D4 |
| `unappliedAmount` | Total Unapplied, shown only in Payoff, with the sign flipped. It has no date limit, so the Payoff value equals the as-of-today value | Excel engine | SPI application logic | D2, D7 |

Paid-off and written-off loans skip the engine in the SPI Application, and every due and balance
field shows 0. The API mirrors this rule (D8).

### Options for values that need SPI logic

**Principal balance (SPI SQL logic)**

- **P-A. `rw` view selects from the SPI function** (`dbo.lrGetPrincipalRemaining` or
  `dbo.lrFnGetBalanceSummary`). Exact match to the SQL path and follows future SPI changes. But
  the API would then call an SPI function indirectly through the view.
- **P-B. Reimplement the function's logic in the `rw` view**, reading SPI tables directly
  (contract-level loan amount less principal paid). Simple aggregation; stays inside Principle III.
  Needs re-checking if SPI changes the function.

**Recommendation: P-B.** The logic is a small aggregation, and P-B needs no constitution change.
The parity test calls the SPI function read-only (allowed for tests) as an independent reference,
so any SPI change is caught.

> **⚠ Constitution flag for Farzin (D1).** Principle III says application code MUST NOT call SPI
> stored procedures or functions, "directly or indirectly". A `rw` view that selects from an SPI
> function is an indirect call by the application. As written, P-A is not allowed, and
> Principle III is NON-NEGOTIABLE, so it cannot be waived in Complexity Tracking; it would need a
> constitution amendment. **Farzin must decide whether a view wrapping an SPI function is
> allowed.** This plan assumes it is not.

**Excel-engine values (SPI application logic)**

A view wrapping an SPI function is **not an option** here: no SPI function or procedure computes
these values. Options:

- **E-A. Reimplement the engine's calculations in T-SQL `rw` views** (payment schedule, payment
  formula, accruals, grace days, fees). Keeps all logic in the database, but schedule generation
  in a view needs recursive CTEs, is hard to read and test, and cannot be unit-tested without a
  database, so none of it would be tested on GitHub.
- **E-B. `rw` input views plus a C# calculator (recommended).** `rw` views expose, straight from
  SPI tables, the same inputs the engine reads through the four `lrExcelGet*` procedures. A pure
  C# `LoanSummaryCalculator` reproduces the engine's Current Payment calculations, including the
  engine's date and rounding rules. It is unit-tested on GitHub with synthetic fixtures and
  parity-tested locally against the real engine.
- **E-C. Run the SPI engine inside the service.** Rejected: it is SPI application code that needs
  Windows and the SPI workbook, which breaks the container and cloud-agnostic rules, and it means
  the application runs SPI logic outside `rw` views.
- **E-D. Ask SPI to store or expose these values** (a table or SQL function maintained by SPI).
  Best long term, but outside this team's control. Worth raising with SPI in parallel.

**Recommendation: E-B.** It stays inside Principle III, keeps the logic testable on GitHub, and
runs in the container.

> **⚠ Spec conflict for Farzin (D2).** FR-003 says the API "MUST NOT recompute, round differently,
> or reinterpret SPI values". For these fields there is no stored SPI value to read: the SPI
> Application computes them in its engine each time the screen opens. Every option except E-C or
> E-D recomputes them. E-B needs FR-003 amended to "MUST reproduce the SPI Application's
> calculation exactly, proven by parity tests".

## Decisions needed from Farzin

Tasks that depend on an open decision are blocked until it is answered. Recommendations are
included; none is assumed final.

| ID | Decision | Recommendation |
| --- | --- | --- |
| D1 | May a `rw` view select from an SPI function? (Principle III forbids indirect calls; a change needs a constitution amendment.) | No. Reimplement in the view (P-B). |
| D2 | Approve reimplementing the Excel engine's calculations (E-B), and amend FR-003 from "must not recompute" to "must reproduce exactly, proven by parity tests". | Approve E-B. |
| D3 | Parity reference for engine values. FR-021 and Principle III allow tests to call SPI procedures and functions, but no procedure or function returns these values. Allow the local parity test to call `SPILoanExcelEngine.GetLoanSummary` from a local, uncommitted copy of the SPI binaries and workbook? It writes no database data, only a temporary workbook copy. | Allow it, local only, path from an environment variable. Without it there is no automated parity reference for most fields, and FR-021 cannot be met. |
| D4 | `interest` and `serviceFees` as of today are not shown on the Current Payment section. The engine computes them, but the screen does not display them. Return the engine's today values (no visible screen value to check by eye), take them from another section, or remove the fields? | Return the engine's as-of-today values and record that they are not displayed. |
| D5 | `daysPastDue` has no screen source. The screen shows "# Pmts Due" (a count of payments). Options: replace with `paymentsDue` (count), derive days from the oldest unpaid due date (no parity source, against Principle IV), or remove. | Replace with `paymentsDue`. `/v1` is not released, so this is not a breaking change yet. |
| D6 | Confirm the mapping `totalBalance` ← Total Amount Due and `pastDueAmount` ← Schedule Due. Note that Total Amount Due is the amount due now (scheduled due plus late fees plus Other due, less unapplied), not the loan's total balance. | Confirm, and consider renaming `totalBalance` to `totalAmountDue` before release. |
| D7 | `unappliedAmount` is shown only in Payoff, with the sign flipped. Its value has no date limit, so it is the same as of today. Accept that source, and which sign? | Accept, and return the sign the screen shows. |
| D8 | Principal Remaining: the screen shows the engine's value, not the SQL function's. They matched on the 5 compared loans. The SQL function works per contract, not per loan record (affects old rewritten loans, FR-008a). Paid-off and written-off loans show 0. Which is the reference, and is contract-level principal acceptable for rewritten loans? | Reference the engine value. Also compare with the SQL function and report any differences privately. Accept contract-level principal, because it is what the screen shows. |
| D9 | Other bucket names are configured per **resort** (`lrControl`, the contract's resort), not per tenant as the spec assumes. When the contract has no resort, the screen uses the *signed-in user's* default resort, which has no API equivalent. The screen's default label is "Other n Due", but the spec says "Other N". | Use the contract's resort. When there is none, return the "Other N" defaults. Keep the spec's "Other N" default name. Update the spec assumption. |
| D10 | As-of date: the screen uses the SPI client machine's date. The API uses the SQL Server's current date (`CAST(SYSDATETIME() AS date)`). Confirm that time zone. | Use the SQL Server date. |
| D11 | Applying the `rw` scripts to `AlphaPMS_ResortWise` adds `rw` views but changes no SPI data. Confirm this is consistent with "stays exactly as restored". | Confirm. The parity test cannot run otherwise. |

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Check | Result |
| --- | --- | --- |
| I. API first | One public endpoint under `/v1`, documented in the committed OpenAPI contract. The contract file is served by the API and checked by a unit test. No internal endpoints. | PASS |
| II. Tenant isolation and authorization | API key → caller → allowed tenants; tenant → one connection string name. Checks in fixed order before any data read. Unknown tenant answers like a forbidden one. Isolation tests use both local databases. The database login has SELECT on schema `rw` only. | PASS |
| III. SPI schema read-only | `rw` views only; app reads only `rw` views; no SPI function inside views (P-B, E-B). Tests call SPI procedures and functions read-only, locally. The test-setup script changes `AlphaPMS_ResortWise_B` only. | PASS with the recommended options. **Fails if P-A is chosen without an amendment (D1).** |
| IV. Parity with the SPI Application | Field-by-field parity test against `AlphaPMS_ResortWise`. | **BLOCKED**: D2–D8. Three returned fields (`interest`, `serviceFees`, `daysPastDue`) have no displayed as-of-today screen value, and most fields have no database reference without D3. |
| V. Public repo hygiene | No SPI source, formulas, cell references, or form paths committed. Config placeholders only. Synthetic fixtures. Parity values read at run time. Key hashes only, and only in local config. | PASS |
| VI. Stable error contract | Codes fixed in the contract; one error writer; messages are constants with no exception text. | PASS |
| VII. Tested against SPI data | Unit tests on GitHub (`Category!=Database`); database tests local only; the PR run summary reports counts only. Setup script checks `DB_NAME()` first. | PASS (parity part depends on D3) |
| VIII. URL-path versioning | `/v1` prefix. D5 and D6 may rename fields **before** first release; after release, renames would need `/v2`. | PASS |

**Gate result**: PASS for the unblocked design. **BLOCKED** for the value calculations and for the
final field list in the contract until D1–D11 are answered. No principle violation is being
justified, so Complexity Tracking is empty.

**Post-design re-check (after Phase 1)**: unchanged. data-model.md and the contract change add no
new risks. The pending field changes (D4–D7) are listed but not yet applied to the contract.

## Project Structure

### Documentation (this feature)

```text
specs/001-loan-balance-summary/
├── plan.md              # This file
├── research.md          # Phase 0: decisions, options, open questions
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
    │   ├── LoanSummaryCalculator.cs     # Pure engine reproduction (E-B; blocked on D2)
    │   └── Models/
    ├── openapi/                   # Copy of the contract, served at /v1/openapi.yaml
    ├── appsettings.json           # Placeholders only
    └── appsettings.Local.json.example

db/
└── rw/
    ├── V001__create_schema_rw.sql
    ├── V002__view_loan.sql              # Loan, status, contract, resort
    ├── V003__view_other_bucket_names.sql
    ├── V004__view_principal_remaining.sql
    └── V005__views_engine_inputs.sql    # Inputs mirroring the lrExcelGet* reads

tools/
└── ResortWise.RwMigrator/         # DbUp console; applies db/rw scripts; journal in rw schema

tests/
├── ResortWise.LoanBalance.UnitTests/    # No database; runs on GitHub
│   ├── Auth/ Tenancy/ Errors/ Audit/ Loans/
│   ├── Contract/                        # Contract and routes, error codes agree
│   └── Scripts/                         # Every DDL in db/rw targets schema rw only
├── ResortWise.LoanBalance.DbTests/      # [Trait("Category","Database")]; local only
│   ├── Parity/                          # vs SPI procedures/functions (and engine, D3)
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

No constitution violations are justified in this plan. If Farzin chooses P-A (D1), that is not a
waivable deviation. It would require a constitution amendment on its own branch first.
