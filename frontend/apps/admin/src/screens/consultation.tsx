'use client';

import {
  BOOKING_STATUSES, SESSION_MODES, type Booking, type BookingAction, type BookingDetail, type BookingStatus, type Consultant, type ConsultationMe,
  type ConsultationNote, type ConsultationSettings, type SessionMode, type Slot, type TenantUser,
} from '@eemp/api-client';
import { DateField, ErrorAlert, LoadError, useApiMutation, useApiQuery, useSession } from '@eemp/app';
import { BookingStatusBadge, useConsultationLabels, useSessionTime, useWeek, WeekToolbar, WeekView } from '@eemp/app/consultation';
import { WEEKDAYS, zonedToUtc } from '@eemp/i18n';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Link } from '@eemp/i18n/navigation';
import {
  Alert, Badge, Button, Card, Checkbox, ConfirmDialog, DataTable, DescriptionList, Fieldset, FormGrid, PageHeader, SelectField, Spinner, StatePanel,
  TextAreaField, TextField, useToast,
} from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState } from 'react';

import { BackLink, useFieldErrors } from '../components/common';
import { sectionTitle, useBranchNames } from './finance/common';

function useMe() {
  return useApiQuery<ConsultationMe>('consultation-me', (api) => api.consultation.me());
}

/* ======================= my schedule / availability ======================= */

export function ConsultationScheduleScreen() {
  const t = useTranslations('consultation.schedule');
  const common = useTranslations('common');
  const me = useMe();
  const consultants = useApiQuery<Consultant[]>('consultation-consultants|active', (api) =>
    api.consultation.consultants.list().catch(() => [] as Consultant[]));
  const [selected, setSelected] = useState('');
  if (me.error && !me.data) return <LoadError error={me.error} onRetry={me.reload} />;
  if (!me.data || !consultants.data) return <Spinner label={common('loading')} />;
  const list = consultants.data;
  const own = me.data.consultant;
  const consultantId = selected || (own && me.data.canConsult ? own.id : list[0]?.id ?? '');
  const consultant = list.find((c) => c.id === consultantId) ?? (own?.id === consultantId ? own : undefined);
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      {!own && !me.data.canManageConsultants && <Alert tone="info" style={{ marginBlockEnd: 12 }}>{t('notConsultant')}</Alert>}
      {(me.data.canManageConsultants || me.data.canReadBookings) && list.length > 0 && (
        <div style={{ maxInlineSize: '24rem', marginBlockEnd: 12 }}>
          <SelectField label={t('consultant')} name="consultant" value={consultantId} onChange={(e) => setSelected(e.target.value)}
            options={list.map((c) => ({ value: c.id, label: c.displayName }))} />
        </div>
      )}
      {consultant ? <ScheduleWeek key={consultant.id} consultant={consultant} canEdit={(own?.id === consultant.id && me.data.canConsult) || me.data.canManageConsultants} />
        : <StatePanel title={t('noConsultant')} />}
    </>
  );
}

