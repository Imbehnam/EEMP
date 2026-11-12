# Test Strategy

## Layers
- Domain unit tests: invariants, calculations, state transitions.
- Application tests: use cases, authorization, validation.
- Infrastructure integration tests: SQL Server, RLS, EF mappings, Redis, outbox, provider adapters.
- API contract tests: OpenAPI compatibility, errors, pagination, auth.
- Frontend component tests: forms, locale, RTL/LTR, accessibility.
- E2E tests: critical end-user journeys with Playwright.
- Security tests: IDOR, tenant escape, privilege escalation, CSRF/XSS, webhook forgery, upload abuse.
- Performance tests: tenant isolation under load, report generation, calendar queries, reminders.
- Resilience tests: provider outage, retry, duplicate webhook, queue backlog, database failover.

## Mandatory suites
1. Tenant/branch isolation.
2. Parent/guardian access.
3. Teacher assignment access.
4. Grade publication and correction.
5. Invoice/payment/refund/reconciliation.
6. Workflow concurrency and versioning.
7. Appointment booking concurrency.
8. Reminder scheduling, deduplication, rescheduling, timezone/DST.
9. Referral scope/consent/expiry/revocation.
10. Localization and generated documents.

## Test data
Use synthetic data only. Include multiple tenants, sibling branches, guardians with multiple children, shared teachers, revoked grants, and different locales/timezones.
