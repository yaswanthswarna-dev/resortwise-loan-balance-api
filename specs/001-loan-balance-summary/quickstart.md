# Quickstart: Loan Balance Summary API

How to run and validate the feature locally. Never commit the files or values created here.
Connection strings, key hashes, and test output stay in git-ignored files or environment
variables.

## Prerequisites

- .NET 8 SDK, Docker, `sqlcmd`
- Local SQL Server with `AlphaPMS_ResortWise` and `AlphaPMS_ResortWise_B` restored
- For engine parity (only if D3 is approved): a local SPI Application install. Its path is set in
  `RW_SPI_ENGINE_PATH`.

## 1. Apply the `rw` scripts to both test databases

```powershell
dotnet run --project tools/ResortWise.RwMigrator -- --connection-env RW_MIGRATE_A
dotnet run --project tools/ResortWise.RwMigrator -- --connection-env RW_MIGRATE_B
```

Expected: `db/rw` scripts are applied in order and journaled in `rw.SchemaVersions`. No SPI object
changes. Grant the API login `SELECT ON SCHEMA::rw` only.

## 2. Prepare tenant B

```powershell
sqlcmd -S <server> -d AlphaPMS_ResortWise_B -E -b -i tests/db/setup/B001__diverge_tenant_b.sql
```

Expected: the script succeeds on `AlphaPMS_ResortWise_B`. As a safety check, running it with
`-d AlphaPMS_ResortWise` must fail immediately with "Wrong database" and change nothing.

## 3. Create a caller key

```powershell
$bytes = [byte[]]::new(32); [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$key = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
$hash = -join ([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($key)) | % { $_.ToString('x2') })
```

Give `$key` to the caller. Put only `$hash` in configuration.

## 4. Configure (git-ignored `appsettings.Local.json` or environment variables)

Copy `src/ResortWise.LoanBalance.Api/appsettings.Local.json.example` and fill in: two tenants
(`tenant-a` → `AlphaPMS_ResortWise`, `tenant-b` → `AlphaPMS_ResortWise_B`), one caller allowed
both, one caller allowed only `tenant-a`. See [data-model.md](data-model.md#configuration-entities).

## 5. Run

```powershell
dotnet run --project src/ResortWise.LoanBalance.Api
# or
docker build -t resortwise-loan-balance . ; docker run --env-file <local env file> -p 8080:8080 resortwise-loan-balance
```

## 6. Smoke checks

| Request | Expected |
| --- | --- |
| No `Authorization` | 401 `UNAUTHENTICATED` |
| Valid key, no `X-Tenant-Id` | 400 `TENANT_REQUIRED`, field `X-Tenant-Id` |
| Tenant-A-only key, `X-Tenant-Id: tenant-b` | 403 `TENANT_FORBIDDEN` |
| Valid key, unknown tenant | 403 `TENANT_FORBIDDEN` (identical body) |
| `/v1/loans/007/balance-summary` | 400 `LOAN_ID_INVALID`, field `loanId` |
| A loan ID that does not exist | 404 `LOAN_NOT_FOUND` |
| An active loan | 200; body matches the [contract](contracts/loan-balance-summary.openapi.yaml) |

Each request writes exactly one JSON audit line to stdout, with no amounts.

## 7. Tests

```powershell
dotnet test --filter "Category!=Database"          # what GitHub runs
$env:RW_DB_TESTS = "1"; dotnet test --filter "Category=Database"   # local only
```

Expected: parity has 0 mismatches over all active loans in `AlphaPMS_ResortWise` (SC-001), and
the isolation suite passes (SC-002, SC-003, SC-005). The test run prints the PR summary: date,
database name, loans checked, passed and failed counts. Paste only that summary into the PR.
