# M2 frontend — integration report (2026-11-25)

Branch `feature/m2-academic-frontend` (merged with `main` after PRs #1–#3, and with `fix/e2e-real-api-seed` / PR #4 so CI's
e2e-real-api job starts the API with the dev seed). Backend under test: `main`'s `backend/` (M1 + M2 academic module).

## Setup used

```bash
# API: built from this checkout, run from its output directory with the Development seed (SQLite, 1-minute tokens)
dotnet build backend/src/EEMP.Api/EEMP.Api.csproj -c Release -o /tmp/m2fe-api
cd /tmp/m2fe-api && ASPNETCORE_ENVIRONMENT=Development Database__Provider=Sqlite \
  Database__ConnectionString="Data Source=/tmp/m2fe.db" Jwt__SigningKey=<dev key ≥32 chars> Jwt__AccessTokenMinutes=1 \
  Seed__Enabled=true Seed__PlatformAdminPassword=Platform123 Seed__DemoAdminPassword=DemoAdmin123 Seed__DemoUserPassword=DemoUser123 \
  RateLimiting__AuthPermitLimit=1000 ASPNETCORE_URLS=http://127.0.0.1:5091 dotnet EEMP.Api.dll
# Frontend (frontend/): build, then the real-API suite (Playwright starts both apps in real mode)
pnpm build
EEMP_E2E_API=http://127.0.0.1:5091 EEMP_E2E_ADMIN_PORT=3011 EEMP_E2E_WEB_PORT=3010 pnpm test:e2e:real
```

## Results

| Suite | Result |
| --- | --- |
| `pnpm lint` / `pnpm typecheck` | pass |
| `pnpm test` (Vitest) | 10 files, 79 tests passed |
| `pnpm build` | admin and web compiled |
| `pnpm test:e2e` (mock) | 14/14 passed (7 new academic) |
| `pnpm test:e2e:real` (real API, fresh DB) | 30/30 passed (5 new academic); see note on the one flaky M1 spec |

New real-API specs (`e2e-real/academic.real.spec.ts`):

1. teacher.one marks attendance for MATH-7A in the portal (bulk "everyone present", one student late), saves twice; the API shows one session with 1 present / 1 late.
2. Grade entry → publish: teacher.one creates an assessment, enters scores, submits (no approve button); guardian.one does not see it (nor the seeded draft Midterm); branch.manager approves and publishes in the console; the guardian then sees `19 / 20` and still not the Midterm.
3. Scope: guardian.one gets 404 for an unlinked student's grades; demo.admin (TenantAdmin) gets 403 on a gradebook and sees no assessment/report-card navigation.
4. fa: a term entered as `1405/08/01`–`۱۴۰۵/۰۸/۱۰` is sent as `2026-10-23` and the overlap is shown with the Persian `academic.term_overlap` text; existing terms list Jalali dates.
5. Report cards: branch.manager generates and publishes Term 1; guardian.one opens the published card.

## Findings

- **No backend bugs found**: every academic endpoint matched the contract shapes the frontend was built from (DTO records on `main`).
- Frontend fixes found by the runs: constants imported by server pages from `'use client'` modules (class tabs) became client
  references → moved to plain modules; the attendance "saved" notice was cleared by the reload it triggered; score inputs were
  disabled for students without a result yet.
- M1 spec `people.real.spec.ts › cross-branch access…` failed once with `net::ERR_ABORTED` (an in-flight request hit 401 after the
  cookies were cleared and redirected to login); hardened by leaving the app (`about:blank`) before clearing cookies.

## Gaps

- Mock e2e cannot cover cross-app flows (separate in-process mocks per app); covered by the real-API suite.
- No axe scan of the new screens; no PDF report cards; lookups limited to 100 records.
