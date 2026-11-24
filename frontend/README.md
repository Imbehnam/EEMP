# EEMP frontend

pnpm workspace with two Next.js 16 (App Router) apps and shared packages. Milestone 1 scope:
authentication, platform tenants, organization, branches, roles and permissions, users and scope
assignments, people (students, guardians, teachers, staff) and the client portal, in four locales.

| Path | What it is |
| --- | --- |
| `apps/admin` | Administration console for platform and tenant admins. Port **3001** |
| `apps/web` | Client portal for students, guardians and teachers. Port **3000** |
| `packages/api-client` | Typed `/api/v1` client, problem+json errors, BFF server (httpOnly cookies, CSRF, refresh), `proxy.ts` factory, in-process mock backend |
| `packages/app` | Shared app wiring: providers, session context, data hooks, locale and theme switchers, login and preferences forms, error alerts |
| `packages/i18n` | next-intl routing and request config, catalogs `fa` (default, RTL, Jalali), `en`, `tr`, `ar` (RTL), formatting helpers |
| `packages/ui` | Design tokens, Emotion SSR registry (RTL via stylis), accessible components |
| `packages/config` | Shared tsconfig, ESLint flat config (blocks hard-coded JSX text), Tailwind v4 preset, security headers |

## Requirements

- Node.js ≥ 22
- pnpm 10 (`packageManager` pins `pnpm@10.34.9`; `corepack enable` picks it up)

## Getting started

```bash
cd frontend
pnpm install

# Without the .NET API: in-process mock backend (development only)
pnpm dev:mock                # web on :3000, admin on :3001

# Against the real API
cp .env.example apps/admin/.env.local   # and apps/web/.env.local
# set EEMP_API_BASE_URL=http://localhost:5080/api/v1
pnpm dev
```

Mock mode sign-ins (the password is `eemp-mock`, or `EEMP_MOCK_PASSWORD` if you set it):

| Portal | Institute code | Username | Notes |
| --- | --- | --- | --- |
| admin | (tick "platform administrator") | `platform.admin` | tenants only |
| admin | `demo` | `demo.admin` | TenantAdmin (organization-wide) and BranchManager (North branch) |
| admin | `demo` | `manager.south` | BranchManager for the South branch only |
| admin | `alborz` | `alborz.admin` | second tenant, used to check tenant isolation |
| web | `demo` | `guardian.ali` | guardian with two linked students |
| web | `demo` | `student.reza` | student |
| web | `demo` | `teacher.sara` | teacher |

The mock backend is a test fixture and not a security boundary. A production build refuses to start
with `EEMP_API_MOCK=true` unless `EEMP_ALLOW_MOCK_IN_PRODUCTION=true` is also set, which only the
e2e job does.

## Scripts (run from `frontend/`)

| Command | What it does |
| --- | --- |
| `pnpm lint` | ESLint, zero warnings allowed (includes `i18next/no-literal-string` on JSX text) |
| `pnpm typecheck` | `tsc` for every package and app (apps run `next typegen` first), plus tests and configs |
| `pnpm test` | Vitest unit and integration suites |
| `pnpm build` | `next build` for both apps |
| `pnpm test:e2e` | Playwright smoke tests against the production builds in mock mode (run `pnpm build` first; `pnpm exec playwright install --with-deps chromium` once) |
| `pnpm i18n:check` | Catalog parity and ICU validity only |

## How requests flow

The browser never sees tokens and never calls the API directly.

1. `POST /api/auth/login` (BFF route) exchanges credentials with the API and stores the access and
   refresh tokens in httpOnly, SameSite=Lax cookies `eemp_<app>_at` and `eemp_<app>_rt`. It also sets
   the non-sensitive display cookies `eemp_tz` and `NEXT_LOCALE` from the user's preferences.
2. Client screens call `/api/bff/<path>`. The catch-all route adds `Authorization: Bearer`, refreshes
   once on expiry (single-flight, rotating refresh tokens), checks double-submit CSRF
   (`X-CSRF-Token` header must equal the `eemp_<app>_csrf` cookie, plus the Origin check) on
   mutations, and passes problem+json through unchanged.
3. `src/proxy.ts` (Next 16's replacement for `middleware.ts`) handles next-intl locale routing, a
   per-request CSP nonce, the CSRF cookie, and a coarse session gate. Server layouts re-validate
   with `GET /auth/me`. **The backend authorizes every call**; hiding navigation is UX only.

## Localization

- Locales: `fa` (default, RTL, Solar Hijri calendar, Persian digits), `en`, `tr`, and `ar` (RTL,
  Gregorian calendar, Arabic-Indic digits). Every user-facing string lives in
  `packages/i18n/messages/*.json`. A test enforces identical keys and ICU arguments in all four.
- API errors are shown by translating the problem `code` and the field error codes. Server English
  is never displayed. Unknown codes fall back to a generic message that includes the code.
- Instants are stored in UTC and displayed in the user's IANA time zone. Calendar dates
  (`yyyy-mm-dd`) are formatted without any time-zone shift.
