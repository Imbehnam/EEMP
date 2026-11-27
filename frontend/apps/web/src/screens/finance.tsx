'use client';

import { authorityFromRedirect, isApiError, newIdempotencyKey, type Invoice, type PaymentStatusResult, type StudentAccount } from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery } from '@eemp/app';
import {
  FileDownloadButton, InvoiceNumber, InvoiceStatusBadge, InvoiceView, Money, MoneyUnitToggle, PaymentStatusBadge, useFinanceLabels, useMoney, usePdfLanguage,
} from '@eemp/app/finance';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link, useRouter } from '@eemp/i18n/navigation';
import { Alert, Button, Card, ConfirmDialog, DataTable, PageHeader, Spinner, StatePanel } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useEffect, useRef, useState } from 'react';

import { StudentPage } from './academic';

const backLink = (href: string, label: string) => (
  <Link href={href} style={{ fontSize: '0.875rem' }}><span aria-hidden="true" className="eemp-flip">{'←'}</span> {label}</Link>
);

/* ======================= account: invoices, installments, balance, receipts ======================= */

export function PortalFinanceScreen({ student }: { student?: string }) {
  const t = useTranslations('finance.portal');
  return (
    <StudentPage title={t('title')} intro={t('intro')} selected={student}>
      {(studentId) => <AccountData key={studentId} studentId={studentId} />}
    </StudentPage>
  );
}

function AccountData({ studentId }: { studentId: string }) {
  const t = useTranslations('finance.portal');
  const fc = useTranslations('finance.common');
  const labels = useFinanceLabels();
  const format = useLocaleFormat();
  const pdfLang = usePdfLanguage();
  const common = useTranslations('common');
  const query = useApiQuery<StudentAccount>(`portal-account|${studentId}`, (api) => api.finance.portal.account(studentId));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const a = query.data;
  if (!a) return <Spinner label={common('loading')} />;
  const c = a.currency;
  const tile = (label: string, amount: number, testId: string, danger?: boolean) => (
    <Card style={{ padding: 16 }}>
      <div style={{ fontSize: '0.8125rem', color: 'var(--eemp-color-fg-muted)' }}>{label}</div>
      <div style={{ fontSize: '1.25rem', color: danger ? 'var(--eemp-color-danger)' : undefined }}><Money amount={amount} currency={c} strong testId={testId} /></div>
    </Card>
  );
  return (
    <div style={{ display: 'grid', gap: 16 }}>
      <div style={{ display: 'grid', gap: 12, gridTemplateColumns: 'repeat(auto-fit, minmax(11rem, 1fr))' }}>
        {tile(t('balance'), a.balance, 'portal-balance')}
        {tile(t('overdue'), a.overdue, 'portal-overdue', a.overdue > 0)}
        {tile(t('paid'), a.totalPaid, 'portal-paid')}
        {a.creditBalance > 0 && tile(t('credit'), a.creditBalance, 'portal-credit')}
      </div>
      <MoneyUnitToggle currency={c} />
      <Card aria-labelledby="portal-inv-h">
        <h2 id="portal-inv-h" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('invoices')}</h2>
        <DataTable
          caption={t('invoices')}
          rows={a.invoices}
          getRowKey={(r) => r.id}
          empty={<StatePanel title={t('noInvoices')} />}
          columns={[
            { key: 'n', header: fc('number'), isRowHeader: true, cell: (r) => <Link href={`/invoices/${r.id}`}><InvoiceNumber number={r.number} /></Link> },
            { key: 'd', header: fc('dueDate'), cell: (r) => (r.dueDate ? format.calendarDate(r.dueDate) : fc('none')) },
            { key: 't', header: fc('total'), cell: (r) => <Money amount={r.total} currency={r.currency} /> },
            { key: 'b', header: fc('balance'), cell: (r) => <Money amount={r.balance} currency={r.currency} /> },
            { key: 's', header: '', cell: (r) => <InvoiceStatusBadge status={r.status} overdue={r.isOverdue} /> },
          ]}
        />
      </Card>
      <Card aria-labelledby="portal-pay-h">
        <h2 id="portal-pay-h" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('payments')}</h2>
        <DataTable
          caption={t('payments')}
          rows={a.payments}
          getRowKey={(r) => r.id}
          empty={<StatePanel title={t('noPayments')} />}
          columns={[
            { key: 'r', header: fc('number'), isRowHeader: true, cell: (r) => (r.receiptNumber ? <span className="eemp-ltr">{r.receiptNumber}</span> : t('pending')) },
            { key: 'd', header: fc('date'), cell: (r) => format.calendarDate(r.receivedOn) },
            { key: 'm', header: fc('method'), cell: (r) => labels.method(r.method) },
            { key: 'a', header: fc('amount'), cell: (r) => <Money amount={r.amount} currency={r.currency} /> },
            { key: 's', header: '', cell: (r) => <PaymentStatusBadge status={r.status} /> },
            {
              key: 'x', header: '',
              cell: (r) => r.receiptNumber && (
                <FileDownloadButton variant="ghost" labelKey="downloadReceipt" testId={`receipt-${r.receiptNumber}`}
                  load={(api) => api.finance.portal.receiptPdf(r.id, pdfLang)} fallbackName={`receipt-${r.receiptNumber}.pdf`} />
              ),
            },
          ]}
        />
      </Card>
    </div>
  );
}

