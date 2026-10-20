# ResortWise Loan Balance API

ResortWise is an open API layer over SPI loan data, built with .NET on Microsoft SQL Server.

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

Before every commit, check the staged diff (`git diff --cached`) for secrets and real data. If a
secret is ever committed, treat it as compromised and rotate it. Rewriting history is not enough.

## Database rules

- **Never touch SPI objects.** Do not create, alter, drop, or write to SPI tables, stored
  procedures, or functions.
- **New database objects go only in the `rw` schema.** ResortWise may add views and tables there,
  and nowhere else.
- **The API reads SPI data only through `rw` views.** Application code never queries SPI tables or
  calls SPI procedures directly.
- Every `rw` object is created by a versioned script committed to this repo.

## Constitution summary

1. **API first.** Every action goes through the API. There are no internal-only endpoints; our own
   screens use the same public API as external systems. Every endpoint is documented in OpenAPI.
2. **Tenant isolation (non-negotiable).** Every request needs an `X-Tenant-Id` header. If it is
   missing or empty, return HTTP 400 with error code `TENANT_REQUIRED` before touching data.
   Never return data from another tenant.
3. **SPI schema is read-only (non-negotiable).** See [Database rules](#database-rules).
4. **Parity with the SPI Application.** API values must match what the SPI Application shows,
   proven by tests against the `AlphaPMS_ResortWise` test database. If two SPI screens disagree,
   record the loan and both values in the spec and escalate. Never pick one silently.
5. **Public repo hygiene (non-negotiable).** See [This repository is PUBLIC](#this-repository-is-public).
6. **Stable error contract.** Every error has a stable error code, plus the field name for input
   errors. Codes are listed in OpenAPI and never renamed. Clients map errors by code, never by
   message text. Messages never include secrets, SQL, or stack traces.
7. **Tested against SPI data.** Every endpoint has automated tests: success, `TENANT_REQUIRED`,
   tenant isolation, and its error codes. Returned values are checked against the SPI database,
   not only mocks.

## Spec Kit workflow

All feature work happens on a feature branch, never directly on `main`. **Commit after each step.**

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

When the feature is complete, open a pull request for review. Reviewers check compliance with the
constitution. After merge, set the spec's **Status** to `Done`.
