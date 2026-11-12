# 05 — Unresolved Decisions Register

**Status:** PROPOSED. Recommendations are advice, not decisions. Nothing here is entered in `DECISION_LOG.md`; that happens only on the product owner's approval.

Columns: **Blocks** = first task that cannot meet Definition of Ready without it (task IDs from `TASK_TRACKER.md`). **Rec.** = recommendation, with the reason. "Gate" = must be decided before the named task starts.

## A. Gate for Phase 1 (EEMP-002 .. EEMP-005) — decide first

| ID | Decision | Blocks | Rec. |
|---|---|---|---|
| APR-1 | Approve (or amend) the architecture baseline: modular monolith, Clean Architecture, API-first, outbox/inbox (CF-01) | EEMP-001 | Approve with module enforcement (D-01) and canonical module list (doc 03 §3) |
| APR-2 | Initialize version control and branch/PR policy (A-01) | EEMP-002 | Git repository now; protected main; evidence in commits |
| OD-002 | Tenant definition | EEMP-003 | One tenant per customer organization (legal institute group); institutes/branches inside it |
| OD-003 | Central office model | EEMP-006 | One central office per tenant at launch; model as optional organization unit so it can be generalized |
| OD-004 | Student identity across institutes | EEMP-003/007 | Global login account, tenant-scoped person profiles (T-01) |
| OD-023 | Identity provider | EEMP-004 | Decide authentication mechanism separately from authorization (D-03); BFF cookie pattern (D-04) |
| OD-024 | Deployment topology and DB engine/hosting (see D-11) | EEMP-002/003 | Shared cluster, shared schema initially; confirm engine before RLS harness |
| OD-029 | Support access approval model | EEMP-005 | Tenant approval + platform approver (dual control); "break-glass" only if bounded by baseline #3, time-boxed, post-reviewed and never hidden |
| CF-03 | MFA scope and priority | EEMP-004 | MFA mandatory for privileged roles in Phase 1 (IAM-N03) |
| Q-1..Q-5 | Questions in doc 04 §6 (tenant-admin definition, supervision grant, baseline 7–8 provenance, role assignment, tenant export) | EEMP-005 | Answer before permission catalog is frozen |
| NEW-1 | Permission self-grant rule (T-02) | EEMP-005 | Second approver for sensitive-data permissions |
| NEW-2 | Authorization decision algorithm (doc 04 §4) | EEMP-005 | Approve as written or amend |
| NEW-3 | Identifier strategy (DB-02) and composite tenant FKs (DB-01) | EEMP-003 | UUIDv7/sequential GUID; composite FKs |
| NEW-4 | Locales shipped first (OD-019) and calendar systems (OD-020) | EEMP-021 (foundation touches Phase 1) | Persian + English initially; Gregorian + Jalali display; defers Arabic/Turkish |

## B. Gate for Phases 2–3 (organization, people, academic)

| ID | Decision | Blocks | Rec. |
|---|---|---|---|
| OD-005 | Transfer semantics (copy / move / referral-only) | EEMP-019, ORG-005 | Referral-copy with provenance first; permanent transfer later |
| OD-006 | Consent/guardian rules, age of majority | EEMP-007 | Jurisdiction-specific policy table; start with one jurisdiction (OD-001) |
| OD-007 | Grading model | EEMP-010 | Configurable schemes; ship numeric + letter bands first |
| OD-017 | Counseling data classification | EEMP-007/012 | Restricted class; separate store; access logged |
| OD-026 | Attendance/timetable in MVP | EEMP-009 | Include attendance; timetable minimal |
| OD-025 | Import/export formats | EEMP-006 | CSV import with dry-run; JSON/CSV export |
| NEW-5 | Guardian authority catalog (IAM-N04) | EEMP-007 | Define flags before schema |
| NEW-6 | LegalEntity/issuer model (ORG-N01) | EEMP-006/014 | Add to Organization |
| NEW-7 | Login identifier policy (IAM-N01) | EEMP-004 | Email or phone, verified; username for minors via guardian |

## C. Gate for Phases 4–5 (workflow, services, finance)

| ID | Decision | Blocks | Rec. |
|---|---|---|---|
| OD-001 | Target market(s) and legal jurisdictions | Most of Phase 4+ | **Highest-leverage business decision.** Drives payments, SMS, tax, privacy, language, calendars |
| OD-009 | Payment funds-flow (institute-owned accounts vs platform collection/split) | EEMP-014 | Institute-owned gateway accounts first (avoids money-transmission/regulated-intermediary exposure); confirm with counsel |
| OD-011 | Currencies and tax; money representation (DB-04) | EEMP-014 | Single currency first, ISO code + denomination stored |
| OD-012 | Payment providers | EEMP-014 | Shortlist after OD-001 |
| OD-008 | Tuition calculation | EEMP-014 | Fee catalog + per-term and per-course items; installments |
| OD-027 | Workflow configurability depth | EEMP-011 | Start with states/transitions/roles/SLA; conditions next; parallel approval later |
| OD-028 | Subscription suspension behavior | EEMP-020 | Grace → read-only + export window; never delete on suspension |
| OD-010 | SaaS pricing model; SAAS-N01 quota semantics | EEMP-020 | Hybrid tier + active-student band; define "active student" |
| NEW-8 | Ledger design: double-entry or not (M-08); invoice numbering gapless (FIN-N03) | EEMP-014 | Double-entry postings, append-only |
| NEW-9 | Dependency re-sequencing (CF-08): where payment hooks go for planning/consultation/enrollment | EEMP-009/012/013 | Introduce a "payment-required" port stub or move Finance ahead (see doc 07 §2) |
| NEW-10 | Provider-secret storage (SEC-N01, CF-09) | EEMP-014/017 | Envelope encryption via KMS/secret manager; write-only secrets |

## D. Gate for Phases 6–8

| ID | Decision | Rec. |
|---|---|---|
| OD-013 | SMS provider(s), sender rules; NOT-N01 consent | Platform-managed catalog; tenant uses allowed senders |
| OD-014 / CF-04 / CF-14 | Reminder precedence and dedup key | Opt-out always wins; mandatory notices exempt per policy; dedup key includes event version; "1 day" = 24 h elapsed unless event-local semantics are chosen |
| OD-015 | Online meeting provider | External provider via adapter |
| OD-016 | Recorded video hosting | Object storage + CDN with signed URLs first |
| OD-018 | Card verification | Signed QR token + online verification, revocable |
| OD-021 | Custom domains/white-label | Later phase (affects T-07) |
| OD-022 | Data residency / backup region | Decide with OD-001 |
| OD-030 | RPO/RTO and service tiers; DB-07 crypto-shredding | Set per plan after workload discovery |
| NFR-N01/N02 | Client platform; search | Web/PWA first; search deferred |

## E. Conflict resolutions needed (from doc 01)

CF-01 approval status; CF-02 payment consistency (rec. D-02); CF-03 MFA; CF-04 dedup key; CF-05 module list; CF-06 roadmap gaps; CF-07 SaaS phase; CF-08 phase ordering; CF-09 secret storage; CF-10 worker; CF-11 vocabulary; CF-12 "supervision"; CF-13 baseline 7–8 provenance; CF-14 reminder offset semantics. All open.

## F. Priority

If only three decisions can be made first: **OD-001** (market), **OD-002/004** (tenant and identity), **APR-1** (architecture approval). They unlock Phase 1 scope and constrain most later choices.
