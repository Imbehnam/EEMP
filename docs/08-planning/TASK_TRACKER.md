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
| EEMP-008 | Academic catalog and class operations | NOT_STARTED | EEMP-007 |
| EEMP-009 | Enrollment and attendance | NOT_STARTED | EEMP-008 |
| EEMP-010 | Assessment, grades, report cards | NOT_STARTED | EEMP-009 |
| EEMP-011 | Workflow engine | NOT_STARTED | EEMP-005 |
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
