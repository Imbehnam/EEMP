# 03 — Architecture, Domain, and Database Review

**Status:** PROPOSED review and recommendations. Nothing here is approved. Recommendations (R) become binding only after approval and a Decision Log entry.

## 1. Architecture review

### Sound as proposed
Modular monolith with Clean Architecture; API-first; adapters for external providers; outbox/inbox for external side effects; DTOs rather than entities; UTC instants with IANA zones.

### Findings

| ID | Sev | Finding | Recommendation |
|---|---|---|---|
| D-01 | S1 | **Module boundaries are not enforceable as written.** A shared database plus "cross-module writes must be explicit" lets any module join any table. | R: one schema and one `DbContext` per module; cross-module references by ID only (no cross-schema FKs except to Tenancy); architecture tests (e.g., NetArchTest) that fail on forbidden project references; cross-module reads via query contracts. |
| D-02 | S1 | **Payment → enrollment consistency** (CF-02). | R: Finance commits settlement + outbox event in one transaction; Enrollment consumes the event idempotently (inbox) and activates. User-visible state is "payment received, activation pending" until processed. Rewrite journey step 6 accordingly. |
| D-03 | S1 | **Identity provider undecided (OD-023)** but baseline #5 (revocation on next request) constrains token design. | R: access tokens carry identity and session ID only, never permissions; permissions resolved server-side per request from the database with a permission-version check; short token lifetime; sessions revocable server-side. Choice of ASP.NET Identity vs external OIDC is then an implementation detail of authentication, not authorization. |
| D-04 | S2 | **Browser auth pattern unspecified.** | R: Next.js as backend-for-frontend with HttpOnly, Secure, SameSite cookies, CSRF protection on state-changing calls; no tokens in `localStorage`. |
| D-05 | S1 | **Double-booking on SQL Server.** SQL Server has no range-exclusion constraint, so "no overlapping bookings" cannot be a declarative constraint. | R: materialize fixed-granularity slot rows with a unique index `(TenantId, ResourceId, SlotStart)`; booking inserts slot rows in one transaction; unique-violation = conflict. For non-grid durations use `sp_getapplock` scoped to tenant+resource. Same pattern for teacher-assignment and timetable overlap. |
| D-06 | S2 | **Background worker** (CF-10): jobs, outbox dispatch, reminders and webhook retries all need it from Phase 4. | R: treat the worker as a required host from the first outbox use (can share the API codebase, separate process). Every job payload carries `TenantId`, re-resolves tenant context, and revalidates state. |
| D-07 | S2 | **Hangfire-style schedulers store jobs in a shared table**, so tenant isolation and fairness (PERFORMANCE doc) are not native. | R: evaluate in the selection gate; require per-tenant queues/concurrency limits; never put PII in job arguments (IDs only). |
| D-08 | S2 | **Realtime (SignalR)** hubs can leak across tenants. | R: defer; if adopted, groups keyed by tenant+scope and authorization on join and on every push. |
| D-09 | S2 | **Module list drift** (CF-05). | R: adopt one canonical list in a single document; add owners for Referral, Files/Attachments, Provider Configuration, Cards & Documents, Attendance. See §3. |
| D-10 | S3 | 19 modules on day one is heavy. | R: keep boundaries, but start as a small number of deployable assemblies per layer, with modules as namespaces/projects created when the owning phase begins. |
| D-11 | S2 | **Database engine** is "proposed" with RLS as an unvalidated defense in depth (licensing and hosting unresolved). PostgreSQL has mature RLS and range-exclusion constraints, which would simplify D-05. | Decision required (OD-024 related). Not changed here; both options remain open. |
| D-12 | S2 | **API surface gaps** versus requirements: no group for auth/me, invitations, workflow definitions, referrals, consents, support access, provider webhooks, files, imports, settings, fee catalog, reports. | R: extend the group list; tenant is derived from trusted context, never from a path or body field. |
| D-13 | S2 | **Idempotency-key storage** undefined. | R: store `(TenantId, Principal, Route, Key, RequestHash, Response, ExpiresAt)`; reject same key with different hash. |
| D-14 | S2 | **Webhook tenant resolution** undefined. | R: webhook URL carries an opaque, unguessable provider-endpoint ID mapped server-side to a tenant/provider config; signature is verified with that config's secret before any state change; replay window + inbox dedup on provider event ID. |

## 2. Domain model validation

Checked `DOMAIN_MODEL.md` against requirements and security baseline.

