'use client';

import {
  TICKET_CATEGORIES, type PortalBooking, type PortalConsultant, type PortalSlot, type PortalStudent, type TicketCategory, type TicketDetail, type TicketSummary,
} from '@eemp/api-client';
import { ErrorAlert, LoadError, useApiMutation, useApiQuery } from '@eemp/app';
import { BookingStatusBadge, TicketStatusBadge, TicketThread, useConsultationLabels, useSessionTime } from '@eemp/app/consultation';
import { instantDayIso } from '@eemp/i18n';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link } from '@eemp/i18n/navigation';
import {
  Alert, Button, Card, ConfirmDialog, DataTable, FormGrid, PageHeader, SelectField, Spinner, StatePanel, TextAreaField, TextField, useToast,
} from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState } from 'react';

import { StudentPage } from './academic';
import { usePortalKind } from '../app/[locale]/(portal)/portal-kind';

const backLink = (href: string, label: string) => (
  <Link href={href} style={{ fontSize: '0.875rem' }}><span aria-hidden="true" className="eemp-flip">{'←'}</span> {label}</Link>
);
const h2 = { fontSize: '1.0625rem', marginBlockStart: 0 } as const;

/* ======================= book a session ======================= */

export function BookConsultationScreen({ student }: { student?: string }) {
  const t = useTranslations('consultation.portal');
  return (
    <StudentPage title={t('bookTitle')} intro={t('bookIntro')} selected={student}>
      {(studentId) => <BookingFlow key={studentId} studentId={studentId} />}
    </StudentPage>
  );
}

