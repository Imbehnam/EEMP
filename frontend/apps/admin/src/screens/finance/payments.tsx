'use client';

import {
  MANUAL_PAYMENT_METHODS, newIdempotencyKey, PAYMENT_METHODS, PAYMENT_STATUSES, type InvoiceSummary, type Payment, type PaymentMethod, type Refund,
} from '@eemp/api-client';
import { ButtonLink, DateField, ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession, useTodayIso } from '@eemp/app';
import {
  AmountField, ApprovalStatusBadge, FileDownloadButton, Money, PaymentStatusBadge, useFinanceLabels, useMoney, usePdfLanguage,
} from '@eemp/app/finance';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link, useRouter } from '@eemp/i18n/navigation';
import {
  Alert, Button, Card, ConfirmDialog, DataTable, DescriptionList, FormGrid, PageHeader, SelectField, Spinner, StatePanel, TextAreaField, TextField,
} from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useMemo, useState } from 'react';

import { BackLink, PagedList, useFieldErrors } from '../../components/common';
import { StudentPicker, sectionTitle, useTenantCurrency, useUserNames } from './common';

/* ======================= list ======================= */

export function PaymentsScreen() {
  const t = useTranslations('finance.payments');
  const fc = useTranslations('finance.common');
  const labels = useFinanceLabels();
  const format = useLocaleFormat();
  const { can } = useSession();
  const [method, setMethod] = useState('');
  const [status, setStatus] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={can('finance.payments.manage') && <ButtonLink href="/finance/payments/new">{t('record')}</ButtonLink>} />
      <FormGrid style={{ marginBlockEnd: 12 }}>
        <SelectField label={fc('method')} name="method" value={method} placeholder={t('allMethods')} onChange={(e) => setMethod(e.target.value)}
          options={PAYMENT_METHODS.map((m) => ({ value: m, label: labels.method(m) }))} />
        <SelectField label={fc('filter')} name="status" value={status} placeholder={t('allStatuses')} onChange={(e) => setStatus(e.target.value)}
          options={PAYMENT_STATUSES.map((m) => ({ value: m, label: labels.paymentStatus(m) }))} />
        <DateField label={fc('from')} name="from" value={from} onChange={setFrom} />
        <DateField label={fc('to')} name="to" value={to} onChange={setTo} />
      </FormGrid>
      <PagedList<Payment>
        queryKey={`payments|${method}|${status}|${from}|${to}`}
        load={(api, q) => api.finance.payments.list({ ...q, method: method || undefined, status: status || undefined, from: from || undefined, to: to || undefined })}
        caption={t('title')}
        searchLabel={t('searchLabel')}
        emptyText={t('empty')}
        getRowKey={(r) => r.id}
        columns={[
          { key: 'r', header: t('receipt'), isRowHeader: true, cell: (r) => <Link href={`/finance/payments/${r.id}`}>{r.receiptNumber ? <span className="eemp-ltr">{r.receiptNumber}</span> : labels.paymentStatus(r.status)}</Link> },
          { key: 's', header: fc('student'), cell: (r) => r.studentName },
          { key: 'd', header: t('receivedOn'), cell: (r) => format.calendarDate(r.receivedOn) },
          { key: 'm', header: fc('method'), cell: (r) => labels.method(r.method) },
          { key: 'a', header: fc('amount'), cell: (r) => <Money amount={r.amount} currency={r.currency} /> },
          { key: 'st', header: '', cell: (r) => <PaymentStatusBadge status={r.status} /> },
        ]}
      />
    </>
  );
}

/* ======================= record ======================= */

