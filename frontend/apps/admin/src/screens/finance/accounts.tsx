'use client';

import type { StudentAccount } from '@eemp/api-client';
import { ButtonLink, ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { InvoiceNumber, InvoiceStatusBadge, Money, PaymentStatusBadge, useFinanceLabels } from '@eemp/app/finance';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link, useRouter } from '@eemp/i18n/navigation';
import { Alert, Button, Card, ConfirmDialog, DataTable, PageHeader, SelectField, Spinner, StatePanel } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState, type ReactNode } from 'react';

import { BackLink } from '../../components/common';
import { StudentPicker, sectionTitle } from './common';

export function AccountsScreen() {
  const t = useTranslations('finance.accounts');
  const router = useRouter();
  const [studentId, setStudentId] = useState('');
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      <Card style={{ maxInlineSize: '36rem' }}>
        <form onSubmit={(e) => { e.preventDefault(); if (studentId) router.push(`/finance/accounts/${studentId}`); }} style={{ display: 'grid', gap: 12 }}>
          <StudentPicker value={studentId} onChange={setStudentId} required />
          <div><Button type="submit" disabled={!studentId}>{t('pick')}</Button></div>
        </form>
      </Card>
    </>
  );
}

interface LedgerRow {
  key: string;
  date: string;
  label: ReactNode;
  debit: number;
  credit: number;
  running: number;
}

/**
 * Ledger built for display from the account's issued invoices (charges) and succeeded payments (credits); the
 * balances shown in the summary are the server's. Voided invoices and their released payments are listed without
 * a charge.
 */
function ledger(account: StudentAccount, labels: { invoice: (n: string) => string; payment: (r: string) => string }): LedgerRow[] {
  const entries = [
    ...account.invoices
      .filter((i) => i.status !== 'draft')
      .map((i) => ({ key: `i${i.id}`, date: i.issueDate ?? '', label: <Link href={`/finance/invoices/${i.id}`}>{labels.invoice(i.number ?? '')}</Link>, debit: i.status === 'void' ? 0 : i.total, credit: 0 })),
    ...account.payments
      .filter((p) => p.status === 'succeeded')
      .map((p) => ({ key: `p${p.id}`, date: p.receivedOn, label: <Link href={`/finance/payments/${p.id}`}>{labels.payment(p.receiptNumber ?? '')}</Link>, debit: 0, credit: p.amount - p.refunded })),
  ].sort((a, b) => a.date.localeCompare(b.date) || (a.debit ? -1 : 1));
  let running = 0;
  return entries.map((e) => {
    running += e.debit - e.credit;
    return { ...e, running };
  });
}

