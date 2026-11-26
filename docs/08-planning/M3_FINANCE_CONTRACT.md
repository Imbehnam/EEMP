# EEMP Milestone 3 — Institute finance (EEMP-014 backend): shared API contract

Scope = tracker EEMP-014 backend: fee types/items, invoices (draft → issued → partiallyPaid → paid, void, credit notes),
idempotent invoice generation from enrollments, discounts/scholarships with a local maker-checker approval, installment
plans, optional late fees, manual payments with receipts, online payments through `IPaymentGateway` (fake + Zarinpal
adapter, off by default), refunds with approval, allocation and credit balance, portal views for students/linked guardians,
reports with CSV, invoice/receipt PDFs. Branch `feature/m3-finance-backend` (contains `feature/report-card-pdf`, PR #6).
Everything in the M1 and M2 contracts still holds; this document only adds to it. Decisions: DL-059..DL-066.

## 0. Conventions (unchanged, restated)
- Base `/api/v1`, JSON camelCase, enums camelCase strings, RFC 7807 problems with `code`/`params`/`errors`.
- Paged lists `{ items, page, pageSize, totalCount }`; **(array)** = plain array.
- Every mutable resource returns `rowVersion`; state-changing POSTs that say so require it → `409 concurrency.conflict`.
- Out of scope → `404 resource.not_found`; readable but not changeable → `403 authorization.forbidden_scope`;
  missing permission → `403 authorization.forbidden`. Tenant only from the JWT; branch scope server-side.
- Date-only fields (`issueDate`, `dueDate`, `receivedOn`, installment `dueDate`, cheque `dueDate`) are ISO `yyyy-MM-dd`
  calendar dates in the tenant time zone; instants are UTC `…Z`. "Today" = tenant-local date.

## 1. Money (DL-059)
- Amounts are JSON **numbers** with explicit `currency` (ISO 4217) on every invoice, payment, refund and fee item.
- The currency is the tenant's (`Tenant.Currency`, default `IRR`, read through `ITenantFinanceSettings`) and is stamped on each record
  at creation; it is never taken from the client. A request whose fee item / invoice currency differs → `422 finance.currency_mismatch`.
- Storage `decimal(19,4)`; every stored amount is rounded to the currency's minor unit (IRR/IRT: 0 decimals; others ISO default),
  `MidpointRounding.AwayFromZero`. Client amounts with more decimals than the currency allows → `400 validation.failed`
  (`amount: validation.invalid_value`). Percentages: 0 < p ≤ 100, max 2 decimals.
- Installment splits and discount rounding: computed amounts rounded per line; installment remainder goes to the **last** installment.

## 2. Permissions (new; `GET /permissions` lists them)
| code | isSensitive | branchScoped | meaning |
|---|---|---|---|
| `finance.setup` | false | false | finance settings, fee types, fee items |
| `finance.read` | true | true | invoices, installments, payments, receipts, refunds, student accounts, PDFs |
| `finance.invoices.manage` | true | true | create/edit draft invoices, generate, issue, void, credit notes, installments, request discounts, apply late fees, apply credit |
| `finance.payments.manage` | true | true | record manual payments, cheque status, request refunds |
| `finance.approve` | true | true | approve/reject discounts and refunds (checker; never the maker) |
| `finance.reports` | true | true | outstanding, aging, collections, CSV export |

Built-in role **FinanceOfficer** (new, created for every tenant by an idempotent startup sync, DL-064): all `finance.*` codes +
`organization.read`, `branches.read`, `catalog.read`, `classes.read`, `students.read`, `guardians.read`, `enrollments.read`.
**TenantAdmin gets no `finance.*` code** (security baseline #1); BranchManager unchanged. Maker-checker needs two officers
(or an officer + a custom role holding `finance.approve`).

## 3. Settings — `GET/PUT /finance/settings` (`finance.setup`)
```json
{ "currency": "IRR", "vatEnabled": false, "vatRatePercent": 0, "lateFeeEnabled": false, "lateFeeType": "fixed|percent",
  "lateFeeValue": 0, "lateFeeGraceDays": 0, "siblingDiscountPercent": 0, "blockResultsOnOverdue": false,
  "onlinePaymentsEnabled": false, "rowVersion": "…" }
```
`currency` is read-only. VAT (DL-062) applies only to lines whose fee type is `taxable`, at the rate in force when the invoice is
**issued** (stored on the invoice). `blockResultsOnOverdue` (DL-065, default false): when on, portal grade/report-card reads for a
student with an overdue issued balance return `403 finance.results_blocked` (staff views unaffected).

## 4. Fee setup (`finance.setup` to write, `finance.read` or `finance.setup` to read)
- `GET /finance/fee-types` (array) · `POST` · `PUT /{id}` · `DELETE /{id}` (`409 finance.fee_type_in_use`)
  `{ id, code, name, taxable, isActive, rowVersion }`
- `GET /finance/fee-items?termId=&feeTypeId=&activeOnly=` (array) · `POST` · `PUT /{id}` · `DELETE /{id}` (`409 finance.fee_item_in_use`)
  `{ id, feeTypeId, feeTypeCode, name, amount, currency, termId?, courseId?, branchId?, isActive, rowVersion }`
  (`termId`/`courseId`/`branchId` null = applies to any; generation only uses items whose scope matches the enrollment.)

## 5. Invoices
```json
{ "id", "number": "INV-000012" | null, "studentId", "studentName", "studentNumber", "branchId", "termId"?, "status":
  "draft|issued|partiallyPaid|paid|void", "currency", "issueDate"?, "dueDate"?, "subtotal", "discountTotal", "taxRatePercent",
  "taxTotal", "lateFeeTotal", "creditTotal", "total", "paidAmount", "balance", "isOverdue", "note"?, "voidReason"?,
  "issuedAt"?, "voidedAt"?, "lines": [{ "id", "feeItemId"?, "enrollmentId"?, "description", "quantity", "unitPrice", "amount", "taxable" }],
  "discounts": [{ "id", "kind": "discount|scholarship|sibling", "type": "percent|fixed", "value", "amount", "reason", "status":
  "pending|approved|rejected", "requestedBy", "decidedBy"?, "decidedAt"?, "rowVersion" }],
  "installments": [{ "id", "seqNo", "dueDate", "amount", "paidAmount", "lateFeeCharged", "status": "open|paid|overdue" }],
  "adjustments": [{ "id", "kind": "creditNote|lateFee", "number", "amount", "reason", "installmentId"?, "createdAt" }],
  "rowVersion" }
```
`total = subtotal − approved discounts + tax`; `balance = total + lateFeeTotal − creditTotal − paidAmount`.
- `GET /finance/invoices?status=&studentId=&branchId=&termId=&overdueOnly=` (paged, `finance.read`, branch-scoped)
- `GET /finance/invoices/{id}` · `POST /finance/invoices` (draft) `{ studentId, termId?, dueDate?, note?, lines:[{feeItemId?|description, quantity, unitPrice?}] }`
  (`unitPrice` required only for free-text lines; fee-item lines take the item's amount) · `PUT /{id}` (draft only, rowVersion) ·
  `DELETE /{id}` (draft only, no generated-charge history kept)
- `POST /{id}/issue` `{ rowVersion, issueDate?, dueDate? }` → assigns the gap-free number; `422 finance.discount_pending` while a
  discount is pending; `422 finance.invoice_empty` for zero total.
- `POST /{id}/void` `{ rowVersion, reason }` → issued/partiallyPaid/paid → void; allocations are released to the student's
  credit balance; generated charges are released so the enrollment can be invoiced again.
- `POST /{id}/credit-notes` `{ rowVersion, amount, reason }` (issued/partiallyPaid; `amount ≤ balance` else `422 finance.amount_exceeds_balance`)
- `POST /finance/invoices/generate` `{ enrollmentId, feeItemIds[], dueDate? }` and
  `POST /finance/invoices/generate-bulk` `{ classId? | termId?, feeItemIds[], dueDate? }` → `{ created, skipped, invoiceIds[] }`.
  One draft invoice per enrollment; a charge (enrollment × fee item) is generated **at most once** (unique key) — replays
  and concurrent calls skip it. Withdrawn enrollments are skipped. Sibling rule: when `siblingDiscountPercent > 0` and another
  student sharing a guardian already has an invoice in the same term, a **pending** `sibling` discount is added.
- Issued invoices are never edited: changes go through credit notes, late fees or void.

## 6. Discounts / scholarships (local maker-checker, DL-063)
- `POST /finance/invoices/{id}/discounts` `{ kind, type, value, reason }` (draft invoice, `finance.invoices.manage`) → pending.
- `POST /finance/discounts/{id}/approve|reject` `{ rowVersion, note? }` (`finance.approve`; maker ≠ checker else
  `403 finance.maker_checker`). Approval recomputes the draft's totals; a discount larger than the remaining subtotal is capped.
- `DELETE /finance/discounts/{id}` (pending only, maker or `finance.approve`).
- `GET /finance/approvals?status=pending` (array of `{ type: discount|refund, id, invoiceId?, paymentId?, studentId, amount, requestedBy, requestedAt, reason }`).

## 7. Installments and late fees
- `PUT /finance/invoices/{id}/installments` `{ rowVersion, items:[{dueDate, amount}] }` **or** `{ rowVersion, count, firstDueDate, intervalDays }`
  (equal split, remainder on last). Sum must equal the invoice `total` (draft) or current `balance` + already-paid portions (issued)
  → else `422 finance.installments_mismatch`. Allowed while no installment has a payment.
- `POST /finance/late-fees/apply` `{ asOf? }` (`finance.invoices.manage`, in caller branch scope) → `{ applied }`. When enabled, each
  installment (or single-due invoice) overdue by more than `lateFeeGraceDays` gets **one** late fee adjustment (idempotent).

## 8. Payments, receipts, credit
```json
{ "id", "studentId", "branchId", "method": "cash|pos|transfer|cheque|online", "status": "pending|succeeded|failed|cancelled",
  "amount", "currency", "receivedOn", "reference"?, "cheque": { "number", "bank", "dueDate", "status": "received|cleared|bounced" }?,
  "provider"?, "providerRef"?, "receiptNumber"?, "allocated", "unallocated", "refunded", "allocations": [{ "invoiceId", "invoiceNumber", "installmentId"?, "amount" }],
  "createdAt", "rowVersion" }
```
- `POST /finance/payments` (`finance.payments.manage`) `{ studentId, method, amount, receivedOn?, reference?, invoiceId?, cheque?, idempotencyKey? }`
  cash/pos/transfer → `succeeded` immediately with a receipt number; cheque → `pending` until
  `POST /finance/payments/{id}/cheque-status` `{ rowVersion, status: cleared|bounced }`. Same `idempotencyKey` → the same payment (200).
- Allocation (DL-061): to `invoiceId` when given, otherwise oldest-due issued invoices of the student; inside an invoice, installments by
  due date; the remainder becomes **credit balance** (`unallocated`). `POST /finance/students/{studentId}/apply-credit` `{ invoiceId }`.
- Receipt numbers (`R-000001`), invoice numbers (`INV-…`) and credit-note numbers (`CN-…`) are per tenant, gap-free (DL-060).
- `GET /finance/payments?studentId=&method=&status=&from=&to=&branchId=` (paged), `GET /finance/payments/{id}`.
- `GET /finance/students/{studentId}/account` → `{ studentId, currency, totalInvoiced, totalPaid, balance, creditBalance, overdue, invoices[], payments[] }`.

## 9. Refunds (approval)
- `POST /finance/payments/{id}/refunds` `{ amount, reason }` (`finance.payments.manage`): only from the payment's unallocated credit
  (`422 finance.refund_exceeds_credit`). → pending. `POST /finance/refunds/{id}/approve|reject` (`finance.approve`, maker ≠ checker).
  Approval reduces the payment's unallocated amount (money leaves the institute outside EEMP; gateway refunds are not automated).
- `GET /finance/refunds?status=` (array).

## 10. Online payments (DL-066)
- `POST /portal/me/finance/invoices/{id}/pay` `{ installmentId?, amount?, idempotencyKey }` (student/linked guardian) →
  `{ paymentId, redirectUrl, provider }`. The amount is computed on the server (installment remaining or invoice balance);
  a client `amount` is accepted only as a partial amount `0 < amount ≤ outstanding`. `422 finance.online_payments_disabled` when the
  tenant setting or every gateway is off.
- Callback (anonymous): `GET|POST /finance/gateway/{provider}/callback/{paymentId}?Authority=&Status=` → verifies with the provider
  using the **stored** amount; replays return the stored result; a provider-reported amount different from the stored amount →
  payment `failed` with `finance.amount_mismatch`. Responds `302` to `Finance:Payments:ReturnUrl?paymentId=&status=` when configured,
  otherwise JSON `{ paymentId, status, receiptNumber }`.
- `POST /portal/me/finance/payments/{id}/verify` (owner) – same verification, idempotent.
- Gateways: `fake` (config `Finance:Gateways:Fake:Enabled`, Development/Testing only) and `zarinpal`
  (`Finance:Gateways:Zarinpal:{Enabled=false, MerchantId, Sandbox, CallbackBaseUrl}`; merchant id from environment only).

## 11. Portal (student or linked guardian; drafts never visible)
- `GET /portal/me/finance/students/{studentId}/account` (summary + invoices + payments) · `GET /portal/me/finance/invoices/{id}` ·
  `GET /portal/me/finance/invoices/{id}/pdf` · `GET /portal/me/finance/payments/{id}` · `GET /portal/me/finance/payments/{id}/receipt/pdf`.
  Not linked/own → 404.

## 12. Reports (`finance.reports`, branch-scoped; `?format=csv` → `text/csv; charset=utf-8` with BOM)
- `GET /finance/reports/outstanding?branchId=&termId=` → rows `{ invoiceId, number, studentId, studentName, branchId, dueDate, total, paid, balance }`
- `GET /finance/reports/aging?asOf=&branchId=` → `{ asOf, buckets: { current, d1_30, d31_60, d61_90, d90plus }, rows[] }` (by student)
- `GET /finance/reports/collections?from=&to=&branchId=` → rows `{ date, branchId, method, count, amount }` (succeeded payments by `receivedOn`)

## 13. PDFs
`GET /finance/invoices/{id}/pdf` (`finance.read`, issued/paid/void only — draft → `422 finance.invoice_not_issued`) and
`GET /finance/payments/{id}/receipt/pdf` (succeeded only, `422 finance.receipt_unavailable`). `?lang=` as report cards (DL-058),
Jalali dates for fa, `Cache-Control: no-store`, file names `invoice-INV-000012.pdf` / `receipt-R-000003.pdf` (no personal data).

## 14. Error codes (fa/en/tr/ar)
`finance.currency_mismatch`, `finance.invalid_state`, `finance.invoice_empty`, `finance.invoice_not_issued`, `finance.discount_pending`,
`finance.maker_checker`, `finance.amount_exceeds_balance`, `finance.installments_mismatch`, `finance.installments_locked`,
`finance.refund_exceeds_credit`, `finance.online_payments_disabled`, `finance.gateway_unavailable`, `finance.amount_mismatch`,
`finance.payment_failed`, `finance.receipt_unavailable`, `finance.fee_type_in_use`, `finance.fee_item_in_use`, `finance.fee_item_inactive`,
`finance.results_blocked`, `finance.no_credit`.

## 15. Audit
Every finance state change writes an append-only `finance.FinanceAudit` row (actor, action, entity, invoice/payment/student ids,
amount, currency, at) in the **same transaction** as the change (DL-063). The generic audit trail still records changed field names.
