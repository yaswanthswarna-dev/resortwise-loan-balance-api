# Quickstart: Loan Balance Summary API

How to run and validate the feature locally. Never commit the files or values created here.
Connection strings, key hashes, and test output stay in git-ignored files or environment
variables.

## Prerequisites

- .NET 8 SDK, Docker, `sqlcmd`
- Local SQL Server with `AlphaPMS_ResortWise` and `AlphaPMS_ResortWise_B` restored

## 1. Apply the `rw` scripts and create the logins

Apply the scripts with an owner-level local connection (Windows authentication). `RW_MIGRATE_A`
and `RW_MIGRATE_B` hold those connection strings for `AlphaPMS_ResortWise` and
`AlphaPMS_ResortWise_B`.

```powershell
dotnet run --project tools/ResortWise.RwMigrator -- --connection-env RW_MIGRATE_A
dotnet run --project tools/ResortWise.RwMigrator -- --connection-env RW_MIGRATE_B
```

Expected: on a database without schema `rw`, the migrator first runs
`db/rw/bootstrap/create_schema_and_journal.sql` (schema `rw` and `rw.SchemaVersions`), then
applies the `db/rw/V###__*.sql` scripts in order, journaled in `rw.SchemaVersions`. Only `rw`
objects are added; no SPI object and no data changes, so `AlphaPMS_ResortWise` still counts as
"exactly as restored".

Then create two logins and their users in both test databases (research R9). This step runs
**once** per server; rerunning it fails because the logins and users already exist.

Passwords are typed at the prompt and passed as `sqlcmd` variables. Use local test passwords with
no quotes or spaces, because `sqlcmd -v` values are visible on the process command line and break
on those characters; or pass them another way (for example, create the logins interactively in
SQL Server Management Studio). Never write passwords to a file or commit them.

```powershell
$api = [Net.NetworkCredential]::new('', (Read-Host 'Password for rw_api' -AsSecureString)).Password
$ref = [Net.NetworkCredential]::new('', (Read-Host 'Password for rw_parity_ref' -AsSecureString)).Password

# Once per server
sqlcmd -S <server> -E -b -v ApiPwd="$api" RefPwd="$ref" -Q 'CREATE LOGIN rw_api WITH PASSWORD = N''$(ApiPwd)'', CHECK_POLICY = ON; CREATE LOGIN rw_parity_ref WITH PASSWORD = N''$(RefPwd)'', CHECK_POLICY = ON;'

# In each test database
foreach ($db in 'AlphaPMS_ResortWise', 'AlphaPMS_ResortWise_B') {
  sqlcmd -S <server> -d $db -E -b -Q 'CREATE USER rw_api FOR LOGIN rw_api; GRANT SELECT ON SCHEMA::rw TO rw_api; DENY SELECT, EXECUTE ON SCHEMA::dbo TO rw_api; CREATE USER rw_parity_ref FOR LOGIN rw_parity_ref; ALTER ROLE db_datareader ADD MEMBER rw_parity_ref; ALTER ROLE db_denydatawriter ADD MEMBER rw_parity_ref; GRANT EXECUTE ON dbo.lrGetLoanDetails TO rw_parity_ref; GRANT EXECUTE ON dbo.lrGetLoanMasterInfo TO rw_parity_ref; GRANT EXECUTE ON dbo.lrGetLoanStatusTypes TO rw_parity_ref; GRANT EXECUTE ON dbo.lrGetTransactionActivity TO rw_parity_ref;'
}
Remove-Variable api, ref
```

- **`rw_api`** is the API login: `SELECT` on schema `rw` only. The API and every API request in
  the database tests use it. The `DENY` on schema `dbo` blocks anything it could inherit from
  `public` or `guest` grants in the restored database. The `rw` views still work, because SQL
  Server does not check permissions on objects a view reaches through the same owner (`dbo`).
- **`rw_parity_ref`** is the test-only reference login: read-only on SPI tables, plus `EXECUTE` on
  only the read-only SPI procedures in research R4. Never grant it `EXECUTE` on any other
  procedure, because a procedure's own writes would bypass `db_denydatawriter`.
- Users and grants change no data or SPI object definitions. Granting `EXECUTE` on SPI
  procedures to `rw_parity_ref` is allowed (plan, O3, closed 2026-10-23).

## 2. Prepare tenant B

Run it by hand with an owner-level local connection. The database tests do not run it, so they
need no login that can write.

```powershell
sqlcmd -S <server> -d AlphaPMS_ResortWise_B -E -b -i tests/db/setup/B001__diverge_tenant_b.sql
```

Expected: the script succeeds on `AlphaPMS_ResortWise_B`. Never run it against
`AlphaPMS_ResortWise` or any other database, not even to test its guard (constitution
Principle III). The unit test `SetupScriptGuardTests` checks the guard without running the
script.

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
both, one caller allowed only `tenant-a`. Both tenant connection strings use the `rw_api` login,
never `rw_parity_ref` or an owner login. See [data-model.md](data-model.md#configuration-entities).

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
| An active loan | 200; `loanId`, `loanStatus`, `asOfDate`, `principalBalance` only, matching the [contract](contracts/loan-balance-summary.openapi.yaml) |
| An old rewritten loan record | 200; its own non-active status, principal `0.00` |

Each request writes exactly one JSON audit line to stdout, with no amounts.

## 7. Tests

The database tests read these environment variables. Set them in the local session only; never
put them in a committed file.

| Variable | Value |
| --- | --- |
| `RW_DB_TESTS` | `1` to run the database tests; unset, they skip |
| `RW_DB_A` | Connection string to `AlphaPMS_ResortWise` as `rw_api` (tenant A) |
| `RW_DB_B` | Connection string to `AlphaPMS_ResortWise_B` as `rw_api` (tenant B) |
| `RW_REF_A` | Connection string to `AlphaPMS_ResortWise` as `rw_parity_ref` (parity references) |
| `RW_REF_B` | Connection string to `AlphaPMS_ResortWise_B` as `rw_parity_ref` (isolation references) |

```powershell
dotnet test --filter "Category!=Database"          # what GitHub runs
$env:RW_DB_TESTS = "1"; dotnet test --filter "Category=Database"   # local only
```

Expected: parity has 0 mismatches over all active loans in `AlphaPMS_ResortWise` (SC-001), every
non-active record returns principal `0.00`, the API login cannot read SPI objects, and the
isolation suite passes (SC-002, SC-003, SC-005). The test run prints the PR summary: date,
database name, loans checked, passed and failed counts. Paste only that summary into the PR.
