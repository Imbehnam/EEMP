# Performance and Scalability Plan

## Workload dimensions
Active tenants, students, parents, teachers, branches, concurrent sessions, calendar events, report-card batches, media bandwidth, SMS volume, payment callbacks, and scheduled jobs.

## Design controls
- Tenant-leading indexes for tenant-scoped queries.
- Avoid N+1 queries; project only required columns.
- Pagination and bounded exports.
- Async processing for PDFs, bulk notifications, imports, and media.
- Cache only safe, invalidatable, tenant-scoped data.
- Do not cache authorization decisions beyond revocation guarantees.
- Queue partitioning/fairness to prevent one tenant monopolizing workers.
- Rate limits by tenant, user, endpoint, and provider.
- Separate analytics/read models when transactional load requires it.
- Load-test noisy-neighbor behavior and large tenants.

## Targets
Set measurable SLOs after workload discovery. Do not invent latency or availability guarantees before traffic, infrastructure, and plan tiers are defined.
