# 01 — Documentation Audit (Completeness and Consistency)

**Status:** Analysis artifact — PROPOSED, not approved. Does not modify any approved document.
**Scope:** All 44 Markdown files plus `package-manifest.json` as found on disk.
**Method:** Full read of every file; mechanical cross-checks (file lists, requirement ID counts, term searches). Evidence is cited as `file:line` or by the command class used.

Severity: **S1** blocks a Phase 1 task or contradicts an approved decision; **S2** blocks a later phase or creates ambiguity that will cause rework; **S3** hygiene.

## A. Repository state (verified)

| Check | Result |
|---|---|
| Git repository | **No** (`git rev-parse` fails). `TASK_TRACKER.md` requires commit evidence for completion; none can exist yet. |
| `src/` | Absent (consistent with README). |
| Requirements | 94 total: 65 P0, 28 P1, 1 P2. All "Proposed" (`PRODUCT_REQUIREMENTS.md:3`). |
| Approved items | Only the security baseline (8 items) and DL-037..DL-042. Nothing else is approved. |
| Task tracker | All 24 tasks `NOT_STARTED`. This is accurate; no evidence of completion exists. |
| CHANGELOG | Absent, though `AGENTS.md:18` requires updating one. |

## B. Completeness findings

| ID | Sev | Finding | Evidence |
|---|---|---|---|
| A-01 | S1 | **No version control.** Evidence-based completion (commit, test output) is impossible until a repository exists. | git check; `TASK_TRACKER.md` footer |
| A-02 | S2 | `DOCUMENT_INDEX.md` omits `REQUIREMENT_TRACEABILITY.md` (39 listed, 40 on disk besides itself). Manifest `document_count: 44` is correct. | file-list diff |
| A-03 | S2 | **Traceability is area-level only.** No requirement → task → test → acceptance mapping. No doc other than `PRODUCT_REQUIREMENTS.md` references a requirement ID. | grep for ID patterns |
| A-04 | S2 | **Phase 0 exit deliverables do not exist:** ERD, API contracts, permission matrix, workflow catalog (`IMPLEMENTATION_ROADMAP.md`, Phase 0). | directory listing |
| A-05 | S2 | **DL-001..DL-036 are absent.** The log starts at DL-037 "carried forward"; the original decision text and the "M1" milestone are not in the package. Faithful carry-forward cannot be verified. | `DECISION_LOG.md:3-4` |
| A-06 | S2 | **No document carries version, status, owner, or date**, though README requires semantic revisions and owner/date per decision. Only `README.md` has a status line. | grep |
| A-07 | S2 | **No explicit out-of-scope list** in the charter (payroll/HR, library, transport, hostel, canteen, LMS quizzes, etc.). | `PROJECT_CHARTER.md` |
| A-08 | S2 | **Acceptance criteria are global only**, with no per-requirement criteria. Areas with none: referral (XFR), cards, planning, learning, SaaS quotas/entitlements, reporting, import/export. | `ACCEPTANCE_CRITERIA.md` |
| A-09 | S2 | **Baseline rules lack matching acceptance criteria and test suites.** Baseline requires automated tests for all 8 rules. Acceptance says only "time-limited"; the 90-day cap, fresh-renewal and small-cell <5 suppression appear in neither `ACCEPTANCE_CRITERIA.md` nor the mandatory suites in `TEST_STRATEGY.md`. | both files |
| A-10 | S3 | `TASK_TRACKER.md` has no per-task acceptance criteria, owner, or risk columns; AGENTS "Definition of Ready" cannot be evaluated from it (see doc 06). | tracker |

## C. Consistency findings (conflicts requiring a decision)

AGENTS.md: *"If documents conflict, stop, identify the conflict, and request a decision. Do not silently choose."* These are logged as CF-xx in `05_DECISIONS_REGISTER.md`; none has been resolved here.