function BookingFlow({ studentId }: { studentId: string }) {
  const t = useTranslations('consultation.portal');
  const common = useTranslations('common');
  const labels = useConsultationLabels();
  const time = useSessionTime();
  const format = useLocaleFormat();
  const consultants = useApiQuery<PortalConsultant[]>(`portal-consultants|${studentId}`, (api) => api.consultation.portal.consultants(studentId));
  const [consultantId, setConsultantId] = useState('');
  const chosen = consultantId || consultants.data?.[0]?.id || '';
  const slots = useApiQuery<PortalSlot[] | null>(`portal-slots|${studentId}|${chosen}`, (api) =>
    chosen ? api.consultation.portal.slots(studentId, { consultantId: chosen }) : Promise.resolve(null));
  const [slot, setSlot] = useState<PortalSlot | null>(null);
  const [topic, setTopic] = useState('');
  const [note, setNote] = useState('');
  const [done, setDone] = useState<PortalBooking | null>(null);
  const book = useApiMutation((api, s: PortalSlot) => api.consultation.portal.book({ studentId, slotId: s.id, topic: topic.trim(), note: note.trim() || null }));
  if (consultants.error && !consultants.data) return <LoadError error={consultants.error} onRetry={consultants.reload} />;
  if (!consultants.data) return <Spinner label={common('loading')} />;
  if (done) {
    return (
      <Alert tone="success" role="status" data-testid="booking-requested" title={t('requestedTitle')}>
        <p style={{ margin: '0 0 8px' }}>{t('requested', { when: time.full(done.startsAt), consultant: done.consultantName })}</p>
        <span style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          <Link href="/consultation/bookings">{t('viewMine')}</Link>
          <Button size="sm" variant="secondary" onClick={() => { setDone(null); setSlot(null); setTopic(''); setNote(''); slots.reload(); }}>{t('bookAnother')}</Button>
        </span>
      </Alert>
    );
  }
  if (consultants.data.length === 0) return <StatePanel title={t('noConsultants')} />;
  const days = new Map<string, PortalSlot[]>();
  for (const s of slots.data ?? []) {
    const key = instantDayIso(s.startsAt, format.timeZone);
    days.set(key, [...(days.get(key) ?? []), s]);
  }
  return (
    <div style={{ display: 'grid', gap: 16 }}>
      <Card aria-labelledby="pick-consultant-h">
        <h2 id="pick-consultant-h" style={h2}>{t('step1')}</h2>
        <div role="radiogroup" aria-labelledby="pick-consultant-h" style={{ display: 'grid', gap: 8, gridTemplateColumns: 'repeat(auto-fit, minmax(16rem, 1fr))' }}>
          {consultants.data.map((c) => {
            const active = c.id === chosen;
            return (
              <button key={c.id} type="button" role="radio" aria-checked={active} data-testid="consultant-option"
                onClick={() => { setConsultantId(c.id); setSlot(null); }}
                style={{
                  textAlign: 'start', padding: 12, borderRadius: 12, cursor: 'pointer', font: 'inherit', color: 'inherit',
                  border: `2px solid ${active ? 'var(--eemp-color-primary)' : 'var(--eemp-color-border)'}`,
                  background: active ? 'var(--eemp-color-primary-soft)' : 'var(--eemp-color-surface)',
                }}>
                <strong style={{ display: 'block' }}>{c.displayName}</strong>
                {c.specialties && <span style={{ display: 'block', fontSize: '0.875rem' }}>{c.specialties}</span>}
                <span style={{ display: 'block', fontSize: '0.8125rem', color: 'var(--eemp-color-fg-muted)' }}>
                  {c.nextSlotAt ? t('nextSlot', { when: time.full(c.nextSlotAt) }) : t('noNextSlot')}
                </span>
              </button>
            );
          })}
        </div>
      </Card>
      <Card aria-labelledby="pick-slot-h">
        <h2 id="pick-slot-h" style={h2}>{t('step2')}</h2>
        {slots.error && !slots.data ? <LoadError error={slots.error} onRetry={slots.reload} /> : !slots.data ? <Spinner label={common('loading')} /> : days.size === 0 ? (
          <StatePanel title={t('noSlots')} />
        ) : (
          <div style={{ display: 'grid', gap: 12 }}>
            {[...days.entries()].map(([day, list]) => (
              <section key={day} aria-labelledby={`slots-${day}`}>
                <h3 id={`slots-${day}`} style={{ fontSize: '0.9375rem', margin: '0 0 6px' }}>{time.day(list[0]!.startsAt)}</h3>
                <div role="radiogroup" aria-labelledby={`slots-${day}`} style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
                  {list.map((s) => (
                    <Button key={s.id} type="button" size="sm" role="radio" aria-checked={slot?.id === s.id} data-testid="slot-option"
                      variant={slot?.id === s.id ? 'primary' : 'secondary'} onClick={() => setSlot(s)}
                      aria-label={`${time.full(s.startsAt)} · ${labels.mode(s.mode)}`}>
                      {time.time(s.startsAt)}
                    </Button>
                  ))}
                </div>
              </section>
            ))}
          </div>
        )}
      </Card>
      <Card aria-labelledby="confirm-h">
        <h2 id="confirm-h" style={h2}>{t('step3')}</h2>
        {!slot ? <p style={{ margin: 0 }}>{t('chooseSlotFirst')}</p> : (
          <form style={{ display: 'grid', gap: 8 }} onSubmit={async (e) => {
            e.preventDefault();
            if (!topic.trim()) return;
            const r = await book.run(slot);
            if (r.ok) setDone(r.value);
            else slots.reload();
          }}>
            <p style={{ margin: 0 }} data-testid="selected-slot">
              {t('selected', { when: time.full(slot.startsAt), mode: labels.mode(slot.mode) })}{slot.location ? ` · ${slot.location}` : ''}
            </p>
            {slot.mode === 'online' && <p style={{ margin: 0, fontSize: '0.875rem' }}>{t('onlineNote')}</p>}
            <ErrorAlert error={book.error} />
            <TextField label={t('topic')} name="topic" required maxLength={200} value={topic} onChange={(e) => setTopic(e.target.value)} hint={t('topicHint')} />
            <TextAreaField label={t('note')} name="note" maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} hint={t('noteHint')} />
            <div><Button type="submit" disabled={book.busy || !topic.trim()} data-testid="book-submit">{t('submit')}</Button></div>
          </form>
        )}
      </Card>
    </div>
  );
}

/* ======================= my sessions ======================= */

