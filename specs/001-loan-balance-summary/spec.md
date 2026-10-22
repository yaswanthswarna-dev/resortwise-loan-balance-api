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
  SPI Application's current business date? → A: Current business date only. The response returns
  the as-of date used, and there is no date input.
- Q: Should the API keep an audit record of every balance-summary request, and if so, how long?
  → A: A structured audit log entry for every request, including refused ones: time, caller ID,
  tenant ID, loan ID, status/error code, and a correlation ID. No balance values. Written to
  stdout; retention is set by the hosting environment.
- Q: What request rate per tenant counts as "normal load" for the target that 95% of requests
  finish in under 1 second (SC-006)? → A: 10 requests/second per tenant, 2 tenants at the same
  time (20 req/s total).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Get a loan's balance summary (Priority: P1)

A ResortWise screen or an external system, acting for one SPI client (tenant), asks for the
balance summary of one loan. It receives the loan ID, loan status, as-of date, principal balance,
interest, late fees, service fees, unapplied amount, the five "Other" buckets (each with its
client-configured name), total balance, past-due amount, and days past due. Every value matches
what the SPI Application's Balance Summary screen shows for that loan.

**Why this priority**: This is the feature's whole purpose. Without matching values, no consumer
can trust ResortWise over the SPI Application.

**Independent Test**: With a caller allowed the tenant mapped to `AlphaPMS_ResortWise`, request
the balance summary for each active loan and compare every field with the Balance Summary screen
values for the same loan.

**Acceptance Scenarios**:

1. **Given** an authorized caller and an active loan in the caller's tenant, **When** the caller
   requests that loan's balance summary, **Then** the response contains every listed field and
   each value equals the Balance Summary screen value for that loan.
2. **Given** a client that has named Other bucket 1 (for example "Maintenance Fees") and left
   buckets 2 to 5 unnamed, **When** the caller requests a loan's balance summary, **Then** bucket 1
   is returned with the client's name and buckets 2 to 5 are returned as "Other 2" to "Other 5",
   each marked as a default name.
3. **Given** a loan with no amount in some buckets, **When** the caller requests its balance
   summary, **Then** those amounts are returned as zero, not omitted, and all five Other buckets
   are always present in order 1 to 5.
4. **Given** any successful response, **Then** it contains no payoff amount.

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
  off or cancelled): the summary is still returned, with the status the SPI Application shows.
  Parity success (SC-001) is measured only on loans whose status is "Active".
- **Old loan row left behind by a rewrite**: also outside the SC-001 parity scope. What the API
  returns for its loan ID is undecided; see the rewritten-loan open question.
- **Delinquent loan**: still has the "Active" status in SPI (past due is calculated from
  payments), so it is inside the SC-001 parity scope, and its past-due amount and days past due
  must match the Balance Summary screen.
- **Client named a bucket with only spaces**: treated as unnamed; the default "Other N" is
  returned.
- **Client named a bucket "Other 3" for bucket 3**: returned as the client's name, marked as not a
  default name.
- **Negative amounts** (for example an overpayment or credit shown by the SPI Application): returned
  as the SPI Application shows them; the API does not clamp or recompute.
- **Loan not past due**: past-due amount is zero and days past due is 0.
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
  as of the SPI Application's current business date; the request takes no date input, and the
  response returns the as-of date used.
- **FR-002**: A successful response MUST contain: loan ID, loan status, as-of date, principal
  balance, interest, late fees, service fees, unapplied amount, Others 1 to 5, total balance,
  past-due amount, and days past due.
- **FR-003**: Every returned value MUST equal the value the SPI Application's Balance Summary
  screen shows for the same loan, tenant, and as-of date. The API MUST NOT recompute, round
  differently, or reinterpret SPI values.
- **FR-004**: Others 1 to 5 MUST always be returned as five entries in bucket order, each with a
  bucket number, a name, an indicator of whether the name is the default, and an amount.
- **FR-005**: Each Other bucket's name MUST be the name the client configured for that bucket. Only
  when the client has not named the bucket (no name, or a blank name) MUST the name fall back to
  "Other N", where N is the bucket number.
- **FR-006**: Amounts MUST be exact decimal values with two decimal places; zero amounts MUST be
  returned as zero, not omitted.
- **FR-007**: The response MUST NOT include a payoff amount.
- **FR-008**: The feature is read-only. No request may change any data.

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

### Key Entities

- **Tenant**: One SPI client. Identified by a tenant ID; mapped by the tenant registry to exactly
  one database. Callers are allowed a set of tenants.
- **Caller**: A system or ResortWise screen presenting a credential. Has a set of tenants it is
  allowed to use.
