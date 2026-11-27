'use client';

import {
  INVOICE_STATUSES, toAsciiDigits, type DiscountKind, type DiscountType, type FeeItem, type Invoice, type InvoiceDiscount, type InvoiceSummary,
} from '@eemp/api-client';
import { ButtonLink, DateField, ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession, useTodayIso } from '@eemp/app';
import {
  AmountField, FileDownloadButton, InvoiceNumber, InvoiceStatusBadge, InvoiceView, Money, useFinanceLabels, usePdfLanguage,
} from '@eemp/app/finance';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link, useRouter } from '@eemp/i18n/navigation';
import {
  Alert, Button, Card, Checkbox, ConfirmDialog, DescriptionList, FormGrid, PageHeader, SelectField, Spinner, TextAreaField, TextField,
} from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState } from 'react';

import { BackLink, Notice, PagedList, useFieldErrors } from '../../components/common';
import { useAcademicLookups, useTermLabel } from '../academic/options';
import { StudentPicker, sectionTitle, useBranchNames, useTenantCurrency, useUserNames } from './common';

/* ======================= list ======================= */

export function InvoicesScreen({ studentId, notice }: { studentId?: string; notice?: string }) {
  const t = useTranslations('finance.invoices');
  const common = useTranslations('finance.common');
  const labels = useFinanceLabels();
  const format = useLocaleFormat();
  const { can } = useSession();
  const lookups = useAcademicLookups();
  const termLabel = useTermLabel();
  const branches = useBranchNames();
  const [status, setStatus] = useState('');
  const [overdueOnly, setOverdueOnly] = useState(false);
  const [branchId, setBranchId] = useState('');
  const [termId, setTermId] = useState('');
  return (
    <>
      <PageHeader
        title={t('title')}
        description={t('intro')}
        actions={can('finance.invoices.manage') && (
          <>
            <ButtonLink href="/finance/invoices/new">{t('new')}</ButtonLink>
            <ButtonLink href="/finance/invoices/generate" variant="secondary">{t('generate')}</ButtonLink>
          </>
        )}
      />
      <Notice notice={notice} />
      <FormGrid style={{ marginBlockEnd: 12 }}>
        <SelectField label={t('status')} name="status" value={status} placeholder={t('allStatuses')} onChange={(e) => setStatus(e.target.value)}
          options={INVOICE_STATUSES.map((s) => ({ value: s, label: labels.invoiceStatus(s) }))} />
        {branches.list.length > 1 && (
          <SelectField label={common('branch')} name="branchId" value={branchId} placeholder={common('allBranches')} onChange={(e) => setBranchId(e.target.value)}
            options={branches.list.map((b) => ({ value: b.id, label: b.name }))} />
        )}
        <SelectField label={common('term')} name="termId" value={termId} placeholder={common('anyTerm')} onChange={(e) => setTermId(e.target.value)}
          options={lookups.terms.map((x) => ({ value: x.id, label: termLabel(x.id) }))} />
        <div style={{ alignSelf: 'end' }}>
          <Checkbox label={t('overdueOnly')} name="overdueOnly" checked={overdueOnly} onChange={(e) => setOverdueOnly(e.target.checked)} />
        </div>
      </FormGrid>
      <PagedList<InvoiceSummary>
        queryKey={`invoices|${status}|${overdueOnly}|${branchId}|${termId}|${studentId ?? ''}`}
        load={(api, q) => api.finance.invoices.list({ ...q, status: status || undefined, overdueOnly: overdueOnly || undefined, branchId: branchId || undefined, termId: termId || undefined, studentId })}
        caption={t('title')}
        searchLabel={t('searchLabel')}
        emptyText={t('empty')}
        getRowKey={(r) => r.id}
        columns={[
          { key: 'number', header: common('number'), isRowHeader: true, cell: (r) => <Link href={`/finance/invoices/${r.id}`}><InvoiceNumber number={r.number} /></Link> },
          { key: 'student', header: common('student'), cell: (r) => <>{r.studentName} <span className="eemp-ltr" style={{ color: 'var(--eemp-color-fg-muted)' }}>{r.studentNumber}</span></> },
          { key: 'status', header: t('status'), cell: (r) => <InvoiceStatusBadge status={r.status} overdue={r.isOverdue} /> },
          { key: 'due', header: common('dueDate'), cell: (r) => (r.dueDate ? format.calendarDate(r.dueDate) : common('none')) },
          { key: 'total', header: common('total'), cell: (r) => <Money amount={r.total} currency={r.currency} /> },
          { key: 'balance', header: common('balance'), cell: (r) => <Money amount={r.balance} currency={r.currency} /> },
        ]}
      />
    </>
  );
}

