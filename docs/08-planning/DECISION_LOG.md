# Decision Log

## Approved carried-forward decisions
- DL-037..DL-042: previously approved EEMP M1 security/tenancy decisions. Preserve their substance as summarized in `docs/04-security/APPROVED_SECURITY_BASELINE.md`: tenant admins do not receive implicit access to student/guardian/teacher/consultation/finance records; sibling institute isolation; supervision grants max 90 days with fresh renewal approval; small-cell suppression under 5 where applicable; revocation effective on next authorization; support access decisions audited.

## Approved decisions (2026-11-12)

### DL-043 — Architecture baseline
- Date: 2026-11-12
- Status: Approved
- Context: Analysis in `docs/10-analysis/` (CF-01, D-01, D-02). Approval given by the product owner in the working session as "Approve as proposed".
- Decision: Modular Monolith + Clean Architecture; API-first ASP.NET Core Web API + Next.js/TypeScript; transactional outbox/inbox for external side effects; one schema and one DbContext per module, cross-module references by ID only, and architecture tests that fail the build on forbidden module/layer references.
- Alternatives: Microservices (rejected as premature); unconstrained shared schema (rejected, boundaries not enforceable).
- Consequences: AGENTS.md "approved modular boundaries" is now accurate. The canonical module list in `docs/10-analysis/03_ARCHITECTURE_AND_DATA_MODEL_REVIEW.md` §3 is **not** yet approved; CF-05 remains open until it is.
- Owner: Product owner.

### DL-044 — Version control
- Date: 2026-11-12
- Status: Approved
- Context: Repository was not under version control (A-01); completion evidence requires commits.
- Decision: Initialize a git repository at the project root. First commit contains the existing documentation only, no application code.
- Alternatives: Owner-initialized repository (declined).
- Consequences: Branch and pull-request policy is still to be defined in EEMP-002. Commits need a configured author identity.
- Owner: Product owner.

### DL-045 — Database engine
- Date: 2026-11-12
- Status: Approved
- Context: OD-024 / D-11. Product owner selected SQL Server as proposed.
- Decision: SQL Server with EF Core is the target engine for EEMP-002/003. Hosting, edition and licensing remain to be confirmed (TECHNOLOGY_STACK selection gates). Double-booking and overlap protection uses slot-row uniqueness / application locks (D-05), because SQL Server has no range-exclusion constraint.
- Alternatives: PostgreSQL (not selected).
- Consequences: SQL Server RLS is an intended defense-in-depth layer but is not relied on until the EEMP-003 harness proves pooled-connection context handling (T-09); if it cannot be proven, record that in this log.
- Owner: Product owner.

### DL-046 — Authorized scope
- Date: 2026-11-12
- Status: Approved
- Decision: Only EEMP-002 (repository, CI, local environment) is authorized. EEMP-003..005 remain blocked on OD-002, OD-004, OD-023, OD-029 and Q-1..Q-5. Stop for user verification after EEMP-002.
- Owner: Product owner.

## Proposed, not yet approved
- Shared database with TenantId and SQL Server RLS defense-in-depth (engine approved in DL-045; tenancy model awaits OD-002/OD-004).
- Provider adapters for payments, SMS, video, storage.
- Translation-key-based i18n with RTL/LTR.
- Durable notification scheduling with outbox/inbox and idempotency.

## New decisions
Add entries using:
- ID:
- Date:
- Status: Proposed / Approved / Rejected / Superseded
- Context:
- Decision:
- Alternatives:
- Consequences:
- Owner:
