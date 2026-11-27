'use client';

import {
  TICKET_PRIORITIES, TICKET_STATUSES, type TicketDetail, type TicketPriority, type TicketStatus, type TicketSummary,
} from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { TicketStatusBadge, TicketThread, useConsultationLabels } from '@eemp/app/consultation';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link } from '@eemp/i18n/navigation';
import { Badge, Button, Card, Checkbox, DescriptionList, FormGrid, PageHeader, SelectField, Spinner, TextAreaField, useToast } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState } from 'react';

import { BackLink, PagedList, useFieldErrors } from '../components/common';
import { sectionTitle } from './finance/common';

/* ======================= inbox ======================= */

export function SupportInboxScreen() {
  const t = useTranslations('support.inbox');
  const s = useTranslations('support.common');
  const common = useTranslations('common');
  const labels = useConsultationLabels();
  const format = useLocaleFormat();
  const [status, setStatus] = useState<TicketStatus | ''>('');
  const [assignee, setAssignee] = useState<'any' | 'me' | 'unassigned'>('any');
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      <FormGrid style={{ marginBlockEnd: 12 }}>
        <SelectField label={common('status')} name="status" value={status} placeholder={t('allStatuses')} onChange={(e) => setStatus(e.target.value as TicketStatus | '')}
          options={TICKET_STATUSES.map((x) => ({ value: x, label: labels.ticketStatus(x) }))} />
        <SelectField label={s('assignee')} name="assignee" value={assignee} onChange={(e) => setAssignee(e.target.value as 'any' | 'me' | 'unassigned')}
          options={[{ value: 'any', label: t('anyone') }, { value: 'me', label: t('mine') }, { value: 'unassigned', label: t('unassigned') }]} />
      </FormGrid>
      <PagedList<TicketSummary>
        queryKey={`support-inbox|${status}|${assignee}`}
        load={(api, q) => api.support.tickets.list({ ...q, status: status || undefined, assignee })}
        caption={t('title')}
        searchLabel={t('search')}
        emptyText={t('empty')}
        getRowKey={(r) => r.id}
        columns={[
          { key: 'n', header: s('number'), isRowHeader: true, cell: (r) => <Link href={`/support/${r.id}`}>{s('numberValue', { number: r.number })}</Link> },
          { key: 'subject', header: s('subject'), cell: (r) => <Link href={`/support/${r.id}`}>{r.subject}</Link> },
          { key: 'student', header: s('student'), cell: (r) => r.studentName },
          { key: 'cat', header: s('category'), cell: (r) => labels.category(r.category) },
          { key: 'pri', header: s('priority'), cell: (r) => <Badge tone={r.priority === 'high' ? 'danger' : 'neutral'}>{labels.priority(r.priority)}</Badge> },
          { key: 'status', header: common('status'), cell: (r) => <TicketStatusBadge status={r.status} /> },
          { key: 'who', header: s('assignee'), cell: (r) => r.assigneeName ?? s('unassigned') },
          { key: 'last', header: s('lastActivity'), cell: (r) => format.dateTime(r.lastMessageAt) },
        ]}
      />
    </>
  );
}

/* ======================= ticket ======================= */