- **Loan**: A loan record in one tenant's database, identified within that tenant by its loan ID
  (the SPI loan record's numeric ID), with a status. The same loan ID may exist in more than one
  tenant with unrelated data. When a loan is rewritten, SPI creates a new loan record on the same
  contract and the old record stays with a non-active status.
- **Loan Balance Summary**: The read-only set of balance values the Balance Summary screen shows
  for one loan as of a date: principal, interest, late fees, service fees, unapplied amount,
  Others 1 to 5, total balance, past-due amount, and days past due.
- **Other Bucket**: One of five client-configurable balance categories. Has a bucket number (1 to
  5), an optional client-configured name, and an amount per loan.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For 100% of active loans (SPI status exactly "Active", including delinquent loans)
  in the `AlphaPMS_ResortWise` test database, every returned field matches the SPI Application's Balance Summary screen for that loan. Zero mismatches are
  allowed.
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

- **Balance Summary vs Loan Summary screen discrepancies**: Any loan where the screens disagree
  must be recorded and escalated (constitution Principle IV). No value may be chosen silently.
  - *Comparison done*: 6 test loans were compared in the SPI Application on 2026-10-22. The
    loan-level details were sent privately to Farzin. This repository is public (Principle V), so
    no loan IDs, owner names, or amounts are recorded here.
  - *Where each field appears*: The Balance Summary tab on the Transaction Activity screen shows
    only Principal YTD, Interest YTD, Principal Remaining, Payments Remaining, Last Payment, and
    Next Payment. Interest due, late fees, service fees, unapplied, Others 1 to 5, total balance,
    past-due amount, and days past due appear only on the Loan Summary Information tab and the
    Loan Setup screen. Some of those appear twice, as of two dates: today (Current Payment
    Information) and the Valid Through date (Payoff Information).
  - *Results*: Principal Remaining and Payments Remaining matched on all 6 loans. One difference
    was found on a rewritten loan (Last Payment and YTD totals); it is recorded under the
    rewritten-loan question below.
  - *Open question for Farzin*: For fields that are not on the Balance Summary tab, which screen
    and section is the parity source (Loan Summary Information tab or Loan Setup screen; Current
    Payment Information as of today or Payoff Information as of the Valid Through date)?
  - *Status*: Open, awaiting Farzin's decision. The plan and tests must not assume a source.
  - *Where values are recorded*: Loan IDs and screen values are recorded only in a non-public
    tracker (for example an internal ticket or document). Each discrepancy is listed here as one
    entry with an opaque tracker reference, the affected field, and the escalation status, and
    never with a loan ID or value. The feature is not done while any entry here is unresolved.
- **Rewritten loans (for Farzin)**: In SPI, rewriting a loan creates a new loan row on the same
  contract, and the old row stays with a non-active status. When a caller asks for the old,
  rewritten loan's ID, what should the API return?
  - (a) The old loan row's own balance summary, with its non-active status.
  - (b) The current (new) loan's balance summary on the same contract.
  - (c) An error.
  - *Screen comparison finding (2026-10-22)*: On a rewritten loan, Last Payment on the Balance
    Summary tab shows a payment made on the old loan, but Last Payment is blank on the Loan
    Summary tab. The Balance Summary tab's YTD totals also include the old loan's payments. So the
    Balance Summary tab works per contract for rewritten loans, while the Loan Summary tab works
    per loan row. Farzin's answer should also say which of these the API follows for the new
    loan's Last Payment and YTD fields.
  - *Status*: Open, awaiting Farzin's decision. No answer has been chosen; the plan and tests
    must not assume one.
- **Test loans with conflicting values (for Farzin)**: Some test loans show values that conflict
  on the same screen, for example two different monthly payments, or a principal balance with a
  zero payoff amount. These are likely left over from earlier rewrite bug testing. Should the
  parity test exclude such loans, or report them as SPI defects?
  - *Status*: Open, awaiting Farzin's decision. The parity test must not silently skip or accept
    these loans until this is answered.

## Assumptions

- The as-of date is the current business date the SPI Application uses for the Balance Summary
  screen. Requesting balances as of a past or future date is out of scope for this feature.
- The caller credential mechanism (issuer, format, and how a caller's allowed tenants are
  recorded) is chosen in the plan; the contract shows it as a bearer credential until then.
- The loan ID is the SPI loan record's numeric ID (a positive whole number), not free text. Its
  exact maximum value follows the SPI loan record ID's data type, confirmed in the plan.
- Loan status is returned as the text the SPI Application shows; this feature does not define a
  fixed list of statuses.
- "Active loan" means a loan whose SPI status is exactly "Active" (LoanStatusId = 1). Delinquent
  loans keep the Active status in SPI, because past due is calculated from payments and is not
  stored as a status, so delinquent loans are inside the SC-001 parity scope. Loans in non-active
  statuses (for example paid off, cancelled, or old loan rows left behind when a loan is
  rewritten) are outside the SC-001 parity scope. Paid-off and cancelled loans are still returned
  by the API; what is returned for an old rewritten loan row is an open question.
- Loans are in a single currency per tenant; no currency conversion is needed.
- Other bucket names are configured per tenant (client), not per loan.
- The SPI Application's Balance Summary screen is the system of record for every returned value.
- Data is read from SPI only through `rw` views created by versioned scripts in this repository;
  SPI objects are never changed (constitution Principle III).
- Tests that use `AlphaPMS_ResortWise` or `AlphaPMS_ResortWise_B` run locally only, and the
  test-setup script that changes `AlphaPMS_ResortWise_B` follows the constitution's test data
  rules.
- Payoff amount is out of scope and will be a separate feature.