| ID | Sev | Missing or ambiguous | Needed for |
|---|---|---|---|
| M-01 | S1 | **Support/supervision grant** entity (scope, purpose, approver, start, expiry ≤90 days, renewal, revocation, use log). | Baseline #3, #6, #8 |
| M-02 | S1 | **Permission policy and scope types** (`ScopeAssignment` exists by name only). Need scope kinds: Tenant, Institute, Branch, Resource-assignment, Relationship. | IAM-003, IAM-008 |
| M-03 | S2 | **LegalEntity / invoice issuer.** | FIN-002, ORG-N01 |
| M-04 | S2 | **Applicant / Application** entity. | ADM-N01 |
| M-05 | S2 | **Tenant lifecycle state machine** and **TermsAcceptance**. | ORG-001, SAAS-N02 |
| M-06 | S2 | **Attendance owner** and **TeacherAssignment owner** are undefined between modules. | Module boundaries |
| M-07 | S2 | **FileObject / Attachment** (classification, scan state, owner aggregate, retention). | WFL-003, DOC-N01, LRN |
| M-08 | S2 | **Ledger model:** no decision on double-entry, accounts, or posting rules. `LedgerEntry` alone is ambiguous. | FIN-007 |
| M-09 | S2 | **Invoice state machine incomplete:** "Overdue" is time-derived and should not be a persisted state; Voided vs Credited after partial payment undefined; no overpayment/credit-balance state. | FIN-002, FIN-006 |
| M-10 | S2 | **Payment lifecycle** omits authorized/captured (if gateways support it) and refund/dispute sub-states; says refund is separate but gives no states. | FIN-006 |
| M-11 | S2 | **Referral** has no owning module and no distinction between intra-tenant (sibling institute) and inter-tenant flows. | XFR |
| M-12 | S3 | **AuditEvent, ConsentRecord, RetentionPolicy, LegalHold, ExportRequest** are listed in module docs but not in the domain model. | OPS-002 |
| M-13 | S3 | Guardian authority attributes undefined (IAM-N04). | IAM-005 |

Invariants worth adding to the domain model: *a role-assigner cannot self-grant sensitive permissions (T-02)*; *a support grant never exceeds 90 days*; *an invoice's issuing legal entity is immutable after issue*; *a ledger posting set balances to zero per currency*.

## 3. Proposed canonical module ownership (for approval)

| Module | Adds / clarifies |
|---|---|
| Platform & Tenancy | Tenant lifecycle, TermsAcceptance, plan association |
| Identity & Access | Accounts, sessions, roles, permissions, scope assignments, **support/supervision grants** |
| Organization | Central office, institute, branch, **LegalEntity** |
| People | Person, guardian relationships and authority flags, consent records |
| Academic Catalog | Years, terms, subjects, courses, class definitions, **TeacherAssignment** |
| Enrollment | Applications/applicants, enrollment, class membership, **attendance** (proposed) |
| Assessment / Report Cards | As proposed |
| Workflow | As proposed |
| Consultation / Planning | As proposed |
| Finance | As proposed + ledger, maker-checker |
| Scheduling | Calendar events, recurrence, meeting refs |
| Notification | Templates, preferences, schedules, deliveries, consent/opt-out register |
| Files & Documents (new) | FileObject, scan state, generated documents, cards |
| Referral (new) | Referral lifecycle, scopes, transfer packages, grants |
| Integrations (new) | Provider configuration, secrets, webhook inbox/endpoints |
| SaaS Billing | As proposed |
| Reporting | Read models, suppression rules |
| Audit & Privacy | Audit, consent evidence, retention, legal hold, exports |

## 4. Database review

| ID | Sev | Finding | Recommendation |
|---|---|---|---|
| DB-01 | S1 | **Cross-tenant references are not prevented by FKs on `Id` alone.** A row in tenant A could point to a parent in tenant B. | R: every tenant-owned parent has unique `(TenantId, Id)`; child FKs are composite `(TenantId, ParentId)`. Gives structural isolation independent of query filters. |
| DB-02 | S2 | **Identifier strategy undecided** ("GUID/UUID or approved strategy"). | R: server-generated UUIDv7 or sequential GUID to avoid clustered-index fragmentation; non-guessable externally; never use ordinal IDs in URLs. |
| DB-03 | S2 | **Effective-dated overlaps** (teacher assignment, guardian relationship, branch assignment) have no mechanism. | R: `ValidFrom/ValidTo` (UTC) with application-level and filtered-unique-index protection; see D-05 for range conflicts. |
| DB-04 | S2 | **Money:** `decimal(p,s)` unspecified; currency exponents differ; if Iran-first, Rial/Toman denomination is a classic defect. | R: `decimal(19,4)` plus ISO-4217 code and stored *denomination*; rounding policy as data; decision needed with OD-011. |
| DB-05 | S2 | **Append-only is not enforced technically.** | R: audit/ledger tables written through an INSERT-only database role; no UPDATE/DELETE grants; optional hash chaining for audit. |
| DB-06 | S2 | **Audit retention vs erasure** (AUD-N02). | R: store subject IDs and redacted metadata, not raw PII values, in audit payloads. |
| DB-07 | S2 | **Per-tenant encryption and offboarding.** Deleting a tenant from backups is impractical. | Decision: per-tenant data keys (crypto-shredding) vs retention-window deletion. Ties to OD-022/030. |
| DB-08 | S2 | **Report card snapshot.** | R: immutable JSON snapshot + rendered artifact reference with content hash, formula version, and template version. |
| DB-09 | S3 | **Indexes** "tenant, branch, status": make `TenantId` the leading key of every tenant-scoped index. | — |
| DB-10 | S3 | **Soft delete** is rejected in the design; confirm that archival (ORG-005) is a status, not deletion. | — |
| DB-11 | S2 | **Migrations** need a documented policy for RLS predicates, backfills, and expand/contract. | R: expand → migrate → contract; migration role distinct from runtime role. |
