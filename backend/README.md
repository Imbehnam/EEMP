# EEMP backend

ASP.NET Core (.NET 10 LTS) modular monolith. Milestone 1 covers tracker items EEMP-002..007:
repository/CI/local environment, tenant context and isolation, authentication, dynamic
authorization with branch scopes, organization/central office/branches, and
student/guardian/teacher/staff profiles. The API contract shared with the frontend is
`/api/v1` (see "API conventions" below).

## Layout

```
backend/
  EEMP.slnx                     solution (22 projects)
  global.json                   SDK 10.0.404 (rollForward latestFeature)
  Directory.Build.props         nullable, warnings as errors, analyzers
  Directory.Packages.props      central package versions
  dotnet-tools.json             local tools (dotnet-ef 10.0.15)
  check-migrations.sh           fails if any DbContext has un-migrated model changes
  src/
    EEMP.SharedKernel/          entity markers, errors/codes, validation, paging, permission catalog
    EEMP.Infrastructure/        tenant context, EF conventions + tenant filters, save pipeline (tenant
                                stamping, timestamps, audit), problem+json, localization (fa/en/tr/ar),
                                permission policies, rate limiting, audit store (schema audit)
    EEMP.Api/                   host: Serilog JSON, OpenTelemetry, OpenAPI, health, dev seed
    Modules/<Module>/
      *.Domain                  entities
      *.Application             use cases, DTOs (no web/EF-provider dependencies)
      *.Contracts               the only project other modules may reference
      *.Infrastructure          DbContext (own schema + own migrations history), endpoints
  tests/
    EEMP.UnitTests              validator, domain rules, localization catalog completeness
    EEMP.ArchitectureTests      module/layer reference rules, table classification
    EEMP.IntegrationTests       WebApplicationFactory against the SQLite fallback
```

Modules and schemas: `tenancy` (tenants), `identity` (users, roles, role permissions, scoped
assignments, refresh tokens), `organization` (central office, branches), `people` (students,
guardians, teachers, staff, student-guardian links), plus `audit` (audit events).

## Run locally

Prerequisites: .NET SDK 10.0.404+, and either Docker or a SQL Server instance.

### Full stack with Docker

```bash
cp .env.example .env        # set MSSQL_SA_PASSWORD, JWT_SIGNING_KEY and the SEED_* passwords
docker compose up --build   # sqlserver, redis, api on http://localhost:5080
```

The API applies migrations on startup (Development) and seeds demo data when the three seed
passwords are set.

### API from source against your own SQL Server

```bash
cd backend
export Database__ConnectionString="Server=localhost,1433;Database=eemp;User Id=sa;Password=...;TrustServerCertificate=True"
export Jwt__SigningKey="a-random-string-of-at-least-32-characters"
export Seed__PlatformAdminPassword=... Seed__DemoAdminPassword=... Seed__DemoUserPassword=...
dotnet run --project src/EEMP.Api        # http://localhost:5080, ASPNETCORE_ENVIRONMENT=Development
```

### Without SQL Server (quick look only)

`Database__Provider=Sqlite` with `Database__ConnectionString="Data Source=eemp.db"` creates the
schema from the model (no migrations, rowversion emulated). This is the provider the integration
tests use; it is not a supported runtime target.

Endpoints: OpenAPI `GET /openapi/v1.json`, liveness `GET /health/live`, readiness `GET /health/ready`
(database reachable). CORS allows `http://localhost:3000` and `http://localhost:3001` in Development.

### Development seed

Runs only when `Seed:Enabled=true` (appsettings.Development.json) **and** all three passwords are
supplied via configuration/environment. Nothing secret is committed.

| User | Tenant (`tenantSlug`) | Role / scope | Password |
|---|---|---|---|
| `platform.admin` | `null` | platform administrator | `Seed__PlatformAdminPassword` |
| `demo.admin` | `demo` | TenantAdmin (tenant-wide) | `Seed__DemoAdminPassword` |
| `branch.manager` | `demo` | BranchManager @ NORTH | `Seed__DemoUserPassword` |
| `teacher.one` | `demo` | Teacher @ NORTH, linked teacher profile | `Seed__DemoUserPassword` |
| `guardian.one` | `demo` | Guardian @ NORTH, linked guardian profile | `Seed__DemoUserPassword` |
| `student.one` | `demo` | Student @ NORTH, linked student profile (S0001) | `Seed__DemoUserPassword` |

Demo tenant: branches NORTH and SOUTH, students S0001–S0003, teachers T0001–T0002, one staff
member, one guardian linked to S0001. Passwords need 8+ characters with upper, lower and a digit.

