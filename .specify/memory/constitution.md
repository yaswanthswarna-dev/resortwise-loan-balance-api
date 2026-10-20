<!--
Sync Impact Report
- Version change: (unversioned template) → 1.0.0
- Principles defined (initial ratification):
  - I. API First
  - II. Tenant Isolation (NON-NEGOTIABLE)
  - III. SPI Schema Is Read-Only (NON-NEGOTIABLE)
  - IV. Parity with the SPI Application
  - V. Public Repository Hygiene (NON-NEGOTIABLE)
  - VI. Stable Error Contract
  - VII. Tested Against SPI Data
- Added sections: Data & Platform Constraints; Development Workflow; Governance
- Removed sections: none
- Templates reviewed: plan-template.md (Constitution Check reads gates from this file at runtime),
  spec-template.md (Status field used by Workflow rule), tasks-template.md — no edits required
- Deferred TODOs: none
-->

# ResortWise Constitution

ResortWise is an open API layer over SPI loan data. These principles govern every feature,
specification, plan, and pull request in this repository.

## Core Principles

### I. API First

- Every action in the system MUST be performed through an API call. No business operation may
  bypass the API (no direct database writes from UIs, scripts, or jobs).
- There are NO internal-only APIs. ResortWise's own screens MUST consume the same public
  endpoints that external systems use, with the same authentication, tenancy, and error rules.
- Every endpoint MUST be described in the OpenAPI document, including request and response
  schemas, the `X-Tenant-Id` header, and every error code it can return. An endpoint that is not
  in the OpenAPI document is not done.

**Rationale**: One public surface keeps behavior consistent for every consumer and makes the
OpenAPI contract the single source of truth.

### II. Tenant Isolation (NON-NEGOTIABLE)

- Every request MUST carry a tenant identifier in the `X-Tenant-Id` header.
- A request without the header, or with an empty value, MUST be rejected with HTTP 400 and error
  code `TENANT_REQUIRED` before any data access occurs.
- Every data access MUST be scoped to the request's tenant. The API MUST NEVER return, count,
  or reveal the existence of data belonging to another tenant.
- Tenant scoping MUST be covered by automated tests, including a test proving that one tenant
  cannot read another tenant's records.

**Rationale**: Loan data is client-confidential; a single cross-tenant leak is a breach.

### III. SPI Schema Is Read-Only (NON-NEGOTIABLE)

- ResortWise MUST NEVER create, alter, or drop SPI tables, stored procedures, functions, or
  other SPI-owned objects, and MUST NEVER write to SPI tables.
- ResortWise MAY add new views and tables only, and all of them MUST live in the `rw` schema.
- The API MUST read SPI data only through `rw` views. Application code MUST NOT query SPI tables
  or call SPI procedures directly.
- Every `rw` object MUST be created by a versioned, reviewable script committed to this repository.

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
- A secret that is committed, even briefly, MUST be treated as compromised and rotated; removing
  it from git history alone is not sufficient.

**Rationale**: Anything pushed to a public repository must be assumed copied permanently.

### VI. Stable Error Contract

- Every error response MUST include a stable, machine-readable error code (for example
  `TENANT_REQUIRED`).
- Input validation errors MUST also include the name of the offending field.
- Error codes are part of the public contract: once released they MUST NOT be renamed or
  repurposed, and each MUST be listed in the OpenAPI document.
- Clients map errors by code, never by message text. Message text MAY change at any time and
  MUST NOT contain secrets, SQL, stack traces, or other tenants' data.

**Rationale**: Stable codes let every client handle errors reliably while messages stay free to
improve.

### VII. Tested Against SPI Data

- Every endpoint MUST have automated tests covering success, tenant rejection
  (`TENANT_REQUIRED`), tenant isolation, and its documented error codes.
- Returned values MUST be checked against the SPI database (see Principle IV), not only against
  mocks.
- A feature MUST NOT merge with failing or skipped endpoint tests unless the skip is documented
  and approved in the pull request.

**Rationale**: Automated checks against real SPI data are the only durable proof of correctness
and parity.

## Data & Platform Constraints

- Platform: .NET API over Microsoft SQL Server.
- SPI data access: read-only, via `rw` views only (Principle III).
- Test database: `AlphaPMS_ResortWise` is the reference for parity tests. Its connection details
  are supplied through git-ignored local settings or environment variables only (Principle V).
- Contract: the OpenAPI document is the authoritative description of every endpoint, header,
  schema, and error code (Principles I and VI).

## Development Workflow

1. **Feature branch**: All work happens on a feature branch; nothing is committed directly to
   `main`.
2. **Specify**: Each spec MUST link its design and/or API contract (OpenAPI).
3. **Commit per step**: A commit MUST be made after each Spec Kit step (constitution, specify,
   clarify, plan, tasks, analyze, implement).
4. **Analyze before implement**: `/speckit-analyze` MUST be run, and its critical findings
   resolved, before `/speckit-implement` starts.
5. **Review**: Every feature is merged through a pull request that is reviewed for compliance
   with this constitution, including the plan's Constitution Check.
6. **Done**: When a feature is complete and merged, its spec's **Status** MUST be set to `Done`.

## Governance

- This constitution supersedes all other development practices in this repository. Where a spec,
  plan, or task conflicts with it, the constitution wins.
- Every plan MUST pass the Constitution Check gate. Any deviation MUST be justified in the plan's
  Complexity Tracking section and explicitly approved in pull request review. Principles marked
  NON-NEGOTIABLE (II, III, V) cannot be waived.
- Amendments are made by pull request that updates this file, states the version bump and
  rationale, and lists any dependent templates or docs that must change.
- Versioning follows semantic versioning: MAJOR for removing or redefining a principle in a
  backward-incompatible way; MINOR for adding a principle or section or materially expanding
  guidance; PATCH for clarifications and wording fixes.
- Reviewers MUST verify compliance with these principles on every pull request.

**Version**: 1.0.0 | **Ratified**: 2026-10-20 | **Last Amended**: 2026-10-20