function ScheduleWeek({ consultant, canEdit }: { consultant: Consultant; canEdit: boolean }) {
  const t = useTranslations('consultation.schedule');
  const c = useTranslations('consultation.common');
  const labels = useConsultationLabels();
  const time = useSessionTime();
  const toast = useToast();
  const week = useWeek();
  const slots = useApiQuery<Slot[]>(`consultation-slots|${consultant.id}|${week.start}`, (api) =>
    api.consultation.slots.list({ consultantId: consultant.id, from: week.from, to: week.to }));
  const [dialog, setDialog] = useState<'single' | 'bulk' | null>(null);
  const [removing, setRemoving] = useState<Slot | null>(null);
  const remove = useApiMutation((api, s: Slot) => api.consultation.slots.remove(s.id));
  if (slots.error && !slots.data) return <LoadError error={slots.error} onRetry={slots.reload} />;
  const rows = (slots.data ?? []).filter((s) => s.status === 'open');
  return (
    <>
      <WeekToolbar week={week} actions={canEdit && (
        <>
          <Button size="sm" onClick={() => setDialog('single')} data-testid="slot-add">{t('addSlot')}</Button>
          <Button size="sm" variant="secondary" onClick={() => setDialog('bulk')} data-testid="slot-bulk">{t('repeat')}</Button>
        </>
      )} />
      {!slots.data ? <Spinner label={c('loading')} /> : (
        <WeekView
          week={week} items={rows} testId="schedule-week" getKey={(s) => s.id} startOf={(s) => s.startsAt} emptyLabel={t('dayEmpty')}
          render={(s) => (
            <div data-testid="slot" style={{ display: 'grid', gap: 2, padding: 6, borderRadius: 8, background: s.booking ? 'var(--eemp-color-primary-soft)' : 'var(--eemp-color-surface-muted)' }}>
              <span style={{ fontWeight: 600 }}>{time.range(s.startsAt, s.endsAt)}</span>
              <span style={{ fontSize: '0.8125rem' }}>{labels.mode(s.mode)}{s.location ? ` · ${s.location}` : ''}</span>
              {s.booking ? (
                <Link href={`/consultation/bookings/${s.booking.id}`} style={{ fontSize: '0.8125rem' }}>
                  {s.booking.studentName} · {labels.status(s.booking.status)}
                </Link>
              ) : (
                <span style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: '0.8125rem' }}>
                  <Badge tone="success">{t('free')}</Badge>
                  {canEdit && (
                    <Button size="sm" variant="ghost" onClick={() => { remove.reset(); setRemoving(s); }} aria-label={t('removeSlotFor', { time: time.full(s.startsAt) })}>
                      {t('remove')}
                    </Button>
                  )}
                </span>
              )}
            </div>
          )}
        />
      )}
      {dialog === 'single' && <SingleSlotDialog consultant={consultant} onClose={(changed) => { setDialog(null); if (changed) { toast.show(t('slotAdded')); slots.reload(); } }} />}
      {dialog === 'bulk' && <BulkSlotsDialog consultant={consultant} onClose={(message) => { setDialog(null); if (message) { toast.show(message); slots.reload(); } }} />}
      <ConfirmDialog
        open={removing !== null} tone="danger" title={t('removeTitle')} confirmLabel={t('remove')} cancelLabel={c('cancel')} busy={remove.busy}
        onCancel={() => setRemoving(null)}
        onConfirm={async () => {
          if (!removing) return;
          const r = await remove.run(removing);
          if (r.ok) { setRemoving(null); toast.show(t('removed')); slots.reload(); }
        }}
      >
        <ErrorAlert error={remove.error} />
        <p>{removing && t('removeBody', { time: time.full(removing.startsAt) })}</p>
      </ConfirmDialog>
    </>
  );
}

function BranchSelect({ consultant, value, onChange }: { consultant: Consultant; value: string; onChange: (v: string) => void }) {
  const c = useTranslations('consultation.common');
  const branches = useBranchNames();
  if (consultant.branchIds.length <= 1) return null;
  return (
    <SelectField label={c('branch')} name="branchId" value={value} onChange={(e) => onChange(e.target.value)}
      options={consultant.branchIds.map((b) => ({ value: b, label: branches.name(b) || b.slice(0, 8) }))} />
  );
}

function ModeFields({ mode, setMode, location, setLocation }: { mode: SessionMode; setMode: (m: SessionMode) => void; location: string; setLocation: (v: string) => void }) {
  const c = useTranslations('consultation.common');
  const labels = useConsultationLabels();
  return (
    <>
      <SelectField label={c('mode')} name="mode" value={mode} onChange={(e) => setMode(e.target.value as SessionMode)}
        options={SESSION_MODES.map((m) => ({ value: m, label: labels.mode(m) }))} />
      <TextField label={c('location')} name="location" maxLength={200} value={location} onChange={(e) => setLocation(e.target.value)}
        hint={mode === 'online' ? c('onlineHint') : undefined} />
    </>
  );
}

function SingleSlotDialog({ consultant, onClose }: { consultant: Consultant; onClose: (changed: boolean) => void }) {
  const t = useTranslations('consultation.schedule');
  const c = useTranslations('consultation.common');
  const format = useLocaleFormat();
  const [branchId, setBranchId] = useState(consultant.branchIds[0] ?? '');
  const [date, setDate] = useState('');
  const [start, setStart] = useState('10:00');
  const [duration, setDuration] = useState('30');
  const [mode, setMode] = useState<SessionMode>('inPerson');
  const [location, setLocation] = useState('');
  const [local, setLocal] = useState<string | null>(null);
  const save = useApiMutation((api) => {
    const startsAt = zonedToUtc(date, start, format.timeZone)!;
    const endsAt = new Date(Date.parse(startsAt) + Number(duration) * 60_000).toISOString();
    return api.consultation.slots.create({ consultantId: consultant.id, branchId, startsAt, endsAt, mode, location: location.trim() || null });
  });
  const errors = useFieldErrors(save.error);
  return (
    <ConfirmDialog open title={t('addSlotTitle')} confirmLabel={t('addSlot')} cancelLabel={c('cancel')} busy={save.busy} onCancel={() => onClose(false)}
      onConfirm={async () => {
        if (!date || !zonedToUtc(date, start, format.timeZone)) { setLocal(t('dateTimeRequired')); return; }
        setLocal(null);
        const r = await save.run();
        if (r.ok) onClose(true);
      }}>
      {local && <Alert tone="danger" role="alert">{local}</Alert>}
      <ErrorAlert error={save.error} />
      <FormGrid>
        <BranchSelect consultant={consultant} value={branchId} onChange={setBranchId} />
        <DateField label={t('date')} name="date" value={date} onChange={setDate} required error={errors.startsAt} />
        <TextField label={t('startTime')} name="start" type="time" required value={start} onChange={(e) => setStart(e.target.value)} />
        <SelectField label={t('duration')} name="duration" value={duration} onChange={(e) => setDuration(e.target.value)} error={errors.endsAt}
          options={['15', '30', '45', '60', '90'].map((m) => ({ value: m, label: t('minutes', { n: Number(m) }) }))} />
        <ModeFields mode={mode} setMode={setMode} location={location} setLocation={setLocation} />
      </FormGrid>
    </ConfirmDialog>
  );
}

function BulkSlotsDialog({ consultant, onClose }: { consultant: Consultant; onClose: (message: string | null) => void }) {
  const t = useTranslations('consultation.schedule');
  const c = useTranslations('consultation.common');
  const academic = useTranslations('academic.enums');
  const [branchId, setBranchId] = useState(consultant.branchIds[0] ?? '');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [days, setDays] = useState<string[]>(['saturday', 'sunday', 'monday', 'tuesday', 'wednesday']);
  const [start, setStart] = useState('10:00');
  const [end, setEnd] = useState('12:00');
  const [duration, setDuration] = useState('30');
  const [mode, setMode] = useState<SessionMode>('inPerson');
  const [location, setLocation] = useState('');
  const save = useApiMutation((api) => api.consultation.slots.bulk({
    consultantId: consultant.id, branchId, fromDate, toDate, weekdays: days, startTime: start, endTime: end, durationMinutes: Number(duration), mode,
    location: location.trim() || null,
  }));
  const errors = useFieldErrors(save.error);
  return (
    <ConfirmDialog open title={t('repeatTitle')} confirmLabel={t('repeatConfirm')} cancelLabel={c('cancel')} busy={save.busy} onCancel={() => onClose(null)}
      onConfirm={async () => {
        const r = await save.run();
        if (r.ok) onClose(t('bulkDone', { created: r.value.created, skipped: r.value.skipped }));
      }}>
      <ErrorAlert error={save.error} />
      <p style={{ marginBlockStart: 0 }}>{t('repeatIntro')}</p>
      <FormGrid>
        <BranchSelect consultant={consultant} value={branchId} onChange={setBranchId} />
        <DateField label={t('fromDate')} name="fromDate" value={fromDate} onChange={setFromDate} required error={errors.fromDate} />
        <DateField label={t('toDate')} name="toDate" value={toDate} onChange={setToDate} required error={errors.toDate} />
        <TextField label={t('startTime')} name="start" type="time" required value={start} onChange={(e) => setStart(e.target.value)} error={errors.startTime} />
        <TextField label={t('endTime')} name="end" type="time" required value={end} onChange={(e) => setEnd(e.target.value)} error={errors.endTime} />
        <SelectField label={t('duration')} name="duration" value={duration} onChange={(e) => setDuration(e.target.value)} error={errors.durationMinutes}
          options={['15', '30', '45', '60', '90'].map((m) => ({ value: m, label: t('minutes', { n: Number(m) }) }))} />
        <ModeFields mode={mode} setMode={setMode} location={location} setLocation={setLocation} />
      </FormGrid>
      <Fieldset style={{ marginBlockStart: 12 }}>
        <legend>{t('weekdays')}</legend>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12 }}>
          {WEEKDAYS.map((d) => (
            <Checkbox key={d} name={`day-${d}`} label={academic('weekday', { value: d })} checked={days.includes(d)}
              onChange={(e) => setDays((cur) => (e.target.checked ? [...cur, d] : cur.filter((x) => x !== d)))} />
          ))}
        </div>
        {errors.weekdays && <p role="alert" style={{ color: 'var(--eemp-color-danger)' }}>{errors.weekdays}</p>}
      </Fieldset>
    </ConfirmDialog>
  );
}

