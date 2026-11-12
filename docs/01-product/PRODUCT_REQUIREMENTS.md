# Product Requirements

Requirement IDs are stable references. Priority: P0 launch-critical, P1 important, P2 later. Status: Proposed until approved.

## Organization and institute management
- ORG-001 [P0] Platform operator can provision, suspend, reactivate, and offboard tenant accounts.
- ORG-002 [P0] Tenant can define a central office and one or more institutes/branches.
- ORG-003 [P0] Central office can manage permitted shared settings and branch configuration.
- ORG-004 [P0] Branch managers can access only assigned branches and authorized records.
- ORG-005 [P1] Support institute hierarchy, branch transfers, closure, and archival with history.
- ORG-006 [P1] Configure institute identity, branding, contact information, academic calendar, locale, currency, and timezone.

## Identity, people, and access
- IAM-001 [P0] Manage users, invitations, activation, deactivation, and account recovery.
- IAM-002 [P0] Support student, parent/guardian, teacher, staff, counselor, branch manager, institute admin, and platform operator personas.
- IAM-003 [P0] Support dynamic roles, permissions, scoped assignments, and permission policies.
- IAM-004 [P0] Support one user with multiple relationships/roles without duplicating identity.
- IAM-005 [P0] Parent/guardian relationship and authority are explicit per student.
- IAM-006 [P0] Record permission changes and privileged actions in audit logs.
- IAM-007 [P1] Support MFA, SSO, and configurable session policies.
- IAM-008 [P0] Apply server-side authorization at endpoint, operation, tenant, branch, and resource level.

## Academic operations
- EDU-001 [P0] Define academic years, terms/semesters, subjects, courses, classes, class groups, and schedules.
- EDU-002 [P0] Assign teachers to subjects, classes, branches, and date ranges.
- EDU-003 [P0] Enroll students with lifecycle status and effective dates.
- EDU-004 [P0] Manage class capacity, attendance, timetable conflicts, and class status.
- EDU-005 [P0] Support assessments, grading schemes, score entry, review, approval, and publication.
- EDU-006 [P0] Generate versioned report cards with controlled visibility and printable output.
- EDU-007 [P1] Support grading schemes per course/term/institute and localization of labels.
- EDU-008 [P1] Preserve historical academic records when students or teachers move.

## Parent and student experience
- PORT-001 [P0] Parent can view only students for whom an active authorized relationship exists.
- PORT-002 [P0] Student can view own schedule, grades, invoices, learning content, and requests as permitted.
- PORT-003 [P1] Support multiple guardians, emergency contacts, custody/communication restrictions, and consent records.
- PORT-004 [P1] Provide student and teacher digital identity cards with configurable fields, expiry, status, and revocation.

## Workflow and requests
- WFL-001 [P0] Provide configurable request types, forms, states, transitions, roles, SLA, and notifications.
- WFL-002 [P0] Support submission, assignment, review, approval, rejection, return-for-correction, cancellation, and escalation.
- WFL-003 [P0] Maintain immutable request history, comments, attachments, and actor/time audit.
- WFL-004 [P0] Workflow definitions are versioned; in-flight requests retain their definition version.
- WFL-005 [P1] Support conditional routing, parallel approvals, delegation, and SLA calendars.
- WFL-006 [P1] Support process templates for academic planning, counseling, transfer, support, refunds, and enrollment.

## Consultation and support
- CNS-001 [P0] Define counselor/support staff availability, services, duration, capacity, and booking rules.
- CNS-002 [P0] Allow eligible users to request, reschedule, cancel, and attend consultation/support appointments.
- CNS-003 [P0] Prevent double booking with concurrency-safe reservation.
- CNS-004 [P0] Support online/offline appointment modes and secure meeting links.
- CNS-005 [P1] Maintain private case notes with strict access and retention policies.
- CNS-006 [P1] Configure booking windows, cancellation policy, buffers, and no-show handling.

## Academic planning
- PLN-001 [P0] Student/parent can submit an academic planning request.
- PLN-002 [P0] Planning request follows a configurable workflow with required information and attachments.
- PLN-003 [P0] Charge and collect a configurable planning fee before or after service according to policy.
- PLN-004 [P0] Assign planner, schedule sessions, create plan versions, and track review/approval.
- PLN-005 [P1] Support plan goals, milestones, tasks, follow-ups, and progress reports.
- PLN-006 [P1] Restrict access to sensitive planning notes and disclose only approved plan content.

