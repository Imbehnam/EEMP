# 07 — Task Readiness and Proposed Sequencing

**Status:** PROPOSED. `TASK_TRACKER.md` and `IMPLEMENTATION_ROADMAP.md` are **not modified**. All 24 tracker tasks remain `NOT_STARTED`, which is the accurate state: no implementation, commit, or test evidence exists.

## 1. Definition-of-Ready assessment (AGENTS.md) for every tracker task

"Blocked by" lists the decisions from doc 05 that must be approved first. A task is READY only when approved requirement, bounded scope, acceptance criteria, security/tenant impact, API/data impact and test approach all exist. Today **no task is READY**; the common cause is that all requirements are still "Proposed".

| Task | Work package | Blocked by | Spec status |
|---|---|---|---|
| EEMP-000 | Discovery and open-decision resolution | This analysis → product owner | In progress (docs 01–07) |
| EEMP-001 | Architecture and security baseline approval | APR-1, CF-01..CF-13 | Review done (docs 03, 04) |
| EEMP-002 | Repo, CI, local env | APR-1, APR-2, OD-024 | Full spec (doc 06 §1) |
| EEMP-003 | Tenant context and isolation harness | OD-002, OD-004, NEW-3, D-11 | Full spec (doc 06 §2) |
| EEMP-004 | Authentication and accounts | OD-023, NEW-7, CF-03 | Full spec (doc 06 §3) |
| EEMP-005 | Dynamic authorization | Q-1..Q-5, NEW-1, NEW-2, CF-12, OD-029 | Full spec (doc 06 §4) |
| EEMP-006 | Organization, central office, branches | OD-003, OD-025, NEW-6 | Needs spec |
| EEMP-007 | Student/guardian/teacher/staff | OD-006, OD-017, NEW-5, IAM-N04 | Needs spec |
| EEMP-008 | Academic catalog and classes | OD-019/020, CAL-N01 | Needs spec |
| EEMP-009 | Enrollment and attendance | OD-026, ADM-N01, CF-08 | Needs spec |
| EEMP-010 | Assessment, grades, report cards | OD-007, EDU-N01 | Needs spec |
| EEMP-011 | Workflow engine | OD-027, DOC-N01 | Needs spec |
| EEMP-012 | Consultation booking | OD-017, D-05, CF-08 | Needs spec |
| EEMP-013 | Academic planning, paid service | CF-08, OD-009 | Needs spec |
| EEMP-014 | Institute finance | OD-001, 008, 009, 011, 012, NEW-8, NEW-10 | Needs spec; most blocked |
| EEMP-015 | Recorded courses/media | OD-016 | Needs spec |
| EEMP-016 | Calendar, online meetings | OD-015, CAL-N01 | Needs spec |
| EEMP-017 | SMS reminders, notification center | OD-013, OD-014, CF-04, CF-14, NOT-N01/N02 | Needs spec |
| EEMP-018 | Digital cards | OD-018 | Needs spec |
| EEMP-019 | Cross-institute referral | OD-004, OD-005, OD-006, M-11, Q-2 | Needs spec |
| EEMP-020 | SaaS plans, quotas | OD-010, OD-028, SAAS-N01 | Needs spec |
| EEMP-021 | Localization, RTL/LTR, templates | OD-019, OD-020 | Needs spec |
| EEMP-022 | Reporting, analytics, export | RPT-N01, baseline #4 | Needs spec |
| EEMP-023 | Commercial readiness, security review | all release-critical | Later |

Per-phase specs for Phases 2–8 are intentionally deferred: writing them before OD-001/002/004/009 would be speculation (AGENTS: never silently infer legal, financial, grading, or sharing rules). Each is produced as its gating decisions are approved, using the same format as doc 06.

## 2. Sequencing defects in the current plan

| Defect | Evidence | Proposed correction (needs approval) |
|---|---|---|
| Paid planning (PLN-003) and paid consultation need Finance but Finance is Phase 5 | CF-08; EEMP-013 depends only on 011/012 | Split EEMP-014: **014A** fee catalog, invoice, ledger, payment-intent port + one adapter, pulled forward after EEMP-007; **014B** refunds, reconciliation, installments, SaaS billing stay in Phase 5. Add 014A as dependency of EEMP-012/013 |
| Enrollment activation on payment (Phase 3) precedes Finance | USER_JOURNEYS enrollment step 6 | Phase 3 ships enrollment with policy `PaymentRequired=false`; payment gating activates via event when 014A exists (D-02) |
| Consultation booking needs calendar events, meeting links, reminders but EEMP-016/017 depend on EEMP-012 | tracker deps | Split EEMP-016: **016A** calendar core (events, recurrence, conflict checks) before EEMP-012; **016B** meeting-provider adapter later. Reminders (EEMP-017) consume calendar events |
| No task for audit, observability, support access, localization foundation | doc 06 §5 | Add EEMP-003A, EEMP-005A, EEMP-005B |
| Referral (P0) has no phase | CF-06 | Keep EEMP-019, assign Phase 4, depends on 005A (grant mechanism) and 007 |
| Reporting (P0) and import (P0 journey step) have no phase | CF-06 | Report catalog and suppression layer with first sensitive aggregate (Phase 3); import with EEMP-006 |
| SaaS entitlements enforced only after Phase 5 although provisioning is Phase 2 | CF-07 | EEMP-020 split: **020A** tenant lifecycle + plan/entitlement scaffolding at Phase 2; **020B** metering/billing at Phase 5 |
| Tracker rows lack acceptance, owner, risk | A-10 | Add columns; link each task to requirement IDs and test suites |

## 3. Proposed requirement → task → suite traceability (starting point)

| Requirement area | Tasks | Mandatory suite (TEST_STRATEGY) | New suite needed |
|---|---|---|---|
| ORG, IAM | 003–006 | 1 Tenant/branch isolation | Support grants (90-day, renewal); self-grant |
| PORT | 007 | 2 Guardian access | Authority flags |
| EDU | 008–010 | 3 Teacher access, 4 Grades | — |
| WFL | 011 | 6 Workflow | — |
| CNS | 012 | 7 Booking concurrency | — |
| FIN | 014A/B | 5 Finance | Maker-checker, numbering concurrency |
| NOT, CAL | 016–017 | 8 Reminders | Opt-out precedence |
| XFR | 019 | 9 Referral | — |
| LOC | 021 | 10 Localization | — |
| RPT | 022 | — | **Small-cell suppression (baseline #4)** |
| SaaS | 020A/B | — | Entitlement enforcement; lifecycle/offboarding |

## 4. What happens next

No implementation begins until the product owner responds with approvals; see doc 00 for the exact approval request.
