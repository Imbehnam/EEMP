# Changelog

All notable changes to this project are documented here. Dates are UTC.

## [Unreleased]

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