export function RecordPaymentScreen({ studentId: initialStudent, invoiceId: initialInvoice }: { studentId?: string; invoiceId?: string }) {
  const t = useTranslations('finance.payments');
  const fc = useTranslations('finance.common');
  const common = useTranslations('common');
  const labels = useFinanceLabels();
  const router = useRouter();
  const today = useTodayIso();
  const currency = useTenantCurrency();
  const money = useMoney();
  const [studentId, setStudentId] = useState(initialStudent ?? '');
  const [invoiceId, setInvoiceId] = useState(initialInvoice ?? '');
  const [method, setMethod] = useState<PaymentMethod>('cash');
  const [amount, setAmount] = useState<number | null>(null);
  const [receivedOn, setReceivedOn] = useState('');
  const [reference, setReference] = useState('');
  const [cheque, setCheque] = useState({ number: '', bank: '', dueDate: '' });
  // One key per form: a double click or a retry after a network error records the payment once.
  const idempotencyKey = useMemo(() => newIdempotencyKey('pay'), []);
  const invoices = useApiQuery<InvoiceSummary[]>(`pay-invoices|${studentId}`, async (api) =>
    studentId ? (await api.finance.invoices.list({ studentId, pageSize: 100 })).items.filter((i) => i.status === 'issued' || i.status === 'partiallyPaid') : [],
  );
  const record = useApiMutation((api) =>
    api.finance.payments.record({
      studentId, method, amount: amount ?? 0, receivedOn: receivedOn || today, reference: reference.trim() || null, invoiceId: invoiceId || null,
      cheque: method === 'cheque' ? cheque : null, idempotencyKey,
    }),
  );
  const errors = useFieldErrors(record.error);
  return (
    <>
      <PageHeader before={<BackLink href="/finance/payments" />} title={t('recordTitle')} description={t('recordIntro')} />
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          const r = await record.run();
          if (r.ok) router.push(`/finance/payments/${r.value.id}?notice=${r.value.receiptNumber ? 'recorded' : 'pending'}`);
        }}
      >
        <ErrorAlert error={record.error} />
        <Card style={{ display: 'grid', gap: 16 }}>
          <FormGrid>
            <StudentPicker value={studentId} required error={errors.studentId} onChange={(id) => { setStudentId(id); setInvoiceId(''); }} />
            <div style={{ display: 'grid', gap: 8, alignContent: 'start' }}>
              <SelectField label={t('invoice')} name="invoiceId" value={invoiceId} placeholder={t('oldestFirst')} error={errors.invoiceId} onChange={(e) => setInvoiceId(e.target.value)}
                options={(invoices.data ?? []).map((i) => ({ value: i.id, label: `${i.number ?? ''} · ${money.format(i.balance, i.currency)}` }))} />
              <SelectField label={fc('method')} name="method" required value={method} error={errors.method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}
                options={MANUAL_PAYMENT_METHODS.map((m) => ({ value: m, label: labels.method(m) }))} />
            </div>
          </FormGrid>
          <FormGrid>
            <AmountField label={fc('amount')} name="amount" required currency={currency} error={errors.amount} onAmount={setAmount} />
            <DateField label={t('receivedOn')} name="receivedOn" value={receivedOn || today} onChange={setReceivedOn} error={errors.receivedOn} />
            <TextField label={fc('reference')} name="reference" dir="ltr" value={reference} onChange={(e) => setReference(e.target.value)} />
          </FormGrid>
          {method === 'cheque' && (
            <FormGrid>
              <TextField label={t('chequeNumber')} name="chequeNumber" dir="ltr" required value={cheque.number} error={errors['cheque.number']} onChange={(e) => setCheque({ ...cheque, number: e.target.value })} />
              <TextField label={t('chequeBank')} name="chequeBank" required value={cheque.bank} error={errors['cheque.bank']} onChange={(e) => setCheque({ ...cheque, bank: e.target.value })} />
              <DateField label={t('chequeDue')} name="chequeDue" required value={cheque.dueDate} error={errors['cheque.dueDate']} onChange={(dueDate) => setCheque({ ...cheque, dueDate })} />
            </FormGrid>
          )}
          <div style={{ display: 'flex', gap: 8 }}>
            <Button type="submit" loading={record.busy} disabled={!studentId || !amount}>{t('submit')}</Button>
            <ButtonLink href="/finance/payments" variant="secondary">{common('cancel')}</ButtonLink>
          </div>
        </Card>
      </form>
    </>
  );
}

/* ======================= detail ======================= */

