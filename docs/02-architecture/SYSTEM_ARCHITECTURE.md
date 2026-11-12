# System Architecture

## Recommended style
Modular Monolith with Clean Architecture boundaries, API-first. Avoid premature microservices. Modules own their data and expose application contracts/events; cross-module writes must be explicit.

## Applications
- `Eemp.Api`: ASP.NET Core HTTP API, authentication, authorization, versioning, OpenAPI.
- `Eemp.Web`: Next.js App Router web experience.
- Optional later: background worker host if job load or deployment isolation requires it.

## Backend layers
- Domain: entities, value objects, domain events, invariants; no EF, HTTP, or provider dependencies.
- Application: use cases, commands/queries, validators, authorization policies, ports/interfaces, DTOs.
- Infrastructure: EF Core, SQL Server, Redis, object storage, payment/SMS/video adapters, job scheduler, outbox.
- API: endpoints/controllers, authentication, request mapping, rate limits, OpenAPI, middleware.
- Shared kernel kept deliberately small; avoid a generic dumping-ground project.

## Suggested modules
1. Platform & Tenancy
2. Identity & Access
3. Organization & Branches
4. People & Relationships
5. Academic Catalog & Calendar
6. Enrollment & Class Operations
7. Attendance & Assessment
8. Grades & Report Cards
9. Workflow & Requests
10. Consultation & Support
11. Academic Planning
12. Finance & Payments
13. Learning Content & Recorded Courses
14. Online Sessions & Scheduling
15. Notifications & Communications
16. Cards & Documents
17. Reporting & Analytics
18. Audit, Privacy & Compliance
19. SaaS Plans, Entitlements & Usage

## Data ownership
Each module owns its aggregate roots and write model. Cross-module reads use application-level query contracts or read models. Avoid sharing EF DbContext entities across modules.

## Integration patterns
- Domain events for in-process module notifications.
- Transactional outbox for reliable external delivery.
- Inbox/idempotency records for webhook and message processing.
- Adapter interfaces for payment, SMS, video, storage, and email.
- REST APIs for frontend and external integrations.
- WebSocket/SignalR only for features that need realtime updates.

## Frontend applications
Use a single Next.js application initially with role-aware route groups and shared design system. Separate public marketing site or branded tenant domains only if commercial/SEO needs justify it.
Suggested route groups: public, platform, tenant-admin, teacher, student, parent, counselor.

## Deployment
Containerized deployment. Keep API, frontend, worker, SQL Server, Redis, and object storage as independently configurable services. Production secrets must come from secret manager/environment injection, not committed files.

## Architecture quality attributes
Security, tenant isolation, maintainability, observability, testability, localization, accessibility, performance, recoverability, portability, and cost efficiency.
