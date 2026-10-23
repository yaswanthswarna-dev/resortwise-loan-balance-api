# Data Model: Loan Balance Summary API

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

All examples are synthetic. SPI table and column names marked *(confirm)* are verified from the
local schema in the first implementation task.

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
| `ConnectionStringName` | string | Required. Must resolve to `ConnectionStrings:{name}`. Exactly one per tenant. |

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
| `principalBalance` | decimal(2) | `rw.LoanPrincipalBalance.PrincipalBalance` |

Rules: all four fields are always present; a zero principal is returned as `0.00` (FR-006). A
non-active loan record, including an old record left behind by a rewrite, returns its own status
and principal `0.00` (FR-008a; plan, per-loan-record rule).

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
| `LoanId` | `lrLoanReceivable` |
| `LoanStatusId` | `lrLoanReceivable` |
| `LoanStatusName` | Loan status type table *(confirm)* |
| `SalesContractId` | `lrLoanReceivable` |

### `rw.LoanPrincipalBalance`

One row per loan record.

| Column | Value |
| --- | --- |
| `LoanId` | `lrLoanReceivable` |
| `AsOfDate` | `CAST(GETDATE() AS date)`, the same clock the function call uses |
| `PrincipalBalance` | `LoanStatusId = 1`: the principal from `dbo.lrFnGetBalanceSummary(LoanId, today)` *(confirm function type, column, and column data type)*, cast to `decimal(19,2)` only if its scale is at most 2; otherwise rounded exactly as the screen does, confirmed by the parity test. Any other status: `0.00` (pending Farzin's confirmation, spec FR-008a), and the function is not called |

The function returns a per-contract value; the status rule is what keeps the result per loan
record (plan, per-loan-record rule).

### `rw.SchemaVersions` (table)

The DbUp journal. It is the only `rw` table. It is created, together with schema `rw`, by the
committed bootstrap script `db/rw/bootstrap/create_schema_and_journal.sql`, not by DbUp (research
R9).

## State

The service holds no state. There are no state transitions. Loan status is read, never changed.
