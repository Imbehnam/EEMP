'use client';

import type { BookingStatus, TicketDetail, TicketStatus } from '@eemp/api-client';
import { addDaysIso, instantDayIso, todayIso, weekDays, weekStartIso } from '@eemp/i18n';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Badge, Button, Card, IconChevron, StatePanel, type Tone } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useMemo, useState, type ReactNode } from 'react';

/* ---------------- labels and badges ---------------- */

/** Translated labels for consultation/support enums (ICU selects with an `other` fallback). */
export function useConsultationLabels() {
  const t = useTranslations('consultation.enums');
  const s = useTranslations('support.enums');
  return useMemo(
    () => ({
      status: (value: string) => t('status', { value }),
      mode: (value: string) => t('mode', { value }),
      action: (value: string) => t('action', { value }),
      cancelledBy: (value: string) => t('cancelledBy', { value }),
      ticketStatus: (value: string) => s('status', { value }),
      priority: (value: string) => s('priority', { value }),
      category: (value: string) => s('category', { value }),
    }),
    [t, s],
  );
}

const BOOKING_TONE: Record<BookingStatus, Tone> = { requested: 'warning', confirmed: 'info', completed: 'success', cancelled: 'neutral', noShow: 'danger' };
const TICKET_TONE: Record<TicketStatus, Tone> = { open: 'warning', inProgress: 'info', waitingOnRequester: 'neutral', resolved: 'success', closed: 'neutral' };

export function BookingStatusBadge({ status }: { status: BookingStatus }) {
  const labels = useConsultationLabels();
  return <Badge tone={BOOKING_TONE[status] ?? 'neutral'} data-status={status}>{labels.status(status)}</Badge>;
}

export function TicketStatusBadge({ status }: { status: TicketStatus }) {
  const labels = useConsultationLabels();
  return <Badge tone={TICKET_TONE[status] ?? 'neutral'} data-status={status}>{labels.ticketStatus(status)}</Badge>;
}

