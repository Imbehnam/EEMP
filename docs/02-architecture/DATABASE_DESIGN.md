# Database Design Principles

## Tenant and scope columns
Tenant-owned tables should carry `TenantId` unless a documented ownership model proves otherwise. Branch-scoped records also carry `BranchId` or a validated scope relation. Global reference data must be explicitly classified as global.

## Core principles
- Use immutable identifiers (GUID/UUID or approved strategy).
- Store UTC instants; store local schedule rule/timezone separately.
- Use `decimal(p,s)` for money and explicit currency code.
- Use concurrency tokens/rowversion for mutable aggregates.
- Add indexes for tenant, branch, status, foreign keys, and common search patterns.
- Define unique constraints with tenant scope.
- Avoid soft-delete as a substitute for retention/legal deletion policy.
- Financial ledger and audit records are append-only.
- Keep PII and sensitive case data separated where it enables stricter access and retention.
- Use explicit status enums/value objects and transition validation.
- Use migration review and rollback/forward-fix plans.

## Candidate bounded data areas
Tenancy; Identity; Organization; Person & Guardian; Academic Catalog; Class & Enrollment; Attendance; Assessment; Report Cards; Workflow; Consultation; Planning; Finance; Learning; Calendar; Notifications; Cards; SaaS Subscription; Audit & Consent.

## Important modeling notes
- A user account is not the same as a person record.
- A person may have multiple roles/relationships.
- Parent-to-student relationship is many-to-many and has authority attributes.
- A class definition differs from a class offering/section in a specific term.
- Teacher assignment is effective-dated.
- A report card is a published snapshot/version, not a live query over mutable scores.
- Invoice, payment attempt, settlement, refund, and ledger entry are distinct concepts.
- Workflow definition version is pinned to each workflow instance.
- Notification schedule and delivery attempt are separate records.
- Calendar event and external meeting provider reference are separate.
