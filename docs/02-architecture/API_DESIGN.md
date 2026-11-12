# API Design

## Conventions
- Version APIs (`/api/v1/...`) and publish OpenAPI.
- Use resource-oriented endpoints and explicit command actions for state transitions.
- Use DTOs, never serialize persistence entities.
- Use cursor pagination for large feeds where appropriate; bounded page sizes for offset pagination.
- Standardize validation and problem-details error responses.
- Use idempotency keys for payment initiation, workflow commands with retry risk, and other non-repeatable operations.
- Include correlation/request IDs.
- Enforce authorization and tenant scope on server.
- Use ETags/rowversion or explicit concurrency tokens for conflict-sensitive updates.
- Never expose internal storage paths, provider credentials, or unrestricted meeting URLs.

## API groups
`/platform`, `/tenants`, `/organization`, `/branches`, `/users`, `/roles`, `/permissions`, `/students`, `/guardians`, `/teachers`, `/academic-years`, `/terms`, `/subjects`, `/courses`, `/classes`, `/enrollments`, `/attendance`, `/assessments`, `/grades`, `/report-cards`, `/requests`, `/consultations`, `/planning`, `/invoices`, `/payments`, `/refunds`, `/learning-courses`, `/calendar`, `/meetings`, `/notifications`, `/cards`, `/subscriptions`, `/usage`, `/audit`, `/exports`.

## Error taxonomy
Validation, authentication, authorization, not-found, conflict/concurrency, business-rule violation, rate-limit, dependency failure, and unexpected server error. Do not leak internal exception details.
