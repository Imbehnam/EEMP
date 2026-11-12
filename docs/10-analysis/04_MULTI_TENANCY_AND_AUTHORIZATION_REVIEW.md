# 04 — Multi-Tenancy and Authorization Review

**Status:** PROPOSED. The eight items in `APPROVED_SECURITY_BASELINE.md` are treated as binding and are **not reinterpreted** here. Where a baseline item is ambiguous, the ambiguity is raised as a question, not resolved.

## 1. Baseline preservation check

For each approved item: how the proposal honors it, and what is still missing to test it.

| # | Baseline rule | How preserved in this analysis | Gap to close |
|---|---|---|---|
| 1 | Tenant admins have no implicit access to student/guardian/teacher/consultation/finance records | Permission model grants nothing by role name or hierarchy; sensitive permissions must be explicitly assigned | Self-grant loophole (T-02); which personas count as "tenant administrators" (Q-1); tenant export/offboarding access (T-03) |
| 2 | Sibling institutes/branches isolated unless a specific scope grant | Scope kinds are explicit; no hierarchy inheritance | DL summary says "institute", baseline says "institutes/branches" — confirm both (CF-11) |
| 3 | Supervision/support grants ≤90 days; renewal needs fresh approval | Grant entity M-01 with hard cap enforced in domain and DB check constraint | "Supervision" undefined (CF-12); OD-029 "break-glass" option must not violate the cap or create hidden access |
| 4 | Small cells <5 suppressed where the privacy rule applies | Reporting reads through a suppression layer | Which aggregates are "sensitive" (RPT-N01); secondary suppression |
| 5 | Revocation effective on next authorization decision | Tokens carry no permissions; DB-resolved per request (D-03) | Cache policy must be explicit (T-05) |
| 6 | Support access decisions recorded and auditable | Grant lifecycle and every use audited | Audit viewer scope (AUD-N01) |
| 7 | No secrets/sensitive data in docs, source, logs, prompts | Redaction rules and secret-storage requirement | Provenance of item (CF-13) |
| 8 | Platform support separated from tenant business data | Platform operator has tenant metadata only; data access only via grant | Platform DB roles and RLS bypass accounts (T-06) |

## 2. Tenant resolution and identity scoping

| ID | Sev | Finding | Recommendation |
|---|---|---|---|
| T-01 | S1 | **Tenant definition (OD-002) and global vs tenant-scoped identity (OD-004) are unresolved** but every isolation rule depends on them. The docs imply a global `UserAccount` with `TenantMembership` (IAM-004) and tenant-scoped `Person` (XFR creates a new profile in the destination). | R: global `UserAccount` holding only credentials and minimal contact; tenant-scoped `Person`/profiles; account-enumeration protections (uniform invitation and recovery responses); a tenant never sees another tenant's membership list. Needs approval (OD-002/004). |
| T-07 | S2 | **Tenant selection for multi-membership users** is unspecified (custom domains are future, OD-021). | R: tenant derived from subdomain (`{slug}.<host>`) or from an explicit tenant selection stored server-side in the session and revalidated against active membership on every request; never from a client-supplied ID alone. Platform operator console has no tenant context. |
| T-08 | S2 | **Branch scope is enforced only by application code** (RLS covers tenant). `BranchId` "or a validated scope relation" is vague. | R: branch scope is an authorization-layer rule, tested by the isolation matrix; query filters apply tenant, and branch where the aggregate is branch-owned; document which aggregates are tenant-wide vs branch-owned. |

## 3. Authorization threats and gaps