/* ======================= create ======================= */

interface LineDraft {
  key: string;
  kind: 'fee' | 'text';
  feeItemId: string;
  description: string;
  quantity: number;
  unitPrice: number | null;
}

export function InvoiceCreateScreen({ studentId: initialStudent }: { studentId?: string }) {
  const t = useTranslations('finance.invoices');
  const common = useTranslations('common');
  const fc = useTranslations('finance.common');
  const router = useRouter();
  const lookups = useAcademicLookups();
  const termLabel = useTermLabel();
  const currency = useTenantCurrency();
  const feeItems = useApiQuery<FeeItem[]>('fee-items-active', (api) => api.finance.feeItems.list({ activeOnly: true }));
  const [studentId, setStudentId] = useState(initialStudent ?? '');
  const [termId, setTermId] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [note, setNote] = useState('');
  const [lines, setLines] = useState<LineDraft[]>([]);
  const [lineCounter, setLineCounter] = useState(0);
  const create = useApiMutation((api) =>
    api.finance.invoices.create({
      studentId, termId: termId || null, dueDate: dueDate || null, note: note.trim() || null,
      lines: lines.map((l) => (l.kind === 'fee' ? { feeItemId: l.feeItemId, quantity: l.quantity } : { description: l.description.trim(), quantity: l.quantity, unitPrice: l.unitPrice })),
    }),
  );
  const errors = useFieldErrors(create.error);
  const add = (fee: boolean) => {
    setLineCounter((n) => n + 1);
    setLines([...lines, { key: `l${lineCounter}`, kind: fee ? 'fee' : 'text', feeItemId: fee ? (feeItems.data?.[0]?.id ?? '') : '', description: '', quantity: 1, unitPrice: null }]);
  };
  const update = (key: string, patch: Partial<LineDraft>) => setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  return (
    <>
      <PageHeader before={<BackLink href="/finance/invoices" />} title={t('createTitle')} />
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          const r = await create.run();
          if (r.ok) router.push(`/finance/invoices/${r.value.id}?notice=created`);
        }}
        style={{ display: 'grid', gap: 16 }}
      >
        <ErrorAlert error={create.error} />
        <Card>
          <FormGrid>
            <StudentPicker value={studentId} onChange={setStudentId} required error={errors.studentId} />
            <div style={{ display: 'grid', gap: 8, alignContent: 'start' }}>
              <SelectField label={fc('term')} name="termId" value={termId} placeholder={fc('anyTerm')} onChange={(e) => setTermId(e.target.value)}
                options={lookups.terms.map((x) => ({ value: x.id, label: termLabel(x.id) }))} />
              <DateField label={fc('dueDate')} name="dueDate" value={dueDate} onChange={setDueDate} hint={t('dueDateHint')} error={errors.dueDate} />
            </div>
          </FormGrid>
          <TextAreaField label={t('noteLabel')} name="note" value={note} onChange={(e) => setNote(e.target.value)} style={{ marginBlockStart: 12 }} />
        </Card>
        <Card aria-labelledby="lines-h">
          <h2 id="lines-h" style={sectionTitle}>{t('lines')}</h2>
          {errors.lines && <Alert tone="danger" role="alert">{t('noLines')}</Alert>}
          <div style={{ display: 'grid', gap: 12 }}>
            {lines.map((l, i) => (
              <fieldset key={l.key} style={{ border: '1px solid var(--eemp-color-border)', borderRadius: 8, padding: 12 }} data-testid="invoice-line">
                <legend className="eemp-visually-hidden">{t('lines')} {i + 1}</legend>
                <FormGrid>
                  {l.kind === 'fee' ? (
                    <SelectField label={t('feeItem')} name={`lines[${i}].feeItemId`} value={l.feeItemId} placeholder={t('chooseFeeItem')} error={errors[`lines[${i}].feeItemId`]}
                      onChange={(e) => update(l.key, { feeItemId: e.target.value })}
                      options={(feeItems.data ?? []).map((f) => ({ value: f.id, label: f.name }))} />
                  ) : (
                    <>
                      <TextField label={t('description')} name={`lines[${i}].description`} required value={l.description} error={errors[`lines[${i}].description`]}
                        onChange={(e) => update(l.key, { description: e.target.value })} />
                      <AmountField label={t('unitPrice')} name={`lines[${i}].unitPrice`} required currency={currency} error={errors[`lines[${i}].unitPrice`]}
                        onAmount={(unitPrice) => update(l.key, { unitPrice })} />
                    </>
                  )}
                  <TextField label={t('quantity')} name={`lines[${i}].quantity`} type="number" min={1} dir="ltr" required value={l.quantity}
                    error={errors[`lines[${i}].quantity`]} onChange={(e) => update(l.key, { quantity: Math.max(1, Number(e.target.value) || 1) })} />
                </FormGrid>
                {l.kind === 'fee' && l.feeItemId && <p style={{ marginBlock: '8px 0' }}><Money amount={(feeItems.data?.find((f) => f.id === l.feeItemId)?.amount ?? 0) * l.quantity} currency={currency} /></p>}
                <Button type="button" size="sm" variant="ghost" onClick={() => setLines(lines.filter((x) => x.key !== l.key))}>{t('removeLine')}</Button>
              </fieldset>
            ))}
          </div>
          <div style={{ display: 'flex', gap: 8, marginBlockStart: 12, flexWrap: 'wrap' }}>
            <Button type="button" variant="secondary" disabled={!feeItems.data?.length} onClick={() => add(true)}>{t('addFeeLine')}</Button>
            <Button type="button" variant="secondary" onClick={() => add(false)}>{t('addTextLine')}</Button>
          </div>
        </Card>
        <div style={{ display: 'flex', gap: 8 }}>
          <Button type="submit" loading={create.busy} disabled={!studentId || lines.length === 0}>{t('createDraft')}</Button>
          <ButtonLink href="/finance/invoices" variant="secondary">{common('cancel')}</ButtonLink>
        </div>
      </form>
    </>
  );
}

