# ResortWise Loan Balance API

This repository is the ResortWise loan balance API: a read-only service in the ResortWise
platform that returns SPI loan balances through new `rw` views. It is built with .NET on
Microsoft SQL Server.

The full rules are in [`.specify/memory/constitution.md`](.specify/memory/constitution.md).
If this file and the constitution ever disagree, the constitution wins.

## This repository is PUBLIC

Never commit any of the following, not even briefly and not even in a test fixture:

- Client data or AlphaPMS data (extracts, query results, screenshots)
- Database backups or data files (`*.bak`, `*.mdf`, `*.ldf`, `*.bacpac`, ...)
- Connection strings, passwords, API keys, certificates, or private keys

Secrets live only in git-ignored local settings: `dotnet user-secrets`, `appsettings.Local.json`,
`appsettings.Development.json`, or environment variables. Committed config files such as
`appsettings.json` contain placeholders only. Committed test data must be synthetic.

Pull request descriptions, comments, and reviews are public too. Never paste loan values, owner
details, or any other client data into them.

Before every commit, check the staged diff (`git diff --cached`) for secrets and real data. If a
secret is ever committed, treat it as compromised and rotate it. Rewriting history is not enough.

## Database rules

- **Never change SPI.** ResortWise never changes SPI Application code or SPI database objects
  (tables, views, stored procedures, functions). Do not create, alter, drop, or write to them.
  The SPI Excel loan engine is one example and is never modified.
- **New database objects go only in the `rw` schema.** ResortWise may add views and tables there,
  and nowhere else.
- **`rw` views may call SPI functions read-only**, so SPI logic is reused instead of rebuilt. No
  `rw` object may call an SPI procedure or anything that writes data.
- **The API reads SPI data only through `rw` views.** Application code never queries SPI tables
  and never calls SPI procedures or functions itself.
- **Tests may call SPI procedures and functions read-only for parity.** Tests may call them only
  against the local test databases, only to get parity reference values. Tests never call any SPI
  procedure that writes data. This exception never applies to application code.
- Every `rw` object is created by a versioned script committed to this repo.
- **One database per tenant.** Each SPI client has its own database. A tenant registry maps each
  tenant ID to exactly one database, and the API only ever connects to the database mapped to the
  request's tenant. Connection details live only in git-ignored local settings or environment
  variables, never in the repo.

## Test data rules

- **Test-setup scripts change data only in `AlphaPMS_ResortWise_B`.** They never change data in
  `AlphaPMS_ResortWise` or in a client database, and they never create, alter, or drop SPI
  objects.
- **`AlphaPMS_ResortWise` is the parity reference and stays exactly as restored.** Its data never
  changes: no script may ever change data in it. Adding `rw` objects to it is allowed.
- Tenant isolation tests use `AlphaPMS_ResortWise` and `AlphaPMS_ResortWise_B`. A test-setup
  script changes or removes some loans in `AlphaPMS_ResortWise_B` so the two databases return
  different results.
- Every test-setup script checks the database name first and stops with an error, before changing
  anything, unless it is exactly `AlphaPMS_ResortWise_B`.
- Test-setup scripts never contain loan values or client data. They select rows by rule (for
  example, a condition on loan IDs), never by listing specific loans or values.

## Platform

- The service runs in a container and stays cloud-agnostic. Do not depend on any one cloud
  vendor's services.
- All runtime configuration comes from environment variables.

## Constitution summary

1. **API first.** Every action goes through the API. There are no internal-only endpoints; our own
   screens use the same public API as external systems. Every endpoint is documented in OpenAPI.
2. **Tenant isolation and authorization (non-negotiable).** Every request needs an `X-Tenant-Id`
   header and a valid caller credential (the mechanism is chosen in the plan). Before reading any
   data, the API confirms the caller is allowed that tenant:
   - Missing or empty `X-Tenant-Id`: HTTP 400, `TENANT_REQUIRED`.
   - Missing or invalid credential: HTTP 401, `UNAUTHENTICATED`.
   - Caller not allowed that tenant: HTTP 403, `TENANT_FORBIDDEN`.

   Never return data from another tenant. Isolation tests use at least two tenant databases
   (`AlphaPMS_ResortWise` and `AlphaPMS_ResortWise_B` locally) and prove one tenant cannot read
   another tenant's loans.
3. **SPI schema is read-only (non-negotiable).** See [Database rules](#database-rules).
   ResortWise never changes SPI Application code or SPI database objects. `rw` views may call SPI
   functions read-only; the application itself never calls SPI procedures or functions. The only
   other exceptions are test-setup scripts, which may change data in `AlphaPMS_ResortWise_B` only (see
   [Test data rules](#test-data-rules)), and tests, which may call SPI procedures and functions
   read-only against the local test databases for parity reference values, never one that
   writes data.
4. **Parity with the SPI Application.** API values must match what the SPI Application shows,
   proven by tests against the `AlphaPMS_ResortWise` test database. If two SPI screens disagree,
   record the loan and both values in the spec and escalate. Never pick one silently.
5. **Public repo hygiene (non-negotiable).** See [This repository is PUBLIC](#this-repository-is-public).
6. **Stable error contract.** Every error has a stable error code, plus the field name for input
   errors. Codes are listed in OpenAPI and never renamed. Clients map errors by code, never by
   message text. Messages never include secrets, SQL, or stack traces.
7. **Tested against SPI data.** Every endpoint has automated tests: success, `TENANT_REQUIRED`,
   `UNAUTHENTICATED`, `TENANT_FORBIDDEN`, tenant isolation, and its error codes. Returned values
   are checked against the SPI database, not only mocks.
   - Tests that need `AlphaPMS_ResortWise` or any SPI database run **locally before the PR is
     opened, never on GitHub**. GitHub runs only tests that need no database.
   - The PR description includes a summary of the local run: date, test database name, number of
     loans checked, and passed and failed counts. No data.
   - Test-setup scripts follow the [Test data rules](#test-data-rules).
8. **URL-path API versioning.** The version is in the URL path, starting at `/v1`. Adding optional
   fields is not breaking. Removing or renaming a field, changing a field's type or meaning, or
   changing an error code is breaking and requires a new API version.

## Spec Kit workflow

**Nothing goes directly to `main`**, including constitution changes. All work happens on a
branch and reaches `main` only through a reviewed pull request. (The initial setup commit was the
only exception.) **Commit after each step.**

| Step | Command | Output |
| --- | --- | --- |
| 1. Constitution | `/speckit-constitution` | Project principles (only when amending them) |
| 2. Specify | `/speckit-specify` | `spec.md`, which must link the design or API contract |
| 3. Clarify | `/speckit-clarify` | Ambiguities in the spec resolved |
| 4. Plan | `/speckit-plan` | `plan.md` and design artifacts; must pass the Constitution Check |
| 5. Tasks | `/speckit-tasks` | `tasks.md` |
| 6. Analyze | `/speckit-analyze` | Consistency check across spec, plan, and tasks; fix critical findings |
| 7. Implement | `/speckit-implement` | Code and tests |
| 8. Converge | `/speckit-converge` | Any unbuilt work added to `tasks.md` and finished |

Analyze runs after tasks because it checks `tasks.md`. It must always run before implement.

When the feature is complete, run the database tests locally, then open a pull request for review
with the local run summary (no data) in the description. Reviewers check compliance with the
constitution. After merge, set the spec's **Status** to `Done`.
