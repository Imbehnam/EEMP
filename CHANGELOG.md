# Changelog

All notable changes to this project are documented here. Dates are UTC.

## [Unreleased]

### Added — Consultation and support booking (EEMP-012), 2026-11-28
- Backend module `Consultation` (schema `consultation`): consultant profiles per branch, availability slots (single and weekly pattern in
  the tenant time zone), portal booking for linked students in their own branch with atomic slot reservation, lifecycle
  confirm/complete/no-show/cancel with a configurable cancellation window, confidential append-only consultation notes (own consultant or
  `consultation.notes.read`; every read audited; TenantAdmin and BranchManager excluded), support tickets with internal notes.
- Permissions `consultation.*` and `support.*` (DL-091), built-in roles Consultant and SupportDesk, BranchManager grants; booking policy
  settings; fa/en/tr/ar error messages; dev seed `consultant.one` with a week of slots, a requested booking and a ticket.
- Admin console: consultation schedule (Saturday-first week view, add/repeat slots), bookings week/list, booking detail with confidential
  notes, consultants and booking policy, support inbox and ticket thread. Portal: book a consultation, my sessions with cancel, support
  requests. Shared `@eemp/app/consultation` (week view, thread, badges) and `@eemp/i18n` zoned time helpers.
- Fixed: `DevelopmentSeed.cs` compile error and invalid `messages.ar.json` left on `main` by the PR #8/#9 merges.
- Decisions DL-090..DL-095 (Proposed); contract `docs/08-planning/CONSULTATION_CONTRACT.md`.

### Changed — Accessible neumorphic UI redesign (both web apps), 2026-11-27
- New "accessible soft UI" design system (DL-080, `docs/05-experience/DESIGN_SYSTEM.md`): soft single-base light and dark palettes,
  dual-shadow raised surfaces, inset inputs/selected states, RTL-mirrored light source, Vazirmatn for every locale, 44px targets,
  visible 3px focus ring, reduced-motion support; all text/boundary pairs WCAG 2.2 AA (tested).
- Restyled centrally in `@eemp/ui`: buttons, text fields/selects/text areas, checkboxes/radios (global), cards, alerts (icon + text),
  badges, data tables (sticky header, zebra, tabular numbers, skeleton loading), pagination, search, confirm dialog, state panels.
- New components: `ToastProvider`/`useToast` (redirect notices now toast), `StatCard`/`StatGrid`, `Skeleton`, icon set incl.
  direction-aware `IconChevron`.
- App shell: off-canvas navigation drawer with backdrop below 1024px (Escape/backdrop close, focus management), header actions move
  into the drawer on phones, breadcrumbs derived from navigation, brand mark, content max width.
- Screens: redesigned sign-in page (brand panel + card), admin dashboard KPI cards (students, teachers, branches, users, permissions;
  tenants for platform admins), quick-link and portal-home tiles, empty lists offer the create action.
- New strings (fa/en/tr/ar): `common.close`, `common.breadcrumb`, `auth.tagline`, `dashboard.kpiTitle|kpiViewAll|kpiPermissions`.
- Tests: `packages/ui/test/contrast.test.ts`, `packages/ui/test/ssr.test.tsx`, `e2e/a11y.spec.ts` (axe-core via
  `@axe-core/playwright`), design-review screenshot config `playwright.shots.config.ts`.

### Added — Milestone 3 institute finance backend (EEMP-014), 2026-11-26
- New module `Finance` (schema `finance`): settings (VAT, late fees, sibling discount, results blocking, online payments), fee types and
  items, invoices with gap-free per-tenant numbers (INV/R/CN, DL-060), discounts/scholarships with local maker-checker approval (DL-063),
  installments, late fees, manual and online payments, allocation and credit balance (DL-061), refunds with approval, credit notes, void.
- Online payments: `IPaymentGateway` with a fake gateway (dev/test) and a Zarinpal v4 adapter (off by default, config only); anonymous
  callback `/api/v1/finance/gateway/{provider}/callback/{paymentId}` bound to the stored authority, verified with the stored amount, idempotent.
