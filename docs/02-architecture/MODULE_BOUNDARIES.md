# Module Boundaries and Responsibilities

| Module | Owns | Must not own |
|---|---|---|
| Platform/Tenancy | Tenant lifecycle, plan association, tenant context | Student academic records |
| Identity/Access | Accounts, credentials, roles, permissions, assignments | Business-specific enrollment state |
| Organization | Central office, institute, branch hierarchy | Authorization policy implementation |
| People | Person profiles, guardian relationships, staff profiles | Academic grades or payment ledger |
| Academic Catalog | Academic years, terms, subjects, courses, class definitions | Student billing |
| Enrollment | Applications, enrollment, class membership | Payment settlement |
| Assessment | Assessment definitions, score submissions | Report publication policy |
| Report Cards | Approved grade snapshots and publication | Editing original assessment records |
| Workflow | Definitions, versions, instances, transitions, tasks | Owning every business aggregate |
| Consultation | Availability, bookings, case metadata | Payment gateway settlement |
| Planning | Planning requests, plans, milestones | General invoice ledger |
| Finance | Fee catalog, invoices, payments, ledger, refunds | Enrollment eligibility policy |
| Learning | Recorded content, entitlements, progress | Raw media storage infrastructure |
| Scheduling | Calendar events, recurrence, meeting references | Video provider internals |
| Notification | Templates, preferences, schedules, delivery logs | Business decisions that trigger messages |
| SaaS Billing | Platform plans, subscriptions, usage | Institute tuition ledger |
| Reporting | Authorized read models and aggregates | Bypassing source authorization |
| Audit/Privacy | Audit, consent, retention, export requests | Business workflow ownership |

Cross-module interactions must use contracts/events and explicit transaction boundaries.
