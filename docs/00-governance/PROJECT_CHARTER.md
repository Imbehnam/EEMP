# Project Charter

## Product
EEMP is a commercial, multi-tenant education management platform for education institutes and their branches. It provides central-office and branch operations through a unified management experience, alongside role-specific portals for students, parents, teachers, counselors, and administrators.

## Product outcomes
- Reduce administrative workload across institutes and branches.
- Provide a consistent academic and financial record for each learner.
- Support configurable workflows rather than hard-coded request paths.
- Enable institutes to configure their own academic, financial, notification, and branding settings within platform-level guardrails.
- Offer a scalable subscription product with tenant isolation, operational visibility, and controlled feature entitlements.
- Support multiple languages, locales, calendars, currencies, and time zones.

## Target users / buyers
- Platform operator (SaaS provider).
- Institute owner / central office.
- Institute manager and branch manager.
- Academic coordinator and registrar.
- Finance staff.
- Teachers and instructors.
- Students and parents/guardians.
- Counselors and academic planners.
- Support agents with time-limited, audited access where authorized.

## Product boundary
The platform includes organization/branch management, academic operations, identity and access, parent/student/teacher lifecycle, grades/report cards, requests/workflows, consultation/support, academic planning, invoicing/payments, recorded courses, online sessions/calendar, notifications, cards, reporting, configuration, localization, and SaaS subscription controls.

External services (payment gateways, SMS providers, video meeting providers, email, object storage, identity providers) are integrated through adapters. The platform does not assume one vendor.

## Commercialization requirements
- Tenant provisioning, plan catalog, subscription lifecycle, trial/suspension/cancellation.
- Feature and quota entitlements (branches, active students, staff, storage, video minutes, SMS credits, courses, API usage).
- Usage metering, billing, invoices, renewals, grace periods, and dunning policy.
- Tenant branding, custom domains (future capability), support tiers, and auditability.
- Data export, retention, tenant offboarding, and deletion policy.
- Platform-wide operational controls separated from tenant business data access.

## Delivery principles
- Analyze first; implement only after approval.
- Modular monolith first; extract services only when justified by scale or organizational needs.
- API-first backend with independently deployable frontend.
- Security and privacy by design.
- Configuration over tenant-specific code forks.
- Localization designed into data contracts and UI from the beginning.
- Incremental delivery with testable milestones.
