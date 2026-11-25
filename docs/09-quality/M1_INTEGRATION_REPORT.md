# M1 integration report — Next.js apps against the real .NET API

Date: 2026-11-24 (sandbox run). Frontend `feature/m1-frontend` (PR #2), backend `feature/m1-backend` (PR #1).
Scope: Milestone 1 (EEMP-002..007) end to end, with no mock backend in the path.

## How to run it

```bash
# 1. API on SQLite with the Development seed. Run it FROM its output directory: the content root is the
#    working directory, and Seed:Enabled=true lives in appsettings.Development.json (or set Seed__Enabled=true).
cd backend/src/EEMP.Api/bin/Debug/net10.0      # after `dotnet build`
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5080 \
Database__Provider=Sqlite Database__ConnectionString="Data Source=/tmp/eemp-e2e.db" \
Jwt__SigningKey=<32+ byte dev key> Jwt__AccessTokenMinutes=1 RateLimiting__AuthPermitLimit=1000 \
Seed__Enabled=true Seed__PlatformAdminPassword=Platform123 Seed__DemoAdminPassword=DemoAdmin123 Seed__DemoUserPassword=DemoUser123 \
dotnet EEMP.Api.dll

# 2. Frontend (from frontend/)
pnpm install && pnpm build
pnpm test:e2e:real          # starts admin :3001 and web :3000 with EEMP_API_MOCK=false,
                            # EEMP_API_BASE_URL=http://127.0.0.1:5080/api/v1, EEMP_COOKIE_SECURE=false
```

- `Jwt__AccessTokenMinutes=1` lets the refresh spec see a real expiry in about 30 seconds. The access cookie lives `expiresAt - 30s`. With longer tokens that spec skips itself.
- `RateLimiting__AuthPermitLimit=1000` is needed because the whole suite signs in from one address.
- Seed passwords can be overridden with `EEMP_E2E_PLATFORM_PASSWORD`, `EEMP_E2E_DEMO_ADMIN_PASSWORD` and `EEMP_E2E_DEMO_USER_PASSWORD`. The API URL can be overridden with `EEMP_E2E_API`.
- The suite's global setup now fails fast if `demo/student.one` cannot sign in (API up but seed missing), instead of letting every test time out at sign-in.
- CI runs the same flow in the `e2e-real-api` job of `.github/workflows/frontend.yml`. It uses the backend from the same checkout once it exists, and checks out `feature/m1-backend` until then.

The suite lives in `frontend/e2e-real/` and has its own Playwright config. Files must match `*.real.spec.ts`, so the default `pnpm test:e2e` (mock, `./e2e`) does not pick it up.

## Coverage (25 tests)

| Spec | What it proves against the real API |
| --- | --- |
| `platform.real.spec.ts` | platform admin: tenant list, create (with first admin), suspend (tenant admin then blocked with translated `tenant.suspended`), activate (sign-in works again), search |
| `tenant-admin.real.spec.ts` | demo.admin: organization edit (persisted, `fa-IR` shown as فارسی); branch create/edit/delete; duplicate code shows `branch.code_taken`, not a concurrency warning; role create with permissions (immutable code after create); user create (locale stored as `fa-IR`) and branch-scoped assignment, verified with `/auth/me`; tenant admin gets the forbidden state and a 403 on platform screens |
| `people.real.spec.ts` | branch.manager: guardian and student CRUD (server-generated student number), guardian link (relationship `mother`) saved and reloaded; editing a portal-linked student keeps `userId`; stale `rowVersion` gives the translated conflict and "Load latest version"; cross-branch student gives 404 in the API and the not-found state in the UI, only the own branch is listed; tenant admin without people permissions gets the forbidden state |
| `portal.real.spec.ts` | student.one sees own profile (fa/RTL, Jalali `۱ فروردین ۱۳۹۱`); guardian.one sees the linked student summary (number, relationship, primary badge) and own profile; teacher.one sees subjects; an account with no person record gets `portal.profile_not_found` translated |
| `locale.real.spec.ts` | fa/en/tr/ar: wrong password gives the exact catalog text for `auth.invalid_credentials` with the right `dir`; `branch.code_taken` translated in each locale; switcher fa→en→tr→ar; portal preferences persist `tr`, then `fa-IR`, through `/auth/me` |
| `session.real.spec.ts` | access cookie expiry, then SSR refresh through `/api/auth/refresh` with a rotated refresh token; an invalid access cookie on a BFF call gets 401 upstream, one refresh, then the retry succeeds and rotates again; the old refresh token is refused (401); logout revokes the refresh token server-side, clears cookies, and protected pages then redirect to login |

## API types vs OpenAPI and real responses: mismatches found

OpenAPI (`/openapi/v1.json`) documents request bodies and query parameters but **no response schemas**. Response shapes were therefore checked against live responses.

| # | Mismatch | Effect before the fix | Fixed on |
| --- | --- | --- | --- |
| 1 | Locale tags: API uses `fa-IR`; the UI sent `fa` for tenant default/supported locales, organization default, user locale and preferences | tenant create (default form values), user create/edit with Persian, preferences→Persian and organization save after picking Persian all failed with 400 `validation.unsupported_locale`; language selects showed no match for `fa-IR`; tenant list showed the raw `fa-IR` | frontend (`toApiLocale` / `toSupportedLocale` at the boundary) |
| 2 | `GET /permissions` `module` is a group (`people`, `identity`, `organization`, `audit`), not a resource | role editor matrix kept one row per group, so guardians, teachers and staff permissions (and users vs roles, branches vs organization) could not be granted | frontend (group by code prefix; `audit` label added) |
| 3 | Teacher `subjects` is a string; the frontend typed it as `string[]` | portal teacher profile crashed (`.join` on a string); admin teacher save sent an array, which the API cannot bind | frontend |
| 4 | `GET /portal/me/students` returns summaries `{id, branchId, firstName, lastName, studentNumber, relationship, isPrimary}`; the frontend expected full `Student[]` | guardian "My students" page crashed (`student.guardians.find`) | frontend (new `PortalStudent` type and summary card) |
| 5 | Person `PUT` is a full replacement including `userId`; the admin form did not send it | editing any portal-linked student, guardian or teacher silently unlinked their portal account | frontend (sends the current `userId`) |
| 6 | Any HTTP 409 was treated as a concurrency conflict | duplicate branch/role/user/tenant codes and in-use deletes showed "Someone else changed this record" | frontend (`isConflict` only for `concurrency.conflict`; `resource.duplicate` mapped to its `params.field`) |
| 7 | ~20 backend error codes had no UI translation (`authorization.forbidden`, `authorization.forbidden_scope`, `branch.code_taken`, `role.code_taken`, `user.username_taken`, `tenant.slug_taken`, `resource.duplicate`, `rate_limit.exceeded`, `portal.profile_not_found`, `people.*`, `identity.*`, `auth.account_inactive`, `auth.session_revoked`, `auth.refresh_token_reused`, `validation.invalid_value`, `validation.invalid_timezone`, `validation.password_too_weak`) | generic "Something went wrong (code)" | frontend (all four catalogs) |
| 8 | Guardian relationship enum has `grandparent` and `sibling`; the UI offered 4 values | seeded or API-created links rendered as "Other" and could not be chosen | frontend |
| 9 | `PUT /roles/{id}` takes no `code`; the edit form let custom-role codes be edited | code edits were silently dropped | frontend (code read-only after create; update sends name + permissions) |
| 10 | `GET /students/{id}/guardians` items also carry `firstName`, `lastName`, `phone`, `branchId` | none (superset), but the frontend sent them back on PUT | frontend (`StudentGuardianDetail` type; PUT strips to link fields) |
| 11 | Auth rate limit is per client IP, but all browsers reach the API through the BFF | site-wide 10 sign-ins per minute (the suite hit it after about 10 logins) | **backend** (trusted `X-Forwarded-For`) + frontend (BFF forwards `X-Forwarded-For`) |
| 12 | OpenAPI advertised `Page`/`PageSize`/`Search`; the contract says camelCase | none at runtime (binding is case-insensitive); generated clients would be wrong | **backend** |
| 13 | User DTO carries `createdAt` (not in the contract list) | none | frontend type (optional field) |

The mock backend was aligned with the real shapes (portal students summary, guardian link details, subjects string, `fa-IR` locales, `authorization.forbidden` / `portal.profile_not_found` codes, permission module groups) so the mock e2e and Vitest suites keep exercising the same contract.

## Fixes per branch

- **Backend `feature/m1-backend`**, commit `1221108`, "fix(api): per-client auth rate limit behind the web BFF; camelCase paging in OpenAPI":
  - `UseForwardedHeaders` (X-Forwarded-For, ForwardLimit 1, loopback plus `ReverseProxy:KnownProxies/KnownNetworks`).
  - `PagingQuery` names pinned to camelCase.
  - Regression tests in `tests/EEMP.IntegrationTests/M1IntegrationRegressionTests.cs`: per-client limit behind a loopback BFF, OpenAPI camelCase paging, camelCase binding.
- **Frontend `feature/m1-frontend`**: the items marked frontend in the table above, the `e2e-real/` suite, the `e2e-real-api` CI job, and this report. Regression unit tests are in `packages/api-client/test/integration-regressions.test.ts`.

## Results (sandbox, 2026-11-24)

| Check | Command | Result |
| --- | --- | --- |
| Backend tests | `cd backend && dotnet test EEMP.slnx` | 116 passed: 31 unit, 48 architecture, 37 integration (was 34) |
| Frontend lint | `pnpm lint` | 0 errors, 0 warnings |
| Frontend typecheck | `pnpm typecheck` | pass (all workspaces, plus `e2e-real`) |
| Frontend unit | `pnpm test` | 8 files, 65 tests passed (was 61) |
| Frontend build | `pnpm build` | admin and web compiled |
| Mock e2e | `pnpm test:e2e` | 7 passed |
| Real-API e2e | `pnpm test:e2e:real` | **25 passed** (1.3 min) |

## CI fix: `e2e-real-api` job (2026-11-25)

**Symptom.** On PR #2 (run 45473198875, job 138726397020) 14 of 19 executed tests failed (6 did not run): browser sign-ins timed out waiting to leave `/login`, and direct `POST /auth/login` for `demo/student.one` returned 401. The 5 that passed were the wrong-password and not-linked checks, which do not need seeded users. The same suite passed 25/25 in the sandbox.

**Root cause.** The job started the API with `dotnet "$RUNNER_TEMP/api/EEMP.Api.dll"` from the checkout root. ASP.NET Core takes the content root from the working directory (the log shows `Content root path: /home/runner/work/EEMP/EEMP`), so neither `appsettings.json` nor `appsettings.Development.json` was loaded. `Seed:Enabled=true` exists only in `appsettings.Development.json`, so `Seed:Enabled` was false and the development seed returned without creating any user and without logging anything; the `Seed__*Password` variables were read but unused. Environment (`Development`), SQLite path and password policy were all fine. Reproduced locally with the identical build (`-c Release -o <dir>`) and env: started from the repo root, student.one sign-in → 401; started from the output directory, or with `Seed__Enabled=true` → 200.

**Fix.**
- Workflow (`.github/workflows/frontend.yml`): start the API from `$RUNNER_TEMP/api`, set `Seed__Enabled: 'true'` explicitly, and check a seeded sign-in (HTTP 200) right after the health check, printing the seed/content-root log lines and failing the step otherwise.
- `e2e-real/global-setup.ts`: fail fast with a clear message when the seeded user cannot sign in.
- Backend `feature/m1-backend` `d23471b`: the seed now logs a warning naming the content root when seed passwords are configured but `Seed:Enabled` is false (regression tests `DevelopmentSeedTests.cs`, +2 unit); compose network `KnownNetworks`. Merged into `feature/m2-academic-backend` (`610fd82`).

**Local verification (CI-equivalent).** API built `dotnet build … -c Release -o /tmp/api-ci`, started from that directory with the workflow's env on a fresh SQLite file; `pnpm build`; `pnpm test:e2e:real` → **25 passed (1.5 min)**. Backend `dotnet test EEMP.slnx -c Release`: m1 118 passed (33 unit, 48 architecture, 37 integration); m2 166 passed (56 unit, 62 architecture, 48 integration). Frontend `pnpm lint` and `pnpm typecheck` pass.

## Remaining gaps

- OpenAPI has no response schemas (minimal APIs return untyped `IResult`), so response types cannot be generated or diffed automatically yet. Adding `.Produces<T>()` or typed results would close this.
- Behind Docker or another proxy, `ReverseProxy:KnownNetworks` must cover the web containers' network, otherwise forwarded addresses are ignored and the BFF's address is used again. `docker-compose.yml` now pins the compose network subnet (`EEMP_DOCKER_SUBNET`, default `172.29.0.0/24`) and trusts it; compose has no web containers yet, so this is untested with a containerised BFF (no Docker in the sandbox, `docker compose config` not run).
- Person `PUT` unlinks the portal account when `userId` is omitted. That is safe in the UI now, but a PATCH-like rule ("omitted = unchanged") on the API would be more forgiving for other clients. It needs a business decision.
- The real suite runs on SQLite. SQL Server-specific behaviour (rowversion and collation search) is not covered end to end.
- The real suite shares one database and creates uniquely named records. It does not clean up, and seed records it edits (Ali Karimi's last name, Sara Ahmadi's phone, the organization phone) change on each run.
