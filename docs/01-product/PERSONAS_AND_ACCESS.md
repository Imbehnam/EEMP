# Personas and Access Model

## Personas
- Platform Operator: operates SaaS infrastructure and subscription controls; no default access to tenant education records.
- Tenant Owner: owns commercial relationship and tenant-level configuration.
- Central Office Administrator: manages allowed cross-branch policies and aggregate operations.
- Branch Manager: manages assigned branch scope.
- Registrar / Academic Coordinator: manages enrollment, classes, timetable, grades within assigned scope.
- Finance Officer: manages invoices, payments, refunds subject to separation of duties.
- Teacher: accesses assigned classes, rosters, attendance, assessments, and approved communication.
- Student: accesses own learning and academic information.
- Parent/Guardian: accesses linked student information according to relationship and authority.
- Counselor / Planner: accesses assigned consultation and planning cases only.
- Support Agent: temporary, purpose-bound access only after authorization and audit.

## Authorization dimensions
Authorization is not a simple role check. Evaluate:
1. Identity and authentication strength.
2. Tenant membership.
3. Role and permission.
4. Institute/branch scope.
5. Resource ownership/assignment.
6. Relationship (guardian-to-student).
7. Record sensitivity and consent.
8. Workflow state and separation-of-duty constraints.
9. Subscription entitlement where relevant.

## Suggested permission format
`{module}.{resource}.{action}` with scope metadata.
Examples:
- `education.class.read`
- `education.class.manage`
- `academic.grade.submit`
- `academic.report-card.publish`
- `finance.invoice.issue`
- `finance.payment.refund`
- `workflow.request.approve`
- `consultation.case.read`
- `tenant.branch.manage`

Roles are permission bundles, not hard-coded security boundaries. Assignments carry tenant, branch, resource, and effective date scope.

## Guardrails
- Deny by default.
- Server-side authorization on every request.
- A user can hold multiple roles and assignments.
- Parent relationship is not equivalent to unrestricted custody/authority.
- Teacher access is assignment- and date-scoped.
- Central office visibility is explicitly granted, not inferred from hierarchy.
- Platform operations are separated from tenant business data.
