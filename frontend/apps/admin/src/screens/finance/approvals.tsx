'use client';

import type { ApprovalItem } from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { Money, useFinanceLabels } from '@eemp/app/finance';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link } from '@eemp/i18n/navigation';
import { Alert, Button, ConfirmDialog, DataTable, PageHeader, SelectField, StatePanel, TextAreaField } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState } from 'react';

import { useUserNames } from './common';

/**
 * Maker-checker queue for discounts/scholarships and refunds (DL-063). Approve/reject are offered only to holders of
 * finance.approve and never on the user's own requests; the API enforces both (403 finance.maker_checker).
 */
export function FinanceApprovalsScreen() {
  const t = useTranslations('finance.approvals');
  const fc = useTranslations('finance.common');
  const common = useTranslations('common');
  const labels = useFinanceLabels();
  const format = useLocaleFormat();
  const userName = useUserNames();
  const { can, user } = useSession();
  const [status, setStatus] = useState('pending');
  const query = useApiQuery<ApprovalItem[]>(`fin-approvals|${status}`, (api) => api.finance.approvals.list(status));
  const [decision, setDecision] = useState<{ item: ApprovalItem; approve: boolean } | null>(null);
  const [note, setNote] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const decide = useApiMutation((api, d: { item: ApprovalItem; approve: boolean }): Promise<unknown> => {
    const body = { rowVersion: d.item.rowVersion, note: note.trim() || null };
    if (d.item.type === 'discount') return d.approve ? api.finance.discounts.approve(d.item.id, body) : api.finance.discounts.reject(d.item.id, body);
    return d.approve ? api.finance.refunds.approve(d.item.id, body) : api.finance.refunds.reject(d.item.id, body);
  });
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      {message && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }}>{message}</Alert>}
      <div style={{ maxInlineSize: '16rem', marginBlockEnd: 12 }}>
        <SelectField label={common('status')} name="status" value={status} onChange={(e) => setStatus(e.target.value)}
          options={['pending', 'returned', 'approved', 'rejected'].map((s) => ({ value: s, label: labels.approvalStatus(s) }))} />
      </div>
      <DataTable
        caption={t('title')}
        rows={query.data ?? []}
        loading={query.loading}
        loadingLabel={common('loading')}
        getRowKey={(r) => `${r.type}|${r.id}`}
        empty={<StatePanel title={t('empty')} />}
        columns={[
          { key: 'type', header: t('type'), isRowHeader: true, cell: (r) => labels.approvalType(r.type) },
          { key: 'student', header: fc('student'), cell: (r) => r.studentName },
          { key: 'amount', header: fc('amount'), cell: (r) => <Money amount={r.amount} currency={r.currency} /> },
          { key: 'detail', header: t('detail'), cell: (r) => <span className="eemp-ltr">{r.detail}</span> },
          { key: 'reason', header: fc('reason'), cell: (r) => r.reason },
          { key: 'by', header: fc('requestedBy'), cell: (r) => <>{userName(r.requestedBy)} · {format.dateTime(r.requestedAt)}</> },
          {
            key: 'open', header: '',
            cell: (r) => (
              <span style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                {r.invoiceId ? <Link href={`/finance/invoices/${r.invoiceId}`}>{t('openInvoice')}</Link> : r.paymentId ? <Link href={`/finance/payments/${r.paymentId}`}>{t('openPayment')}</Link> : null}
                {r.workflowInstanceId && <Link href={`/workflow/instances/${r.workflowInstanceId}`}>{fc('viewApproval')}</Link>}
              </span>
            ),
          },
          ...(status === 'pending'
            ? [{
              key: 'decide', header: common('actions'),
              cell: (r: ApprovalItem) =>
                r.requestedBy === user.id ? (
                  <span style={{ fontSize: '0.8125rem' }} data-testid="own-request">{fc('ownRequest')}</span>
                ) : can('finance.approve') ? (
                  <span style={{ display: 'flex', gap: 4 }}>
                    <Button size="sm" data-testid="approval-approve" onClick={() => { decide.reset(); setNote(''); setDecision({ item: r, approve: true }); }}>{fc('approve')}</Button>
                    <Button size="sm" variant="secondary" onClick={() => { decide.reset(); setNote(''); setDecision({ item: r, approve: false }); }}>{fc('reject')}</Button>
                  </span>
                ) : null,
            }]
            : []),
        ]}
      />
      <ConfirmDialog
        open={decision !== null}
        tone={decision?.approve ? 'primary' : 'danger'}
        title={decision?.approve ? t('approveTitle') : t('rejectTitle')}
        confirmLabel={decision?.approve ? fc('approve') : fc('reject')}
        cancelLabel={common('cancel')}
        busy={decide.busy}
        onCancel={() => setDecision(null)}
        onConfirm={async () => {
          if (!decision) return;
          if (!decision.approve && !note.trim()) return;
          const r = await decide.run(decision);
          if (r.ok) {
            setMessage(decision.approve ? fc('approved') : fc('rejected'));
            setDecision(null);
            query.reload();
          }
        }}
      >
        <ErrorAlert error={decide.error} onReload={() => { decide.reset(); setDecision(null); query.reload(); }} />
        {/* Rejecting needs a reason: the workflow engine records it as the decision comment (DL-082). */}
        <TextAreaField label={decision?.approve === false ? fc('rejectNote') : fc('decide')} name="note" required={decision?.approve === false} value={note}
          onChange={(e) => setNote(e.target.value)} />
      </ConfirmDialog>
    </>
  );
}
