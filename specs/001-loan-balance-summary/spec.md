# Feature Specification: Loan Balance Summary API

**Feature Branch**: `001-loan-balance-summary`

**Created**: 2026-10-22

**Status**: Draft

**API Contract**: [contracts/loan-balance-summary.openapi.yaml](contracts/loan-balance-summary.openapi.yaml)
(OpenAPI). The contract is part of this specification; where they differ, update both together.

**Input**: User description: "Build a read-only Loan Balance Summary API for ResortWise. A caller asks for one loan's balance summary and gets back the same values the SPI Application's Balance Summary screen shows for that loan. Fields returned: loan ID, loan status, as-of date, principal balance, interest, late fees, service fees, unapplied amount, Others 1 to 5, total balance, past-due amount, and days past due. For Others 1 to 5, return the bucket name the client configured, and only fall back to "Other 1" style names when the client has not named it. Payoff amount is out of scope; it will be its own feature. Every request must carry the X-Tenant-Id header and a valid caller credential, and the caller must be allowed that tenant: missing tenant returns 400 TENANT_REQUIRED, missing or invalid credential returns 401 UNAUTHENTICATED, a tenant the caller is not allowed returns 403 TENANT_FORBIDDEN. A loan that does not exist in that tenant returns 404 LOAN_NOT_FOUND, and a loan from another tenant must look exactly like a loan that does not exist. Every error returns a stable error code, plus the field name for input errors. The API is versioned under /v1. Success means: for every active loan in the AlphaPMS_ResortWise test database, the API values match the Balance Summary screen; a tenant mapped to AlphaPMS_ResortWise_B returns different results than one mapped to AlphaPMS_ResortWise; and no request can read another tenant's data. If the Balance Summary screen and the Loan Summary screen show different values for any test loan, do not pick one; list the loan and both values as an open question for clarify. Create an OpenAPI contract for this endpoint in the feature's contracts folder and link it from the spec, so the contract is part of the spec. Do not commit yet."

## Clarifications

### Session 2026-10-22

- Q: Where should Balance Summary vs Loan Summary discrepancies (loan ID and both screen values) be
  recorded, given that this repository is public? → A: In a non-public tracker (for example an
  internal ticket or document). The spec lists only an opaque reference, the affected field, and
  the escalation status, with no loan IDs or values.
- Q: Which loans count as "active" for the 100% parity test against the Balance Summary screen
  (SC-001)? → A: Only loans whose SPI status is exactly "Active" (LoanStatusId = 1). Delinquent
  loans keep the Active status in SPI (past due is calculated from payments, not stored as a
  status), so they are inside the parity scope.
- Q: Should callers be able to request a balance summary as of a specific date, or only as of the
  SPI Application's current business date? → A: Current business date only (superseded 2026-10-23, see Assumptions). The response returns
  the as-of date used, and there is no date input.
- Q: Should the API keep an audit record of every balance-summary request, and if so, how long?
  → A: A structured audit log entry for every request, including refused ones: time, caller ID,
  tenant ID, loan ID, status/error code, and a correlation ID. No balance values. Written to
  stdout; retention is set by the hosting environment.
- Q: What request rate per tenant counts as "normal load" for the target that 95% of requests
  finish in under 1 second (SC-006)? → A: 10 requests/second per tenant, 2 tenants at the same
  time (20 req/s total).

### Farzin's decisions (2026-10-22)

- Q: For fields that are not on the Balance Summary tab, which screen and section is the parity
  source? → A: Principal Remaining and Payments Remaining come from the Balance Summary tab of the
  Transaction Activity screen. Every other returned field comes from the Loan Summary Information
  tab, Current Payment Information section, as of today. Payoff Information is out of scope and
  belongs to the payoff feature.
- Q: What does the API return for an old, rewritten loan ID? → A: (a) The API works per loan
  record. An old, rewritten loan ID returns that loan record's own balance summary with its
  non-active status. Last Payment and the YTD totals are not returned by this API, so the
  per-contract behavior of the Balance Summary tab does not affect it.