## Build, test, migrations

```bash
cd backend
dotnet build EEMP.slnx
dotnet test EEMP.slnx                     # unit + architecture + integration (SQLite)
./check-migrations.sh                     # no pending model changes in any module

# add a migration for one module (example: people)
dotnet tool restore
dotnet dotnet-ef migrations add <Name> \
  --project src/Modules/People/EEMP.Modules.People.Infrastructure \
  --startup-project src/Modules/People/EEMP.Modules.People.Infrastructure \
  --context PeopleDbContext --output-dir Migrations
```

Each module context has its own `<schema>.__EFMigrationsHistory` table. Design-time factories mean
dotnet-ef never starts the API host. `EEMP_MIGRATIONS_CONNECTION` overrides the design-time
connection string when running `database update` from the CLI.

## Configuration

| Key | Default | Notes |
|---|---|---|
| `Database:Provider` | `SqlServer` | `Sqlite` is the test fallback |
| `Database:ConnectionString` | — | required |
| `Database:ApplyMigrationsOnStartup` | `false` (`true` in Development) | |
| `Jwt:SigningKey` | — | required, 32+ chars, from env/secret store |
| `Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` | 15 / 14 | |
| `RateLimiting:AuthPermitLimit` / `AuthWindowSeconds` | 10 / 60 | per client IP on `/auth/login|refresh|logout` |
| `ReverseProxy:KnownProxies` / `KnownNetworks` | [] / [] | extra trusted proxies (besides loopback) whose `X-Forwarded-For` sets the client IP — e.g. the Next.js BFF containers' network in Docker; without it every browser shares the BFF's rate-limit bucket |
| `Cors:AllowedOrigins` | `[]` (localhost:3000/3001 in Development) | applied in Development only |
| `Seed:*` | disabled | see above |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | unset | enables OTLP trace export when set |

## API conventions (summary of the shared contract)

- Base `/api/v1`, JSON camelCase, enums as camelCase strings (`active`/`inactive`, `male`/`female`/`other`,
  tenant `active`/`suspended`), dates `yyyy-MM-dd`, instants UTC ISO-8601 with `Z`.
- Errors: `application/problem+json` with `code`, `params`, `errors` (field → codes), `traceId`,
  `correlationId`. Clients translate `code`; `title` is a server-localized fallback (`Accept-Language`).
- Paging: `page`, `pageSize` (≤100), `search` → `{ items, page, pageSize, totalCount }`.
  `GET /roles` and `GET /portal/me/students` are plain arrays.
- Concurrency: resources expose `rowVersion` (base64); updates must send it; stale → 409 `concurrency.conflict`.
  DELETE needs no rowVersion.
- `X-Correlation-Id` is accepted (if well-formed) and echoed.
- Auth: `POST /auth/login {tenantSlug,userName,password}` → `{accessToken, accessTokenExpiresAt, refreshToken, user}`;
  `POST /auth/refresh`, `POST /auth/logout`, `GET /auth/me`, `PUT /auth/me/preferences`.

## Security model (M1)

- Tenant comes only from the validated access token's `tenant_id` claim; every request re-validates
  user, tenant status and session (refresh-token family) against the database, so logout,
  deactivation, tenant suspension and refresh-token reuse take effect on the next request.
- Access tokens carry identity/tenant/session only, never permissions; grants are read per request.
- Tenant-owned tables have a central EF query filter (no tenant context ⇒ no rows) and the save
  pipeline stamps/rejects `TenantId`. Cross-tenant references use tenant-composite foreign keys
  (`(TenantId, Id)`).
- Branch scope: assignments are tenant-wide or per branch; out-of-scope records answer 404 (no
  existence leak), out-of-scope writes answer 403 `authorization.forbidden_scope`.
- TenantAdmin has no people permissions (no implicit access to student/guardian/teacher/staff
  records). Users cannot change their own assignments. System roles are immutable.
- Refresh tokens are stored as SHA-256 hashes, rotated on every use; reuse revokes the family.
- Lockout after 5 failed logins (15 min); login failures do not reveal whether the account exists.
- Audit: create/update/delete of audited entities and auth events (identifiers and changed field
  names only, never values or tokens).

## Known limitations

See `docs/08-planning/DECISION_LOG.md` (DL-048..DL-052) and `CHANGELOG.md`. Highlights: SQL Server
migrations were generated but not applied in the build sandbox (no SQL Server/Docker there);
integration tests run on SQLite; SQL Server RLS is not implemented; tenant provisioning spans module
contexts without one transaction; Redis is provisioned but unused; MFA is not implemented.
