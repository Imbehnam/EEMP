# Implementation Roadmap

This roadmap is a proposal; sequence and scope require approval.

## Phase 0 — Product discovery and specification
- Confirm business model, target countries, legal requirements, personas, pricing model.
- Resolve open decisions.
- Approve requirements, domain model, architecture, data classification, and UX journeys.
- Produce ERD, API contracts, permission matrix, workflow catalog, and acceptance criteria.
**Exit:** signed-off product/architecture baseline.

## Phase 1 — Foundation and security
- Repository, CI, local Docker environment.
- Identity/authentication and tenant context.
- Tenant/branch model and authorization framework.
- SQL Server isolation/RLS verification harness.
- Audit, localization foundation, observability, test infrastructure.
**Exit:** cross-tenant and cross-branch isolation tests pass.

## Phase 2 — Organization and people
- Tenant onboarding, central office, institutes, branches.
- Users, roles, dynamic permissions, memberships.
- Student, guardian, teacher, staff profiles and relationships.
**Exit:** scoped administration and portal identity flows work.

## Phase 3 — Academic core
- Academic years/terms, subjects, courses, classes, schedules.
- Teacher assignments, enrollment, attendance.
- Assessment, grading, approval, report cards.
**Exit:** complete academic lifecycle with published report card.

## Phase 4 — Workflow and service operations
- Workflow designer/runtime and request inbox.
- Consultation/support availability and bookings.
- Academic planning requests, fees, plans, milestones.
**Exit:** configurable request lifecycle and concurrency-safe booking.

## Phase 5 — Finance
- Fee catalog, tuition policies, invoices, payment intents.
- Gateway adapters, webhooks, receipts, refunds, reconciliation.
- SaaS subscription billing and usage entitlements.
**Exit:** end-to-end verified payment and financial audit.

## Phase 6 — Learning and online sessions
- Recorded courses, media pipeline, entitlements, progress.
- Calendar aggregation and online meeting provider.
**Exit:** protected course access and online session lifecycle.

## Phase 7 — Communications and cards
- Configurable SMS reminders and templates.
- Delivery dashboard, retries, preferences.
- Student/teacher cards and generated documents.
**Exit:** deduplicated configurable reminders and revocable cards.

## Phase 8 — Commercial readiness
- Tenant branding, plans, quotas, usage, support tooling.
- Data export/offboarding, operational runbooks, security review.
- Load, accessibility, localization, disaster recovery tests.
**Exit:** release readiness checklist approved.

## Release strategy
Private alpha → pilot institutes → controlled beta → general availability. Do not onboard paying customers until privacy, payment, support, backup/restore, and tenant-isolation gates are met.