| ID | Sev | Conflict | Documents |
|---|---|---|---|
| CF-01 | S1 | **Approval status of architecture.** AGENTS calls the modular boundaries "approved" and makes outbox/inbox "non-negotiable"; the Decision Log lists modular monolith, outbox, ASP.NET+Next.js as "Proposed, not yet approved"; CLAUDE.md requires explicit architecture approval. | `AGENTS.md:36`; `DECISION_LOG.md`; `CLAUDE.md` |
| CF-02 | S1 | **Same-transaction payment/enrollment update vs module boundaries.** Journey says invoice balance *and* enrollment state update "transactionally"; Finance "must not own enrollment eligibility" and cross-module work must use contracts/events. | `USER_JOURNEYS.md:60`; `MODULE_BOUNDARIES.md` |
| CF-03 | S1 | **MFA priority.** Threat model lists "MFA for privileged users" as a control; IAM-007 (MFA) is P1; the Phase 1 foundation has no MFA. Support/finance/admin privileged flows are P0. | `SECURITY_PRIVACY_THREAT_MODEL.md:10`; `PRODUCT_REQUIREMENTS.md:20` |
| CF-04 | S2 | **Reminder dedup key.** Domain model: event+recipient+channel+offset. Notifications doc: also includes *event version*. Without version, a rescheduled event's same-offset reminder is wrongly suppressed. | `DOMAIN_MODEL.md:43`; `NOTIFICATIONS_AND_REMINDERS.md:27` |
| CF-05 | S2 | **Module lists disagree.** Architecture has 19 modules; boundaries table has 18, with different splits. Missing from the boundaries table: Cards & Documents, Attendance, Scheduling/Calendar ownership split, and any owner for Referral, file/attachment storage, and provider configuration. | `SYSTEM_ARCHITECTURE.md`; `MODULE_BOUNDARIES.md` |
| CF-06 | S2 | **Roadmap vs requirement priority.** XFR-001..005 are P0 but the roadmap schedules no referral work; traceability says "Phase 4/8". Reporting (RPT P0), data import (journey step 6), and support-access tooling (baseline items 3, 6, 8) have no phase before Phase 8. | `IMPLEMENTATION_ROADMAP.md`; `REQUIREMENT_TRACEABILITY.md` |
| CF-07 | S2 | **SaaS phase mismatch.** Traceability: Phase 1/8. Roadmap: Phase 5. Tracker EEMP-020 depends only on EEMP-002. | three files |
| CF-08 | S2 | **Phase ordering vs dependencies.** Planning fees (PLN-003, Phase 4) and paid consultation precede Finance (Phase 5); enrollment activation on payment (Phase 3) precedes Finance; consultation booking needs meeting links/reminders (Phases 6–7) but EEMP-016/017 depend on EEMP-012. | roadmap; tracker; journeys |
| CF-09 | S2 | **Provider secrets.** Tenant-level provider configuration (assumptions) vs "avoid storing provider secrets in tenant-visible settings" vs "credentials encrypted, never returned to browser". No secret-storage model reconciles these. | `ASSUMPTIONS_AND_CONSTRAINTS.md`; `RECORDED_COURSES_…`; `EXTERNAL_INTEGRATIONS.md` |
| CF-10 | S2 | **Background worker "optional later"** vs required component in deployment and by outbox/reminders from Phase 4. | `SYSTEM_ARCHITECTURE.md`; `DEPLOYMENT_AND_ENVIRONMENTS.md` |
| CF-11 | S2 | **Tenant hierarchy vocabulary.** "Institute account" (assumptions), Tenant/CentralOffice/Institute/Branch (domain), "Branch … under an institute or central office" (glossary). Baseline says "institutes/branches"; DL summary says only "institute". | assumptions; glossary; baseline; DL |
| CF-12 | S2 | **"Supervision grants" undefined.** Baseline #3 says "Supervision/support grants"; no document defines supervision, and the domain model has no grant entity for it (only referral `AccessGrant`). | grep "supervis" |
| CF-13 | S2 | **Baseline items 7–8 have no DL ID.** DL-037..042 summary covers baseline items 1–6; items 7 and 8 are not in the summary. They remain binding as stated, but provenance needs confirmation. | `DECISION_LOG.md` vs baseline |
| CF-14 | S3 | "1 day before" (NOT-001) vs "24h" (acceptance) differ across DST for event-local semantics. | requirements; acceptance; notifications |

## D. Terms flagged TBD / assumption-only

Only three literal `TBD`/`Decision Required` markers exist. Unresolved items are tracked in `OPEN_DECISIONS.md` (30 rows), which has no owner, due date, blocking-task or status columns. Doc 05 adds those.

## E. What the documentation does well (to preserve)

Clear precedence order in AGENTS; explicit separation of user account vs person, class definition vs offering, invoice vs payment vs ledger, schedule vs delivery attempt; deny-by-default authorization dimensions; versioned workflows and immutable report-card snapshots; separate institute vs SaaS ledgers.
