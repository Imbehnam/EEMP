# 02 — Missing Business Requirements (Proposed)

**Status:** PROPOSED additions. None is approved; none changes `PRODUCT_REQUIREMENTS.md`. IDs use a `-N` suffix style (`ADM-N01`) so they cannot be confused with approved IDs. Priority is a recommendation for the product owner.

Basis: each gap is traceable to a statement in the existing docs that implies a capability with no requirement behind it.

## 1. Gaps implied by existing documents

| ID | Proposed requirement | Implied by | Rec. |
|---|---|---|---|
| ADM-N01 | Public/unauthenticated enrollment application with applicant (prospect) entity, anti-abuse controls, and conversion to student on approval. | Enrollment journey step 1 (applicant has no account yet); no Applicant in domain model | P0 |
| IMP-N01 | Validated bulk import (dry-run, error report, idempotent re-run, tenant-scoped, audited) and tenant export formats. | Onboarding journey step 6; OD-025; SaaS-005 | P0 |
| IAM-N01 | Login identifier policy (email / phone / username / national ID), verification, and recovery for users without email (e.g., minors). | IAM-001; OD-001 Iran-first implies phone-based identity | P0 |
| IAM-N02 | Password/credential policy, lockout, session lifetime, device/session revocation. | IAM-007 is P1 only; baseline #5 needs session revocation | P0 |
| IAM-N03 | MFA mandatory for privileged roles (platform operator, support, tenant owner, finance refund/approve). Resolves CF-03. | Threat model control | P0 |
| IAM-N04 | Guardian authority catalog: named authority flags per student-guardian link (view academics, view finance, pay, book, receive notifications, pickup, consent). | IAM-005, PORT-003, personas "not unrestricted custody" | P0 |
| IAM-N05 | Age-of-majority / consent transitions: guardian access changes when the student reaches the applicable age. | OD-006; AUDIT_RETENTION_AND_CONSENT | P1 |
| IAM-N06 | Prevent self-grant: an actor who can assign roles cannot grant themselves data-sensitive permissions without second approval (see doc 04, T-02). | Baseline #1 has a loophole without it | P0 |
| SUP-N01 | Support/supervision access grants: request, approve (per OD-029), scope, ≤90 days, fresh approval to renew, revoke, audit, no shared accounts. | Baseline #3, #6, #8; SaaS-007 is P1 and only in Phase 8 | P0 |
| AUD-N01 | Audit log access: who may search/export which audit scope; tenant audit vs platform audit; redaction rules. | `/audit` API group; AUDIT doc | P0 |
| AUD-N02 | Erasure vs immutability: audit/financial records reference subjects by ID and are pseudonymized, not rewritten. | AUDIT_RETENTION_AND_CONSENT; DATABASE_DESIGN append-only | P0 |
| RPT-N01 | Report catalog and sensitivity classification: which aggregates are "sensitive" and subject to the <5 small-cell rule; secondary-suppression rule. | Baseline #4; RPT-002 gives no threshold or scope | P0 |
| ORG-N01 | Issuing legal entity per institute/branch (name, tax ID, address, invoice numbering scope). | FIN-002 numbering scope; no LegalEntity in domain model | P0 |
| ORG-N02 | Academic year rollover: promotion, class roster rollover, graduation, withdrawal, transfer-out status. | EDU-003, EDU-008; no lifecycle beyond "status" | P1 |
| EDU-N01 | Cumulative academic transcript (distinct from per-term report card), GPA/credit policy. | EDU-006/007; OD-007 | P1 |
| EDU-N02 | Waitlist and capacity overflow rules. | EDU-004 capacity | P1 |
| EDU-N03 | Room/facility resource model and conflict policy. | CAL-003; domain "Classroom/Location" | P1 |
| DOC-N01 | Student/applicant document upload (IDs, prior transcripts) with classification, malware scan, retention. | WFL-003 attachments; workflow doc | P0 |
| COM-N01 | School-to-parent announcements and teacher–guardian messaging (or explicit exclusion). | Personas: teacher "approved communication"; NOT-005 in-app | P1 |
| NOT-N01 | SMS consent/opt-out register, sender-ID rules, quiet hours, and who pays for SMS credits. | NOT-004; SaaS-003 meters SMS; OD-013/014 | P0 |
| NOT-N02 | Definition of non-optional (transactional/security) vs optional notifications; opt-out always wins over tenant enablement. | NOTIFICATIONS precedence list | P0 |
| FIN-N01 | Refund maker-checker (initiator ≠ approver), thresholds, and reasons. | FINANCE_AND_PAYMENTS controls; no FIN requirement | P0 |
| FIN-N02 | Financial period close, locked periods, and adjustments after close. | FINANCE controls; no FIN requirement | P1 |
| FIN-N03 | Gapless/sequential invoice numbering where legally required, concurrency-safe. | FIN-002 "sequential numbering policy" | P0 |
| FIN-N04 | Credit balance/overpayment handling and allocation order. | FIN-006 overpayments | P0 |
| FIN-N05 | Withdrawal refund and proration policy. | FIN-001/006; no business rule | P1 |
| SAAS-N01 | Quota enforcement semantics: soft/hard limit, grace, what is blocked; definition of "active student" and metering window. | SaaS-002/003; OD-010 | P0 |
| SAAS-N02 | Terms/DPA acceptance record per tenant, versioned. | Onboarding journey step 2; release checklist | P0 |
| SAAS-N03 | Tenant-facing API keys / outbound webhooks (or explicit exclusion). | SaaS-003 meters "API calls"; architecture "REST for external integrations" | P1 |
| SEC-N01 | Per-tenant provider-secret storage (envelope encryption, rotation, no read-back). Resolves CF-09. | EXTERNAL_INTEGRATIONS | P0 |
| SEC-N02 | Data classification catalog (public / internal / personal / sensitive / restricted) mapped to fields. | THREAT_MODEL "sensitive data" | P0 |
| SEC-N03 | National-ID and similar identifiers: collection justification, masking, storage, search restrictions. | Persian locale context; PII | P1 |
| CAL-N01 | Calendar system rules: Gregorian vs Jalali/Hijri for academic year, term, due dates, reports. | OD-020; INTERNATIONALIZATION | P1 |
| CRD-N01 | Digital card verification (signed token, online check, revocation). | PORT-004; OD-018 | P1 |
| NFR-N01 | Client platform decision: web only, PWA, or native (implied by "push"). | NOT-005 | P1 |
| NFR-N02 | Search: scope, Unicode normalization (Persian/Arabic), tenant-scoped, authorization-trimmed. | I18N doc; isolation doc "search indexes" | P1 |

## 2. Requirements that exist but are under-specified

| Requirement | Missing detail |
|---|---|
| ORG-005 | Branch transfer: do students/records move, copy, or stay? Interaction with sibling isolation. |
| IAM-003 | Permission catalog, scope types, and policy language not defined (draft in doc 06). |
| EDU-005 | Grade lifecycle states and who may transition them. |
| WFL-005 | Parallel approvals/delegation depth (OD-027). |
| CNS-003 | Slot granularity and the concurrency mechanism (see doc 03, D-05). |
| FIN-004 | How tenant is derived when a provider webhook arrives. |
| XFR-001..006 | Intra-tenant (sibling institute) vs inter-tenant; copy vs move (OD-005). |
| RPT-001/002 | No catalog; threshold only in baseline. |
| OPS-002 | Retention periods per data category (all TBD). |
| LOC-004 | Which locales ship first (OD-019). |

## 3. Proposed explicit non-goals (for confirmation)

Payroll and HR contracts; library, transport, hostel, canteen; native mobile apps (unless NFR-N01 decides otherwise); in-product video authoring; live proctoring; marketplace of third-party plugins. Each should be confirmed or struck by the product owner.