export function PaymentDetailScreen({ id, notice }: { id: string; notice?: string }) {
  const t = useTranslations('finance.payments');
  const fc = useTranslations('finance.common');
  const common = useTranslations('common');
  const labels = useFinanceLabels();
  const format = useLocaleFormat();
  const { can, user } = useSession();
  const userName = useUserNames();
  const pdfLang = usePdfLanguage();
  const query = useApiQuery<Payment>(`payment|${id}`, (api) => api.finance.payments.get(id));
  const refunds = useApiQuery<Refund[]>(`payment-refunds|${id}`, async (api) => (await api.finance.refunds.list()).filter((r) => r.paymentId === id));
  const [dialog, setDialog] = useState<'cleared' | 'bounced' | 'refund' | 'resubmit' | null>(null);
  const [returned, setReturned] = useState<Refund | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [amount, setAmount] = useState<number | null>(null);
  const [reason, setReason] = useState('');
  const action = useApiMutation(async (api, kind: 'cleared' | 'bounced' | 'refund' | 'resubmit', p: Payment) => {
    if (kind === 'resubmit' && returned) {
      // DL-081: the maker edits a refund the approver returned and resubmits it to its workflow.
      await api.finance.refunds.resubmit(returned.id, { amount: amount ?? 0, reason: reason.trim(), rowVersion: returned.rowVersion });
      return api.finance.payments.get(p.id);
    }
    if (kind === 'refund' || kind === 'resubmit') {
      await api.finance.payments.requestRefund(p.id, { amount: amount ?? 0, reason: reason.trim() });
      return api.finance.payments.get(p.id);
    }
    return api.finance.payments.chequeStatus(p.id, { rowVersion: p.rowVersion, status: kind });
  });
  const errors = useFieldErrors(action.error);
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const p = query.data;
  if (!p) return <Spinner label={common('loading')} />;
  const c = p.currency;
  return (
    <>
      <PageHeader
        before={<BackLink href="/finance/payments" />}
        title={p.receiptNumber ? t('receiptTitle', { receipt: p.receiptNumber }) : t('detailTitle')}
        description={<>{p.studentName} · <Link href={`/finance/accounts/${p.studentId}`}>{fc('student')}</Link></>}
        actions={
          <>
            {can('finance.payments.manage') && p.method === 'cheque' && p.status === 'pending' && (
              <>
                <Button onClick={() => { action.reset(); setDialog('cleared'); }}>{t('markCleared')}</Button>
                <Button variant="danger" onClick={() => { action.reset(); setDialog('bounced'); }}>{t('markBounced')}</Button>
              </>
            )}
            {can('finance.payments.manage') && p.status === 'succeeded' && p.unallocated > 0 && (
              <Button variant="secondary" onClick={() => { action.reset(); setAmount(null); setReason(''); setDialog('refund'); }}>{t('requestRefund')}</Button>
            )}
            {p.receiptNumber && (
              <FileDownloadButton testId="receipt-pdf" labelKey="downloadReceipt" load={(api) => api.finance.payments.receiptPdf(p.id, pdfLang)} fallbackName={`receipt-${p.receiptNumber}.pdf`} />
            )}
          </>
        }
      />
      {notice === 'recorded' && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }} data-testid="payment-recorded">{t('recorded', { receipt: p.receiptNumber ?? '' })}</Alert>}
      {notice === 'pending' && <Alert tone="info" role="status" style={{ marginBlockEnd: 12 }}>{t('recordedPending')}</Alert>}
      {message && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }}>{message}</Alert>}
      <div style={{ display: 'grid', gap: 16 }}>
        <Card>
          <DescriptionList data-testid="payment-summary">
            <dt>{t('receipt')}</dt>
            <dd data-testid="receipt-number">{p.receiptNumber ? <span className="eemp-ltr">{p.receiptNumber}</span> : fc('none')}</dd>
            <dt>{common('status')}</dt>
            <dd><PaymentStatusBadge status={p.status} />{p.failureCode && <> · {labels.failure(p.failureCode)}</>}</dd>
            <dt>{fc('method')}</dt>
            <dd>{labels.method(p.method)}</dd>
            <dt>{fc('amount')}</dt>
            <dd><Money amount={p.amount} currency={c} strong /></dd>
            <dt>{t('receivedOn')}</dt>
            <dd>{format.calendarDate(p.receivedOn)}</dd>
            {p.reference && (<><dt>{fc('reference')}</dt><dd className="eemp-ltr">{p.reference}</dd></>)}
            {p.cheque && (
              <>
                <dt>{t('cheque')}</dt>
                <dd><span className="eemp-ltr">{p.cheque.number}</span> · {p.cheque.bank} · {format.calendarDate(p.cheque.dueDate)} · {labels.chequeStatus(p.cheque.status ?? '')}</dd>
              </>
            )}
            {p.provider && (<><dt>{t('provider')}</dt><dd>{p.provider}{p.providerRef && <> · <span className="eemp-ltr">{p.providerRef}</span></>}</dd></>)}
            <dt>{t('allocated')}</dt>
            <dd><Money amount={p.allocated} currency={c} /></dd>
            <dt>{t('unallocated')}</dt>
            <dd><Money amount={p.unallocated} currency={c} testId="payment-credit" /></dd>
            {p.refunded > 0 && (<><dt>{t('refunded')}</dt><dd><Money amount={p.refunded} currency={c} /></dd></>)}
          </DescriptionList>
        </Card>
        <Card aria-labelledby="alloc-h">
          <h2 id="alloc-h" style={sectionTitle}>{t('allocations')}</h2>
          <DataTable
            caption={t('allocations')}
            rows={p.allocations}
            getRowKey={(a) => `${a.invoiceId}|${a.installmentId ?? ''}`}
            empty={<StatePanel title={t('noAllocations')} />}
            columns={[
              { key: 'i', header: t('invoiceOnly'), isRowHeader: true, cell: (a) => <Link href={`/finance/invoices/${a.invoiceId}`}><span className="eemp-ltr">{a.invoiceNumber}</span></Link> },
              { key: 'a', header: fc('amount'), cell: (a) => <Money amount={a.amount} currency={c} /> },
            ]}
          />
        </Card>
        {(refunds.data ?? []).length > 0 && (
          <Card aria-labelledby="refunds-h">
            <h2 id="refunds-h" style={sectionTitle}>{t('refunds')}</h2>
            <DataTable
              caption={t('refunds')}
              rows={refunds.data ?? []}
              getRowKey={(r) => r.id}
              columns={[
                { key: 'a', header: fc('amount'), isRowHeader: true, cell: (r) => <Money amount={r.amount} currency={r.currency} /> },
                { key: 'r', header: fc('reason'), cell: (r) => r.reason },
                { key: 'b', header: fc('requestedBy'), cell: (r) => userName(r.requestedBy) },
                { key: 's', header: '', cell: (r) => <ApprovalStatusBadge status={r.status} /> },
                {
                  key: 'x', header: common('actions'),
                  cell: (r) => (
                    <span style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                      {r.status === 'returned' && r.requestedBy === user.id && can('finance.payments.manage') && (
                        <Button size="sm" data-testid="refund-resubmit"
                          onClick={() => { action.reset(); setReturned(r); setAmount(r.amount); setReason(r.reason); setDialog('resubmit'); }}>{fc('resubmit')}</Button>
                      )}
                      {r.workflowInstanceId && <Link href={`/workflow/instances/${r.workflowInstanceId}`}>{fc('viewApproval')}</Link>}
                    </span>
                  ),
                },
              ]}
            />
          </Card>
        )}
      </div>
      <ConfirmDialog
        open={dialog !== null}
        tone={dialog === 'bounced' ? 'danger' : 'primary'}
        title={dialog === 'refund' ? t('refundTitle') : dialog === 'resubmit' ? t('resubmitTitle') : dialog === 'cleared' ? t('markCleared') : t('markBounced')}
        description={dialog === 'refund' ? t('refundBody') : dialog === 'resubmit' ? t('resubmitBody') : undefined}
        confirmLabel={dialog === 'refund' ? t('requestRefund') : dialog === 'resubmit' ? fc('resubmit') : dialog === 'cleared' ? t('markCleared') : t('markBounced')}
        cancelLabel={common('cancel')}
        busy={action.busy}
        onCancel={() => setDialog(null)}
        onConfirm={async () => {
          if (!dialog) return;
          if ((dialog === 'refund' || dialog === 'resubmit') && (!amount || !reason.trim())) return;
          const r = await action.run(dialog, p);
          if (r.ok) {
            query.setData(r.value);
            refunds.reload();
            setMessage(dialog === 'refund' ? t('refundRequested') : dialog === 'resubmit' ? fc('resubmitted') : t('chequeUpdated'));
            setDialog(null);
          }
        }}
      >
        <ErrorAlert error={action.error} onReload={() => { action.reset(); setDialog(null); query.reload(); }} />
        {(dialog === 'refund' || dialog === 'resubmit') && (
          <div style={{ display: 'grid', gap: 12 }}>
            <AmountField label={fc('amount')} name="refundAmount" required currency={c} error={errors.amount} onAmount={setAmount}
              initial={dialog === 'resubmit' ? returned?.amount : undefined} />
            <TextAreaField label={fc('reason')} name="refundReason" required value={reason} error={errors.reason} onChange={(e) => setReason(e.target.value)} />
          </div>
        )}
      </ConfirmDialog>
    </>
  );
}
