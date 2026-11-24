# Changelog

All notable changes to this project are documented here. Dates are UTC.

## [Unreleased]

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

### Known limitations
- Migrations not yet applied against a real SQL Server (no SQL Server/Docker in the build sandbox); CI also
  runs integration tests on SQLite.
- SQL Server row-level security (DL-045 defense-in-depth) not implemented; isolation relies on EF filters,
  composite keys and service checks.
- Tenant provisioning writes to several module contexts without a single transaction.
- Redis is in docker-compose but unused; MFA not implemented.