export function MyConsultationsScreen() {
  const t = useTranslations('consultation.portal');
  const common = useTranslations('common');
  const labels = useConsultationLabels();
  const time = useSessionTime();
  const toast = useToast();
  const query = useApiQuery<PortalBooking[]>('portal-bookings', (api) => api.consultation.portal.bookings());
  const [cancelling, setCancelling] = useState<PortalBooking | null>(null);
  const [reason, setReason] = useState('');
  const cancel = useApiMutation((api, b: PortalBooking) => api.consultation.portal.cancel(b.id, b.rowVersion, reason.trim() || null));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  return (
    <>
      <PageHeader title={t('myTitle')} description={t('myIntro')} actions={<Link href="/consultation">{t('bookNew')}</Link>} />
      {!query.data ? <Spinner label={common('loading')} /> : (
        <DataTable<PortalBooking>
          caption={t('myTitle')} rows={query.data} getRowKey={(b) => b.id} empty={<StatePanel title={t('myEmpty')} action={<Link href="/consultation">{t('bookNew')}</Link>} />}
          columns={[
            { key: 'when', header: t('when'), isRowHeader: true, cell: (b) => <>{time.day(b.startsAt)}<br />{time.range(b.startsAt, b.endsAt)}</> },
            { key: 'student', header: t('student'), cell: (b) => b.studentName },
            { key: 'consultant', header: t('consultant'), cell: (b) => b.consultantName },
            { key: 'topic', header: t('topic'), cell: (b) => b.topic },
            { key: 'where', header: t('where'), cell: (b) => `${labels.mode(b.mode)}${b.location ? ` · ${b.location}` : ''}` },
            { key: 'status', header: common('status'), cell: (b) => <BookingStatusBadge status={b.status} /> },
            {
              key: 'x', header: '',
              cell: (b) => b.canCancel ? (
                <Button size="sm" variant="ghost" data-testid="portal-cancel" onClick={() => { cancel.reset(); setReason(''); setCancelling(b); }}
                  aria-label={t('cancelFor', { when: time.full(b.startsAt) })}>{t('cancel')}</Button>
              ) : (b.status === 'requested' || b.status === 'confirmed') ? (
                <span style={{ fontSize: '0.8125rem', color: 'var(--eemp-color-fg-muted)' }}>{t('cancelClosed')}</span>
              ) : null,
            },
          ]}
        />
      )}
      <ConfirmDialog open={cancelling !== null} tone="danger" title={t('cancelTitle')} confirmLabel={t('cancel')} cancelLabel={t('keep')} busy={cancel.busy}
        onCancel={() => setCancelling(null)}
        onConfirm={async () => {
          if (!cancelling) return;
          const r = await cancel.run(cancelling);
          if (r.ok) { setCancelling(null); toast.show(t('cancelled')); query.reload(); }
        }}>
        <ErrorAlert error={cancel.error} onReload={() => { cancel.reset(); setCancelling(null); query.reload(); }} />
        {cancelling && <p>{t('cancelBody', { when: time.full(cancelling.startsAt) })}</p>}
        <TextAreaField label={t('cancelReason')} name="reason" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
      </ConfirmDialog>
    </>
  );
}

/* ======================= support requests ======================= */

export function PortalSupportScreen() {
  const t = useTranslations('support.portal');
  const s = useTranslations('support.common');
  const common = useTranslations('common');
  const labels = useConsultationLabels();
  const format = useLocaleFormat();
  const query = useApiQuery<TicketSummary[]>('portal-tickets', (api) => api.support.portal.list());
  const [creating, setCreating] = useState(false);
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} actions={!creating && <Button onClick={() => setCreating(true)} data-testid="ticket-new">{t('new')}</Button>} />
      {creating && <NewTicketForm onDone={(created) => { setCreating(false); if (created) query.reload(); }} />}
      {!query.data ? <Spinner label={common('loading')} /> : (
        <DataTable<TicketSummary>
          caption={t('title')} rows={query.data} getRowKey={(r) => r.id} empty={<StatePanel title={t('empty')} />}
          columns={[
            { key: 'subject', header: s('subject'), isRowHeader: true, cell: (r) => <Link href={`/support/${r.id}`}>{r.subject}</Link> },
            { key: 'n', header: s('number'), cell: (r) => s('numberValue', { number: r.number }) },
            { key: 'student', header: s('student'), cell: (r) => r.studentName },
            { key: 'cat', header: s('category'), cell: (r) => labels.category(r.category) },
            { key: 'status', header: common('status'), cell: (r) => <TicketStatusBadge status={r.status} /> },
            { key: 'last', header: s('lastActivity'), cell: (r) => format.dateTime(r.lastMessageAt) },
          ]}
        />
      )}
    </>
  );
}

