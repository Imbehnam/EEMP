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

## Institute finance (M3, DL-059..DL-066)
Full contract: `docs/08-planning/M3_FINANCE_CONTRACT.md`.
- Money: JSON numbers with an explicit ISO 4217 `currency` taken from the tenant, never from the client; stored `decimal(19,4)` and
  rounded to the currency's minor unit (IRR: 0 decimals). Clients never send totals, balances, payment status or currency.
- Staff endpoints under `/finance` (settings, fee-types, fee-items, invoices + issue/void/credit-notes/installments/discounts/generate,
  approvals, discounts/{id}/approve|reject, refunds, payments + cheque-status/refunds, students/{id}/account|apply-credit, late-fees/apply,
  reports/outstanding|aging|collections, invoices/{id}/pdf, payments/{id}/receipt/pdf). All branch-scoped and audited.
- Portal endpoints under `/portal/me/finance` for the student and linked guardians; drafts are never visible.
- The only anonymous finance endpoint is the payment-provider callback `GET|POST /finance/gateway/{provider}/callback/{paymentId}`:
  it is bound to the payment id + stored provider authority, verifies with the stored amount and is idempotent.
- Idempotency: `idempotencyKey` on manual and online payments; invoice generation is idempotent per (enrollment, fee item).