export function AccountDetailScreen({ studentId }: { studentId: string }) {
  const t = useTranslations('finance.accounts');
  const fc = useTranslations('finance.common');
  const ti = useTranslations('finance.invoices');
  const common = useTranslations('common');
  const labels = useFinanceLabels();
  const format = useLocaleFormat();
  const { can } = useSession();
  const query = useApiQuery<StudentAccount>(`account|${studentId}`, (api) => api.finance.students.account(studentId));
  const [apply, setApply] = useState(false);
  const [invoiceId, setInvoiceId] = useState('');
  const [message, setMessage] = useState(false);
  const applyCredit = useApiMutation((api) => api.finance.students.applyCredit(studentId, invoiceId));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const a = query.data;
  if (!a) return <Spinner label={common('loading')} />;
  const c = a.currency;
  const openInvoices = a.invoices.filter((i) => i.status === 'issued' || i.status === 'partiallyPaid');
  const rows = ledger(a, { invoice: (n) => t('entryInvoice', { number: n }), payment: (r) => t('entryPayment', { receipt: r }) });
  const tile = (label: string, amount: number, testId: string, tone?: string) => (
    <Card style={{ padding: 16 }}>
      <div style={{ fontSize: '0.8125rem', color: 'var(--eemp-color-fg-muted)' }}>{label}</div>
      <div style={{ fontSize: '1.25rem', color: tone }}><Money amount={amount} currency={c} strong testId={testId} /></div>
    </Card>
  );
  return (
    <>
      <PageHeader
        before={<BackLink href="/finance/accounts" />}
        title={t('detailTitle', { name: a.studentName })}
        description={<span className="eemp-ltr">{a.studentNumber}</span>}
        actions={
          <>
            {can('finance.payments.manage') && <ButtonLink href={`/finance/payments/new?studentId=${studentId}`}>{ti('recordPayment')}</ButtonLink>}
            {can('finance.invoices.manage') && a.creditBalance > 0 && openInvoices.length > 0 && (
              <Button variant="secondary" onClick={() => { applyCredit.reset(); setInvoiceId(openInvoices[0]!.id); setApply(true); }}>{t('applyCredit')}</Button>
            )}
          </>
        }
      />
      {message && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }}>{t('creditApplied')}</Alert>}
      <div style={{ display: 'grid', gap: 12, gridTemplateColumns: 'repeat(auto-fit, minmax(11rem, 1fr))', marginBlockEnd: 16 }}>
        {tile(t('totalInvoiced'), a.totalInvoiced, 'acct-invoiced')}
        {tile(t('totalPaid'), a.totalPaid, 'acct-paid')}
        {tile(t('balance'), a.balance, 'acct-balance')}
        {tile(t('credit'), a.creditBalance, 'acct-credit')}
        {tile(t('overdue'), a.overdue, 'acct-overdue', a.overdue > 0 ? 'var(--eemp-color-danger)' : undefined)}
      </div>
      <div style={{ display: 'grid', gap: 16 }}>
        <Card aria-labelledby="ledger-h">
          <h2 id="ledger-h" style={sectionTitle}>{t('ledger')}</h2>
          <DataTable
            caption={t('ledger')}
            rows={rows}
            getRowKey={(r) => r.key}
            empty={<StatePanel title={t('noEntries')} />}
            columns={[
              { key: 'd', header: fc('date'), cell: (r) => (r.date ? format.calendarDate(r.date) : fc('none')) },
              { key: 'l', header: fc('reference'), isRowHeader: true, cell: (r) => r.label },
              { key: 'db', header: t('debit'), cell: (r) => (r.debit ? <Money amount={r.debit} currency={c} /> : '') },
              { key: 'cr', header: t('creditColumn'), cell: (r) => (r.credit ? <Money amount={r.credit} currency={c} /> : '') },
              { key: 'rb', header: t('running'), cell: (r) => <Money amount={r.running} currency={c} /> },
            ]}
          />
        </Card>
        <Card aria-labelledby="acct-inv-h">
          <h2 id="acct-inv-h" style={sectionTitle}>{t('invoices')}</h2>
          <DataTable
            caption={t('invoices')}
            rows={a.invoices}
            getRowKey={(r) => r.id}
            empty={<StatePanel title={t('noEntries')} />}
            columns={[
              { key: 'n', header: fc('number'), isRowHeader: true, cell: (r) => <Link href={`/finance/invoices/${r.id}`}><InvoiceNumber number={r.number} /></Link> },
              { key: 's', header: common('status'), cell: (r) => <InvoiceStatusBadge status={r.status} overdue={r.isOverdue} /> },
              { key: 'd', header: fc('dueDate'), cell: (r) => (r.dueDate ? format.calendarDate(r.dueDate) : fc('none')) },
              { key: 't', header: fc('total'), cell: (r) => <Money amount={r.total} currency={r.currency} /> },
              { key: 'b', header: fc('balance'), cell: (r) => <Money amount={r.balance} currency={r.currency} /> },
            ]}
          />
        </Card>
        <Card aria-labelledby="acct-pay-h">
          <h2 id="acct-pay-h" style={sectionTitle}>{t('payments')}</h2>
          <DataTable
            caption={t('payments')}
            rows={a.payments}
            getRowKey={(r) => r.id}
            empty={<StatePanel title={t('noEntries')} />}
            columns={[
              { key: 'r', header: fc('number'), isRowHeader: true, cell: (r) => <Link href={`/finance/payments/${r.id}`}>{r.receiptNumber ? <span className="eemp-ltr">{r.receiptNumber}</span> : labels.paymentStatus(r.status)}</Link> },
              { key: 'd', header: fc('date'), cell: (r) => format.calendarDate(r.receivedOn) },
              { key: 'm', header: fc('method'), cell: (r) => labels.method(r.method) },
              { key: 'a', header: fc('amount'), cell: (r) => <Money amount={r.amount} currency={r.currency} /> },
              { key: 'u', header: t('credit'), cell: (r) => <Money amount={r.unallocated} currency={r.currency} /> },
              { key: 's', header: '', cell: (r) => <PaymentStatusBadge status={r.status} /> },
            ]}
          />
        </Card>
      </div>
      <ConfirmDialog
        open={apply}
        tone="primary"
        title={t('applyCreditTitle')}
        description={t('applyCreditBody')}
        confirmLabel={t('applyCredit')}
        cancelLabel={common('cancel')}
        busy={applyCredit.busy}
        onCancel={() => setApply(false)}
        onConfirm={async () => {
          const r = await applyCredit.run();
          if (r.ok) {
            setApply(false);
            setMessage(true);
            query.reload();
          }
        }}
      >
        <ErrorAlert error={applyCredit.error} />
        <SelectField label={fc('number')} name="invoiceId" value={invoiceId} onChange={(e) => setInvoiceId(e.target.value)}
          options={openInvoices.map((i) => ({ value: i.id, label: i.number ?? '' }))} />
      </ConfirmDialog>
    </>
  );
}
