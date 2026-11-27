'use client';

import type { Invoice, InvoiceStatus, PaymentStatus } from '@eemp/api-client';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Badge, Card, DataTable, DescriptionList, type Tone } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useMemo, type ReactNode } from 'react';

import { Money } from './money';

/** Translated labels for finance enums (ICU selects with an `other` fallback for unknown values). */
export function useFinanceLabels() {
  const t = useTranslations('finance.enums');
  return useMemo(
    () => ({
      invoiceStatus: (value: string) => t('invoiceStatus', { value }),
      paymentStatus: (value: string) => t('paymentStatus', { value }),
      method: (value: string) => t('method', { value }),
      discountKind: (value: string) => t('discountKind', { value }),
      discountType: (value: string) => t('discountType', { value }),
      installmentStatus: (value: string) => t('installmentStatus', { value }),
      chequeStatus: (value: string) => t('chequeStatus', { value }),
      approvalStatus: (value: string) => t('approvalStatus', { value }),
      adjustmentKind: (value: string) => t('adjustmentKind', { value }),
      approvalType: (value: string) => t('approvalType', { value }),
      agingBucket: (value: string) => t('agingBucket', { value }),
      lateFeeType: (value: string) => t('lateFeeType', { value }),
      failure: (value: string) => t('failure', { value }),
    }),
    [t],
  );
}

const INVOICE_TONE: Record<string, Tone> = { draft: 'neutral', issued: 'info', partiallyPaid: 'warning', paid: 'success', void: 'danger' };
const PAYMENT_TONE: Record<string, Tone> = { pending: 'warning', succeeded: 'success', failed: 'danger', cancelled: 'neutral' };
const APPROVAL_TONE: Record<string, Tone> = { pending: 'warning', approved: 'success', rejected: 'danger' };

export function InvoiceStatusBadge({ status, overdue }: { status: InvoiceStatus | string; overdue?: boolean }) {
  const labels = useFinanceLabels();
  const t = useTranslations('finance.common');
  return (
    <span style={{ display: 'inline-flex', gap: 4, flexWrap: 'wrap' }}>
      <Badge tone={INVOICE_TONE[status] ?? 'neutral'} data-status={status}>{labels.invoiceStatus(status)}</Badge>
      {overdue && <Badge tone="danger">{t('overdue')}</Badge>}
    </span>
  );
}

export function PaymentStatusBadge({ status }: { status: PaymentStatus | string }) {
  const labels = useFinanceLabels();
  return <Badge tone={PAYMENT_TONE[status] ?? 'neutral'} data-status={status}>{labels.paymentStatus(status)}</Badge>;
}

export function ApprovalStatusBadge({ status }: { status: string }) {
  const labels = useFinanceLabels();
  return <Badge tone={APPROVAL_TONE[status] ?? 'neutral'} data-status={status}>{labels.approvalStatus(status)}</Badge>;
}

/** The invoice number, or "Draft" while it has none (numbers are assigned on issue). */
export function InvoiceNumber({ number }: { number: string | null }) {
  const t = useTranslations('finance.invoices');
  return number ? <span className="eemp-ltr">{number}</span> : <>{t('draftNumber')}</>;
}

export interface InvoiceViewProps {
  invoice: Invoice;
  /** Extra controls per installment (e.g. the portal's "Pay" button). */
  installmentAction?: (installmentId: string) => ReactNode;
  /** Extra content under the discounts table (e.g. approve buttons). */
  discountAction?: (discountId: string) => ReactNode;
}

