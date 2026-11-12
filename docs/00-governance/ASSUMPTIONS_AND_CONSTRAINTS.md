# Assumptions, Constraints, and Validation

## Known direction
- Backend: ASP.NET Core Web API.
- Frontend: Next.js with TypeScript.
- Product: commercial multi-tenant SaaS.
- The system must support central institute management and branch-level operations in one management platform.
- Dynamic permissions and configurable workflows are required.
- Internationalization is required from the beginning.

## Proposed defaults (not final approval)
- Architecture: Modular Monolith + Clean Architecture.
- Database: SQL Server with EF Core; confirm hosting and licensing constraints.
- Cache/distributed coordination: Redis.
- Background processing: durable job scheduler plus transactional outbox.
- Files/media: S3-compatible object storage abstraction.
- API: REST + OpenAPI; consider realtime transport only for specific use cases.
- Frontend: Next.js App Router, TypeScript, server/client component boundaries, accessible component system.
- Authentication: standards-based OIDC/OAuth2 where possible; exact identity provider is TBD.
- Payment and SMS: provider adapters with tenant-level configuration and platform-approved provider catalog.

## Product assumptions to validate
- An institute account may contain multiple institutes and branches.
- Central office can manage shared policies while branches receive scoped operational permissions.
- A student may have relationships with more than one institute over time; cross-institute transfer must be explicit and consent/authority controlled.
- A parent/guardian may be linked to multiple students and may have different legal/communication permissions per student.
- Teachers may work across branches and classes, subject to explicit assignments.
- Academic structures (year, term, course, class, lesson, assessment) vary by institute.
- Workflow definitions can be configured but must be versioned and guarded against unsafe changes.
- Payment settlement and refund rules differ by gateway and jurisdiction.
- Video meeting providers and SMS providers may differ by country.

## Decisions required before implementation
See `docs/08-planning/OPEN_DECISIONS.md`. Never silently infer legal, financial, grading, or cross-institute sharing rules.