/* ======================= bookings (week + list) ======================= */

export function ConsultationBookingsScreen() {
  const t = useTranslations('consultation.bookings');
  const common = useTranslations('common');
  const labels = useConsultationLabels();
  const time = useSessionTime();
  const me = useMe();
  const week = useWeek();
  const [status, setStatus] = useState<BookingStatus | ''>('');
  const [scope, setScope] = useState<'all' | 'mine' | ''>('');
  const [view, setView] = useState<'week' | 'list'>('week');
  const canAll = !!me.data && (me.data.canReadBookings || me.data.canManageBookings || me.data.canReadNotes);
  const effectiveScope = scope || (canAll ? 'all' : 'mine');
  const bookings = useApiQuery(`consultation-bookings|${effectiveScope}|${status}|${week.start}|${view}|${me.data ? 1 : 0}`, (api) =>
    me.data
      ? api.consultation.bookings.list({ scope: effectiveScope, status: status || undefined, pageSize: 100, ...(view === 'week' ? { from: week.from, to: week.to } : {}) })
      : Promise.resolve(null));
  if (me.error && !me.data) return <LoadError error={me.error} onRetry={me.reload} />;
  if (!me.data) return <Spinner label={common('loading')} />;
  const rows = bookings.data?.items ?? [];
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      <FormGrid style={{ marginBlockEnd: 12 }}>
        <SelectField label={t('view')} name="view" value={view} onChange={(e) => setView(e.target.value === 'list' ? 'list' : 'week')}
          options={[{ value: 'week', label: t('weekView') }, { value: 'list', label: t('listView') }]} />
        {canAll && me.data.consultant && (
          <SelectField label={t('scope')} name="scope" value={effectiveScope} onChange={(e) => setScope(e.target.value === 'mine' ? 'mine' : 'all')}
            options={[{ value: 'all', label: t('all') }, { value: 'mine', label: t('mine') }]} />
        )}
        <SelectField label={common('status')} name="status" value={status} placeholder={t('allStatuses')} onChange={(e) => setStatus(e.target.value as BookingStatus | '')}
          options={BOOKING_STATUSES.map((s) => ({ value: s, label: labels.status(s) }))} />
      </FormGrid>
      {bookings.error && !bookings.data ? <LoadError error={bookings.error} onRetry={bookings.reload} /> : !bookings.data ? <Spinner label={common('loading')} /> : view === 'week' ? (
        <>
          <WeekToolbar week={week} />
          <WeekView
            week={week} items={rows} testId="bookings-week" getKey={(b) => b.id} startOf={(b) => b.startsAt} emptyLabel={t('dayEmpty')}
            render={(b) => <BookingChip booking={b} />}
          />
        </>
      ) : (
        <DataTable<Booking>
          caption={t('title')} rows={rows} getRowKey={(b) => b.id} empty={<StatePanel title={t('empty')} />}
          columns={[
            { key: 'when', header: t('when'), isRowHeader: true, cell: (b) => <Link href={`/consultation/bookings/${b.id}`}>{time.full(b.startsAt)}</Link> },
            { key: 'student', header: t('student'), cell: (b) => b.studentName },
            { key: 'consultant', header: t('consultant'), cell: (b) => b.consultantName },
            { key: 'topic', header: t('topic'), cell: (b) => b.topic },
            { key: 'status', header: common('status'), cell: (b) => <BookingStatusBadge status={b.status} /> },
          ]}
        />
      )}
    </>
  );
}