function NewTicketForm({ onDone }: { onDone: (created: boolean) => void }) {
  const t = useTranslations('support.portal');
  const s = useTranslations('support.common');
  const common = useTranslations('common');
  const labels = useConsultationLabels();
  const kind = usePortalKind();
  const toast = useToast();
  const profile = useApiQuery(`portal-profile|${kind}`, (api) => (kind === 'student' ? api.portal.profile() : Promise.resolve(null)));
  const linked = useApiQuery<PortalStudent[]>(`portal-students|${kind}`, (api) => (kind === 'guardian' ? api.portal.students() : Promise.resolve([])));
  const students = kind === 'student' && profile.data ? [{ id: profile.data.profile.id, name: '' }]
    : (linked.data ?? []).map((x) => ({ id: x.id, name: `${x.firstName} ${x.lastName}` }));
  const [studentId, setStudentId] = useState('');
  const [subject, setSubject] = useState('');
  const [category, setCategory] = useState<TicketCategory>('general');
  const [body, setBody] = useState('');
  const chosen = studentId || students[0]?.id || '';
  const create = useApiMutation((api) => api.support.portal.create({ studentId: chosen, subject: subject.trim(), category, body: body.trim() }));
  return (
    <Card aria-labelledby="new-ticket-h" style={{ marginBlockEnd: 16 }}>
      <h2 id="new-ticket-h" style={h2}>{t('newTitle')}</h2>
      <form style={{ display: 'grid', gap: 8 }} onSubmit={async (e) => {
        e.preventDefault();
        if (!chosen || !subject.trim() || !body.trim()) return;
        const r = await create.run();
        if (r.ok) { toast.show(t('created')); onDone(true); }
      }}>
        <ErrorAlert error={create.error} />
        <FormGrid>
          {kind === 'guardian' && students.length > 1 && (
            <SelectField label={s('student')} name="studentId" value={chosen} onChange={(e) => setStudentId(e.target.value)}
              options={students.map((x) => ({ value: x.id, label: x.name }))} />
          )}
          <TextField label={s('subject')} name="subject" required maxLength={200} value={subject} onChange={(e) => setSubject(e.target.value)} />
          <SelectField label={s('category')} name="category" value={category} onChange={(e) => setCategory(e.target.value as TicketCategory)}
            options={TICKET_CATEGORIES.map((c) => ({ value: c, label: labels.category(c) }))} />
        </FormGrid>
        <TextAreaField label={t('message')} name="body" required maxLength={4000} value={body} onChange={(e) => setBody(e.target.value)} />
        <div style={{ display: 'flex', gap: 8 }}>
          <Button type="submit" disabled={create.busy || !chosen || !subject.trim() || !body.trim()} data-testid="ticket-submit">{t('submit')}</Button>
          <Button type="button" variant="ghost" onClick={() => onDone(false)}>{common('cancel')}</Button>
        </div>
      </form>
    </Card>
  );
}

export function PortalTicketScreen({ id }: { id: string }) {
  const t = useTranslations('support.portal');
  const s = useTranslations('support.common');
  const common = useTranslations('common');
  const labels = useConsultationLabels();
  const toast = useToast();
  const query = useApiQuery<TicketDetail>(`portal-ticket|${id}`, (api) => api.support.portal.get(id));
  const [text, setText] = useState('');
  const [closing, setClosing] = useState(false);
  const reply = useApiMutation((api) => api.support.portal.reply(id, text.trim()));
  const close = useApiMutation((api, d: TicketDetail) => api.support.portal.close(id, d.ticket.rowVersion));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const d = query.data;
  if (!d) return <Spinner label={common('loading')} />;
  const closed = d.ticket.status === 'closed';
  return (
    <>
      <PageHeader before={backLink('/support', t('back'))} title={d.ticket.subject}
        description={<>{s('numberValue', { number: d.ticket.number })} · {d.ticket.studentName} · {labels.category(d.ticket.category)}</>}
        actions={!closed && <Button variant="secondary" onClick={() => { close.reset(); setClosing(true); }} data-testid="ticket-close">{t('close')}</Button>} />
      <p style={{ display: 'flex', gap: 8, alignItems: 'center' }}><TicketStatusBadge status={d.ticket.status} /></p>
      <Card aria-labelledby="thread-h">
        <h2 id="thread-h" style={h2}>{t('thread')}</h2>
        <TicketThread detail={d} />
        {closed ? <p role="status" style={{ marginBlockEnd: 0 }}>{t('closedNotice')}</p> : (
          <form style={{ marginBlockStart: 16, display: 'grid', gap: 8 }} onSubmit={async (e) => {
            e.preventDefault();
            if (!text.trim()) return;
            const r = await reply.run();
            if (r.ok) { query.setData(r.value); setText(''); toast.show(t('sent')); }
          }}>
            <ErrorAlert error={reply.error} />
            <TextAreaField label={t('reply')} name="reply" required maxLength={4000} value={text} onChange={(e) => setText(e.target.value)} />
            <div><Button type="submit" disabled={reply.busy || !text.trim()} data-testid="portal-ticket-send">{t('send')}</Button></div>
          </form>
        )}
      </Card>
      <ConfirmDialog open={closing} title={t('closeTitle')} confirmLabel={t('close')} cancelLabel={common('cancel')} busy={close.busy} onCancel={() => setClosing(false)}
        onConfirm={async () => {
          const r = await close.run(d);
          if (r.ok) { query.setData(r.value); setClosing(false); }
        }}>
        <ErrorAlert error={close.error} onReload={() => { close.reset(); setClosing(false); query.reload(); }} />
        <p>{t('closeBody')}</p>
      </ConfirmDialog>
    </>
  );
}
