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
| `principalBalanceAvailable` | boolean | `rw.Loan.LoanStatusId = 1` |
| `principalUnavailableReason` | string (`LOAN_NOT_ACTIVE`), optional | Set by the API when `principalBalanceAvailable` is `false` |

Rules (FR-006, FR-008a; plan, per-loan-record rule):

- `loanId`, `loanStatus`, `asOfDate`, `principalBalance`, and `principalBalanceAvailable` are
  always present. `principalUnavailableReason` is present only when `principalBalanceAvailable`
  is `false`.
- **Active** (`LoanStatusId = 1`): `principalBalance` is the view's value (a zero principal is
  `0.00`), `principalBalanceAvailable` is `true`, and there is no reason. A `NULL` principal for
  an active record is an internal error (500 `INTERNAL_ERROR`), never returned as `null`.
- **Not active** (any other status, including an old record left behind by a rewrite):
  `principalBalance` is `null` (never `0.00`), `principalBalanceAvailable` is `false`, and
  `principalUnavailableReason` is `LOAN_NOT_ACTIVE`. A non-`NULL` principal for a non-active
  record is an internal error (500), so the view and the API cannot disagree silently.

### Error

`code` (enum in the contract), `message` (constant per code), `field` (input errors only).

## `rw` views (versioned scripts in `db/rw`)

Every view is filtered by `LoanId` from the application query. The only SPI function a view may
call is `dbo.lrFnGetBalanceSummary`, read-only (constitution v1.4.0, Principle III). No view calls
an SPI procedure or anything that writes data.

### `rw.Loan`

One row per loan record. Read from SPI tables only.

| Column | From |
| --- | --- |
| `LoanId` | `lrLoanReceivable.ID` (`int`) |
| `LoanStatusId` | `lrLoanReceivable.LoanStatusId` (`int`) |
| `LoanStatusName` | `lrLoanStatus.LoanStatusName` (`varchar(25)`, nullable), joined on `lrLoanStatus.ID` |
| `SalesContractId` | `lrLoanReceivable.SalesContractId` (`int`) |

### `rw.LoanPrincipalBalance`

One row per loan record. Created by `V002`; redefined by `V003` (O2, decided 2026-10-26), because
`V002` is already applied and a versioned script is never edited after it is applied. The table
shows the `V003` definition.

| Column | Value |
| --- | --- |
| `LoanId` | `lrLoanReceivable.ID` |
| `AsOfDate` | `CAST(GETDATE() AS date)`, the same clock the function call uses |
| `PrincipalBalance` | `LoanStatusId = 1`: `principalRemaining` (`money`, scale 4) from the table-valued function `dbo.lrFnGetBalanceSummary(ID, GETDATE())` through `OUTER APPLY` (so a record never drops out of the view; approved 2026-10-26), rounded to 2 places by `CAST(... AS decimal(19,2))` (half away from zero, as the screen does; research R2), confirmed by the parity test. Any other status: `NULL` (spec FR-008a; decided in O2, 2026-10-26; `V002` returned `0.00`), and the function is not called |

The function returns a per-contract value; the status rule is what keeps the result per loan
record (plan, per-loan-record rule).

### `rw.SchemaVersions` (table)

The DbUp journal. It is the only `rw` table. It is created, together with schema `rw`, by the
committed bootstrap script `db/rw/bootstrap/create_schema_and_journal.sql`, not by DbUp (research
R9).

## State

The service holds no state. There are no state transitions. Loan status is read, never changed.
