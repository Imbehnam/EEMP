# Task Tracker

Statuses: `NOT_STARTED`, `IN_PROGRESS`, `IN_REVIEW` (implemented, build and automated tests pass, awaiting user verification), `DONE` (user accepted).

| ID | Work package | Status | Depends on |
|---|---|---|---|
| EEMP-000 | Product discovery and open-decision resolution | NOT_STARTED | — |
| EEMP-001 | Architecture and security baseline approval | NOT_STARTED | EEMP-000 |
| EEMP-002 | Repository, CI, local environment | IN_REVIEW | EEMP-001 |
| EEMP-003 | Tenant context and isolation harness | IN_REVIEW | EEMP-002 |
| EEMP-004 | Authentication and account lifecycle | IN_REVIEW | EEMP-003 |
| EEMP-005 | Dynamic authorization and scope assignments | IN_REVIEW | EEMP-004 |
| EEMP-006 | Organization, central office, branches | IN_REVIEW | EEMP-005 |
| EEMP-007 | Student, guardian, teacher, staff domain | IN_REVIEW | EEMP-006 |
| EEMP-008 | Academic catalog and class operations | IN_REVIEW | EEMP-007 |
| EEMP-009 | Enrollment and attendance | IN_REVIEW | EEMP-008 |
| EEMP-010 | Assessment, grades, report cards | IN_REVIEW | EEMP-009 |
| EEMP-011 | Workflow engine | IN_REVIEW | EEMP-005 |
| EEMP-012 | Consultation and support booking | NOT_STARTED | EEMP-011 |
| EEMP-013 | Academic planning and paid service | NOT_STARTED | EEMP-011, EEMP-012 |
| EEMP-014 | Institute finance and tuition payments | NOT_STARTED | EEMP-007, EEMP-011 |
| EEMP-015 | Recorded courses and media | NOT_STARTED | EEMP-005 |
| EEMP-016 | Calendar and online meetings | NOT_STARTED | EEMP-012 |
| EEMP-017 | SMS reminders and notification center | NOT_STARTED | EEMP-016 |
| EEMP-018 | Digital student/teacher cards | NOT_STARTED | EEMP-007 |
| EEMP-019 | Cross-institute referral/transfer | NOT_STARTED | EEMP-007, EEMP-011 |
| EEMP-020 | SaaS plans, quotas, subscriptions | NOT_STARTED | EEMP-002 |
| EEMP-021 | Localization, RTL/LTR, document templates | NOT_STARTED | EEMP-002 |
| EEMP-022 | Reporting, analytics, export | NOT_STARTED | Core modules |
| EEMP-023 | Commercial readiness and security review | NOT_STARTED | All release-critical work |

Update only after evidence-based completion. Include commit, test results, and reviewer/user acceptance where applicable.

## Milestone 1 backend evidence (2026-11-24, branch `feature/m1-backend`)

Authorized by the product owner's explicit M1 instruction (DL-048). EEMP-000/001 remain open; the
M1 work runs on accepted assumptions recorded in DL-049..DL-051. Not `DONE` until user verification.

Commits: `0930f24` (WIP foundation), `dac5aab` (people + API host + seed), `16c4b5a` (migrations),
`e900c2d` (tests), `8ce3a2b` (compose/Dockerfile/CI), plus the docs commit on the same branch.

Commands (run in `backend/`, .NET SDK 10.0.404): `dotnet build EEMP.slnx` → 0 warnings, 0 errors
(warnings as errors, Debug and Release); `dotnet test EEMP.slnx` → UnitTests 31/31, ArchitectureTests 48/48,
IntegrationTests 34/34 passed; `./check-migrations.sh` → no pending model changes in all 5 contexts.
Integration tests use the SQLite fallback provider (no SQL Server/Docker in the build sandbox).