/* ======================= invoice + pay online ======================= */

export function PortalInvoiceScreen({ id }: { id: string }) {
  const t = useTranslations('finance.portal');
  const common = useTranslations('common');
  const money = useMoney();
  const router = useRouter();
  const pdfLang = usePdfLanguage();
  const query = useApiQuery<Invoice>(`portal-invoice|${id}`, (api) => api.finance.portal.invoice(id));
  const [pay, setPay] = useState<{ installmentId: string | null; amount: number; key: string } | null>(null);
  const start = useApiMutation((api, p: NonNullable<typeof pay>) => api.finance.portal.pay(id, { installmentId: p.installmentId, idempotencyKey: p.key }));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const inv = query.data;
  const open = inv && (inv.status === 'issued' || inv.status === 'partiallyPaid') && inv.balance > 0;
  // One idempotency key per opened dialog: pressing "continue" twice starts one payment.
  const ask = (installmentId: string | null, amount: number) => { start.reset(); setPay({ installmentId, amount, key: newIdempotencyKey('online') }); };
  return (
    <>
      <PageHeader
        before={backLink('/invoices', t('backToInvoices'))}
        title={inv ? t('invoiceTitle', { number: inv.number ?? '' }) : ''}
        actions={inv && (
          <>
            {open && <Button onClick={() => ask(null, inv.balance)} data-testid="pay-balance">{t('payBalance')}</Button>}
            <FileDownloadButton testId="invoice-pdf" load={(api) => api.finance.portal.invoicePdf(inv.id, pdfLang)} fallbackName={`invoice-${inv.number}.pdf`} />
          </>
        )}
      />
      {!inv ? (
        <Spinner label={common('loading')} />
      ) : (
        <>
          <p style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
            <InvoiceStatusBadge status={inv.status} overdue={inv.isOverdue} /> {inv.studentName}
          </p>
          {inv.isOverdue && <Alert tone="warning" style={{ marginBlockEnd: 12 }}>{t('overdueNotice')}</Alert>}
          <InvoiceView
            invoice={inv}
            installmentAction={(installmentId) => {
              const i = inv.installments.find((x) => x.id === installmentId);
              const remaining = i ? Math.min(i.amount - i.paidAmount, inv.balance) : 0;
              return open && i && remaining > 0 ? (
                <Button size="sm" onClick={() => ask(i.id, remaining)} data-testid={`pay-installment-${i.seqNo}`}>{t('payInstallment')}</Button>
              ) : null;
            }}
          />
        </>
      )}
      <ConfirmDialog
        open={pay !== null}
        tone="primary"
        title={t('payTitle')}
        description={pay && inv ? t('payBody', { amount: money.format(pay.amount, inv.currency) }) : undefined}
        confirmLabel={t('payGo')}
        cancelLabel={common('cancel')}
        busy={start.busy}
        onCancel={() => setPay(null)}
        onConfirm={async () => {
          if (!pay) return;
          const r = await start.run(pay);
          if (!r.ok) return;
          const { paymentId, redirectUrl, provider } = r.value;
          if (provider === 'fake') {
            // Development gateway: the portal plays the bank page (no real money).
            const authority = authorityFromRedirect(redirectUrl) ?? '';
            router.push(`/pay/fake?paymentId=${encodeURIComponent(paymentId)}&authority=${encodeURIComponent(authority)}&invoiceId=${encodeURIComponent(id)}`);
          } else if (/^https:\/\//.test(redirectUrl)) {
            window.location.assign(redirectUrl);
          }
        }}
      >
        <ErrorAlert error={start.error} />
      </ConfirmDialog>
    </>
  );
}