/** "Sat 10:00–10:30" in the user's zone and calendar. */
export function useSessionTime() {
  const format = useLocaleFormat();
  return useMemo(
    () => ({
      time: (instant: string) => format.dateTime(instant, { hour: '2-digit', minute: '2-digit' }),
      range: (start: string, end: string) =>
        `${format.dateTime(start, { hour: '2-digit', minute: '2-digit' })}–${format.dateTime(end, { hour: '2-digit', minute: '2-digit' })}`,
      day: (instant: string) => format.dateTime(instant, { weekday: 'long', day: 'numeric', month: 'long' }),
      full: (instant: string) => format.dateTime(instant, { weekday: 'long', day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit' }),
    }),
    [format],
  );
}

/* ---------------- week view ---------------- */

export interface WeekState {
  /** Saturday of the shown week (yyyy-MM-dd in the user's zone). */
  start: string;
  days: string[];
  /** UTC bounds padded by a day on each side; rows are bucketed by their local day. */
  from: string;
  to: string;
  prev: () => void;
  next: () => void;
  today: () => void;
}

/** Saturday-first week navigation in the user's time zone. */
export function useWeek(): WeekState {
  const format = useLocaleFormat();
  const [start, setStart] = useState(() => weekStartIso(todayIso(format.timeZone)));
  const days = weekDays(start);
  return {
    start,
    days,
    from: new Date(Date.parse(`${start}T00:00:00Z`) - 86_400_000).toISOString(),
    to: new Date(Date.parse(`${days[6]}T00:00:00Z`) + 2 * 86_400_000).toISOString(),
    prev: () => setStart((s) => addDaysIso(s, -7) ?? s),
    next: () => setStart((s) => addDaysIso(s, 7) ?? s),
    today: () => setStart(weekStartIso(todayIso(format.timeZone))),
  };
}

export function WeekToolbar({ week, actions }: { week: WeekState; actions?: ReactNode }) {
  const t = useTranslations('consultation.week');
  const format = useLocaleFormat();
  const label = t('range', { start: format.calendarDate(week.days[0]), end: format.calendarDate(week.days[6]) });
  return (
    <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap', marginBlockEnd: 12 }}>
      <Button variant="secondary" size="sm" onClick={week.prev} aria-label={t('prev')} data-testid="week-prev">
        <IconChevron size={16} towards="start" />
      </Button>
      <Button variant="secondary" size="sm" onClick={week.today}>{t('thisWeek')}</Button>
      <Button variant="secondary" size="sm" onClick={week.next} aria-label={t('next')} data-testid="week-next">
        <IconChevron size={16} towards="end" />
      </Button>
      <strong aria-live="polite" data-testid="week-range">{label}</strong>
      <span style={{ marginInlineStart: 'auto', display: 'flex', gap: 8, flexWrap: 'wrap' }}>{actions}</span>
    </div>
  );
}

/**
 * Calendar-style week: one column per day (Saturday first), each a headed list. Collapses to a single column on narrow screens.
 * Items are bucketed by their start instant's local day.
 */
export function WeekView<T>({ week, items, startOf, render, getKey, emptyLabel, testId }: {
  week: WeekState;
  items: T[];
  startOf: (item: T) => string;
  render: (item: T) => ReactNode;
  getKey: (item: T) => string;
  emptyLabel: string;
  testId?: string;
}) {
  const format = useLocaleFormat();
  const today = todayIso(format.timeZone);
  const byDay = new Map<string, T[]>();
  for (const item of [...items].sort((a, b) => startOf(a).localeCompare(startOf(b)))) {
    const key = instantDayIso(startOf(item), format.timeZone);
    byDay.set(key, [...(byDay.get(key) ?? []), item]);
  }
  return (
    <div className="eemp-week" data-testid={testId} style={{ display: 'grid', gap: 8, gridTemplateColumns: 'repeat(auto-fit, minmax(9.5rem, 1fr))' }}>
      {week.days.map((day) => {
        const list = byDay.get(day) ?? [];
        const headingId = `day-${day}`;
        return (
          <Card key={day} aria-labelledby={headingId} data-day={day} style={{ padding: 10, minBlockSize: '8rem', outline: day === today ? '2px solid var(--eemp-color-primary)' : undefined }}>
            <h3 id={headingId} style={{ fontSize: '0.875rem', margin: '0 0 8px' }}>
              {format.dateTime(`${day}T12:00:00Z`, { weekday: 'long', timeZone: 'UTC' })}
              <span style={{ display: 'block', fontWeight: 400, color: 'var(--eemp-color-fg-muted)' }}>{format.calendarDate(day)}</span>
            </h3>
            {list.length === 0 ? (
              <p style={{ margin: 0, fontSize: '0.8125rem', color: 'var(--eemp-color-fg-muted)' }}>{emptyLabel}</p>
            ) : (
              <ul style={{ listStyle: 'none', margin: 0, padding: 0, display: 'grid', gap: 6 }}>
                {list.map((item) => <li key={getKey(item)}>{render(item)}</li>)}
              </ul>
            )}
          </Card>
        );
      })}
    </div>
  );
}

/* ---------------- ticket thread ---------------- */

/** Conversation view shared by the portal and the staff inbox. Internal notes are marked (staff only ever receive them). */
export function TicketThread({ detail }: { detail: TicketDetail }) {
  const t = useTranslations('support.thread');
  const format = useLocaleFormat();
  if (detail.messages.length === 0) return <StatePanel title={t('empty')} />;
  return (
    <ol aria-label={t('title')} data-testid="ticket-thread" style={{ listStyle: 'none', margin: 0, padding: 0, display: 'grid', gap: 10 }}>
      {detail.messages.map((m) => {
        const author = m.mine ? t('you') : m.fromStaff ? (m.authorName || t('team')) : m.authorName || t('requester');
        return (
          <li key={m.id} data-internal={m.isInternal || undefined}
            style={{ justifySelf: m.mine ? 'end' : 'start', maxInlineSize: 'min(40rem, 92%)' }}>
            <Card style={{
              padding: 12,
              background: m.isInternal ? 'var(--eemp-color-warning-soft)' : m.mine ? 'var(--eemp-color-primary-soft)' : undefined,
            }}>
              <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap', fontSize: '0.8125rem', marginBlockEnd: 6 }}>
                <strong>{author}</strong>
                {m.isInternal && <Badge tone="warning">{t('internal')}</Badge>}
                <span style={{ color: 'var(--eemp-color-fg-muted)' }}>{format.dateTime(m.createdAt)}</span>
              </div>
              <p style={{ margin: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{m.body}</p>
            </Card>
          </li>
        );
      })}
    </ol>
  );
}