| ID | Evidence | Open items |
|---|---|---|
| EEMP-002 | Solution + central packages, `global.json`, `docker-compose.yml` (sqlserver/redis/api), `backend/Dockerfile`, `.github/workflows/backend.yml` (restore, build, migration check, test), `backend/README.md`, dev seed | Compose and Dockerfile not executed in sandbox (no Docker); CI first run happens on the PR; branch/PR policy still undefined (DL-044) |
| EEMP-003 | Tenant from JWT claim only; central EF query filters (fail closed); save-pipeline stamping and cross-tenant write rejection; tenant-composite FKs; tests `IsolationTests.Tenants_cannot_see_each_others_data`, architecture `Every_table_is_classified_tenant_owned_or_global`, `Tenant_owned_tables_have_a_query_filter_and_tenant_leading_index` | SQL Server RLS not implemented (DL-045 consequence recorded in DL-052); harness proven on SQLite only |
| EEMP-004 | Login/refresh/logout/me/preferences; hashed rotating refresh tokens with reuse detection; per-request session validation; lockout; rate limit; tests `AuthTests.*`, `RateLimitTests.*` | MFA, password reset and invitation flows not in M1 |
| EEMP-005 | Permission catalog, roles CRUD, tenant-wide/branch assignments, no self-grant, immutable system roles, TenantAdmin without people access, revocation on next request; tests `AuthorizationTests.*`, `IsolationTests.Branch_manager_sees_only_own_branch`, `Sibling_branch_records_are_not_found_and_cannot_be_created` | Supervision grants (90-day) and support access (DL-037..042) not in M1 scope |
| EEMP-006 | `/organization`, `/branches` with branch scope and in-use protection; tests `IsolationTests.Organization_read_and_update`, `Branch_update_with_stale_rowversion_returns_409` | — |
| EEMP-007 | `/students` (+guardian links), `/guardians`, `/teachers`, `/staff`, `/portal/me/profile`, `/portal/me/students`; tests `IsolationTests.People_crud_with_guardian_links_and_stale_rowversion_conflict`, `Portal_shows_only_own_profile_and_linked_students` | Cross-institute identity/transfer (OD-004 alternative) deferred |

## Milestone 2 backend evidence (2026-11-24, branch `feature/m2-academic-backend`)