export function SupportTicketScreen({ id }: { id: string }) {
  const t = useTranslations('support.ticket');
  const s = useTranslations('support.common');
  const common = useTranslations('common');
  const labels = useConsultationLabels();
  const format = useLocaleFormat();
  const toast = useToast();
  const { user } = useSession();
  const query = useApiQuery<TicketDetail>(`support-ticket|${id}`, (api) => api.support.tickets.get(id));
  const [text, setText] = useState('');
  const [internal, setInternal] = useState(false);
  const [edit, setEdit] = useState<{ status: TicketStatus; priority: TicketPriority; assigneeUserId: string | null } | null>(null);
  const reply = useApiMutation((api) => api.support.tickets.reply(id, text.trim(), internal));
  const update = useApiMutation((api, d: TicketDetail, v: NonNullable<typeof edit>) => api.support.tickets.update(id, { ...v, rowVersion: d.ticket.rowVersion }));
  const errors = useFieldErrors(reply.error);
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const d = query.data;
  if (!d) return <Spinner label={common('loading')} />;
  const tk = d.ticket;
  const v = edit ?? { status: tk.status, priority: tk.priority, assigneeUserId: tk.assigneeUserId };
  const closed = tk.status === 'closed';
  return (
    <>
      <PageHeader before={<BackLink href="/support" />} title={tk.subject}
        description={<>{s('numberValue', { number: tk.number })} · {tk.studentName} · {labels.category(tk.category)}</>} />
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 16, alignItems: 'flex-start' }}>
        <Card aria-labelledby="thread-h" style={{ flex: '2 1 26rem', minInlineSize: 0 }}>
          <h2 id="thread-h" style={sectionTitle}>{t('thread')}</h2>
          <TicketThread detail={d} />
          {d.canManage && !closed && (
            <form style={{ marginBlockStart: 16, display: 'grid', gap: 8 }} onSubmit={async (e) => {
              e.preventDefault();
              if (!text.trim()) return;
              const r = await reply.run();
              if (r.ok) { query.setData(r.value); setText(''); setInternal(false); setEdit(null); toast.show(internal ? t('noteAdded') : t('sent')); }
            }}>
              <ErrorAlert error={reply.error} />
              <TextAreaField label={internal ? t('internalLabel') : t('replyLabel')} name="reply" required maxLength={4000} value={text}
                onChange={(e) => setText(e.target.value)} error={errors.body} />
              <Checkbox name="internal" label={t('internal')} hint={t('internalHint')} checked={internal} onChange={(e) => setInternal(e.target.checked)} />
              <div><Button type="submit" disabled={reply.busy || !text.trim()} data-testid="ticket-send">{internal ? t('addNote') : t('send')}</Button></div>
            </form>
          )}
          {closed && <p role="status" style={{ marginBlockEnd: 0 }}>{t('closedNotice')}</p>}
        </Card>
        <Card aria-labelledby="handling-h" style={{ flex: '1 1 16rem', minInlineSize: 0 }}>
          <h2 id="handling-h" style={sectionTitle}>{t('handling')}</h2>
          <DescriptionList style={{ marginBlockEnd: 12 }}>
            <dt>{common('status')}</dt>
            <dd><TicketStatusBadge status={tk.status} /></dd>
            <dt>{s('assignee')}</dt>
            <dd>{tk.assigneeName ?? s('unassigned')}</dd>
            <dt>{t('created')}</dt>
            <dd>{format.dateTime(tk.createdAt)}</dd>
          </DescriptionList>
          {d.canManage && (
            <form style={{ display: 'grid', gap: 8 }} onSubmit={async (e) => {
              e.preventDefault();
              const r = await update.run(d, v);
              if (r.ok) { query.setData(r.value); setEdit(null); toast.show(common('saved')); }
            }}>
              <ErrorAlert error={update.error} onReload={() => { update.reset(); setEdit(null); query.reload(); }} />
              <SelectField label={common('status')} name="status" value={v.status} onChange={(e) => setEdit({ ...v, status: e.target.value as TicketStatus })}
                options={TICKET_STATUSES.map((x) => ({ value: x, label: labels.ticketStatus(x) }))} />
              <SelectField label={s('priority')} name="priority" value={v.priority} onChange={(e) => setEdit({ ...v, priority: e.target.value as TicketPriority })}
                options={TICKET_PRIORITIES.map((x) => ({ value: x, label: labels.priority(x) }))} />
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                <Button type="button" size="sm" variant="secondary" disabled={v.assigneeUserId === user.id} onClick={() => setEdit({ ...v, assigneeUserId: user.id })}>{t('assignToMe')}</Button>
                <Button type="button" size="sm" variant="ghost" disabled={v.assigneeUserId === null} onClick={() => setEdit({ ...v, assigneeUserId: null })}>{t('unassign')}</Button>
              </div>
              {edit && <p style={{ margin: 0, fontSize: '0.8125rem' }}>{t('unsaved')}</p>}
              <div><Button type="submit" disabled={update.busy || !edit} data-testid="ticket-save">{common('save')}</Button></div>
            </form>
          )}
        </Card>
      </div>
    </>
  );
}