/* ======================= detail ======================= */

type Dialog =
  | { kind: 'issue' | 'delete' }
  | { kind: 'void' | 'credit' | 'discount' | 'plan' }
  | { kind: 'decide'; discount: InvoiceDiscount; approve: boolean }
  | { kind: 'withdraw'; discount: InvoiceDiscount }
  | { kind: 'resubmit'; discount: InvoiceDiscount };

export function InvoiceDetailScreen({ id, notice }: { id: string; notice?: string }) {
  const t = useTranslations('finance.invoices');
  const fc = useTranslations('finance.common');
  const common = useTranslations('common');
  const labels = useFinanceLabels();
  const format = useLocaleFormat();
  const router = useRouter();
  const { can, user } = useSession();
  const today = useTodayIso();
  const userName = useUserNames();
  const pdfLang = usePdfLanguage();
  const query = useApiQuery<Invoice>(`invoice|${id}`, (api) => api.finance.invoices.get(id));
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [amount, setAmount] = useState<number | null>(null);
  const [discount, setDiscount] = useState<{ kind: DiscountKind; type: DiscountType; value: string }>({ kind: 'discount', type: 'percent', value: '' });
  const [plan, setPlan] = useState({ count: 2, firstDueDate: '', intervalDays: 30 });
  const action = useApiMutation(async (api, d: Dialog, inv: Invoice): Promise<Invoice | null> => {
    switch (d.kind) {
      case 'issue': return api.finance.invoices.issue(inv.id, { rowVersion: inv.rowVersion });
      case 'delete': await api.finance.invoices.remove(inv.id); return null;
      case 'void': return api.finance.invoices.void(inv.id, { rowVersion: inv.rowVersion, reason: reason.trim() });
      case 'credit': return api.finance.invoices.creditNote(inv.id, { rowVersion: inv.rowVersion, amount: amount ?? 0, reason: reason.trim() });
      case 'plan': return api.finance.invoices.setInstallments(inv.id, { rowVersion: inv.rowVersion, count: plan.count, firstDueDate: plan.firstDueDate, intervalDays: plan.intervalDays });
      case 'discount':
        await api.finance.invoices.requestDiscount(inv.id, { kind: discount.kind, type: discount.type, value: Number(toAsciiDigits(discount.value).replace('٫', '.')), reason: reason.trim() });
        return api.finance.invoices.get(inv.id);
      case 'decide':
        await (d.approve ? api.finance.discounts.approve : api.finance.discounts.reject)(d.discount.id, { rowVersion: d.discount.rowVersion, note: reason.trim() || null });
        return api.finance.invoices.get(inv.id);
      case 'withdraw':
        await api.finance.discounts.remove(d.discount.id);
        return api.finance.invoices.get(inv.id);
      case 'resubmit':
        await api.finance.discounts.resubmit(d.discount.id, {
          kind: discount.kind, type: discount.type, value: Number(toAsciiDigits(discount.value).replace('٫', '.')), reason: reason.trim(), rowVersion: d.discount.rowVersion,
        });
        return api.finance.invoices.get(inv.id);
    }
  });
  const errors = useFieldErrors(action.error);
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const inv = query.data;
  if (!inv) return <Spinner label={common('loading')} />;
  const manage = can('finance.invoices.manage');
  const open = inv.status === 'issued' || inv.status === 'partiallyPaid';
  const start = (d: Dialog) => {
    action.reset();
    setReason('');
    setAmount(null);
    if (d.kind === 'plan') setPlan({ count: 2, firstDueDate: inv.dueDate ?? today, intervalDays: 30 });
    if (d.kind === 'resubmit') {
      setDiscount({ kind: d.discount.kind, type: d.discount.type, value: String(d.discount.value) });
      setReason(d.discount.reason);
    }
    setDialog(d);
  };
  const doneMessage = (d: Dialog, result: Invoice | null) => {
    switch (d.kind) {
      case 'issue': return t('issued', { number: result?.number ?? '' });
      case 'void': return t('voided');
      case 'credit': return t('creditNoteDone');
      case 'plan': return t('planSaved');
      case 'discount': return t('discountRequested');
      case 'decide': return d.approve ? fc('approved') : fc('rejected');
      case 'resubmit': return fc('resubmitted');
      default: return null;
    }
  };
  // Rejecting needs a reason: the workflow engine records it as the decision comment (DL-082).
  const needsReason = dialog?.kind === 'void' || dialog?.kind === 'credit' || dialog?.kind === 'discount' || dialog?.kind === 'resubmit'
    || (dialog?.kind === 'decide' && !dialog.approve);
  const approvalLink = (d: InvoiceDiscount) => d.workflowInstanceId
    ? <Link href={`/workflow/instances/${d.workflowInstanceId}`} style={{ fontSize: '0.8125rem' }} data-testid="discount-workflow">{fc('viewApproval')}</Link>
    : null;
  const discountAction = (discountId: string) => {
    const d = inv.discounts.find((x) => x.id === discountId);
    if (!d) return null;
    const own = d.requestedBy === user.id;
    if (d.status === 'returned') {
      return (
        <span style={{ display: 'flex', gap: 4, flexWrap: 'wrap', alignItems: 'center' }}>
          <span style={{ fontSize: '0.8125rem' }} data-testid="discount-returned">{fc('returnedNote')}</span>
          {own && manage && <Button size="sm" onClick={() => start({ kind: 'resubmit', discount: d })} data-testid="discount-resubmit">{fc('resubmit')}</Button>}
          {(own ? manage : can('finance.approve')) && <Button size="sm" variant="ghost" onClick={() => start({ kind: 'withdraw', discount: d })}>{t('withdrawDiscount')}</Button>}
          {approvalLink(d)}
        </span>
      );
    }
    if (d.status !== 'pending') {
      return (
        <span style={{ display: 'flex', gap: 4, flexWrap: 'wrap', alignItems: 'center', fontSize: '0.8125rem' }}>
          {d.decidedBy && <span>{userName(d.decidedBy)}</span>}
          {approvalLink(d)}
        </span>
      );
    }
    return (
      <span style={{ display: 'flex', gap: 4, flexWrap: 'wrap', alignItems: 'center' }}>
        {/* UX only: the API enforces maker ≠ checker (403 finance.maker_checker). */}
        {can('finance.approve') && !own && (
          <>
            <Button size="sm" onClick={() => start({ kind: 'decide', discount: d, approve: true })} data-testid="discount-approve">{fc('approve')}</Button>
            <Button size="sm" variant="secondary" onClick={() => start({ kind: 'decide', discount: d, approve: false })}>{fc('reject')}</Button>
          </>
        )}
        {own && <span style={{ fontSize: '0.8125rem' }} data-testid="own-request">{fc('ownRequest')}</span>}
        {(own ? manage : can('finance.approve')) && <Button size="sm" variant="ghost" onClick={() => start({ kind: 'withdraw', discount: d })}>{t('withdrawDiscount')}</Button>}
        {approvalLink(d)}
      </span>
    );
  };
  return (
    <>
      <PageHeader
        before={<BackLink href="/finance/invoices" />}
        title={inv.number ? t('detailTitle', { number: inv.number }) : t('draftTitle')}
        description={<>{inv.studentName} · <span className="eemp-ltr">{inv.studentNumber}</span> · <Link href={`/finance/accounts/${inv.studentId}`}>{fc('student')}</Link></>}
        actions={
          <>
            {manage && inv.status === 'draft' && (
              <>
                <Button onClick={() => start({ kind: 'issue' })}>{t('issue')}</Button>
                <Button variant="secondary" onClick={() => { setDiscount({ kind: 'discount', type: 'percent', value: '' }); start({ kind: 'discount' }); }}>{t('requestDiscount')}</Button>
              </>
            )}
            {can('finance.payments.manage') && open && <ButtonLink href={`/finance/payments/new?studentId=${inv.studentId}&invoiceId=${inv.id}`}>{t('recordPayment')}</ButtonLink>}
            {manage && (inv.status === 'draft' || (open && inv.paidAmount === 0)) && <Button variant="secondary" onClick={() => start({ kind: 'plan' })}>{t('planInstallments')}</Button>}
            {manage && open && <Button variant="secondary" onClick={() => start({ kind: 'credit' })}>{t('creditNote')}</Button>}
            {manage && (open || inv.status === 'paid') && <Button variant="danger" onClick={() => start({ kind: 'void' })}>{t('void')}</Button>}
            {manage && inv.status === 'draft' && <Button variant="danger" onClick={() => start({ kind: 'delete' })}>{t('deleteDraft')}</Button>}
            {inv.status !== 'draft' && (
              <FileDownloadButton testId="invoice-pdf" load={(api) => api.finance.invoices.pdf(inv.id, pdfLang)} fallbackName={`invoice-${inv.number ?? 'draft'}.pdf`} />
            )}
          </>
        }
      />
      <Notice notice={notice} />
      {message && <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }}>{message}</Alert>}
      {inv.status === 'void' && <Alert tone="danger" style={{ marginBlockEnd: 12 }}>{t('voidNotice', { reason: inv.voidReason ?? '' })}</Alert>}
      {inv.status === 'draft' && <p style={{ fontSize: '0.875rem', color: 'var(--eemp-color-fg-muted)' }}>{t('pdfDraftHint')}</p>}
      <Card style={{ marginBlockEnd: 16 }}>
        <DescriptionList>
          <dt>{t('status')}</dt>
          <dd><InvoiceStatusBadge status={inv.status} overdue={inv.isOverdue} /></dd>
          {inv.issueDate && (<><dt>{fc('issueDate')}</dt><dd>{format.calendarDate(inv.issueDate)}</dd></>)}
          {inv.dueDate && (<><dt>{fc('dueDate')}</dt><dd>{format.calendarDate(inv.dueDate)}</dd></>)}
          {inv.note && (<><dt>{t('noteLabel')}</dt><dd>{inv.note}</dd></>)}
        </DescriptionList>
      </Card>
      <InvoiceView invoice={inv} discountAction={discountAction} />
      <ConfirmDialog
        open={dialog !== null}
        tone={dialog?.kind === 'void' || dialog?.kind === 'delete' || (dialog?.kind === 'decide' && !dialog.approve) || dialog?.kind === 'withdraw' ? 'danger' : 'primary'}
        title={
          dialog?.kind === 'issue' ? t('issueTitle')
            : dialog?.kind === 'delete' ? t('deleteDraftTitle')
              : dialog?.kind === 'void' ? t('voidTitle')
                : dialog?.kind === 'credit' ? t('creditNoteTitle')
                  : dialog?.kind === 'discount' ? t('discountTitle')
                    : dialog?.kind === 'plan' ? t('planTitle')
                      : dialog?.kind === 'decide' ? (dialog.approve ? fc('approve') : fc('reject'))
                        : dialog?.kind === 'resubmit' ? t('resubmitTitle')
                          : t('withdrawDiscount')
        }
        description={
          dialog?.kind === 'issue' ? t('issueBody')
            : dialog?.kind === 'delete' ? t('deleteDraftBody')
              : dialog?.kind === 'void' ? t('voidBody')
                : dialog?.kind === 'credit' ? t('creditNoteBody')
                  : dialog?.kind === 'discount' ? t('discountBody')
                    : dialog?.kind === 'plan' ? t('planBody')
                      : dialog?.kind === 'resubmit' ? t('resubmitBody') : undefined
        }
        confirmLabel={
          dialog?.kind === 'issue' ? t('issue')
            : dialog?.kind === 'delete' ? common('delete')
              : dialog?.kind === 'void' ? t('void')
                : dialog?.kind === 'decide' ? (dialog.approve ? fc('approve') : fc('reject'))
                  : dialog?.kind === 'withdraw' ? t('withdrawDiscount')
                    : dialog?.kind === 'resubmit' ? fc('resubmit') : common('save')
        }
        cancelLabel={common('cancel')}
        busy={action.busy}
        onCancel={() => setDialog(null)}
        onConfirm={async () => {
          if (!dialog) return;
          if (needsReason && !reason.trim()) return;
          const r = await action.run(dialog, inv);
          if (!r.ok) return;
          if (dialog.kind === 'delete') {
            router.push('/finance/invoices?notice=deleted');
            return;
          }
          if (r.value) query.setData(r.value);
          setMessage(doneMessage(dialog, r.value));
          setDialog(null);
        }}
      >
        <ErrorAlert error={action.error} onReload={() => { action.reset(); setDialog(null); query.reload(); }} />
        {(dialog?.kind === 'discount' || dialog?.kind === 'resubmit') && (
          <FormGrid style={{ marginBlockEnd: 12 }}>
            <SelectField label={t('discountKind')} name="discountKind" value={discount.kind} onChange={(e) => setDiscount({ ...discount, kind: e.target.value as DiscountKind })}
              options={(['discount', 'scholarship', 'sibling'] as const).map((k) => ({ value: k, label: labels.discountKind(k) }))} />
            <SelectField label={t('discountType')} name="discountType" value={discount.type} onChange={(e) => setDiscount({ ...discount, type: e.target.value as DiscountType })}
              options={(['percent', 'fixed'] as const).map((k) => ({ value: k, label: labels.discountType(k) }))} />
            <TextField label={t('discountValue')} name="discountValue" inputMode="decimal" dir="ltr" required value={discount.value} error={errors.value}
              onChange={(e) => setDiscount({ ...discount, value: e.target.value })} />
          </FormGrid>
        )}
        {dialog?.kind === 'credit' && (
          <AmountField label={fc('amount')} name="creditAmount" required currency={inv.currency} error={errors.amount} onAmount={setAmount} />
        )}
        {dialog?.kind === 'plan' && (
          <FormGrid>
            <TextField label={t('planCount')} name="planCount" type="number" min={1} max={24} dir="ltr" value={plan.count} error={errors.count}
              onChange={(e) => setPlan({ ...plan, count: Math.max(1, Number(e.target.value) || 1) })} />
            <DateField label={t('planFirstDue')} name="planFirstDue" required value={plan.firstDueDate} error={errors.firstDueDate} onChange={(firstDueDate) => setPlan({ ...plan, firstDueDate })} />
            <TextField label={t('planInterval')} name="planInterval" type="number" min={1} dir="ltr" value={plan.intervalDays} error={errors.intervalDays}
              onChange={(e) => setPlan({ ...plan, intervalDays: Math.max(1, Number(e.target.value) || 1) })} />
          </FormGrid>
        )}
        {(needsReason || dialog?.kind === 'decide') && (
          <TextAreaField
            label={dialog?.kind === 'void' ? t('voidReason') : dialog?.kind === 'decide' ? (dialog.approve ? fc('decide') : fc('rejectNote')) : fc('reason')}
            name="reason"
            required={needsReason}
            value={reason}
            error={errors.reason ?? errors.note}
            onChange={(e) => setReason(e.target.value)}
          />
        )}
      </ConfirmDialog>
    </>
  );
}