function BookingChip({ booking: b }: { booking: Booking }) {
  const time = useSessionTime();
  return (
    <Link href={`/consultation/bookings/${b.id}`} data-testid="booking-chip"
      style={{ display: 'grid', gap: 2, padding: 6, borderRadius: 8, background: 'var(--eemp-color-surface-muted)', textDecoration: 'none', color: 'inherit' }}>
      <span style={{ fontWeight: 600 }}>{time.range(b.startsAt, b.endsAt)}</span>
      <span>{b.studentName}</span>
      <span style={{ fontSize: '0.8125rem', color: 'var(--eemp-color-fg-muted)' }}>{b.topic}</span>
      <span><BookingStatusBadge status={b.status} /></span>
    </Link>
  );
}

/* ======================= booking detail + confidential notes ======================= */

export function ConsultationBookingScreen({ id }: { id: string }) {
  const t = useTranslations('consultation.booking');
  const c = useTranslations('consultation.common');
  const common = useTranslations('common');
  const labels = useConsultationLabels();
  const time = useSessionTime();
  const format = useLocaleFormat();
  const toast = useToast();
  const query = useApiQuery<BookingDetail>(`consultation-booking|${id}`, (api) => api.consultation.bookings.get(id));
  const [action, setAction] = useState<BookingAction | null>(null);
  const [reason, setReason] = useState('');
  const act = useApiMutation((api, d: BookingDetail, a: BookingAction) => api.consultation.bookings.act(d.booking.id, a, { rowVersion: d.booking.rowVersion, reason: reason.trim() || null }));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const d = query.data;
  if (!d) return <Spinner label={common('loading')} />;
  const b = d.booking;
  return (
    <>
      <PageHeader
        before={<BackLink href="/consultation/bookings" />}
        title={b.topic}
        description={<>{b.studentName} · {time.full(b.startsAt)}</>}
        actions={d.allowedActions.map((a) => (
          <Button key={a} data-testid={`booking-${a}`} variant={a === 'confirm' || a === 'complete' ? 'primary' : a === 'cancel' ? 'danger' : 'secondary'}
            onClick={() => { act.reset(); setReason(''); setAction(a); }}>
            {labels.action(a)}
          </Button>
        ))}
      />
      <div style={{ display: 'grid', gap: 16 }}>
        <Card>
          <DescriptionList>
            <dt>{common('status')}</dt>
            <dd><BookingStatusBadge status={b.status} /></dd>
            <dt>{c('student')}</dt>
            <dd>{b.studentName}</dd>
            <dt>{c('consultant')}</dt>
            <dd>{b.consultantName}</dd>
            <dt>{c('when')}</dt>
            <dd>{time.day(b.startsAt)} · {time.range(b.startsAt, b.endsAt)}</dd>
            <dt>{c('mode')}</dt>
            <dd>{labels.mode(b.mode)}{b.location ? ` · ${b.location}` : ''}</dd>
            <dt>{c('topic')}</dt>
            <dd>{b.topic}</dd>
            {b.requesterNote && (<><dt>{c('requesterNote')}</dt><dd style={{ whiteSpace: 'pre-wrap' }}>{b.requesterNote}</dd></>)}
            <dt>{t('requestedAt')}</dt>
            <dd>{format.dateTime(b.createdAt)}</dd>
            {b.cancelledAt && (
              <>
                <dt>{t('cancelled')}</dt>
                <dd>{format.dateTime(b.cancelledAt)} · {labels.cancelledBy(b.cancelledBy ?? '')}{b.cancelReason ? ` · ${b.cancelReason}` : ''}</dd>
              </>
            )}
          </DescriptionList>
        </Card>
        {d.canReadNotes ? <NotesCard booking={b} canWrite={d.canWriteNotes} /> : (
          <Alert tone="info" data-testid="notes-hidden">{t('notesHidden')}</Alert>
        )}
      </div>
      <ConfirmDialog
        open={action !== null} tone={action === 'cancel' || action === 'noShow' ? 'danger' : 'primary'} title={labels.action(action ?? '')}
        confirmLabel={labels.action(action ?? '')} cancelLabel={common('cancel')} busy={act.busy} onCancel={() => setAction(null)}
        onConfirm={async () => {
          if (!action) return;
          const r = await act.run(d, action);
          if (r.ok) { query.setData(r.value); setAction(null); toast.show(t('updated')); }
        }}
      >
        <ErrorAlert error={act.error} onReload={() => { act.reset(); setAction(null); query.reload(); }} />
        <p>{t('confirmBody', { action: action ?? '' })}</p>
        {action === 'cancel' && (
          <TextAreaField label={t('reason')} name="reason" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} hint={t('reasonHint')} />
        )}
      </ConfirmDialog>
    </>
  );
}