- Q: Should test loans with conflicting values on the same screen be excluded from the parity
  test? → A: No. They stay in the parity test, because the API only has to match the chosen screen
  section, and the conflicting fields are not returned. Suspected SPI defects are noted
  privately, not in this repository.
- Confirmed: delinquent loans count as active; balances are as of today only; every request is
  logged without balances; normal load is 10 requests/second per tenant.
- Added: the parity test gets the screen values automatically by calling, read-only, the same SPI
  procedures or queries the screen uses against `AlphaPMS_ResortWise`, and compares them with the
  API response (FR-021). The exact procedures are identified in the plan.

### Farzin's scope reduction (2026-10-23)

- This slice returns only loan ID, loan status, as-of date, and principal balance. Interest, late
  fees, service fees, unapplied amount, Others 1 to 5 and their names, total balance, past-due
  amount, and days past due are removed and will be a separate feature (see
  [Out of Scope](#out-of-scope)). FR-004 and FR-005 are withdrawn; other requirement IDs are
  unchanged.
- The as-of date is the SQL Server's current date for now (see [Assumptions](#assumptions)).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Get a loan's balance summary (Priority: P1)

A ResortWise screen or an external system, acting for one SPI client (tenant), asks for the
balance summary of one loan. It receives the loan ID, loan status, as-of date, and principal
balance. Every value matches what the SPI Application shows for that loan in the parity source
screen section (see FR-003).

**Why this priority**: This is the feature's whole purpose. Without matching values, no consumer
can trust ResortWise over the SPI Application.

**Independent Test**: With a caller allowed the tenant mapped to `AlphaPMS_ResortWise`, request
the balance summary for each active loan and compare every field with the parity source values
for the same loan, read automatically from SPI as described in FR-021.

**Acceptance Scenarios**:

1. **Given** an authorized caller and an active loan in the caller's tenant, **When** the caller
   requests that loan's balance summary, **Then** the response contains every listed field and
   each value equals the parity source value for that loan (FR-003).
2. **Given** a loan with no principal remaining, **When** the caller requests its balance
   summary, **Then** the principal balance is returned as zero, not omitted.
3. **Given** any successful response, **Then** it contains no payoff amount and none of the
   out-of-scope balance fields (see [Out of Scope](#out-of-scope)).

---

### User Story 2 - Access is limited to authorized callers and their tenants (Priority: P1)

Every request names a tenant and carries a caller credential. The API refuses the request before
reading any loan data unless the credential is valid and the caller is allowed that tenant.

**Why this priority**: Loan data is client-confidential. A single unauthorized or cross-tenant
read is a breach, so this is as critical as returning correct values (constitution Principle II).

**Independent Test**: Send requests with a missing credential, an invalid credential, a missing
or blank tenant header, and a tenant the caller is not allowed; confirm each returns the
documented status and error code and that no loan data is read or returned.

**Acceptance Scenarios**:

1. **Given** a request with no credential or an invalid credential, **When** it is sent, **Then**
   the response is HTTP 401 with code `UNAUTHENTICATED`.
2. **Given** a valid credential and a missing, empty, or whitespace-only `X-Tenant-Id`, **When**
   the request is sent, **Then** the response is HTTP 400 with code `TENANT_REQUIRED` and field
   `X-Tenant-Id`.
3. **Given** a valid credential for a caller not allowed tenant T, **When** the caller requests a
   loan in tenant T, **Then** the response is HTTP 403 with code
   `TENANT_FORBIDDEN`, and no data from tenant T is read.
4. **Given** a valid credential and a tenant ID that is not in the tenant registry, **When** the
   request is sent, **Then** the response is HTTP 403 `TENANT_FORBIDDEN`, identical to a tenant
   the caller is not allowed, so it does not reveal which tenants exist.

---

### User Story 3 - Tenants never see each other's loans (Priority: P1)

Two tenants, each mapped to its own database, can hold loans with the same loan ID but different
data. A request for one tenant only ever returns that tenant's data. A loan that exists only in
another tenant looks exactly like a loan that does not exist.

**Why this priority**: Required by constitution Principle II and part of the stated success
criteria.

**Independent Test**: Map tenant A to `AlphaPMS_ResortWise` and tenant B to
`AlphaPMS_ResortWise_B` (where a test-setup script has changed some loans and removed others, by
rule). Request the same loan IDs through both tenants and confirm each tenant returns only its own
database's values, and that loans removed from B return `LOAN_NOT_FOUND` through B while still
returning data through A.

**Acceptance Scenarios**:

1. **Given** a loan ID changed in `AlphaPMS_ResortWise_B` by the test-setup script, **When** it is
   requested through tenant A and through tenant B, **Then** each response matches its own
   tenant's database and the two responses differ.
2. **Given** a loan ID removed from `AlphaPMS_ResortWise_B`, **When** it is requested through
   tenant B, **Then** the response is HTTP 404 `LOAN_NOT_FOUND`, byte-for-byte identical in status,
   code, and message to the response for a loan ID that exists in no tenant.
3. **Given** a caller allowed only tenant A, **When** it requests any loan with
   `X-Tenant-Id` set to tenant B, **Then** the response is HTTP 403 `TENANT_FORBIDDEN` and no data
   from tenant B is returned.

---

### User Story 4 - Clear, stable errors for bad input (Priority: P2)

A consumer that sends an invalid loan ID, or asks for a loan that does not exist, gets a stable
error code it can handle programmatically, plus the field name for input errors.

**Why this priority**: Consumers need dependable error handling, but this follows naturally once
stories 1 to 3 work.

**Independent Test**: Request a malformed loan ID and a well-formed but unknown loan ID; confirm
the documented status, code, and field.

**Acceptance Scenarios**:

1. **Given** an authorized request with a loan ID that is not a valid SPI loan record ID (for
   example text, a negative number, zero, or a number with leading zeros), **When** it is sent,
   **Then** the response is HTTP 400 with code `LOAN_ID_INVALID` and field `loanId`.
2. **Given** an authorized request for a well-formed loan ID that does not exist in the tenant,
   **When** it is sent, **Then** the response is HTTP 404 with code `LOAN_NOT_FOUND`.
3. **Given** any error response, **Then** its message contains no secrets, SQL, stack traces, or
   other tenants' data.

---

### Edge Cases

- **Several problems at once**: checks run in a fixed order and the first failure is returned:
  credential (401), tenant header (400), tenant authorization (403), loan ID format (400), loan
  existence (404).
- **Loan exists but is not active** (any SPI status other than exactly "Active", for example paid
  off or cancelled): the summary is still returned, with the status the SPI Application shows
  and principal balance `0.00` (FR-008a; *pending Farzin's confirmation*).
  Parity success (SC-001) is measured only on loans whose status is "Active".
- **Old loan record left behind by a rewrite**: the API works per loan record, so the old loan ID
  returns that loan record's own balance summary with its non-active status and principal balance
  `0.00` (FR-008a; *pending Farzin's confirmation*), never the new loan's summary on the same
  contract. It is outside the SC-001 parity scope because it is not active.
- **Delinquent loan**: still has the "Active" status in SPI (past due is calculated from
  payments), so it is inside the SC-001 parity scope.
- **Test loan with conflicting values on the same screen** (for example two different monthly
  payments): stays in the SC-001 parity scope. The API only has to match the parity source
  section, and the conflicting fields are not returned. Suspected SPI defects are noted
  privately, not in this repository.
- **Negative principal balance** (for example an overpayment shown by the SPI Application):
  returned as the SPI Application shows it; the API does not clamp or recompute.
- **Tenant database unreachable**: HTTP 503 `TENANT_UNAVAILABLE`; the API never falls back to
  another tenant's database.
- **Unexpected failure**: HTTP 500 `INTERNAL_ERROR` with no internal details.
- **Loan ID that is not a positive whole number** (letters, a sign, a decimal point, leading
  zeros, surrounding whitespace, zero, or a value too large for the SPI loan record ID): HTTP 400
  `LOAN_ID_INVALID` with field `loanId`. No normalization is applied.

## Requirements *(mandatory)*

### Functional Requirements

**Balance summary**

- **FR-001**: The system MUST let an authorized caller request the balance summary of one loan by
  its loan ID, under the `/v1` version, as defined in the API contract. Balances are always stated
  as of the current date (see [Assumptions](#assumptions)); the request takes no date input, and
  the response returns the as-of date used.
- **FR-002**: A successful response MUST contain exactly: loan ID, loan status, as-of date, and
  principal balance.
- **FR-003**: Every returned value MUST equal the value the SPI Application shows for the same
  loan, tenant, and as-of date (today) in its parity source:
  - Principal balance: Principal Remaining on the Balance Summary tab of the Transaction Activity
    screen. (Payments Remaining also comes from this tab but is not a returned field.)
  - Loan ID and loan status: the Loan Summary Information tab, Current Payment Information
    section, as of today.
  - As-of date: the SQL Server's current date (see [Assumptions](#assumptions)).
  - Payoff Information (as of the Valid Through date) is not a parity source; it belongs to the
    payoff feature.

  The API MUST NOT recompute, round differently, or reinterpret SPI values.
- **FR-004**: *Withdrawn (scope reduction, 2026-10-23).* Others 1 to 5 are out of scope.
- **FR-005**: *Withdrawn (scope reduction, 2026-10-23).* Other bucket names are out of scope.
- **FR-006**: The principal balance MUST be an exact decimal value with two decimal places; a zero
  balance MUST be returned as zero, not omitted.
- **FR-007**: The response MUST NOT include a payoff amount or any of the fields listed in
  [Out of Scope](#out-of-scope).
- **FR-008**: The feature is read-only. No request may change any data.
- **FR-008a**: The API works per loan record. A request for a loan ID returns that loan record's
  own summary and status, including an old loan record left behind by a rewrite (non-active
  status). It never substitutes another loan record on the same contract. Last Payment and
  year-to-date totals are not returned. A non-active loan record (any status other than exactly
  "Active", including an old record left behind by a rewrite) returns principal balance `0.00`,
  because the SPI function the API reuses returns one principal per contract, which belongs to
  the contract's active record (see the plan's per-loan-record rule). *Pending Farzin's
  confirmation.*

**Authentication, authorization, and tenancy**

- **FR-009**: Every request MUST carry a caller credential and an `X-Tenant-Id` header. A missing
  or invalid credential MUST return HTTP 401 `UNAUTHENTICATED`.
- **FR-010**: A missing, empty, or whitespace-only `X-Tenant-Id` MUST return HTTP 400
  `TENANT_REQUIRED` with field `X-Tenant-Id`.
- **FR-011**: A valid caller that is not allowed the requested tenant, or that names a tenant not
  in the tenant registry, MUST receive HTTP 403 `TENANT_FORBIDDEN`. The two cases MUST be
  indistinguishable.
- **FR-012**: All credential and tenant checks MUST complete before any loan data is read.
- **FR-013**: Each tenant MUST map to exactly one database, and a request MUST only ever read from
  the database mapped to its tenant.
- **FR-014**: A request for a loan that does not exist in the caller's tenant MUST return HTTP 404
  `LOAN_NOT_FOUND`. The response MUST be identical whether the loan exists in another tenant or in
  no tenant at all.

**Errors and contract**

- **FR-015**: Every error response MUST include a stable error code and a message; input errors
  MUST also include the offending field name. The codes for this endpoint are `TENANT_REQUIRED`,
  `UNAUTHENTICATED`, `TENANT_FORBIDDEN`, `LOAN_ID_INVALID`, `LOAN_NOT_FOUND`,
  `TENANT_UNAVAILABLE`, and `INTERNAL_ERROR`.
- **FR-016**: The loan ID is the SPI loan record's numeric ID. A loan ID that is not a positive
  whole number, written in plain digits with no leading zeros and within the range of the SPI loan
  record ID, MUST return HTTP 400 `LOAN_ID_INVALID` with field `loanId`.
- **FR-017**: Error messages MUST NOT contain secrets, SQL, stack traces, or any tenant's data.
- **FR-018**: The endpoint, its headers, credential, response schema, and every error code MUST be
  documented in the linked OpenAPI contract, which is the authoritative description.

**Auditing**

- **FR-019**: Every request, including refused ones (400, 401, 403, 404, 503, 500), MUST produce
  exactly one structured audit log entry with: time, caller ID (when the credential identifies
  one), tenant ID as sent, loan ID as sent, HTTP status, error code (if any), and a correlation ID.
- **FR-020**: Audit entries MUST NOT contain balance values, credentials, connection details, or
  any other loan data. They are written to standard output; retention is set by the hosting
  environment, not by the service.

**Parity testing**

- **FR-021**: The parity test MUST get the expected screen values automatically, by calling the
  same SPI stored procedures or queries the SPI Application screen sections in FR-003 use, against
  `AlphaPMS_ResortWise`, and MUST compare them field by field with the API response. These calls
  MUST be read-only: they MUST NOT change data or create, alter, or drop SPI objects. The test is
  test code, not application code, and runs locally only (constitution Principle VII). The exact
  procedures or queries are identified in the plan.

### Key Entities

- **Tenant**: One SPI client. Identified by a tenant ID; mapped by the tenant registry to exactly
  one database. Callers are allowed a set of tenants.
- **Caller**: A system or ResortWise screen presenting a credential. Has a set of tenants it is
  allowed to use.
- **Loan**: A loan record in one tenant's database, identified within that tenant by its loan ID
  (the SPI loan record's numeric ID), with a status. The same loan ID may exist in more than one
  tenant with unrelated data. When a loan is rewritten, SPI creates a new loan record on the same
  contract and the old record stays with a non-active status.
- **Loan Balance Summary**: The read-only values the SPI Application shows for one loan record as
  of today, in the parity source sections of FR-003: loan ID, loan status, as-of date, and
  principal balance.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For 100% of active loans (SPI status exactly "Active", including delinquent loans)
  in the `AlphaPMS_ResortWise` test database, including loans with conflicting values on the same
  screen, every returned field matches its FR-003 parity source for that loan, as read
  automatically by the parity test (FR-021). Zero mismatches are allowed.
- **SC-002**: For every loan the test-setup script changes or removes in `AlphaPMS_ResortWise_B`,
  the tenant mapped to `AlphaPMS_ResortWise_B` returns a different result (different values, or
  `LOAN_NOT_FOUND`) than the tenant mapped to `AlphaPMS_ResortWise`.
- **SC-003**: In the isolation test suite, 0 requests return, count, or reveal data from a tenant
  other than the one the caller is allowed and named.
- **SC-004**: 100% of error scenarios (missing tenant, missing credential, invalid credential,
  forbidden tenant, unknown tenant, invalid loan ID, unknown loan, other-tenant loan) return the
  documented status and error code.
- **SC-005**: The not-found response for another tenant's loan is identical to the not-found
  response for a loan that exists in no tenant in 100% of tested cases.
- **SC-006**: A consumer receives a loan's balance summary in under 1 second for at least 95% of
  requests under normal load, defined as a sustained 10 requests/second per tenant against 2
  tenants at the same time (20 requests/second total).
- **SC-007**: In the test suite, 100% of requests (successful and refused) produce exactly one
  audit entry with the FR-019 fields, and 0 audit entries contain a balance value or credential.

## Open Questions for Clarify

Two questions are **open, waiting on Farzin** (raised 2026-10-23 in the plan):

- **O1. Principal parity reference**: *Status*: Open, waiting on Farzin. The Balance Summary tab
  shows the Excel engine's Principal Remaining, while the API reads the SQL function. Option (a)
  runs the engine read-only in local parity tests; option (b) keeps the SQL function as the
  reference with a documented deviation from Principle IV. See
  [plan.md, Open points for Farzin](plan.md#open-points-for-farzin). Option (a) would also need
  FR-021 updated.
- **O2. Non-active principal**: *Status*: Open, waiting on Farzin. A non-active loan record
  returns principal `0.00` (FR-008a), informed by a private per-status screen check. See
  [plan.md, Open points for Farzin](plan.md#open-points-for-farzin).

The questions below were answered by Farzin on 2026-10-22 and are closed. See
[Farzin's decisions](#farzins-decisions-2026-10-22).

- **Balance Summary vs Loan Summary screen discrepancies**: *Status*: Closed.
  - *Comparison done*: 5 test loans were compared in the SPI Application on 2026-10-22. The
    loan-level details were sent privately to Farzin. This repository is public (Principle V), so
    no loan IDs, owner names, or amounts are recorded here.
  - *Where each field appears*: The Balance Summary tab on the Transaction Activity screen shows
    only Principal YTD, Interest YTD, Principal Remaining, Payments Remaining, Last Payment, and
    Next Payment. Interest due, late fees, service fees, unapplied, Others 1 to 5, total balance,
    past-due amount, and days past due appear only on the Loan Summary Information tab and the
    Loan Setup screen, some of them twice: as of today (Current Payment Information) and as of the
    Valid Through date (Payoff Information).
  - *Results*: Principal Remaining and Payments Remaining matched on all 5 loans. The only
    difference found (Last Payment and YTD totals on a rewritten loan) is in fields this API does
    not return.
  - *Decision*: Principal Remaining and Payments Remaining come from the Balance Summary tab;
    every other returned field comes from the Loan Summary Information tab, Current Payment
    Information section, as of today. Payoff Information is out of scope (payoff feature). See
    FR-003.
  - *Where values are recorded*: If a future comparison finds a discrepancy, loan IDs and screen
    values are recorded only in a non-public tracker. It is listed here only with an opaque
    tracker reference, the affected field, and the escalation status, never with a loan ID or
    value. The feature is not done while any such entry is unresolved. No entries are open.
- **Rewritten loans**: *Status*: Closed. Decision (a): the API works per loan record, and an old,
  rewritten loan ID returns that loan record's own balance summary with its non-active status.
  The per-contract behavior of the Balance Summary tab (Last Payment and YTD totals including the
  old loan's payments) does not affect this API, because those fields are not returned. See
  FR-008a.
- **Test loans with conflicting values**: *Status*: Closed. Such loans stay in the parity test,
  because the API only has to match the chosen screen section and the conflicting fields are not
  returned. Suspected SPI defects are noted privately, not in this repository.

## Out of Scope

- **Other balance fields**: interest, late fees, service fees, unapplied amount, Others 1 to 5 and
  their client-configured names, total balance, past-due amount, and days past due. The SPI Excel
  loan engine calculates these values; they will be a separate feature later.
- **Payoff amount**: a separate feature.
- **Balances as of a past or future date**: the request takes no date input.

## Assumptions

- The as-of date is the SQL Server's current date for now. A hosted server may run in a different
  time zone than the resort, so near midnight the as-of date can differ from the resort's local
  date. Requesting balances as of a past or future date is out of scope for this feature.
- The caller credential mechanism (issuer, format, and how a caller's allowed tenants are
  recorded) is chosen in the plan; the contract shows it as a bearer credential until then.
- The loan ID is the SPI loan record's numeric ID (a positive whole number), not free text. Its
  exact maximum value follows the SPI loan record ID's data type, confirmed in the plan.
- Loan status is returned as the text the SPI Application shows; this feature does not define a
  fixed list of statuses.
- "Active loan" means a loan whose SPI status is exactly "Active" (LoanStatusId = 1). Delinquent
  loans keep the Active status in SPI, because past due is calculated from payments and is not
  stored as a status, so delinquent loans are inside the SC-001 parity scope. Loans in non-active
  statuses (for example paid off, cancelled, or old loan records left behind when a loan is
  rewritten) are outside the SC-001 parity scope, but are still returned by the API with their
  own summary and status (FR-008a).
- Loans are in a single currency per tenant; no currency conversion is needed.
- The SPI Application is the system of record for every returned value, through the parity
  source sections in FR-003.
- Data is read from SPI only through `rw` views created by versioned scripts in this repository;
  SPI objects are never changed (constitution Principle III).
- Tests that use `AlphaPMS_ResortWise` or `AlphaPMS_ResortWise_B` run locally only, and the
  test-setup script that changes `AlphaPMS_ResortWise_B` follows the constitution's test data
  rules.
