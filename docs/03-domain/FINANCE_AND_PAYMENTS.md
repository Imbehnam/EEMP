# Finance and Payment Domain

## Separate ledgers
1. Institute finance: tuition, services, invoices, student receivables, refunds.
2. SaaS finance: tenant subscription, platform plan, usage charges, platform invoices.

Do not mix the two ledgers.

## Invoice lifecycle
Draft → Issued → PartiallyPaid / Paid / Overdue → Voided or Credited according to policy. Issued invoices must not be silently edited; use credit/debit adjustments.

## Payment lifecycle
Created → PendingProvider → RequiresAction → Succeeded / Failed / Expired / Cancelled. Refund is a separate lifecycle. Provider callback is not trusted until signature and/or provider-side verification succeeds.

## Required controls
- Currency and rounding policy.
- Invoice numbering scope and reset policy.
- Tax/VAT rules by jurisdiction.
- Discounts, scholarships, installments, late fees.
- Partial payments and allocation order.
- Refund authorization and maker-checker control.
- Reconciliation and settlement mismatch queue.
- Chargeback handling.
- Provider outage and retry policy.
- Receipt and audit trail.
- Financial period closing and export.

## Security
Never accept amount, currency, invoice ownership, or payment success from the client as authoritative. Verify on server and bind payment intent to tenant, invoice, amount, and currency. Use idempotency keys and unique provider transaction references.
