# Feature Specification: Loan Balance Summary API

**Feature Branch**: `001-loan-balance-summary`

**Created**: 2026-10-22

**Status**: Done

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
  non-active status (principal not available, see O2). Last Payment and the YTD totals are not returned by this API, so the
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
2. **Given** an active loan with no principal remaining whose principal is available (its payoff
   flag is not set, FR-008a), **When** the caller requests its balance summary, **Then** the
   principal balance is returned as zero, not omitted or `null`.
3. **Given** any successful response, **Then** it contains no payoff amount and none of the
   out-of-scope balance fields (see [Out of Scope](#out-of-scope)).
4. **Given** a non-active loan record in the caller's tenant (for example an old record left
   behind by a rewrite), **When** the caller requests its balance summary, **Then** the response
   is HTTP 200 with the record's own status and principal not available as decided in
   [O2](#open-point-decisions) (FR-008a).
5. **Given** an active loan record whose payoff flag is set, **When** the caller requests its
   balance summary, **Then** the response is HTTP 200 with status `Active` and principal not
   available as decided in [O5](#open-point-decisions) (FR-008a).

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
  and principal not available as decided in O2 (FR-008a).
  Parity success (SC-001) is measured only on loans whose status is "Active".
- **Old loan record left behind by a rewrite**: the API works per loan record, so the old loan ID
  returns that loan record's own non-active status with principal not available as decided in
  O2 (FR-008a). It never returns the new loan's principal on the same contract. It is outside
  the SC-001 parity scope because it is not active.
- **Active loan with its payoff flag set** (`IsPayOff = 1`): principal not available as decided
  in O5, whatever its transactions say (FR-008a).
- **Several reasons at once**: one reason is returned, in the order of FR-008a: `LOAN_NOT_ACTIVE`,
  then `PAID_OFF_FLAG_SET`. A non-active record with the payoff flag set returns
  `LOAN_NOT_ACTIVE`.
- **Active loan whose principal cannot be read** (the view returns no principal for an active
  record whose payoff flag is not set): HTTP 500 `INTERNAL_ERROR`. `null` principal is never
  returned for an active record except under O5.
- **Loan whose view row carries a principal although it is not available** (the view returns a
  principal for a non-active record or for an active record with the payoff flag set): HTTP 500
  `INTERNAL_ERROR`. A principal is never returned when it is not available, so the view and the
  API cannot disagree silently.
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
- **FR-002**: A successful response MUST contain exactly: loan ID, loan status, as-of date,
  principal balance, and principal balance available. It MUST also contain principal unavailable
  reason when, and only when, principal balance available is false (FR-008a).
- **FR-003**: Every returned value MUST equal the value the SPI Application shows for the same
  loan, tenant, and as-of date (today) in its parity source:
  - Principal balance: Principal Remaining on the Balance Summary tab of the Transaction Activity
    screen. (Payments Remaining also comes from this tab but is not a returned field.)
  - Loan ID and loan status: the Loan Summary Information tab, Current Payment Information
    section, as of today.
  - As-of date: the SQL Server's current date (see [Assumptions](#assumptions)).
  - Principal balance available and principal unavailable reason: no screen source. Both are
    derived from the loan status and the SPI payoff flag check (O5), by the rules in FR-008a.
    The check detects a condition in SPI tables and calculates no amount.
  - Payoff Information (as of the Valid Through date) is not a parity source; it belongs to the
    payoff feature.

  The API MUST NOT recompute, round differently, or reinterpret SPI values.
- **FR-004**: *Withdrawn (scope reduction, 2026-10-23).* Others 1 to 5 are out of scope.
- **FR-005**: *Withdrawn (scope reduction, 2026-10-23).* Other bucket names are out of scope.
- **FR-006**: When available, the principal balance MUST be an exact decimal value with two
  decimal places; a zero balance MUST be returned as zero, not omitted or `null`. When not
  available (FR-008a), it MUST be `null`, never `0` or `0.00`.
- **FR-007**: The response MUST NOT include a payoff amount or any of the fields listed in
  [Out of Scope](#out-of-scope).
- **FR-008**: The feature is read-only. No request may change any data.
- **FR-008a**: The API works per loan record. A request for a loan ID returns that loan record's
  own summary and status, including an old loan record left behind by a rewrite (non-active
  status). It never substitutes another loan record on the same contract. Last Payment and
  year-to-date totals are not returned. Principal availability is decided by the first rule that
  applies, in this order:
  - **Not active** (any status other than exactly "Active", including an old record left behind
    by a rewrite): as decided in [O2](#open-point-decisions).
  - **Active, payoff flag set** (`lrLoanReceivable.IsPayOff = 1` on the record): as decided in
    [O5](#open-point-decisions).
  - **Active, payoff flag not set**: principal balance is the record's value (FR-003),
    principal balance available is `true`, and principal unavailable reason is absent.

  `null` principal occurs only in the first two cases.

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
  principal balance, plus whether the principal is available and, if not, why (FR-008a).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For 100% of active loans (SPI status exactly "Active", including delinquent loans)
  in the `AlphaPMS_ResortWise` test database, including loans with conflicting values on the same
  screen, every returned field matches its FR-003 parity source for that loan, as read
  automatically by the parity test (FR-021). Zero mismatches are allowed. The automated test
  checks loan status, as-of date, and principal balance. The principal reference is a read-only
  call to the SPI function the `rw` view also uses, so it catches rounding, mapping, and null
  errors but does not prove the value the screen shows, which the SPI Excel loan engine
  calculates. That gap is covered by the manual screen check of about 20 active loans (O1 and
  O4, documented deviations from Principle IV in the plan's Complexity Tracking). Principal is
  compared only for active loans whose principal is available. For every active loan whose payoff
  flag is set, the test instead checks the O5 response (a documented deviation from
  Principle IV).
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

## Open point decisions

The plan raised five open points (see
[plan.md, Open points for Farzin](plan.md#open-points-for-farzin)). Each decision is stated once
here; other sections point to it.

- **O1. Principal parity reference**: The Balance Summary tab shows the Principal Remaining that
  the SPI Excel loan engine calculates. ResortWise never changes or runs SPI Application code,
  and the constitution allows tests to call only SPI procedures and functions, so the parity
  reference for principal is the SPI SQL function path (FR-021). This is a documented deviation
  from Principle IV in the plan's Complexity Tracking. O4 sets the function the test calls.
  Approved by Farzin on 2026-10-27.
- **O2. Non-active principal**: A non-active loan record (any status other than exactly
  "Active") returns its status with `principalBalance` `null` (never `0.00`),
  `principalBalanceAvailable` `false`, and `principalUnavailableReason` `LOAN_NOT_ACTIVE`. The
  screen shows a principal for such a record that only the SPI Excel loan engine calculates, and
  the SPI function returns one principal per contract, so the API returns no value in its
  place. This covers every non-active status, including paid-off and cancelled records, where
  the screen likely shows `0.00`; the engine feature must cover them (see
  [Handed to the engine feature](#handed-to-the-engine-feature)). Approved by Farzin on
  2026-10-27.
- **O3. Test-only database users and grants**: *Status*: Closed, allowed. The test setup may
  create the test database users and grant the test-only reference login `EXECUTE` on the
  read-only SPI procedures in `AlphaPMS_ResortWise` and `AlphaPMS_ResortWise_B`. They add
  permissions only and change no data or object definitions, which fits "exactly as restored"
  and the rule that tests may call SPI procedures read-only.
- **O4. Automated principal comparison**: The parity test compares `principalBalance` for every
  active loan whose principal is available with a read-only call to `dbo.lrFnGetBalanceSummary`
  through the reference login (`rw_parity_ref`). It rounds the function's principal to 2
  decimal places, half away from zero, as the screen does, and checks the field mapping and that
  neither value is `null`. The screen's procedure `lrGetTransactionActivity` is not called,
  because it reaches the database `AlphaSales`, which the reference login cannot access. The
  test does **not** prove the value the screen shows, because the view uses the same function;
  that gap is covered by a manual screen check of about 20 active loans, including partly paid
  and delinquent ones, picked by rule, with results kept private. This is a documented deviation
  from Principle IV in the plan's Complexity Tracking. Revised after Farzin's 2026-10-27 review; his approval comes with the PR.
- **O5. Active loans with the payoff flag set**: An active loan with `IsPayOff = 1` returns
  `principalBalance` `null`, `principalBalanceAvailable` `false`, and
  `principalUnavailableReason` `PAID_OFF_FLAG_SET`. Approved by Farzin on 2026-10-28.

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
  rewritten loan ID returns that loan record's own balance summary with its non-active status
  (principal not available, see O2). The per-contract behavior of the Balance Summary tab (Last Payment and YTD totals including the
  old loan's payments) does not affect this API, because those fields are not returned. See
  FR-008a.
- **Test loans with conflicting values**: *Status*: Closed. Such loans stay in the parity test,
  because the API only has to match the chosen screen section and the conflicting fields are not
  returned. Suspected SPI defects are noted privately, not in this repository.

## Out of Scope

- **Other balance fields**: interest, late fees, service fees, unapplied amount, Others 1 to 5 and
  their client-configured names, total balance, past-due amount, and days past due. The SPI Excel
  loan engine calculates these values; they will be a separate feature later.
- **Principal balance of non-active loan records**: the screen value comes from the SPI Excel
  loan engine; until the engine feature covers it, the principal is not available (O2).
- **Principal balance of active loan records with the payoff flag set**: the screen value comes
  from the SPI Excel loan engine, and the SQL function cannot reproduce it; until the engine
  feature covers it, the principal is not available (O5).
- **Payoff amount**: a separate feature.
- **Balances as of a past or future date**: the request takes no date input.

## Handed to the engine feature

The engine feature (the later feature that covers values the SPI Excel loan engine calculates)
takes over the items below, and its spec must carry each one.

- **O5, `PAID_OFF_FLAG_SET`**: the payoff flag rule is based on a few loans, not on SPI's own
  logic. It may flag loans whose screen value the SQL function matches, and it may miss other
  cases where the screen differs. The engine feature must replace it with the engine's own
  logic.
- **O2, non-active records**: every non-active status, including paid-off and cancelled records,
  where the screen likely shows `0.00`. This is not verified, because those statuses are not in
  use in `AlphaPMS_ResortWise`. The engine feature must cover them.

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
  own status and with principal not available (FR-008a). Active loans with the payoff flag set stay
  in the SC-001 scope for status and as-of date, and are checked for principal not available
  with the right reason instead of a principal value.
- Loans are in a single currency per tenant; no currency conversion is needed.
- The SPI Application is the system of record for every returned value, through the parity
  source sections in FR-003.
- Data is read from SPI only through `rw` views created by versioned scripts in this repository;
  SPI objects are never changed (constitution Principle III).
- Tests that use `AlphaPMS_ResortWise` or `AlphaPMS_ResortWise_B` run locally only, and the
  test-setup script that changes `AlphaPMS_ResortWise_B` follows the constitution's test data
  rules.