function NotesCard({ booking, canWrite }: { booking: Booking; canWrite: boolean }) {
  const t = useTranslations('consultation.booking');
  const common = useTranslations('common');
  const format = useLocaleFormat();
  const toast = useToast();
  const notes = useApiQuery<ConsultationNote[]>(`consultation-notes|${booking.id}`, (api) => api.consultation.bookings.notes(booking.id));
  const [text, setText] = useState('');
  const add = useApiMutation((api) => api.consultation.bookings.addNote(booking.id, text.trim()));
  const errors = useFieldErrors(add.error);
  return (
    <Card aria-labelledby="notes-h" data-testid="notes-card">
      <h2 id="notes-h" style={sectionTitle}>{t('notesTitle')} <Badge tone="warning">{t('confidential')}</Badge></h2>
      <p style={{ marginBlockStart: 0, color: 'var(--eemp-color-fg-muted)' }}>{t('notesIntro')}</p>
      {notes.error && !notes.data ? <LoadError error={notes.error} onRetry={notes.reload} /> : !notes.data ? <Spinner label={common('loading')} /> : notes.data.length === 0 ? (
        <StatePanel title={t('notesEmpty')} />
      ) : (
        <ol style={{ listStyle: 'none', margin: 0, padding: 0, display: 'grid', gap: 8 }}>
          {notes.data.map((n) => (
            <li key={n.id} style={{ padding: 10, borderRadius: 8, background: 'var(--eemp-color-surface-muted)' }}>
              <div style={{ fontSize: '0.8125rem', color: 'var(--eemp-color-fg-muted)' }}>{n.authorName} · {format.dateTime(n.createdAt)}</div>
              <p style={{ margin: '4px 0 0', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{n.body}</p>
            </li>
          ))}
        </ol>
      )}
      {canWrite && (
        <form style={{ marginBlockStart: 12, display: 'grid', gap: 8 }} onSubmit={async (e) => {
          e.preventDefault();
          if (!text.trim()) return;
          const r = await add.run();
          if (r.ok) { setText(''); toast.show(t('noteSaved')); notes.reload(); }
        }}>
          <ErrorAlert error={add.error} />
          <TextAreaField label={t('newNote')} name="note" required maxLength={4000} value={text} onChange={(e) => setText(e.target.value)} error={errors.body} />
          <div><Button type="submit" disabled={add.busy || !text.trim()} data-testid="note-save">{t('addNote')}</Button></div>
        </form>
      )}
    </Card>
  );
}

/* ======================= consultants + policy ======================= */

export function ConsultantsScreen() {
  const t = useTranslations('consultation.consultants');
  const common = useTranslations('common');
  const branches = useBranchNames();
  const format = useLocaleFormat();
  const listFormat = new Intl.ListFormat(format.metadata.intlLocale, { type: 'conjunction', style: 'narrow' });
  const me = useMe();
  const list = useApiQuery<Consultant[]>('consultation-consultants|all', (api) => api.consultation.consultants.list({ includeInactive: true }));
  const [editing, setEditing] = useState<Consultant | 'new' | null>(null);
  const toast = useToast();
  if (list.error && !list.data) return <LoadError error={list.error} onRetry={list.reload} />;
  if (!list.data || !me.data) return <Spinner label={common('loading')} />;
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')}
        actions={me.data.canManageConsultants && <Button onClick={() => setEditing('new')} data-testid="consultant-new">{t('new')}</Button>} />
      <DataTable<Consultant>
        caption={t('title')} rows={list.data} getRowKey={(r) => r.id} empty={<StatePanel title={t('empty')} />}
        columns={[
          { key: 'name', header: t('displayName'), isRowHeader: true, cell: (r) => r.displayName },
          { key: 'spec', header: t('specialties'), cell: (r) => r.specialties ?? '' },
          { key: 'branches', header: t('branches'), cell: (r) => listFormat.format(r.branchIds.map((b) => branches.name(b)).filter(Boolean)) },
          { key: 'active', header: common('status'), cell: (r) => <Badge tone={r.isActive ? 'success' : 'neutral'}>{r.isActive ? common('active') : common('inactive')}</Badge> },
          { key: 'edit', header: '', cell: (r) => me.data?.canManageConsultants && <Button size="sm" variant="ghost" onClick={() => setEditing(r)} aria-label={t('editFor', { name: r.displayName })}>{common('edit')}</Button> },
        ]}
      />
      {me.data.canManageSettings && <SettingsCard />}
      {editing && (
        <ConsultantDialog consultant={editing === 'new' ? null : editing}
          onClose={(saved) => { setEditing(null); if (saved) { toast.show(common('saved')); list.reload(); } }} />
      )}
    </>
  );
}

function ConsultantDialog({ consultant, onClose }: { consultant: Consultant | null; onClose: (saved: boolean) => void }) {
  const t = useTranslations('consultation.consultants');
  const common = useTranslations('common');
  const { can } = useSession();
  const branches = useBranchNames();
  const users = useApiQuery<TenantUser[]>(`consultation-users|${can('users.read')}`, async (api) => (can('users.read') ? (await api.users.list({ pageSize: 100 })).items : []));
  const [userId, setUserId] = useState(consultant?.userId ?? '');
  const [displayName, setDisplayName] = useState(consultant?.displayName ?? '');
  const [bio, setBio] = useState(consultant?.bio ?? '');
  const [specialties, setSpecialties] = useState(consultant?.specialties ?? '');
  const [branchIds, setBranchIds] = useState<string[]>(consultant?.branchIds ?? []);
  const [active, setActive] = useState(consultant?.isActive ?? true);
  const save = useApiMutation((api) => {
    const body = { displayName, bio: bio.trim() || null, specialties: specialties.trim() || null, branchIds };
    return consultant
      ? api.consultation.consultants.update(consultant.id, { ...body, isActive: active, rowVersion: consultant.rowVersion })
      : api.consultation.consultants.create({ ...body, userId: userId.trim() });
  });
  const errors = useFieldErrors(save.error);
  return (
    <ConfirmDialog open title={consultant ? t('editTitle') : t('newTitle')} confirmLabel={common('save')} cancelLabel={common('cancel')} busy={save.busy}
      onCancel={() => onClose(false)} onConfirm={async () => { const r = await save.run(); if (r.ok) onClose(true); }}>
      <ErrorAlert error={save.error} />
      <FormGrid>
        {!consultant && (users.data && users.data.length > 0 ? (
          <SelectField label={t('user')} name="userId" required value={userId} placeholder={t('chooseUser')} onChange={(e) => setUserId(e.target.value)} error={errors.userId}
            options={users.data.map((u) => ({ value: u.id, label: `${u.displayName} (${u.userName})` }))} />
        ) : (
          <TextField label={t('userId')} name="userId" required value={userId} onChange={(e) => setUserId(e.target.value)} hint={t('userIdHint')} error={errors.userId} className="eemp-ltr" />
        ))}
        <TextField label={t('displayName')} name="displayName" required maxLength={200} value={displayName} onChange={(e) => setDisplayName(e.target.value)} error={errors.displayName} />
        <TextField label={t('specialties')} name="specialties" maxLength={500} value={specialties} onChange={(e) => setSpecialties(e.target.value)} />
      </FormGrid>
      <TextAreaField label={t('bio')} name="bio" maxLength={1000} value={bio} onChange={(e) => setBio(e.target.value)} />
      <Fieldset>
        <legend>{t('branches')}</legend>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12 }}>
          {branches.list.map((b) => (
            <Checkbox key={b.id} name={`branch-${b.id}`} label={b.name} checked={branchIds.includes(b.id)}
              onChange={(e) => setBranchIds((cur) => (e.target.checked ? [...cur, b.id] : cur.filter((x) => x !== b.id)))} />
          ))}
        </div>
        {errors.branchIds && <p role="alert" style={{ color: 'var(--eemp-color-danger)' }}>{errors.branchIds}</p>}
      </Fieldset>
      {consultant && <Checkbox name="active" label={t('active')} checked={active} onChange={(e) => setActive(e.target.checked)} />}
    </ConfirmDialog>
  );
}

