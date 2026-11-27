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

## Binary documents (report-card PDFs, DL-057/DL-058)
- Generated documents are rendered by the API from stored, authoritative data (e.g. a report-card version snapshot), never from
  client-supplied content, and are authorized exactly like the JSON read of the same resource (404 outside scope).
- `GET …/pdf` returns `application/pdf` (`application/zip` for archives) with `Content-Disposition: attachment` and a file name built
  from opaque ids only (no personal data), plus `Cache-Control: no-store`; errors stay `application/problem+json`.
- Language via `?lang=` (fa-IR, en, tr, ar) or `Accept-Language`; labels come from the server text catalogs.
- Endpoints: `GET /report-cards/{id}/versions/{versionNo}/pdf`, `GET /portal/me/report-cards/{id}/pdf`, `GET /report-cards/pdf-archive?classId=`.
