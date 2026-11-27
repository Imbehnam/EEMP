# Frontend implementation — Milestone 1

Status: implemented on branch `feature/m1-frontend`, verified in mock mode only (see *Verification*).
Contract: the shared Milestone 1 contract, including the **2026-11-24 contract clarifications**.

## Scope delivered

**Admin console (`apps/admin`, port 3001)**

- Sign-in for tenant users (institute code, username, password) and platform administrators (no institute code).
- Overview: welcome text, the user's permission count and role scopes (role with branch, or "all branches"), and quick links filtered by permission.
- Tenants (platform only): paged search list; create, including the first administrator; edit with rowVersion; suspend and activate behind a confirmation.
- Organization: central office form (read-only without `organization.manage`).
- Branches: paged list; create; edit; delete behind a confirmation (no rowVersion on DELETE).
- Roles and permissions: list from a plain array with client-side search; create and edit with a **permission matrix** (rows are modules, columns are read and manage). Each row shows whether it is branch-scoped or organization-wide. Every permission with `isSensitive` carries a "Sensitive" badge linked to its checkbox through `aria-describedby`. System roles keep their code read-only and cannot be deleted.
- Users: paged list showing the role codes from `assignments[].roleCode`; create; edit; activate and deactivate (you cannot deactivate your own account); **assignments editor** (role plus one branch or "all branches") saved with `PUT /users/{id}/assignments`.
- Students, guardians, teachers and staff: paged lists with branch names; create, edit and delete; kind-specific fields. On student create, `studentNumber` is optional and the server generates one. Students also have a **guardian links editor** (guardian, relationship, single primary contact) using `GET/PUT /students/{id}/guardians`.
- Settings: language and time zone (`PUT /auth/me/preferences`), with a live preview in the chosen calendar.

**Client portal (`apps/web`, port 3000)**

- Sign-in. Platform administrators are refused, and the token issued for them is revoked right away.
- Home: welcome text, roles, cards.
- My profile: `GET /portal/me/profile` → `{ kind, profile }`, read-only.
- My students (guardians only): `GET /portal/me/students`, plain array, with a primary-contact badge.
- Language and time zone settings.

**Shared**

- Problem+json handling: the UI translates `code` and the field error codes and never shows server text; it shows `traceId` as a support reference.
- 409 `concurrency.conflict` offers "Load latest version".
- 403 and 404 load failures render the forbidden and not-found states.
- Dark, light and system theme, stored in the `eemp_theme` cookie so the server renders it with no flash.

## Architecture

| Concern | Implementation |
| --- | --- |
| Routing | App Router under `src/app/[locale]/…` with `localePrefix: 'always'`. Route groups are `(console)` in admin and `(portal)` in web; `[...rest]` sends unknown paths to the localized not-found page. |
| Data | Client screens use `useApiQuery` and `useApiMutation` from `@eemp/app` through the BFF (`/api/bff/*`). Server layouts call `/auth/me` (and, in web, the portal profile kind) with the access-token cookie. |
| Auth | httpOnly cookies, a rotating refresh token with single-flight refresh, a refresh redirect route for server components, and `safeNextPath` to prevent open redirects. |
| CSRF | Double-submit cookie plus header, an Origin check, and SameSite=Lax session cookies. |
| Styling | Emotion (SSR registry with `useServerInsertedHTML`; for RTL the cache uses `stylis-plugin-rtl`; components prefer logical properties) and Tailwind v4 utilities mapped to the same CSS-variable tokens. |
| i18n | next-intl 4 with the plugin in `next.config.ts` and request config from `@eemp/i18n/request`. The time zone comes from the `eemp_tz` cookie. Formatting uses explicit Intl tags: `fa-IR-u-ca-persian-nu-arabext`, `ar-u-ca-gregory-nu-arab`, and others. |
| Security headers | `next.config.ts` sets nosniff, X-Frame-Options DENY, Referrer-Policy, COOP, CORP, Permissions-Policy, and HSTS in production. The CSP is set per request by the proxy. |

### CSP nonce through next-intl (verified)

`proxy.ts` sets `x-nonce` and `content-security-policy` on the **request** headers before it calls
the next-intl middleware. next-intl 4.16.1 builds `new Headers(request.headers)` and passes them to
`NextResponse.next/rewrite({ request: { headers } })`. Checked in
`next-intl/dist/esm/development/middleware/middleware.js`, the `next()` helper. So both Next.js and
our layout see the nonce. Production `next start` (mock mode) gave these results:

- every `<script>` on `/fa/login` carried the nonce (15 of 15);
- the Emotion `<style>` carried it too;
- the response CSP contained `'nonce-…' 'strict-dynamic'`.

The one exception is a pathname that cannot be decoded: next-intl returns a plain `NextResponse.next()`
and Next answers 400 itself.

## Deviations and assumptions (please confirm)

1. **`proxy.ts` instead of `middleware.ts`.** Next.js 16 renamed the file convention. It holds locale routing, the CSP nonce, the CSRF cookie and the session-presence gate.
2. **Extra package `packages/app` (`@eemp/app`).** The contract lists ui, i18n, api-client and config. The shared wiring both apps need (session context, data hooks, login and preferences forms, app frame, server layout helpers) would otherwise be duplicated, so it got its own package.
3. **Cookie naming.**
   - Session cookies are namespaced per app: `eemp_admin_at`, `eemp_admin_rt`, `eemp_admin_csrf`, and `eemp_web_*`. That lets both apps share `localhost` in development.
   - The display cookies are `eemp_tz`, `eemp_theme` and `NEXT_LOCALE` (next-intl's default name).
   - `eemp_tz` is refreshed at login and on preferences save. It can go stale if the user changes their time zone on another device; `/auth/me` remains authoritative.
4. **CSP `style-src 'self' 'unsafe-inline'`.**
   - Scripts are nonce plus `strict-dynamic` (no `unsafe-inline`; `unsafe-eval` only in dev).
   - Emotion `<style>` tags receive the nonce, but React `style={…}` attributes cannot carry one, so `'unsafe-inline'` stays for styles.
   - Follow-up: replace inline style attributes with classes, then drop it.
5. **Locale negotiation.** `fa` is the default, but on a first visit with no `NEXT_LOCALE` cookie, next-intl honours `Accept-Language` (an English browser lands on `/en`). After login the user's saved locale is used. Set `localeDetection: false` if the business wants fa for everyone.
6. **Portal eligibility.**
   - Admin accepts platform admins and tenant users with at least one permission.
   - Web refuses platform admins and returns `auth.portal_not_allowed`, a client-side code that needs a catalog entry (already in all four catalogs).
7. **Guardian `relationship` values.** Settled against the real API (2026-11-24): `father | mother | legalGuardian | grandparent | sibling | other` (backend enum, camelCase). Unknown values display as "Other".
8. **Nested field error keys for tenant creation** are `adminUser.<field>` (for example `adminUser.password`) — confirmed against the real API.
9. **Mock `isSensitive` and `branchScoped` values** are the mock's own choice; the mock's `module` now mirrors the API's coarse groups (`organization`, `identity`, `people`), and the role editor groups rows by the permission code prefix. People modules and `roles.manage` / `users.manage` are sensitive; branches and people modules are branch-scoped. The UI renders whatever the API returns.
10. **Page access gating is UX only.** For example, a tenant user who types `/tenants` sees the page shell, then the forbidden state once the API answers 403.

Real-API integration results and the fixes it produced: `docs/09-quality/M1_INTEGRATION_REPORT.md`.

## Verification (2026-11-24, sandbox, mock mode)

| Check | Command (from `frontend/`) | Result |
| --- | --- | --- |
| Lint | `pnpm lint` | 0 errors, 0 warnings |
| Typecheck | `pnpm typecheck` | all 6 workspaces plus tests: pass |
| Unit and integration | `pnpm test` | 7 files, 61 tests passed |
| Build | `pnpm build` | admin and web compiled (Next 16.4.6, Turbopack) |
| E2E smoke | `pnpm test:e2e` | 7 passed (Chromium) |

The Vitest suites cover:

- **API client:** URL building, problem+json mapping, 401 retry, network errors.
- **BFF:** CSRF accept and reject, cross-origin rejection, login cookies (httpOnly session, `eemp_tz` and `NEXT_LOCALE`, no tokens in the body), portal refusal, refresh rotation and open-redirect protection, proxy 401, bearer forwarding, transparent refresh, blocked raw-token routes and path traversal, CSP nonce, and session gate redirects.
- **Mock backend:** uniform login errors, refresh rotation, deactivation and suspension, every clarification shape, camelCase enum validation, cross-tenant 404, branch scope, no implicit tenant-admin access to people, platform and tenant separation, and rowVersion 409.
- **i18n:** key and ICU-argument parity across fa/en/tr/ar, Jalali date formatting (`2012-04-03` → `۱۵ فروردین ۱۳۹۱`, Nowruz boundary), conversion to the user's time zone, digits, and error-code fallback.
- **UI:** labels and descriptions on fields, table caption and row headers, pagination, and the confirm dialog.

The E2E suite covers:

- the RTL Persian login for a fa browser and English negotiation for an en browser;
- a translated invalid-credentials error;
- admin login, then fa → en → ar switching with `dir` flipping each time;
- sensitive badges in the permission matrix;
- a student profile showing a Jalali birth date;
- a guardian's linked students in English (LTR).

**Not verified:** nothing has run against the real .NET API yet. Run the e2e flows with
`EEMP_API_BASE_URL` once the backend is up, and reconcile with `/openapi/v1.json`.

## Known gaps / follow-ups

- Lookup selects (branches, guardians, roles) load the first 100 records (`pageSize` maximum). They need a typeahead for large tenants.
- No password change or reset, no account linking UI (`userId` on people is display-only), and no guardian → students view in admin. None of these are in the M1 contract.
- No automated axe accessibility scan yet. Components follow WCAG 2.2 AA patterns: labels, `aria-describedby`, focus-visible, 40px targets, and announced errors.
- Inline `style` attributes should become classes so `'unsafe-inline'` can leave `style-src`.
- Arabic could later use the Hijri calendar (OD-020); this only changes `localeMetadata.ar.intlLocale`.

---

# Milestone 2 — Academic core (EEMP-008..010)

Status: implemented on branch `feature/m2-academic-frontend` (2026-11-25), verified in mock mode and against `main`'s real API
(see `docs/09-quality/M2_FRONTEND_INTEGRATION_REPORT.md`). Contract: the shared M2 academic contract plus the M1 clarifications.

## Screens

**Admin console** (navigation section "Academics", each entry hidden unless the permission is held — UX only):

| Route | Permission (UI hint) | What it does |
| --- | --- | --- |
| `/academic/years`, `/new`, `/[id]` | `catalog.read` / `catalog.manage` | Years (status, dates) and, on the year page, its **terms** (add/edit/delete; overlap and outside-year errors) |
| `/academic/subjects`, `/courses`, `/grading-schemes` (+ `/new`, `/[id]`) | `catalog.*` | Catalog CRUD; courses pick a subject and optional scheme; schemes edit kind, range, pass mark, decimals, missing-as-zero, default, bands |
| `/academic/classes`, `/new` | `classes.read` / `classes.manage` | Paged list filtered by term, branch, status; create (term/course/branch immutable afterwards) |
| `/academic/classes/[id]?tab=overview` | `classes.*` | Details (capacity errors), **weekly schedule editor** (Saturday-first, field errors per slot, room/teacher/student conflicts with the conflicting class named), **teacher assignments** (role, dated) |
| `…?tab=roster` | `enrollments.*` | Roster, enroll (class full / not open / branch mismatch / timetable conflict errors), withdraw, show withdrawn |
| `…?tab=attendance` | `attendance.*` | Attendance sheet: date (Jalali for fa), period of that weekday or whole day, per-student status + note, **mark everyone present/absent**, idempotent save, summary |
| `…?tab=assessments`, `…?tab=gradebook` | `grades.*` | Assessments with weights (share of total), create; gradebook with live totals (scheme, band, pass) |
| `/academic/assessments/[id]` | `grades.manage/approve/publish` | Score entry (result, score, comment, optional reason), edit/delete draft, workflow buttons per permission and status (submit, approve, return/reopen with reason, publish), grade history |
| `/academic/report-cards`, `/[id]` | `reportcards.*` | Term/class/status filters, **generate**, **publish all drafts**, list with average/version; card view with versions, publish, print |

**Client portal**: teachers get "My classes" (`/classes`, `/classes/[id]?tab=attendance|assessments|gradebook|roster`,
`/classes/[id]/assessments/[aid]` — submit only, never approve/publish); students and guardians get `/timetable`, `/attendance`,
`/grades` (published only), `/report-cards`, `/report-cards/[id]`. Guardians with several linked students choose one
(`?student=`); the API returns 404 for anyone else.

## Building blocks

- `@eemp/i18n/calendar`: Jalali ⇄ Gregorian (Borkowski 33-year algorithm, cross-checked with Intl for 2000–2040), `parseDateInput` /
  `formatDateInput`, Saturday-first `WEEKDAYS`, `monthGrid`, `todayIso(zone)`.
- `@eemp/app` `DateField`: value is always ISO Gregorian; `fa` types/reads `1405/09/03` (Persian or Latin digits), other locales
  `2026-11-24`; invalid dates are flagged client-side; month picker in the locale calendar, weeks start on Saturday.
- `@eemp/app/academic`: shared panels used by both apps (attendance sheet, score entry/workflow/history, assessments, gradebook,
  timetable, report-card view, enum labels via ICU selects).
- `@eemp/api-client`: `api.academic.*` endpoints and DTOs; the mock backend implements the M2 contract for dev and tests.

## Deviations and assumptions (please confirm)

1. **Saturday-first week in every locale** (en/tr/ar too) for timetables, weekday selects and the date picker, as the tenant is Iranian.
2. **Arabic dates are Gregorian** (ISO input, Arabic-Indic digits accepted), per OD-020; Hijri can be added later.
3. **Times** (`HH:mm`) are wall-clock tenant times; they are shown with locale digits and never converted between zones.
4. **Permission gating in the UI** uses "any assignment holds the code", not the class's branch; the API decides (403/404 are rendered).
5. **Teacher actions** in the portal assume the active assignment (the API checks it); approve/return/reopen/publish are never offered there.
6. **Score range** is also enforced by the browser (`max = maxScore`); the server error `academic.score_out_of_range` is mapped to the row.
7. **Band labels**: the IR20 keys (`excellent`, `good`, `fair`, `pass`, `fail`) are translated; other labels (e.g. `A`–`F`) are shown as stored.
8. **Report cards**: printable HTML view (no PDF, per contract); the list has no branch filter; the default term is the one containing today in the user's zone.
9. **Lookups** (terms, courses, schemes, teachers, students for enrollment) load the first 100 records, like M1.
10. **Mock mode**: admin and web run separate in-process mocks, so cross-app flows (teacher submits → manager publishes → guardian sees) are only covered by the real-API suite.
