# Data Model: Loan Balance Summary API

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

All examples are synthetic. SPI table and column names were confirmed from the local schema in
T007 (research R2, R8, R9).

## Configuration entities

### Caller

| Field | Type | Rules |
| --- | --- | --- |
| `Id` | string | Required, unique. Appears in audit entries. |
| `KeyHash` | string | Required, 64 lowercase hex characters (SHA-256 of the API key), unique. Never the plain key. |
| `Tenants` | string[] | Required, at least one. Each must exist in `Tenants`. |

### Tenant

| Field | Type | Rules |
| --- | --- | --- |
| `TenantId` (config key) | string | Non-blank. Compared ordinally. |
| `ConnectionStringName` | string | Required. Must resolve to `ConnectionStrings:{name}`. Exactly one per tenant. No two tenants may share a connection string name or value (approved 2026-10-26). |

The registry is read once at startup and validated (research R6). It is immutable while the
service runs; a change requires a restart.

## Request-scoped entity

### RequestContext

`CallerId`, `TenantId`, `LoanId`, `CorrelationId`, `Status`, `ErrorCode`. These are filled as the
pipeline advances and written once to the audit log (research R10). No amounts are stored on it.

## Response entities (contract)

See [contracts/loan-balance-summary.openapi.yaml](contracts/loan-balance-summary.openapi.yaml).

### LoanBalanceSummary

| Field | Type | Source (see [plan.md](plan.md#field-sources)) |
| --- | --- | --- |
| `loanId` | int64 | Request, confirmed by `rw.Loan` |
| `loanStatus` | string | `rw.Loan.LoanStatusName` |
| `asOfDate` | date | `rw.LoanPrincipalBalance.AsOfDate` (SQL Server's current date) |
| `principalBalance` | decimal(2), nullable | `rw.LoanPrincipalBalance.PrincipalBalance` |
| `principalBalanceAvailable` | boolean | `rw.Loan.LoanStatusId = 1` and `rw.LoanPrincipalBalance.PayOffFlagSet` is `0` |
| `principalUnavailableReason` | string (`LOAN_NOT_ACTIVE`, `PAID_OFF_FLAG_SET`), optional | Set by the API when `principalBalanceAvailable` is `false`, by the rules below |

Rules (FR-006, FR-008a; plan, per-loan-record rule). The first rule that applies wins:

- `loanId`, `loanStatus`, `asOfDate`, `principalBalance`, and `principalBalanceAvailable` are
  always present. `principalUnavailableReason` is present only when `principalBalanceAvailable`
  is `false`.
1. **Not active** (any status other than `LoanStatusId = 1`, including an old record left behind
   by a rewrite): `principalBalance` is `null` (never `0.00`), `principalBalanceAvailable` is
   `false`, and `principalUnavailableReason` is `LOAN_NOT_ACTIVE`, whatever the flag says
   (O2). Approved by Farzin on 2026-10-27.
2. **Active, `PayOffFlagSet = 1`** (`IsPayOff = 1`): `principalBalance` is `null`,
   `principalBalanceAvailable` is `false`, and `principalUnavailableReason` is
   `PAID_OFF_FLAG_SET` (O5). Approved by Farzin on 2026-10-28.
3. **Active, `PayOffFlagSet = 0`**: `principalBalance` is the view's value (a zero principal is
   `0.00`), `principalBalanceAvailable` is `true`, and there is no reason.

Guards: a `NULL` principal in case 3 is an internal error (500 `INTERNAL_ERROR`), never returned
as `null`. A non-`NULL` principal in cases 1 and 2 is also an internal error (500), so the view
and the API cannot disagree silently.

### Error

`code` (enum in the contract), `message` (constant per code), `field` (input errors only).

## `rw` views (versioned scripts in `db/rw`)

Every view is filtered by `LoanId` from the application query. The only SPI function a view may
call is `dbo.lrFnGetBalanceSummary`, read-only (constitution v1.4.0, Principle III). No view calls
an SPI procedure or anything that writes data.

### `rw.Loan`

One row per loan record. Read from SPI tables only. Defined by `V001`.

| Column | From |
| --- | --- |
| `LoanId` | `lrLoanReceivable.ID` (`int`) |
| `LoanStatusId` | `lrLoanReceivable.LoanStatusId` (`int`) |
| `LoanStatusName` | `lrLoanStatus.LoanStatusName` (`varchar(25)`, nullable), joined on `lrLoanStatus.ID` |
| `SalesContractId` | `lrLoanReceivable.SalesContractId` (`int`) |

### `rw.LoanPrincipalBalance`

One row per loan record. The table shows the current definition, `V005`. Earlier definitions
are applied and never edited, because a versioned script is never edited after it is applied.

| Column | Value |
| --- | --- |
| `LoanId` | `lrLoanReceivable.ID` |
| `AsOfDate` | `CAST(GETDATE() AS date)`, the same clock the function call uses |
| `PayOffFlagSet` | `bit`, not null: `1` when `lrLoanReceivable.IsPayOff = 1` on this record, else `0` (a `NULL` flag counts as `0`). Set for every status (O5) |
| `PrincipalBalance` | `LoanStatusId = 1` and `PayOffFlagSet = 0`: `principalRemaining` (`money`, scale 4) from the table-valued function `dbo.lrFnGetBalanceSummary(ID, GETDATE())` through `OUTER APPLY` (so a record never drops out of the view; approved 2026-10-26), rounded to 2 places by `CAST(... AS decimal(19,2))` (half away from zero, as the screen does; research R2), confirmed by the parity test. Any other status, or `PayOffFlagSet = 1`: `NULL` (rules 1 and 2 above), and the function is not called |

The function returns a per-contract value; the status rule is what keeps the result per loan
record (plan, per-loan-record rule). The flag is detection only: `V005` reads no amount
column, uses no aggregate, and calculates no amount (O5). The reason codes are API constants;
the view exposes only the flag.

### `rw.SchemaVersions` (table)

The DbUp journal. It is the only `rw` table. It is created, together with schema `rw`, by the
committed bootstrap script `db/rw/bootstrap/create_schema_and_journal.sql`, not by DbUp (research
R9).

## State

The service holds no state. There are no state transitions. Loan status is read, never changed.
