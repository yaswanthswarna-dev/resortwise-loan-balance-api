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

| Field | Type | Source (see [plan.md](plan.md#field-sources-design-question)) |
| --- | --- | --- |
| `loanId` | int64 | Request, confirmed by `rw.Loan` |
| `loanStatus` | string | `rw.Loan.LoanStatusName` |
| `asOfDate` | date | Database server date (D10) |
| `principalBalance` | decimal(2) | `rw.LoanPrincipalRemaining` (D1, D8) |
| `interest`, `serviceFees` | decimal(2) | Calculator (D2, D4) |
| `lateFees` | decimal(2) | Calculator (D2) |
| `unappliedAmount` | decimal(2) | Calculator (D2, D7) |
| `others[5]` | OtherBucket | Names from `rw.OtherBucketName`; amounts from calculator (D2, D9) |
| `totalBalance` | decimal(2) | Calculator (D2, D6) |
| `pastDueAmount` | decimal(2) | Calculator (D2, D6) |
| `daysPastDue` | int32 | **No parity source** (D5) |

Rules: all amounts are present, with zero for none (FR-006). Paid-off and written-off loans return
0 for every due and balance field (D8). Other buckets are always 5, ordered 1–5. A name that is
null, empty, or whitespace becomes `Other N` with `nameIsDefault = true`; any other name is
returned as stored, with `nameIsDefault = false` (FR-005, edge cases).

### Error

`code` (enum in the contract), `message` (constant per code), `field` (input errors only).

## `rw` views (versioned scripts in `db/rw`)

Every view reads SPI tables only. No view calls an SPI function or procedure (Principle III,
pending D1). Every view is filtered by `LoanId` from the application query.

### `rw.Loan`

One row per loan record.

| Column | From |
| --- | --- |
| `LoanId` | `lrLoanReceivable` |
| `LoanStatusId` | `lrLoanReceivable` |
| `LoanStatusName` | Loan status type table *(confirm)* |
| `SalesContractId` | `lrLoanReceivable` |
| `ResortId` | Contract's resort *(confirm table)* |
| `IsTerminal` | Status is paid off or written off *(confirm status IDs)* |

### `rw.OtherBucketName`

One row per resort and bucket number (1–5). Built from `lrControl.othLabel1..5` by unpivot.
Joined to `rw.Loan` on `ResortId`. A missing row means default names (D9).

### `rw.LoanPrincipalRemaining`

One row per loan record: contract-level principal remaining as of the end of today, reproducing
`dbo.lrGetPrincipalRemaining` from tables (P-B). Blocked on D1/D8.

### Engine input views (blocked on D2)

These views expose the same data the engine reads through `lrExcelGetLoanById`,
`lrExcelGetTransactions`, `lrExcelGetControlSettings`, and `lrExcelGetContractPaidTotals`, read
directly from SPI tables. Proposed names: `rw.EngineLoan`, `rw.EngineTransaction`,
`rw.EngineControlSetting`, `rw.EngineContractPaidTotal`. Columns are fixed after the procedures'
definitions are read locally.

### `rw.SchemaVersions` (table)

The DbUp journal. It is the only `rw` table.

## State

The service holds no state. There are no state transitions. Loan status is read, never changed.