/* ======================= bulk generation ======================= */

export function GenerateInvoicesScreen() {
  const t = useTranslations('finance.generate');
  const common = useTranslations('common');
  const lookups = useAcademicLookups();
  const termLabel = useTermLabel();
  const [mode, setMode] = useState<'class' | 'term'>('class');
  const [termId, setTermId] = useState('');
  const [classId, setClassId] = useState('');
  const [feeItemIds, setFeeItemIds] = useState<string[]>([]);
  const [dueDate, setDueDate] = useState('');
  const feeItems = useApiQuery<FeeItem[]>('fee-items-active', (api) => api.finance.feeItems.list({ activeOnly: true }));
  const classes = useApiQuery(`gen-classes|${termId}`, async (api) => (termId ? (await api.academic.classes.list({ termId, pageSize: 100 })).items : []));
  const run = useApiMutation((api) =>
    api.finance.invoices.generateBulk({ classId: mode === 'class' ? classId : null, termId: mode === 'term' ? termId : null, feeItemIds, dueDate: dueDate || null }));
  const errors = useFieldErrors(run.error);
  const [result, setResult] = useState<{ created: number; skipped: number } | null>(null);
  const toggle = (feeId: string, on: boolean) => setFeeItemIds((ids) => (on ? [...ids, feeId] : ids.filter((x) => x !== feeId)));
  return (
    <>
      <PageHeader before={<BackLink href="/finance/invoices" />} title={t('title')} description={t('intro')} />
      {result && (
        <Alert tone="success" role="status" style={{ marginBlockEnd: 12 }} action={<ButtonLink href="/finance/invoices" variant="secondary" size="sm">{t('viewDrafts')}</ButtonLink>}>
          <span data-testid="generate-result">{t('result', result)}</span>
        </Alert>
      )}
      <ErrorAlert error={run.error} />
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          setResult(null);
          const r = await run.run();
          if (r.ok) setResult({ created: r.value.created, skipped: r.value.skipped });
        }}
      >
        <Card style={{ display: 'grid', gap: 16 }}>
          <FormGrid>
            <SelectField label={t('scope')} name="mode" value={mode} onChange={(e) => setMode(e.target.value === 'term' ? 'term' : 'class')}
              options={[{ value: 'class', label: t('byClass') }, { value: 'term', label: t('byTerm') }]} />
            <SelectField label={t('term')} name="termId" required value={termId} placeholder={t('chooseTerm')} onChange={(e) => { setTermId(e.target.value); setClassId(''); }}
              options={lookups.terms.map((x) => ({ value: x.id, label: termLabel(x.id) }))} />
            {mode === 'class' && (
              <SelectField label={t('class')} name="classId" required value={classId} placeholder={t('chooseClass')} error={errors.classId} onChange={(e) => setClassId(e.target.value)}
                options={(classes.data ?? []).map((c) => ({ value: c.id, label: `${c.name} (${c.code})` }))} />
            )}
            <DateField label={t('dueDate')} name="dueDate" value={dueDate} onChange={setDueDate} />
          </FormGrid>
          <fieldset style={{ border: 0, padding: 0, margin: 0 }} aria-describedby={errors.feeItemIds ? 'fee-items-error' : undefined}>
            <legend style={{ fontWeight: 600, marginBlockEnd: 8 }}>{t('feeItems')}</legend>
            <div style={{ display: 'grid', gap: 6 }}>
              {(feeItems.data ?? []).map((f) => (
                <Checkbox key={f.id} label={<>{f.name} · <Money amount={f.amount} currency={f.currency} /></>} checked={feeItemIds.includes(f.id)} onChange={(e) => toggle(f.id, e.target.checked)} />
              ))}
            </div>
            {errors.feeItemIds && <p id="fee-items-error" role="alert" style={{ color: 'var(--eemp-color-danger)' }}>{errors.feeItemIds}</p>}
          </fieldset>
          <div>
            <Button type="submit" loading={run.busy} disabled={feeItemIds.length === 0 || (mode === 'class' ? !classId : !termId)}>{t('run')}</Button>{' '}
            <ButtonLink href="/finance/invoices" variant="secondary">{common('cancel')}</ButtonLink>
          </div>
        </Card>
      </form>
    </>
  );
}