- Portal: `/api/v1/portal/me/finance/*` (account, invoices, payments, pay, verify, invoice/receipt PDFs) for the student and linked guardians.
- Reports: outstanding, aging, daily collections by method/branch, `?format=csv` (UTF-8 BOM).
- PDFs: invoice and receipt (fa/en/tr/ar, Jalali for fa) on the shared PDF pipeline.
- Permissions `finance.setup.manage`, `finance.read`, `finance.invoices.manage`, `finance.payments.manage`, `finance.approve`,
  `finance.reports.read`; built-in role `FinanceOfficer`; startup sync adds new system roles/codes to every tenant (DL-064).
  TenantAdmin and BranchManager hold no finance code.
- Cross-module contracts: `ITenantFinanceSettings` (Tenancy), `IEnrollmentDirectory` (Academic), `IPeopleDirectory.GetSiblingIdsAsync`
  (People), `IFinanceStanding`/`IFinanceUsage` (Finance). Academic portal grades/report cards honour `blockResultsOnOverdue` (DL-065).
- 20 new error codes `finance.*` and `finance.pdf.*` labels in fa/en/tr/ar; dev seed adds `finance.officer` and sample finance data.

### Added — Workflow engine backend (EEMP-011), 2026-11-26
- New `Workflow` module (schema `workflow`, own migrations history, 6 tables): tenant-scoped versioned definitions (draft → published →
  retired) of sequential approval steps (permission, optional named assignee, SLA hours, return allowed), instances pinned to a
  version and bound to a subject (`subjectModule/subjectType/subjectId`, optional branch), append-only history, outbox (DL-070..DL-074).
- Endpoints: `/api/v1/workflow/definitions` (list/create/get/update, `POST …/versions`, `PUT …/versions/{n}`, `POST …/versions/{n}/publish`),
  `GET /workflow/inbox`, `GET /workflow/instances?scope=mine|all`, `GET /workflow/instances/{id}` (history + allowed actions),
  `POST /workflow/instances/{id}/approve|reject|return|resubmit|cancel` (`rowVersion`, `comment`, idempotent `requestId`).
- Rules: approver = holds the step permission in the instance branch (tenant-wide for tenant-level instances) and is the named assignee
  when set; maker-checker (requester never decides), distinct approvers per round, rowversion concurrency, SLA due/overdue.
- In-process API for modules: `IWorkflowService` (idempotent start, find, system cancel) and `IWorkflowEventHandler` completion/return
  callbacks delivered at-least-once through the outbox (inline after commit + background retry worker, `Workflow:Outbox:*`).
- Permissions `workflow.definitions.read|manage`, `workflow.instances.read|manage`, `workflow.tasks.approve`; TenantAdmin gets the
  definition permissions only, BranchManager the rest. 14 new error codes translated fa/en/tr/ar. Dev seed: `discount-approval`.
- Tests: unit (state machine, approver policy, backoff), architecture (module boundaries, data classification, tenant-composite FKs,
  permission flags), integration (versioning, maker-checker, distinct approvers, branch/tenant isolation, concurrency, idempotent
  start/retry, outbox retry and exactly-once application by the handler).

### Added — Report-card PDF export (EEMP-010 follow-up), 2026-11-26
- Backend: server-side PDF rendering of report-card versions from the stored snapshot (DL-057, DL-058) — SkiaSharp + HarfBuzzSharp
  (MIT) with an embedded Vazirmatn font (OFL 1.1), HarfBuzz shaping, minimal Unicode bidi, mirrored RTL layout, Persian digits and
  Jalali dates for fa, Arabic-Indic digits for ar, Latin for en/tr; labels from the server catalogs (`reportCard.pdf.*`).
