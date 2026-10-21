<!--
Sync Impact Report
- Version change: 1.1.0 → 1.1.1 (PATCH, as requested in review)
- Modified principles:
  - III. SPI Schema Is Read-Only (states the test-setup exception: data changes in the two local
    test copies only, never schema changes)
  - VII. Tested Against SPI Data (adds the test data rule: test-setup scripts change data only in
    `AlphaPMS_ResortWise` and `AlphaPMS_ResortWise_B`, check the database name first and stop
    otherwise, and contain no loan values or client data; isolation tests use the two copies)
- Added sections: none
- Expanded sections: Data & Platform Constraints (test database bullet names the B copy)
- Removed sections: none
- Templates reviewed: plan-template.md, spec-template.md, tasks-template.md read gates and
  rules from this file at runtime; no edits required
- CLAUDE.md: test data rule added
- Deferred TODOs: none
-->

# ResortWise Loan Balance API Constitution

This repository is the ResortWise loan balance API, a read-only service in the ResortWise
platform that returns SPI loan balances through new `rw` views. These principles govern every
feature, specification, plan, and pull request in this repository.

## Core Principles

### I. API First

- Every action in the system MUST be performed through an API call. No business operation may
  bypass the API (no direct database writes from UIs, scripts, or jobs).
- There are NO internal-only APIs. ResortWise's own screens MUST consume the same public
  endpoints that external systems use, with the same authentication, tenancy, and error rules.
- Every endpoint MUST be described in the OpenAPI document, including request and response
  schemas, the `X-Tenant-Id` header, the caller credential, and every error code it can return.
  An endpoint that is not in the OpenAPI document is not done.

**Rationale**: One public surface keeps behavior consistent for every consumer and makes the
OpenAPI contract the single source of truth.

### II. Tenant Isolation and Authorization (NON-NEGOTIABLE)

- Every request MUST carry a tenant identifier in the `X-Tenant-Id` header and a valid caller
  credential. The credential mechanism is chosen in the plan and documented in OpenAPI.
- Before reading any data, the API MUST authenticate the caller and confirm the caller is
  allowed to use the tenant named in `X-Tenant-Id`:
  - Missing or empty `X-Tenant-Id`: HTTP 400, error code `TENANT_REQUIRED`.
  - Missing or invalid credential: HTTP 401, error code `UNAUTHENTICATED`.
  - Valid credential that is not allowed the requested tenant: HTTP 403, error code
    `TENANT_FORBIDDEN`.
- Each SPI client has its own database. A tenant registry MUST map each tenant ID to exactly one
  database, and the API MUST only ever connect to the database mapped to the request's tenant.
- Tenant database connection details MUST live only in git-ignored local settings or
  environment variables, never in the repository (Principle V).
- The API MUST NEVER return, count, or reveal the existence of data belonging to another tenant.
- Tenant isolation tests MUST use at least two tenant databases and MUST prove that one tenant
  cannot read another tenant's loans.

**Rationale**: Loan data is client-confidential; a single cross-tenant leak is a breach. A tenant
header alone proves nothing about the caller, so access to a tenant must be authorized, and
one database per tenant makes the isolation boundary physical and testable.

### III. SPI Schema Is Read-Only (NON-NEGOTIABLE)

- ResortWise MUST NEVER create, alter, or drop SPI tables, stored procedures, functions, or
  other SPI-owned objects, and MUST NEVER write to SPI tables.
- ResortWise MAY add new views and tables only, and all of them MUST live in the `rw` schema.
- The API MUST read SPI data only through `rw` views. Application code MUST NOT query SPI tables
  or call SPI procedures directly.
- Every `rw` object MUST be created by a versioned, reviewable script committed to this repository.
- The only exception is test-setup scripts, which MAY change data in the two local test copies
  under the test data rule in Principle VII. They MUST NOT create, alter, or drop SPI objects,
  and MUST NEVER run against a client database.

**Rationale**: The SPI Application owns its schema. Isolating ResortWise in `rw` keeps SPI
upgrades safe and gives one auditable seam between the two systems.

### IV. Parity with the SPI Application

- Every value the API returns MUST match the value the SPI Application shows for the same loan
  and tenant.
- Parity MUST be proven by automated tests that run against the `AlphaPMS_ResortWise` test
  database.
- If two SPI screens show different values for the same quantity, the team MUST record the loan
  identifier and both values (with the screens they came from) in the feature spec and escalate
  the discrepancy. The team MUST NEVER silently choose one value; the feature MUST NOT be marked
  done until the escalation is resolved and the chosen rule is documented.

**Rationale**: Clients trust ResortWise only if it agrees with the system of record. Hidden
choices between conflicting numbers become financial errors.

### V. Public Repository Hygiene (NON-NEGOTIABLE)

- This repository is public. The following MUST NEVER be committed: client data, AlphaPMS data
  (including extracts, screenshots, or test fixtures copied from it), database backups or data
  files, connection strings, passwords, API keys, certificates, or private keys.
- Secrets MUST live only in local settings that git ignores (for example user-secrets,
  `appsettings.Local.json`, `appsettings.Development.json`, or environment variables). Committed
  configuration MUST contain placeholders only.
- Test fixtures committed to the repository MUST use synthetic data. Parity tests MUST read real
  values from the test database at run time instead of storing them in the repository.
- Pull request descriptions, comments, and review threads are public too. They MUST NEVER
  contain loan values, owner details, or any other client data.
- A secret that is committed, even briefly, MUST be treated as compromised and rotated; removing
  it from git history alone is not sufficient.

**Rationale**: Anything pushed to a public repository must be assumed copied permanently.

### VI. Stable Error Contract

