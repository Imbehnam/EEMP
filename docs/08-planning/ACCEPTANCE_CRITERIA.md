# Global Acceptance Criteria

## Security and tenancy
- Cross-tenant and unauthorized cross-branch access tests pass.
- Tenant admin cannot access protected student/guardian/teacher/consultation/finance records by role alone.
- Parent and teacher relationship/assignment checks are enforced.
- Revoked permission is denied on next authorization check.
- Support access is purpose-bound, time-limited, and audited.

## Academic
- Enrollment, class assignment, attendance, assessment, grade review, and report publication are auditable.
- Published report card is versioned and immutable.
- Corrections create a new version and preserve prior publication history.

## Finance
- Client cannot manipulate amount/currency/status.
- Webhook signature/provider verification and idempotency are tested.
- Partial payment, failure, refund, and reconciliation paths are covered.
- Institute finance and SaaS billing are separate.

## Workflow
- Workflow instance pins definition version.
- Unauthorized or invalid state transition is rejected.
- Every transition is auditable.
- Concurrent commands do not corrupt state.

## Consultation/calendar/notifications
- Concurrent booking cannot double-book a constrained slot.
- Rescheduling cancels/supersedes old reminders.
- Reminder offsets 24h, 12h, 3h, 30m are configurable independently.
- Duplicate notification delivery is prevented by idempotency.
- Timezone and daylight-saving behavior is tested.

## Localization
- All user-facing strings are localized.
- RTL/LTR, date/time/number/currency, validation, generated documents, and SMS templates are verified.
- Locale switching does not corrupt user-entered content.

## Operations
- Backup restore is demonstrated.
- Health checks and alerting work.
- Critical external dependency failures are observable.
- Tenant export/offboarding is tested.