- Endpoints: `GET /api/v1/report-cards/{id}/versions/{versionNo}/pdf` (staff, `reportcards.read`, branch-scoped),
  `GET /api/v1/portal/me/report-cards/{id}/pdf` (student/guardian, published only), `GET /api/v1/report-cards/pdf-archive?classId=`
  (ZIP of a class's published cards, max 200); `?lang=fa-IR|en|tr|ar` or Accept-Language. New error code `academic.archive_too_large`.
- Organization contracts: `IOrganizationSettings.GetInstituteNameAsync`, `IBranchDirectory.GetNameAsync`.
- Frontend: `api.download()` for binary responses, `academic.reportCards.pdf/archive` and `academic.portal.reportCardPdf`; the BFF
  streams PDF/ZIP responses and forwards `Content-Disposition`; "Download PDF" on the admin and portal report-card pages and
  "Download all PDFs (ZIP)" on the admin list (class selected); HTML print kept; fa/en/tr/ar strings; mock backend serves stand-in PDFs/ZIPs.
- CI: backend job installs `poppler-utils` so PDF text assertions run.
- Tests: unit (bidi, locale, rendering, text extraction), integration (permissions, scope, publication, isolation), Vitest, Playwright
  mock and real-API download specs.

### Added — Milestone 2 academic core frontend (EEMP-008..010), 2026-11-25
- `@eemp/api-client`: typed DTOs and endpoints for every M2 route (`api.academic.*`); the mock backend implements the academic
  contract (catalog, schedule conflicts, capacity, idempotent attendance, grade workflow + history, gradebook totals
  `weightedAverage.v1`, versioned report cards, portal views with published data only) and the 15 new permissions.
- `@eemp/i18n`: Jalali ⇄ Gregorian conversion (cross-checked against Intl for 2000–2040), locale date input parsing
  (Persian/Arabic digits), Saturday-first weekdays and month grid.
- `@eemp/app`: `DateField` (Jalali input for `fa`, ISO for en/tr/ar, Saturday-first month picker) and shared academic panels
  (attendance sheet, score entry + workflow + history, assessments, gradebook, timetable, report-card view).
- Admin console: academic years/terms, subjects, courses, grading schemes, classes (schedule, teachers, roster, attendance,
  assessments, gradebook), assessment score entry and workflow, report cards (generate, versions, publish, batch publish, print);
  navigation hidden by permission.
- Client portal: teacher "My classes" (attendance, assessments/score entry, gradebook, roster); student/guardian timetable,
  attendance, published grades and report cards with a linked-student picker for guardians.
- Translations (fa/en/tr/ar) for all new strings and the 38 new error codes; role editor labels for the new permission groups.
- Tests: Vitest (calendar, mock academic flows), Playwright mock (`e2e/academic.spec.ts`), Playwright real API
  (`e2e-real/academic.real.spec.ts`). E2E ports configurable with `EEMP_E2E_ADMIN_PORT` / `EEMP_E2E_WEB_PORT`.

### Added — Milestone 2 academic core backend (EEMP-008..010), 2026-11-24
- New `Academic` module (Domain/Application/Contracts/Infrastructure, schema `academic`, own migrations history, 17 tables):
  academic years, terms, subjects, courses, configurable grading schemes (IR20 0–20 default, PCT, LETTER), classes/sections with weekly
  schedule slots and effective-dated teacher assignments, enrollments, attendance sessions/records, assessments and scores with the
  draft → submitted → approved → published workflow (return, reopen), append-only grade history, versioned report cards.
- Rules: term inside year and non-overlapping; room, teacher and student timetable conflicts; capacity guarded by the class rowversion;
  student/teacher must belong to the class's branch; idempotent bulk attendance (unique class/date/slot); no future attendance;
  submit requires a mark for every enrolled student; published snapshots for the portal; report-card corrections create new versions.
- Portal: teacher "my classes"; student/guardian timetable, attendance, published grades and published report cards.
- 15 permission codes (`catalog.*`, `classes.*`, `enrollments.*`, `attendance.*`, `grades.read|manage|approve|publish`,
  `reportcards.read|manage|publish`); TenantAdmin gets catalog/classes only; BranchManager gets all; teachers act through assignments.
- Contracts: `IPeopleDirectory` (People), `IOrganizationSettings` (Organization), `IAcademicUsage` (Academic). Branch deletion also checks
  classes; student/teacher deletion returns `409 people.in_use` when referenced.
- 38 new localized problem/field codes (fa/en/tr/ar). SQL Server `InitialCreate` migration for `AcademicDbContext`.
- Dev seed: year 1405-1406 with two terms, subjects, courses, classes MATH-7A (North, teacher.one) and PHYS-7S (South), enrollments,
  an attendance sheet, a published quiz and a draft midterm.
- Tests: 54 unit, 62 architecture, 45 integration (SQLite) — all passing.
- Decisions DL-053..DL-056 (OD-007/OD-026/OD-020 accepted assumptions, single Academic module pending confirmation).

### Known limitations (M2)
- No report-card PDF/templates; existing tenants' system roles are not updated with the new permissions; no holidays/timetable exceptions.

### Added — Milestone 1 backend foundation (EEMP-002..007), 2026-11-24
- .NET 10 modular-monolith solution (`backend/EEMP.slnx`, 22 projects): SharedKernel, Infrastructure,
  API host and Tenancy, Identity, Organization, People modules (Domain/Application/Contracts/Infrastructure).
- Tenant context from the JWT `tenant_id` claim only; central EF tenant query filters (fail closed),
  tenant stamping and cross-tenant write rejection on save, tenant-composite foreign keys.
- Authentication: ASP.NET Core Identity password hashing and lockout, 15-minute JWT access tokens,
  rotating hashed refresh tokens with reuse detection, per-request session validation (logout,
  deactivation and tenant suspension take effect immediately), auth endpoint rate limiting.
- Dynamic authorization: code-defined permission catalog, tenant-defined roles, tenant-wide or
  per-branch assignments, no self-grant, immutable system roles, TenantAdmin without people access.
- Organization: central office (one per tenant) and branches with branch-scoped access.
- People: students (with guardian links), guardians, teachers, staff; self-service portal profile and
  guardian's linked students.
- Platform tenant management (create with first admin, update, suspend, activate).
- RFC 7807 problem+json with stable `code`, localized titles (fa default, en, tr, ar), correlation ids,
  Serilog JSON logs, OpenTelemetry tracing, OpenAPI at `/openapi/v1.json`, `/health/live`, `/health/ready`.
- SQL Server `InitialCreate` migrations per module (own history table each); dotnet-ef 10.0.15 local tool;
  `check-migrations.sh`.
- Development seed (platform admin, demo tenant with two branches, sample users and people); passwords from env.
- Tests: 31 unit, 48 architecture, 34 integration (SQLite fallback) — all passing.
- Root `docker-compose.yml` (sqlserver, redis, api), `backend/Dockerfile`, `.github/workflows/backend.yml`.

### Fixed — M1 end-to-end integration with the Next.js frontend, 2026-11-24
- Auth rate limiting is now per browser client behind the BFF: `X-Forwarded-For` is honoured from trusted
  proxies (loopback + `ReverseProxy:KnownProxies/KnownNetworks`, last hop only). Previously every user signing
  in through the web apps shared the BFF's single per-IP bucket (10 logins/minute for the whole site).
- OpenAPI now advertises the contract's camelCase paging parameters (`page`, `pageSize`, `search`).
- Regression tests: `M1IntegrationRegressionTests.cs` (+3 integration tests).

### Fixed — e2e-real-api CI job (failing on PR #2 and on main since its merge), 2026-11-25
- The development seed no longer skips silently: when `Seed__*Password` values are configured but
  `Seed:Enabled` is false (e.g. the API was started outside its output directory, so the content root has no
  `appsettings.Development.json`), it logs a warning naming the content root. Regression tests:
  `DevelopmentSeedTests.cs` (+2 unit tests).
- `docker-compose.yml`: the compose network has a fixed subnet (`EEMP_DOCKER_SUBNET`, default `172.29.0.0/24`)
  and the API trusts `X-Forwarded-For` from it (`ReverseProxy__KnownNetworks__0`), for the web apps' BFF.
- `frontend.yml` `e2e-real-api`: the API is started from its output directory with `Seed__Enabled=true`, and a
  seeded sign-in is checked before the suite; the real-suite global setup fails fast when the seed is missing.

### Known limitations
- Migrations not yet applied against a real SQL Server (no SQL Server/Docker in the build sandbox); CI also
  runs integration tests on SQLite.
- SQL Server row-level security (DL-045 defense-in-depth) not implemented; isolation relies on EF filters,
  composite keys and service checks.
- Tenant provisioning writes to several module contexts without a single transaction.
- Redis is in docker-compose but unused; MFA not implemented.
