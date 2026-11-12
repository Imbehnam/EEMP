# Multi-Tenancy and Data Isolation

## Recommended model
Shared application and shared database/schema initially, with mandatory TenantId scope and carefully designed branch scope. Evaluate database-per-tenant only for contractual, regulatory, or scale reasons. The final model requires a threat/cost review.

## Tenant resolution
Resolve tenant from trusted host/domain, authenticated membership, or signed invitation context. Never trust a raw client-supplied TenantId. Validate the authenticated user’s active membership on every request.

## Enforcement layers
- API middleware establishes validated tenant context.
- Application authorization verifies tenant, branch, resource, relationship, and action.
- EF Core query filters are defense-in-depth, not the only control.
- SQL Server Row-Level Security (RLS) with SESSION_CONTEXT is a proposed additional isolation boundary; validate connection pooling, context reset, background jobs, admin bypass, and migration behavior.
- Unique constraints include tenant scope where appropriate.
- Background jobs carry explicit tenant context and revalidate permissions/state.
- Cache keys include tenant and relevant scope.
- Search indexes and object-storage paths are tenant-scoped and access-checked.
- Exports and reports reapply authorization.

## Isolation test matrix
- Tenant A cannot read/update/delete Tenant B records.
- Branch A cannot access sibling Branch B without explicit grant.
- Tenant admin cannot access sensitive records merely by being admin.
- Parent cannot access unrelated student.
- Teacher cannot access unassigned class or expired assignment.
- Revoked access is denied on next request.
- Jobs, exports, cache, search, and file links cannot cross tenant boundary.
- RLS context is set/reset safely with pooled connections.

## Approved limits
Apply `APPROVED_SECURITY_BASELINE.md`, including 90-day supervision maximum and fresh renewal approval.