function SettingsCard() {
  const t = useTranslations('consultation.settings');
  const common = useTranslations('common');
  const toast = useToast();
  const query = useApiQuery<ConsultationSettings>('consultation-settings', (api) => api.consultation.settings.get());
  const [form, setForm] = useState<Record<string, string> | null>(null);
  const save = useApiMutation((api, s: ConsultationSettings, f: Record<string, string>) => api.consultation.settings.update({
    cancellationWindowHours: Number(f.cancellationWindowHours), bookingHorizonDays: Number(f.bookingHorizonDays), minLeadHours: Number(f.minLeadHours),
    maxActiveBookingsPerStudent: Number(f.maxActiveBookingsPerStudent), rowVersion: s.rowVersion,
  }));
  const errors = useFieldErrors(save.error);
  const s = query.data;
  if (!s) return null;
  const values = form ?? Object.fromEntries(Object.entries(s).filter(([k]) => k !== 'rowVersion').map(([k, v]) => [k, String(v)]));
  const field = (key: string, min: number, max: number) => (
    <TextField label={t(key)} name={key} type="number" min={min} max={max} required value={values[key] ?? ''} error={errors[key]}
      onChange={(e) => setForm({ ...values, [key]: e.target.value })} />
  );
  return (
    <Card aria-labelledby="cons-settings-h" style={{ marginBlockStart: 16 }}>
      <h2 id="cons-settings-h" style={sectionTitle}>{t('title')}</h2>
      <p style={{ marginBlockStart: 0, color: 'var(--eemp-color-fg-muted)' }}>{t('intro')}</p>
      <form onSubmit={async (e) => {
        e.preventDefault();
        const r = await save.run(s, values);
        if (r.ok) { query.setData(r.value); setForm(null); toast.show(common('saved')); }
      }}>
        <ErrorAlert error={save.error} onReload={() => { save.reset(); setForm(null); query.reload(); }} />
        <FormGrid>
          {field('cancellationWindowHours', 0, 336)}
          {field('bookingHorizonDays', 1, 180)}
          {field('minLeadHours', 0, 168)}
          {field('maxActiveBookingsPerStudent', 1, 20)}
        </FormGrid>
        <Button type="submit" disabled={save.busy} style={{ marginBlockStart: 12 }}>{common('save')}</Button>
      </form>
    </Card>
  );
}
