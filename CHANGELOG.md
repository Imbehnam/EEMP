# Changelog

All notable changes to this project are documented here. Dates are UTC.

## [Unreleased]

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

### Fixed — e2e-real-api CI job (frontend PR #2), 2026-11-25
- The development seed no longer skips silently: when `Seed__*Password` values are configured but
  `Seed:Enabled` is false (e.g. the API was started outside its output directory, so the content root has no
  `appsettings.Development.json`), it logs a warning naming the content root. Regression tests:
  `DevelopmentSeedTests.cs` (+2 unit tests).
- `docker-compose.yml`: the compose network has a fixed subnet (`EEMP_DOCKER_SUBNET`, default `172.29.0.0/24`)
  and the API trusts `X-Forwarded-For` from it (`ReverseProxy__KnownNetworks__0`), for the web apps' BFF.

### Known limitations
- Migrations not yet applied against a real SQL Server (no SQL Server/Docker in the build sandbox); CI also
  runs integration tests on SQLite.
- SQL Server row-level security (DL-045 defense-in-depth) not implemented; isolation relies on EF filters,
  composite keys and service checks.
- Tenant provisioning writes to several module contexts without a single transaction.
- Redis is in docker-compose but unused; MFA not implemented.