/* ======================= fake gateway (development) ======================= */

export function FakeGatewayScreen({ paymentId, authority, invoiceId }: { paymentId?: string; authority?: string; invoiceId?: string }) {
  const t = useTranslations('finance.portal');
  const router = useRouter();
  const callback = useApiMutation((api, status: 'OK' | 'NOK') => api.finance.fakeGatewayCallback(paymentId!, authority!, status));
  if (!paymentId || !authority) return <StatePanel role="alert" title={t('gatewayTitle')} description={t('gatewayMissing')} />;
  const go = async (status: 'OK' | 'NOK') => {
    const r = await callback.run(status);
    // With Finance:Payments:ReturnUrl configured the callback answers with a redirect to the return page instead of JSON;
    // either way the result page asks the API for the payment's real status.
    if (r.ok || !isApiError(r.error)) router.replace(`/pay/result?paymentId=${encodeURIComponent(paymentId)}${invoiceId ? `&invoiceId=${encodeURIComponent(invoiceId)}` : ''}`);
  };
  return (
    <>
      <PageHeader title={t('gatewayTitle')} description={t('gatewayIntro')} />
      <Card style={{ maxInlineSize: '32rem' }}>
        <p className="eemp-ltr" style={{ fontSize: '0.875rem' }}>{t('gatewayAmount', { payment: paymentId })}</p>
        <ErrorAlert error={callback.error} />
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <Button onClick={() => void go('OK')} loading={callback.busy} data-testid="gateway-pay">{t('gatewayPay')}</Button>
          <Button variant="secondary" onClick={() => void go('NOK')} disabled={callback.busy} data-testid="gateway-cancel">{t('gatewayCancel')}</Button>
        </div>
      </Card>
    </>
  );
}

/* ======================= payment result (return page) ======================= */

/**
 * Where the payer lands after the gateway (fake page or the provider's redirect to Finance:Payments:ReturnUrl).
 * The URL's status is never trusted: the page asks the API to verify the payment (idempotent) and shows its answer.
 */
export function PaymentResultScreen({ paymentId, invoiceId }: { paymentId?: string; invoiceId?: string }) {
  const t = useTranslations('finance.portal');
  const pdfLang = usePdfLanguage();
  const verify = useApiMutation((api) => api.finance.portal.verify(paymentId!));
  const [result, setResult] = useState<PaymentStatusResult | null>(null);
  const ran = useRef(false);
  const check = async () => {
    const r = await verify.run();
    if (r.ok) setResult(r.value);
  };
  useEffect(() => {
    if (paymentId && !ran.current) {
      ran.current = true;
      void check();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [paymentId]);
  if (!paymentId) return <StatePanel role="alert" title={t('resultTitle')} description={t('gatewayMissing')} />;
  const status = result?.status;
  return (
    <>
      <PageHeader before={backLink(invoiceId ? `/invoices/${invoiceId}` : '/invoices', t('backToInvoices'))} title={t('resultTitle')} />
      <Card style={{ maxInlineSize: '40rem' }} data-testid="payment-result" data-status={status ?? ''}>
        {!result && !verify.error && <Spinner label={t('resultChecking')} />}
        <ErrorAlert error={verify.error} />
        {status === 'succeeded' && (
          <Alert tone="success" role="status">{t('resultSucceeded', { receipt: result!.receiptNumber ?? '' })}</Alert>
        )}
        {status === 'pending' && <Alert tone="info" role="status">{t('resultPending')}</Alert>}
        {status === 'failed' && <Alert tone="danger" role="alert">{t('resultFailed')}</Alert>}
        {status === 'cancelled' && <Alert tone="warning" role="status">{t('resultCancelled')}</Alert>}
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBlockStart: 12 }}>
          {status === 'succeeded' && result?.receiptNumber && (
            <FileDownloadButton labelKey="downloadReceipt" testId="result-receipt" load={(api) => api.finance.portal.receiptPdf(paymentId, pdfLang)} fallbackName={`receipt-${result.receiptNumber}.pdf`} />
          )}
          {(status === 'pending' || !!verify.error) && <Button variant="secondary" onClick={() => void check()} loading={verify.busy}>{t('checkAgain')}</Button>}
          <Link href={invoiceId ? `/invoices/${invoiceId}` : '/invoices'}>{t('backToInvoices')}</Link>
        </div>
      </Card>
    </>
  );
}