Authorized by DL-053; runs on accepted assumptions DL-054 (OD-007, OD-026, OD-020) and the decisions in DL-055 (single Academic
module pending confirmation). Stacked on `feature/m1-backend` (PR #1). Not `DONE` until user verification.

Commands (run in `backend/`, .NET SDK 10.0.404): `dotnet build EEMP.slnx` → 0 warnings, 0 errors (warnings as errors);
`dotnet test EEMP.slnx` → UnitTests 54/54, ArchitectureTests 62/62, IntegrationTests 45/45 passed (11 new academic integration tests);
`./check-migrations.sh` → no pending model changes in all 6 contexts. Integration tests use the SQLite fallback provider.

| ID | Evidence | Open items |
|---|---|---|
| EEMP-008 | `/academic-years`, `/terms`, `/subjects`, `/courses`, `/grading-schemes`, `/classes` (+ `/schedule`, `/teachers`, `/roster`), `/portal/me/classes`; rules: term inside year/no overlap, slot validation, room/teacher/student timetable conflicts, effective-dated teacher assignments, branch scope; tests `AcademicTests.Catalog_crud_rules_and_permissions`, `Classes_schedule_conflicts_teachers_and_branch_scope`, `Teacher_acts_only_on_assigned_classes`, unit `ScheduleRuleTests.*` | Holidays/exceptions and dated substitutions not modeled (DL-054); split into separate catalog/enrollment/assessment/report modules pending (DL-055) |
| EEMP-009 | `/enrollments` (+ withdraw), `/classes/{id}/attendance` (GET sheet, PUT idempotent bulk), `/attendance/sessions`, `/attendance`; capacity guarded by class rowversion, branch match, timetable conflicts, re-enrollment; tests `Enrollment_capacity_branch_and_timetable_rules`, `Bulk_attendance_is_idempotent_and_validated`, `Concurrent_enrollments_cannot_exceed_capacity`, unit `EnrollmentRuleTests.*` | Attendance policies/notifications out of scope |
| EEMP-010 | `/classes/{id}/assessments`, `/assessments/{id}` (+ `/results`, `/submit`, `/return`, `/approve`, `/publish`, `/reopen`, `/history`), `/classes/{id}/gradebook`, `/report-cards` (generate, list, get, versions, publish, batch publish), portal timetable/attendance/grades/report cards; append-only grade history; published snapshots; versioned report cards; tests `Grade_workflow_publish_gating_history_and_portal_visibility`, `Report_cards_are_versioned_and_portal_sees_published_only`, `Portal_timetable_and_attendance_for_linked_student`, `Academic_data_is_isolated_between_tenants`, `Academic_errors_are_localized`, unit `GradeCalculatorTests.*`, `AssessmentWorkflowTests.*` | Report-card PDF/templates not implemented; existing tenants' system roles not auto-updated with new permissions (DL-056) |

## Milestone 2 frontend evidence (2026-11-25, branch `feature/m2-academic-frontend`)

Frontend portion of EEMP-008..010 against the M2 contract (backend merged to `main` with PR #3). Statuses stay `IN_REVIEW`
until user verification. Details: `docs/05-experience/FRONTEND_IMPLEMENTATION.md` (M2 section) and
`docs/09-quality/M2_FRONTEND_INTEGRATION_REPORT.md`.

Commands (in `frontend/`, pnpm 10, Node 22): `pnpm lint` (0 warnings), `pnpm typecheck`, `pnpm test` → 10 files / 79 tests,
`pnpm build`, `pnpm test:e2e` (mock) → 14/14, `pnpm test:e2e:real` against `main`'s API on SQLite → 30/30 (5 new academic specs).

| ID | Frontend evidence | Open items |
|---|---|---|
| EEMP-008 | Admin: academic years (+ terms) with Jalali date entry for `fa`, subjects, courses, grading schemes (bands, default), classes list/filters/create, class overview with weekly schedule editor (room/teacher/student conflicts named), teacher assignments; portal teacher "My classes"; nav by `catalog.read`/`classes.read`; tests `mock-academic.test.ts`, `calendar.test.ts`, `e2e/academic.spec.ts`, `e2e-real/academic.real.spec.ts` (Jalali term + overlap) | Holidays/exceptions not modeled (backend DL-054) |
| EEMP-009 | Admin roster (enroll with capacity/branch/timetable errors, withdraw), attendance sheet with period picker and bulk mark (shared with the teacher portal); student/guardian attendance view; real-API spec "teacher.one marks attendance … idempotent save" | Attendance history per student in admin (uses `/attendance` list) not built |
| EEMP-010 | Assessments + weights, score entry (mark/score/comment/reason), workflow buttons gated by `grades.manage/approve/publish` (teacher: submit only), grade history, gradebook with live totals, report cards (generate, list, versions, publish one, batch publish, print); portal published grades, report cards; guardian student picker; real-API specs for grade entry → publish → guardian sees only published, report cards publish → guardian opens card | No PDF (browser print only); report-card list filter by branch not exposed |

## Report-card PDF export (EEMP-010 follow-up, 2026-11-26, branch `feature/report-card-pdf`)

Closes the "Report-card PDF not implemented" gap of EEMP-010 (DL-056) — decisions DL-057 (library/licence/font) and DL-058
(behaviour). Branch from `main` with `fix/e2e-real-api-seed` (PR #4) and `feature/m2-academic-frontend` (PR #5) merged in, since
neither was merged yet. Stays `IN_REVIEW` until user verification.

Commands: backend (`backend/`, .NET SDK 10.0.404) `dotnet build EEMP.slnx -c Release` → 0 warnings/0 errors (warnings as errors);
`dotnet test` → UnitTests 71/71 (15 new PDF tests, `pdftotext` assertions), ArchitectureTests 62/62, IntegrationTests 51/51
(3 new: `ReportCardPdfTests.*`). Frontend (`frontend/`, pnpm 10, Node 22) `pnpm lint`, `pnpm typecheck`, `pnpm test` → 11 files /
85 tests, `pnpm build`, `pnpm test:e2e` → 16/16 (2 new), `pnpm test:e2e:real` against this branch's API (SQLite) → 31/31 (1 new).
Rendered samples were inspected visually (fa/en/ar first pages).

| ID | Evidence | Open items |
|---|---|---|
| EEMP-010 (PDF) | `GET /report-cards/{id}/versions/{n}/pdf`, `GET /portal/me/report-cards/{id}/pdf`, `GET /report-cards/pdf-archive?classId=`; SkiaSharp + HarfBuzzSharp renderer with embedded Vazirmatn (OFL); fa/ar RTL shaping, Persian/Arabic-Indic digits, Jalali for fa; tests `ReportCardPdfTests` (unit: bidi, locale, culture-independent dates, fa/en/ar/tr text extraction, multi-page; integration: permissions, branch scope, draft → portal 404, unlinked guardian/student 404, teacher 404/403, tenant isolation, content type, safe file name, archive); frontend "Download PDF" (admin detail, portal detail) and "Download all PDFs (ZIP)" (admin list with a class selected) through the BFF, fa/en/tr/ar strings, Vitest `report-card-pdf.test.ts` + BFF streaming tests, Playwright mock + real-API download specs | Not audited; untagged PDF; no tenant logo/templates (EEMP-021); Docker image not built here (DL-058) |

## EEMP-011 workflow engine backend evidence (2026-11-26, branch `feature/workflow-engine-backend`)

Backend only. Stacked on `feature/report-card-pdf` (draft PR #6, merged in — no rebase). Decisions DL-070..DL-074 (numbering starts at
DL-070 because the parallel Finance branch uses DL-059+). Contract: `docs/08-planning/WORKFLOW_ENGINE_CONTRACT.md`. Stays `IN_REVIEW`
until user verification; OD-027 scope and DL-071/DL-072 need product confirmation.

Commands (`backend/`, .NET SDK 10.0.404): `dotnet build EEMP.slnx` → 0 warnings / 0 errors (warnings as errors); `dotnet test` →
UnitTests 83/83 (12 new: `WorkflowMachineTests`, `ApproverPolicyTests`), ArchitectureTests 77/77 (Workflow added to boundary,
classification and schema checks + `WorkflowArchitectureTests`), IntegrationTests 57/57 (6 new `WorkflowTests`); `./check-migrations.sh`
→ no pending model changes in all 7 contexts. Integration tests run on the SQLite fallback.

| ID | Evidence | Open items |
|---|---|---|
| EEMP-011 | `workflow` schema (Definitions, DefinitionVersions, DefinitionSteps, Instances, InstanceHistory, OutboxMessages) + migration; `/workflow/definitions` CRUD/versions/publish, `/workflow/inbox`, `/workflow/instances` (mine/all), `/workflow/instances/{id}` + approve/reject/return/resubmit/cancel; `IWorkflowService` + `IWorkflowEventHandler` contracts; outbox dispatcher + retry worker; `workflow.*` permissions; fa/en/tr/ar error codes; dev seed `discount-approval`; tests `WorkflowTests.Definitions_are_versioned_and_published_versions_are_immutable`, `Two_step_approval_applies_maker_checker_and_distinct_approvers_and_completes_once`, `Instances_are_isolated_by_branch_and_tenant`, `Return_resubmit_and_cancel_notify_the_owning_module`, `Concurrent_and_retried_commands_do_not_corrupt_state`, `Starting_is_idempotent_and_completion_delivery_retries_until_the_handler_succeeds` | Frontend not built; conditions/parallel/delegation/escalation/reminders not built (OD-027); requester's-manager and role-based steps unavailable (DL-071); existing tenants need the Finance branch's system-role sync (DL-072); Finance still uses its local approval step until it integrates (contract §7) |