## Finance and payments
- FIN-001 [P0] Configure fee catalog, tuition rules, discounts, installments, penalties, scholarships, and due dates.
- FIN-002 [P0] Generate invoices and credit/debit adjustments with sequential numbering policy.
- FIN-003 [P0] Support online payment through provider adapters.
- FIN-004 [P0] Verify payment status server-side using signed callbacks/webhooks or provider verification.
- FIN-005 [P0] Make payment initiation, callback processing, refund, and reconciliation idempotent.
- FIN-006 [P0] Support partial payments, overpayments, failed payments, refunds, chargebacks, and reconciliation.
- FIN-007 [P0] Keep immutable financial ledger entries; never edit a settled transaction to rewrite history.
- FIN-008 [P1] Support multi-currency and localized tax/receipt rules where required.
- FIN-009 [P0] Separate institute receivables from SaaS subscription billing.

## Recorded courses and learning
- LRN-001 [P1] Manage recorded courses, modules, lessons, media, captions, attachments, and publication.
- LRN-002 [P1] Control enrollment, access windows, prerequisites, and entitlements.
- LRN-003 [P1] Track progress and completion without exposing protected media URLs.
- LRN-004 [P1] Support subtitles/captions and localized metadata.
- LRN-005 [P2] Add quizzes, certificates, learning paths, and content analytics.

## Online sessions and calendar
- CAL-001 [P0] Provide calendar views for classes, consultations, support appointments, and online sessions.
- CAL-002 [P0] Store timezone-aware event instants and recurring schedules safely.
- CAL-003 [P0] Prevent schedule conflicts according to resource/participant policy.
- CAL-004 [P0] Integrate online meeting provider through an adapter.
- CAL-005 [P1] Support calendar export/subscription (iCalendar) and external calendar integration.
- CAL-006 [P1] Provide reminders and event change/cancellation notifications.

## Notifications
- NOT-001 [P0] Configure SMS reminders at 1 day, 12 hours, 3 hours, and 30 minutes before an event.
- NOT-002 [P0] Each reminder offset is independently configurable, enableable, and localizable.
- NOT-003 [P0] Support tenant, event-type, and recipient preference policy with precedence rules.
- NOT-004 [P0] Prevent duplicate sends; handle retries, provider errors, opt-outs, and delivery reports.
- NOT-005 [P1] Support SMS, email, in-app, and push channels through adapters.
- NOT-006 [P0] Maintain delivery audit without logging message secrets or sensitive content unnecessarily.

## Cross-institute transfer and referrals
- XFR-001 [P0] Support a controlled referral/transfer request between institutes.
- XFR-002 [P0] Require source authority, destination acceptance, and appropriate student/guardian consent where applicable.
- XFR-003 [P0] Share only selected data categories and fields, not unrestricted profile access.
- XFR-004 [P0] Record purpose, legal basis/consent, scope, expiry, sender, recipient, and access history.
- XFR-005 [P0] Support revoke/expire and transfer completion with provenance.
- XFR-006 [P1] Support secure export/import and duplicate resolution.

## SaaS commercial platform
- SaaS-001 [P0] Define plans, trial, billing cycle, subscription status, and entitlement catalog.
- SaaS-002 [P0] Enforce quotas and features server-side.
- SaaS-003 [P0] Meter usage for seats, active students, branches, storage, SMS, video, and API calls as configured.
- SaaS-004 [P1] Support add-ons, upgrade/downgrade, proration, grace periods, suspension, and cancellation.
- SaaS-005 [P0] Provide tenant export and controlled offboarding.
- SaaS-006 [P1] Support white-label branding and custom domains.
- SaaS-007 [P1] Provide platform support console with audited, time-limited access.

## Localization and accessibility
- LOC-001 [P0] All UI copy is translation-key based; no hard-coded user-facing strings.
- LOC-002 [P0] Support locale selection per user and tenant default.
- LOC-003 [P0] Support culture-aware number, date, time, currency, and plural formatting.
- LOC-004 [P0] Support RTL and LTR layouts, including mixed-direction content.
- LOC-005 [P1] Support localized templates for SMS, email, invoices, report cards, and cards.
- LOC-006 [P1] Store translatable catalog content with a defined fallback language.
- LOC-007 [P1] Meet WCAG 2.2 AA target for key user journeys.

## Reporting and operations
- RPT-001 [P0] Provide scoped operational dashboards and exports.
- RPT-002 [P0] Enforce row-level report authorization and small-cell suppression for sensitive aggregates.
- OPS-001 [P0] Provide health checks, structured logs, metrics, traces, backup, restore, and incident procedures.
- OPS-002 [P0] Support data retention, legal hold, deletion, and export policies.