| ID | Sev | Threat / gap | Recommendation |
|---|---|---|---|
| T-02 | S1 | **Privilege self-escalation.** A tenant admin without data access can assign themselves (or a colluding account) a role that has it, defeating baseline #1. | R: permissions are classified; *sensitive-data permissions* require a second approver, cannot be self-assigned, emit a high-severity audit event, and notify other owners. Role-management permission ≠ data permission. Proposal only; needs approval. |
| T-03 | S1 | **Tenant export/offboarding (SaaS-005) needs access to all records**, contradicting baseline #1 if performed by tenant admins. | Decision required: export executed by system job under a dual-approved, audited export grant (data not viewable by the requester), vs. other. Not resolved here. |
| T-04 | S2 | **Role-assignment ambiguity for "tenant administrator":** Tenant Owner, Central Office Administrator, Institute Admin, Branch Manager. Baseline names only "tenant administrators". Branch managers need operational access to branch records (ORG-004). | Question Q-1: confirm which personas are bound by baseline #1 and which operational roles (registrar, finance) legitimately hold record access by explicit assignment. |
| T-05 | S1 | **Revocation vs caching** (Redis, EF caches, token claims). | R: no authorization decision or grant is cached across requests; if performance requires it, use a per-principal `PermissionVersion` read on every request, incremented on any change, so staleness is impossible. Revocation tests are mandatory. Applies to background jobs: re-check at execution time. |
| T-06 | S1 | **Bypass accounts.** RLS and query filters can be bypassed by `db_owner`, migration, and platform/maintenance identities; EF `IgnoreQueryFilters` is a code-level bypass. | R: runtime role has no bypass; migration role is separate and non-runtime; `IgnoreQueryFilters` forbidden outside an allow-listed, reviewed, analyzer-checked set; every bypass use is audited. No undocumented support access (AGENTS rule). |
| T-09 | S2 | **SQL Server RLS caveats** (doc lists them): `SESSION_CONTEXT` must be set per connection checkout and cleared on return to pool; `@read_only`; job and migration contexts. | R: a pooled-connection test that proves no context leakage; fail-closed (no context ⇒ zero rows); block predicates for writes. |
| T-10 | S2 | **Parent access** depends on guardian authority flags (IAM-N04); divorce/custody restrictions and revoked guardians need immediate effect. | R: relationship check per request using effective-dated links; authority flags per capability; notification fan-out also honors restrictions. |
| T-11 | S2 | **Teacher access** after assignment end or substitution. | R: effective-dated assignments evaluated at request time; substitution is a separate assignment with its own dates. |
| T-12 | S2 | **Counselor case access.** Reassignment and supervisor/quality review need a defined path; this is probably what "supervision grants" covers (to be confirmed, Q-2). | R: case notes in separate store/schema with access log on read. |
| T-13 | S2 | **IDOR via nested IDs** (e.g., `/students/{id}/invoices/{invoiceId}`). | R: every handler loads by `(TenantId, Id)` and then authorizes the loaded resource, never the route parameters alone; return 404 for out-of-scope resources to avoid existence leaks. |
| T-14 | S2 | **Webhook and unauthenticated endpoints** (payments, SMS delivery, video) bypass user auth. | R: signature + replay protection + inbox dedup (D-14); rate limit; no tenant ID taken from payload. |
| T-15 | S2 | **Files and meeting links** (signed URLs). | R: short-lived, single-purpose, tenant-pathed; issue only after authorization; log issuance. |
| T-16 | S2 | **Audit access and tenancy:** audit log contains sensitive metadata and could itself leak. | R: tenant audit scoped by tenant and permission; platform audit separate; audit reads are audited. |

## 4. Proposed authorization decision algorithm (for approval)

A request is allowed only if **all** steps pass; any failure is deny (default deny) and is logged.

1. Authenticate; resolve session; reject revoked or expired sessions.
2. Resolve tenant context from trusted source; verify **active** `TenantMembership`.
3. Check tenant lifecycle/entitlement for the operation (suspended = read-only/export only).
4. Resolve the principal's current `ScopeAssignment`s and permissions (uncached, or version-checked).
5. Match permission `{module}.{resource}.{action}` and a scope covering the target tenant/institute/branch.
6. Load the resource by `(TenantId, Id)`; check resource-level rules: assignment (teacher/counselor), relationship + authority flag (guardian), ownership (student), referral/support grant, sensitivity/consent.
7. Check workflow state and separation of duty (initiator ≠ approver; no self-approval; refund maker-checker).
8. For sensitive-data permissions: confirm explicit assignment, not role-derived; confirm no self-grant lineage.
9. Execute; write audit event (actor, tenant, scope, action, resource, result, correlation ID).

Platform operators: steps 2–6 use platform scope; access to tenant business data additionally requires an active support grant (≤90 days) bound to purpose, tenant, and data categories.

## 5. Isolation test matrix (extends `MULTI_TENANCY_AND_ISOLATION.md`)

Existing matrix retained. Additions needed so every baseline rule has an automated test:

| Test | Baseline |
|---|---|
| Tenant admin with no data permission denied on each protected resource type | 1 |
| Tenant admin cannot self-assign or approve-own a sensitive permission | 1 (T-02) |
| Sibling institute and sibling branch denied; allowed only after an explicit scope grant and denied again after revocation | 2, 5 |
| Support grant creation with duration >90 days rejected; renewal without fresh approval rejected; expired grant denied | 3 |
| Every support grant decision and use produces an audit event | 6 |
| Aggregates with cell count 1–4 suppressed; count 5 shown; secondary-suppression case | 4 |
| Permission removed mid-session denied on the next request; cached/pooled state cannot resurrect it; background job re-checks | 5 |
| No log, audit payload or error response contains test canary secrets/PII | 7 |
| Platform operator without grant denied tenant business data; with grant allowed only within scope/time | 8 |
| Pooled connection reuse across tenants shows no `SESSION_CONTEXT` leakage | RLS |
| Composite FK rejects cross-tenant reference (DB-01) | 2 |

## 6. Questions to the product owner

- **Q-1** Which personas does "tenant administrators" (baseline #1) cover, and which operational roles hold record access only by explicit assignment?
- **Q-2** What exactly is a "supervision grant" (baseline #3, CF-12)? Is it supervisor oversight of counselor/teacher records, central-office oversight of a branch, or the same as a support grant?
- **Q-3** Please confirm baseline items 7–8 (no DL ID in the log) and the extension of DL "sibling institute" to "sibling institutes/branches".
- **Q-4** May tenant admins assign roles at all, and is a second approver acceptable for sensitive permissions (T-02)?
- **Q-5** How is tenant export performed without violating baseline #1 (T-03)?