- Every error response MUST include a stable, machine-readable error code (for example
  `TENANT_REQUIRED`, `UNAUTHENTICATED`, `TENANT_FORBIDDEN`).
- Input validation errors MUST also include the name of the offending field.
- Error codes are part of the public contract: once released they MUST NOT be renamed or
  repurposed within an API version, and each MUST be listed in the OpenAPI document. Changing
  an error code is a breaking change (Principle VIII).
- Clients map errors by code, never by message text. Message text MAY change at any time and
  MUST NOT contain secrets, SQL, stack traces, or other tenants' data.

**Rationale**: Stable codes let every client handle errors reliably while messages stay free to
improve.

### VII. Tested Against SPI Data

- Every endpoint MUST have automated tests covering success, `TENANT_REQUIRED`,
  `UNAUTHENTICATED`, `TENANT_FORBIDDEN`, tenant isolation, and its documented error codes.
- Returned values MUST be checked against the SPI database (see Principle IV), not only against
  mocks.
- Tests that need `AlphaPMS_ResortWise` or any other SPI database MUST run locally before the
  pull request is opened, and MUST NEVER run on GitHub. GitHub runs only tests that need no
  database.
- The pull request description MUST include a summary of the local run: the date, the test
  database name, the number of loans checked, and the passed and failed counts. It MUST NOT
  include loan values or other data (Principle V).
- A feature MUST NOT merge with failing or skipped endpoint tests unless the skip is documented
  and approved in the pull request.
- **Test data**: test-setup scripts MAY change data only in the two local test copies,
  `AlphaPMS_ResortWise` and `AlphaPMS_ResortWise_B`, and MUST NEVER change data in a client
  database.
  - Tenant isolation tests (Principle II) use these two local copies as the two tenant databases.
    A test-setup script changes or removes some loans in `AlphaPMS_ResortWise_B` so the two
    databases return different results.
  - Every test-setup script MUST first check the name of the database it is connected to and MUST
    stop with an error, before changing anything, unless the name is exactly
    `AlphaPMS_ResortWise` or `AlphaPMS_ResortWise_B`.
  - Test-setup scripts MUST NOT contain loan values or client data (Principle V). They select
    rows by rule (for example, by a condition on loan IDs), never by listing specific loans or
    values.

**Rationale**: Automated checks against real SPI data are the only durable proof of correctness
and parity. Keeping database tests off GitHub keeps SPI data and connection details out of a
public CI system, and the run summary gives reviewers evidence without exposing data. The
database name check makes it impossible for a test-setup script to change a client database by
mistake, and rule-based selection keeps client data out of the public repository.

### VIII. URL-Path API Versioning

- The API version MUST be in the URL path, starting at `/v1`.
- Adding optional request or response fields is not a breaking change.
- Removing or renaming a field, changing a field's type or meaning, or changing an error code is
  a breaking change and MUST be released under a new API version. Breaking changes MUST NEVER be
  made to an existing version.

**Rationale**: External systems depend on the contract; an explicit version in the path lets the
contract evolve without breaking existing clients.

## Data & Platform Constraints

- Stack: .NET API over Microsoft SQL Server.
- Runtime: the service MUST run in a container and MUST stay cloud-agnostic. It MUST NOT depend
  on any single cloud vendor's services.
- Configuration: all runtime configuration MUST come from environment variables. Committed
  configuration files contain placeholders only (Principle V).
- Tenant data: one SPI database per client, selected only through the tenant registry
  (Principle II).
- SPI data access: read-only, via `rw` views only (Principle III).
- Test databases: `AlphaPMS_ResortWise` is the reference for parity tests, and a second local
  copy, `AlphaPMS_ResortWise_B`, is changed by test-setup scripts to serve as the second tenant in
  isolation tests. Their connection details are supplied through git-ignored local settings or
  environment variables only (Principle V), and tests against them run locally only
  (Principle VII).
- Contract: the OpenAPI document is the authoritative description of every endpoint, version,
  header, credential, schema, and error code (Principles I, VI, and VIII).

## Development Workflow

1. **Branch and pull request**: Nothing is committed directly to `main`, including constitution
   changes. All work happens on a branch and reaches `main` only through a reviewed pull request.
   The initial setup commit was the only exception.
2. **Specify**: Each spec MUST link its design and/or API contract (OpenAPI).
3. **Commit per step**: A commit MUST be made after each Spec Kit step (constitution, specify,
   clarify, plan, tasks, analyze, implement, converge).
4. **Analyze before implement**: `/speckit-analyze` MUST be run, and its critical findings
   resolved, before `/speckit-implement` starts.
5. **Local database tests**: Before opening the pull request, the author MUST run the database
   tests locally and add the run summary required by Principle VII to the pull request
   description.
6. **Review**: Every pull request is reviewed for compliance with this constitution, including
   the plan's Constitution Check.
7. **Done**: When a feature is complete and merged, its spec's **Status** MUST be set to `Done`.

## Governance

- This constitution supersedes all other development practices in this repository. Where a spec,
  plan, or task conflicts with it, the constitution wins.
- Every plan MUST pass the Constitution Check gate. Any deviation MUST be justified in the plan's
  Complexity Tracking section and explicitly approved in pull request review. Principles marked
  NON-NEGOTIABLE (II, III, V) cannot be waived.
- Amendments are made on a branch and merged by reviewed pull request that updates this file,
  states the version bump and rationale, and lists any dependent templates or docs that must
  change.
- Versioning follows semantic versioning: MAJOR for removing or redefining a principle in a
  backward-incompatible way; MINOR for adding a principle or section or materially expanding
  guidance; PATCH for clarifications and wording fixes.
- Reviewers MUST verify compliance with these principles on every pull request.

**Version**: 1.1.1 | **Ratified**: 2026-10-20 | **Last Amended**: 2026-10-21
