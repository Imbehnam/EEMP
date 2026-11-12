# Agent Instructions — EEMP

## Authority and order of precedence
1. Explicit user instructions in the current task.
2. Approved decisions in `docs/08-planning/DECISION_LOG.md` and approved security baseline.
3. Approved requirements and architecture documents.
4. Unapproved proposals and assumptions (never treat these as binding).

If documents conflict, stop, identify the conflict, and request a decision. Do not silently choose.

## Mandatory workflow
1. Read this file and the relevant product, architecture, domain, security, and task documents.
2. Inspect the repository and report its actual state before editing.
3. Identify the smallest independently verifiable task and its dependencies.
4. Present a short implementation plan, affected files, risks, and acceptance criteria.
5. Implement only the authorized task. Do not expand scope.
6. Build and run relevant tests; report exact commands and results.
7. Update task tracker, decisions, API/domain docs, and changelog as appropriate.
8. Summarize changed files, behavior, tests, known limitations, and next task.
9. Stop and wait for the user's verification before starting the next task when requested.

## Non-negotiable engineering rules
- ASP.NET Core backend is authoritative for business rules, identity, authorization, tenant scope, pricing, payment state, workflow transitions, and audit.
- Never trust tenant, user, role, branch, price, payment status, or permission identifiers supplied by the browser without server-side validation.
- Enforce authorization on every API operation and resource access; hiding UI controls is not security.
- Tenant isolation must be enforced centrally and tested for cross-tenant and cross-branch access.
- Do not grant tenant administrators implicit access to student, guardian, teacher, consultation, or financial records.
- Use explicit DTOs/contracts; do not expose EF entities directly.
- Use UTC for persisted instants; apply tenant/user timezone only for display and scheduling.
- Use decimal for money with explicit currency; never use floating point for financial calculations.
- Make payment callbacks, webhook processing, and scheduled jobs idempotent.
- Use transactional outbox/inbox patterns where reliable event delivery is required.
- Do not store secrets in source code, prompts, logs, or documentation.
- Do not log sensitive student, guardian, payment, or access-token data.
- Do not create hidden superuser access or undocumented support access.
- Avoid premature microservices. Follow the approved modular boundaries.
- Use migrations and reviewed schema changes; never alter production data destructively without an approved plan.
- All user-facing text must be localization-ready; no hard-coded UI strings.
- All time-sensitive reminders must be configurable and deduplicated.
- Do not claim a task is complete unless acceptance criteria and relevant tests pass.

## Definition of Ready
A task is ready only if it has:
- approved requirement / business rule;
- bounded scope and dependencies;
- acceptance criteria;
- security and tenant impact identified;
- API/data impact identified;
- test approach.

## Definition of Done
- Implementation is complete and reviewed.
- Build succeeds.
- Relevant automated tests pass.
- Authorization and tenant-isolation tests are included where applicable.
- Error handling, audit, localization, and observability are addressed.
- Documentation and task tracker are updated.
- No known critical security issue is left unresolved.
- User acceptance is obtained when the task requires it.

## Task execution
- Work one task at a time unless explicitly authorized otherwise.
- Do not skip ahead to dependent tasks.
- Do not fabricate test results or claim tests ran when they did not.
- If tools, SDKs, services, or credentials are unavailable, report the blocker and provide a reproducible next step.
- Never run destructive commands without explicit authorization.
