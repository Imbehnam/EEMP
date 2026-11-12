# 06 — Implementation-Ready Specification: Phase 1 (Foundation and Security)

**Status:** PROPOSED and **CONDITIONAL**. Each work package is specified fully enough to implement, but is marked READY only when the decisions it lists are approved. No application code exists or is authorized. Implementation starts only on explicit approval, one task at a time (AGENTS.md).

Scope: EEMP-002 → EEMP-005, plus two foundation tasks the current tracker omits (audit/observability baseline; support-access grants). See doc 07 for tracker changes.

Engine-specific statements assume SQL Server per the proposed stack; they change if D-11 selects another engine.

---

## 1. EEMP-002 — Repository, CI, local environment

**Decisions needed:** APR-1, APR-2, OD-024 (engine/hosting).
**Scope:** git repo; solution layout; Docker Compose for API, Web, DB, Redis, object storage emulator; CI pipeline; analyzers; secrets handling.

Proposed layout (created only after approval):
```
src/Backend/   Eemp.Api, Eemp.Application, Eemp.Domain, Eemp.Infrastructure, Eemp.Worker(host),
               Modules/<Module>/{Domain,Application,Infrastructure}   (added per phase)
src/Web/       Eemp.Web (Next.js, App Router, TypeScript)
tests/         Domain.Tests, Application.Tests, Integration.Tests (real DB), Security.Tests, ArchTests, e2e/
build/         docker, CI definitions
```
**Acceptance criteria**
1. `build` and `test` run from a clean clone with documented commands; CI runs them on every PR.
2. Architecture tests fail the build on forbidden module/layer references (D-01).
3. Secret scanning and dependency/container scanning run in CI; no secrets in repo (baseline #7).
4. Local environment starts with one command and uses synthetic data only.
5. Central package/version management; analyzer rule forbidding EF entities in API contracts and flagging `IgnoreQueryFilters` (T-06).

**Tests:** CI green on an empty-but-wired solution; deliberate violation (forbidden reference, seeded fake secret) turns CI red.
**Risks:** over-building scaffolding; mitigated by creating module projects only when their phase begins.

---

## 2. EEMP-003 — Tenant context and isolation harness

**Decisions needed:** OD-002, OD-003 (shape only), OD-004, NEW-3, T-07, D-11/T-09.
**Scope:** tenant resolution; request-scoped tenant context; EF filters; RLS (if engine supports it); isolation test harness. No business modules.

### 2.1 Data (proposed)

| Table | Key columns | Notes |
|---|---|---|
| `Tenant` | `Id`, `Slug`(unique), `Status` (Provisioning/Active/GracePeriod/Restricted/Suspended/Exporting/Offboarded), `DefaultLocale`, `DefaultTimeZone`, `RowVersion` | global (no `TenantId`); classified **global** |
| `Institute` | `TenantId`,`Id`, name, status | unique `(TenantId, Id)` |
| `Branch` | `TenantId`,`Id`,`InstituteId`, status | composite FK `(TenantId, InstituteId)` |
| `UserAccount` | `Id`, normalized login, `Status`, credential refs | **global**, minimal data (T-01) |
| `TenantMembership` | `TenantId`,`Id`,`UserAccountId`, `Status`, `ValidFrom/To` | unique `(TenantId, UserAccountId)` |

Every tenant-owned table: `TenantId` leading in PK/unique/indexes; child FKs composite on `(TenantId, ParentId)` (DB-01). Global tables explicitly listed in a classification registry; a test fails if a new table is unclassified.

### 2.2 Behavior
1. Tenant context resolved from trusted source (subdomain or server-held session tenant selection) and checked against an **active** `TenantMembership` on every request; never from body/query/header supplied by the client alone.
2. Fail closed: no tenant context ⇒ tenant-owned queries return zero rows / throw.
3. RLS (if adopted): `SESSION_CONTEXT('TenantId')` set on connection open, cleared on return to pool; block predicates for INSERT/UPDATE/DELETE; runtime role without bypass (T-06, T-09).
4. EF global filters on `TenantId` as defense in depth; `SaveChanges` stamps `TenantId` from context and rejects mismatches.
5. Background jobs: payload carries `TenantId`; handler sets context explicitly and rechecks tenant status.
6. Cache keys and object-storage prefixes include tenant.
7. Unknown/other-tenant resource ⇒ 404 (no existence leak), logged as a security event.

### 2.3 Acceptance criteria
1. Isolation matrix tests (doc 04 §5, tenant rows) pass against a real database with ≥3 tenants and sibling branches.
2. Composite FK rejects a cross-tenant reference.
3. Pooled-connection test shows no context leakage across 1,000 interleaved tenant switches.
4. A table added without `TenantId` or classification fails the registry test.
5. Direct SQL as runtime role cannot read another tenant's rows (if RLS adopted); otherwise documented as not provided and EF-only defense is acknowledged in the Decision Log.

**Out of scope:** authentication, roles, business entities.

---

## 3. EEMP-004 — Authentication and account lifecycle

**Decisions needed:** OD-023, NEW-7, CF-03, D-03/D-04.
**Scope:** registration by invitation, activation, login, logout, session management, recovery, deactivation, MFA for privileged roles, rate limiting.

### 3.1 Requirements covered
IAM-001; IAM-N01..N03 (if approved); part of IAM-007 (MFA for privileged). SSO deferred.

### 3.2 Behavior
1. Invitation tokens: random, single-use, hashed at rest, expiring, bound to tenant and intended role scope; invalid and expired tokens return identical responses.
2. Passwords hashed with a modern adaptive algorithm; policy per IAM-N02; lockout/throttle per account and per IP.
3. Sessions are server-tracked; access tokens contain identity + session ID only (no permissions); session revocation effective on next request; logout and deactivation revoke all sessions.
4. Browser: BFF with HttpOnly/Secure/SameSite cookie, CSRF protection on mutating requests, secure headers.
5. Recovery: single-use time-limited token; uniform response (no account enumeration); notification to the user on credential change.
6. MFA enrolment and enforcement for privileged roles; recovery codes; step-up for sensitive operations (grant approval, refund approval — later).
7. Auth events audited (success, failure, lockout, MFA, recovery, session revoke) without logging secrets or tokens.

### 3.3 API (proposed)
`POST /api/v1/auth/login`, `/logout`, `/refresh` (if tokens), `/invitations/accept`, `/password/forgot`, `/password/reset`, `/mfa/enroll`, `/mfa/verify`; `GET /api/v1/me` (identity, memberships, locale — no permission claims trusted by the client); `DELETE /api/v1/me/sessions/{id}`.

### 3.4 Acceptance criteria
1. Cannot enumerate accounts through login, invitation, or recovery responses (timing and body).
2. Revoked or deactivated user is rejected on the next request with an existing valid token.
3. Invitation/recovery tokens are single-use and expire; replay fails.
4. Privileged role cannot complete login without MFA.
5. Rate-limit and lockout tests pass; CSRF test passes for cookie auth.
6. No token, password, or invitation secret appears in logs (canary test).

---

## 4. EEMP-005 — Dynamic authorization and scope assignments

**Decisions needed:** Q-1..Q-5, NEW-1, NEW-2, CF-12, OD-029.
**Scope:** permission catalog, roles as permission bundles, scoped assignments, authorization service implementing doc 04 §4, support/supervision grants, audit events for permission changes.

### 4.1 Data (proposed)

| Table | Key columns | Notes |
|---|---|---|
| `Permission` | `Key` (`module.resource.action`), `Sensitivity` (Normal/SensitiveData/Privileged) | global catalog, code-defined, versioned |
| `Role` | `TenantId`,`Id`, name, `IsSystem` | tenant-defined bundles |
| `RolePermission` | `(TenantId, RoleId, PermissionKey)` | |
| `RoleAssignment` | `TenantId`,`Id`,`UserAccountId`, `RoleId`, `ScopeType` (Tenant/Institute/Branch/Resource), `ScopeId`, `ValidFrom`, `ValidTo`, `GrantedBy`, `ApprovedBy`, `RevokedAt` | effective-dated |
| `PermissionVersion` | `(TenantId, UserAccountId)`, `Version` | incremented on any change; read per request (T-05) |
| `AccessGrant` | `Id`, `Kind` (Support/Supervision/Referral), `GrantedToUserId`, `TenantId`, scope, purpose, data categories, `ApprovedBy`, `StartsAt`, `ExpiresAt`, `RenewedFromId`, `RevokedAt` | **CHECK `ExpiresAt - StartsAt <= 90 days`**; renewal creates a new row requiring fresh approval (baseline #3) |
| `AccessGrantUse` | grant, actor, time, resource ref, result | append-only (baseline #6) |
| `AuditEvent` | per audit doc fields | INSERT-only role (DB-05) |

### 4.2 Draft permission catalog (Phase 1–3 subset, for approval)
`tenant.config.manage`, `tenant.branch.manage`, `tenant.membership.manage`, `iam.role.manage`, `iam.assignment.manage`, `iam.assignment.grant-sensitive` *(Privileged)*, `audit.tenant.read`, `support.grant.request`, `support.grant.approve` *(Privileged)*, `people.student.read` *(SensitiveData)*, `people.student.manage` *(SensitiveData)*, `people.guardian.read` *(SensitiveData)*, `people.teacher.read` *(SensitiveData)*, `education.class.read`, `education.class.manage`, `academic.grade.read/submit/approve/publish`, `finance.invoice.read` *(SensitiveData)*, `finance.invoice.issue`, `finance.payment.refund.initiate/approve` *(Privileged)*, `consultation.case.read` *(SensitiveData)*, `report.aggregate.read`. Tenant owner/admin system roles contain **no** `SensitiveData` permissions by default (baseline #1).

### 4.3 Behavior
1. Authorization implements doc 04 §4, as one central service (policy handlers per module register rules; endpoints declare required permission and resource type; missing declaration fails an architecture test).
2. Roles never imply data access by name or hierarchy; scope never inherits down to siblings.
3. Granting `SensitiveData` or `Privileged` permissions requires a second approver distinct from the grantor and the grantee (T-02, if approved), is audited at high severity, and notifies other owners.
4. Revocation increments `PermissionVersion`; the next request sees it. No decision cache (T-05).
5. Support grants: requested by platform staff, approved per OD-029, bound to tenant, purpose, data categories; ≤90 days; renewal = new approval; every use writes `AccessGrantUse`; expired grants denied by the same check, not by a cleanup job.
6. Every permission/assignment/grant change emits an `AuditEvent`.

### 4.4 API (proposed)
`/api/v1/roles`, `/role-assignments`, `/permissions` (read-only catalog), `/support-access/requests|approvals|grants`, `/audit/events` (read, scoped). Tenant never supplied in body.

### 4.5 Acceptance criteria
All baseline-linked tests in doc 04 §5 pass, plus:
1. Default tenant-owner role denied on every `SensitiveData` resource stub.
2. Self-grant and grantor=approver attempts rejected.
3. Grant with 91-day window rejected at domain and at the database; renewal without new approver rejected.
4. Revocation denied on the very next request; also for an in-flight background job.
5. Endpoint without declared permission fails the architecture test.
6. Every decision path produces the expected audit event; none contains PII beyond IDs.

---

## 5. Foundation tasks missing from the tracker (proposed additions)

| Proposed ID | Work package | Why |
|---|---|---|
| EEMP-003A | Audit pipeline, structured logging with redaction, correlation IDs, health checks, OpenTelemetry baseline | Phase 1 lists these; no task owns them; DoD requires audit and observability |
| EEMP-005A | Support/supervision grant lifecycle (§4.3 item 5) | Baseline #3/#6/#8 must be enforceable from the first deployment, not Phase 8 |
| EEMP-005B | Localization foundation (message catalogs, stable error codes, locale/dir metadata) | Phase 1 scope; LOC-001 P0 |

## 6. Definition-of-Ready check for the Phase 1 tasks (today)

| Task | Approved requirement | Bounded scope | Acceptance | Security/tenant impact | API/data impact | Test approach | **Ready?** |
|---|---|---|---|---|---|---|---|
| EEMP-002 | partly (IAM/OPS) | yes (above) | yes | yes | n/a | yes | **No** — APR-1, APR-2, OD-024 |
| EEMP-003 | requirements are "Proposed" | yes | yes | yes | yes | yes | **No** — OD-002, OD-004, NEW-3 |
| EEMP-004 | "Proposed" | yes | yes | yes | yes | yes | **No** — OD-023, NEW-7, CF-03 |
| EEMP-005 | "Proposed" | yes | yes | yes | yes | yes | **No** — Q-1..5, NEW-1, NEW-2, CF-12, OD-029 |

Nothing is marked complete or ready here. The tracker is unchanged.