/** Read-only invoice: items, totals, discounts, installments and adjustments. Shared by the console and the portal. */
export function InvoiceView({ invoice, installmentAction, discountAction }: InvoiceViewProps) {
  const t = useTranslations('finance.invoices');
  const common = useTranslations('finance.common');
  const labels = useFinanceLabels();
  const format = useLocaleFormat();
  const c = invoice.currency;
  const m = (amount: number) => <Money amount={amount} currency={c} />;
  return (
    <div style={{ display: 'grid', gap: 16 }}>
      <Card aria-labelledby="inv-lines">
        <h2 id="inv-lines" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('lines')}</h2>
        <DataTable
          caption={t('lines')}
          rows={invoice.lines}
          getRowKey={(l) => l.id}
          columns={[
            { key: 'd', header: t('description'), isRowHeader: true, cell: (l) => l.description },
            { key: 'q', header: t('quantity'), cell: (l) => format.number(l.quantity) },
            { key: 'u', header: t('unitPrice'), cell: (l) => m(l.unitPrice) },
            { key: 'a', header: t('lineAmount'), cell: (l) => m(l.amount) },
          ]}
        />
        <DescriptionList style={{ marginBlockStart: 16 }} data-testid="invoice-totals">
          <dt>{t('subtotal')}</dt>
          <dd>{m(invoice.subtotal)}</dd>
          {invoice.discountTotal > 0 && (
            <>
              <dt>{t('discountTotal')}</dt>
              <dd>{m(-invoice.discountTotal)}</dd>
            </>
          )}
          {invoice.taxTotal > 0 && (
            <>
              <dt>{t('tax', { rate: format.number(invoice.taxRatePercent) })}</dt>
              <dd>{m(invoice.taxTotal)}</dd>
            </>
          )}
          <dt>{t('total')}</dt>
          <dd><Money amount={invoice.total} currency={c} strong testId="invoice-total" /></dd>
          {invoice.lateFeeTotal > 0 && (
            <>
              <dt>{t('lateFees')}</dt>
              <dd>{m(invoice.lateFeeTotal)}</dd>
            </>
          )}
          {invoice.creditTotal > 0 && (
            <>
              <dt>{t('credits')}</dt>
              <dd>{m(-invoice.creditTotal)}</dd>
            </>
          )}
          <dt>{t('paid')}</dt>
          <dd><Money amount={invoice.paidAmount} currency={c} testId="invoice-paid" /></dd>
          <dt>{t('balance')}</dt>
          <dd><Money amount={invoice.balance} currency={c} strong testId="invoice-balance" /></dd>
        </DescriptionList>
      </Card>

      {(invoice.discounts.length > 0 || discountAction) && (
        <Card aria-labelledby="inv-discounts">
          <h2 id="inv-discounts" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('discounts')}</h2>
          <DataTable
            caption={t('discounts')}
            rows={invoice.discounts}
            getRowKey={(d) => d.id}
            empty={<p>{t('noDiscounts')}</p>}
            columns={[
              { key: 'k', header: t('discountKind'), isRowHeader: true, cell: (d) => labels.discountKind(d.kind) },
              { key: 'v', header: t('discountValue'), cell: (d) => (d.type === 'percent' ? format.number(d.value / 100, { style: 'percent', maximumFractionDigits: 2 }) : m(d.value)) },
              { key: 'a', header: common('amount'), cell: (d) => (d.status === 'approved' ? m(d.amount) : common('none')) },
              { key: 'r', header: common('reason'), cell: (d) => d.reason },
              { key: 's', header: '', cell: (d) => <ApprovalStatusBadge status={d.status} /> },
              ...(discountAction ? [{ key: 'x', header: '', cell: (d: Invoice['discounts'][number]) => discountAction(d.id) }] : []),
            ]}
          />
        </Card>
      )}

      <Card aria-labelledby="inv-installments">
        <h2 id="inv-installments" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('installments')}</h2>
        {invoice.installments.length === 0 ? (
          <p style={{ marginBlockEnd: 0 }}>{t('noInstallments')}</p>
        ) : (
          <DataTable
            caption={t('installments')}
            rows={invoice.installments}
            getRowKey={(i) => i.id}
            columns={[
              { key: 'n', header: t('seq'), isRowHeader: true, cell: (i) => format.number(i.seqNo) },
              { key: 'd', header: common('dueDate'), cell: (i) => format.calendarDate(i.dueDate) },
              { key: 'a', header: common('amount'), cell: (i) => m(i.amount) },
              { key: 'p', header: t('installmentPaid'), cell: (i) => m(i.paidAmount) },
              {
                key: 's', header: '',
                cell: (i) => <Badge tone={i.status === 'paid' ? 'success' : i.status === 'overdue' ? 'danger' : 'neutral'} data-status={i.status}>{labels.installmentStatus(i.status)}</Badge>,
              },
              ...(installmentAction ? [{ key: 'x', header: '', cell: (i: Invoice['installments'][number]) => installmentAction(i.id) }] : []),
            ]}
          />
        )}
      </Card>

      {invoice.adjustments.length > 0 && (
        <Card aria-labelledby="inv-adjustments">
          <h2 id="inv-adjustments" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('adjustments')}</h2>
          <DataTable
            caption={t('adjustments')}
            rows={invoice.adjustments}
            getRowKey={(a) => a.id}
            columns={[
              { key: 'k', header: common('number'), isRowHeader: true, cell: (a) => (a.number ? <span className="eemp-ltr">{a.number}</span> : labels.adjustmentKind(a.kind)) },
              { key: 't', header: '', cell: (a) => labels.adjustmentKind(a.kind) },
              { key: 'a', header: common('amount'), cell: (a) => m(a.amount) },
              { key: 'r', header: common('reason'), cell: (a) => a.reason },
              { key: 'd', header: common('date'), cell: (a) => format.instantDate(a.createdAt) },
            ]}
          />
        </Card>
      )}
    </div>
  );
}
