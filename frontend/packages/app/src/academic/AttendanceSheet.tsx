'use client';

import { ATTENDANCE_STATUSES, type AttendanceSheet, type AttendanceStatus, type ScheduleSlot } from '@eemp/api-client';
import { weekdayOfIso as weekdayOf } from '@eemp/i18n';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Alert, Badge, Button, Card, FormGrid, SelectField, Spinner } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useEffect, useMemo, useState } from 'react';

import { DateField, useTodayIso } from '../DateField';
import { ErrorAlert } from '../ErrorAlert';
import { useApiMutation, useApiQuery } from '../use-api';
import { personName, useAcademicLabels } from './labels';

type Marks = Record<string, { status: AttendanceStatus | ''; note: string }>;

const toMarks = (sheet: AttendanceSheet): Marks =>
  Object.fromEntries(sheet.entries.map((e) => [e.studentId, { status: (e.status as AttendanceStatus | null) ?? '', note: e.note ?? '' }]));

export interface AttendanceSheetPanelProps {
  classId: string;
  schedule: ScheduleSlot[];
  /** UX hint only; the API decides (attendance.manage in the class branch or the assigned teacher). */
  canMark: boolean;
  termStart?: string;
  termEnd?: string;
}

/**
 * Attendance sheet for one class, date and (optional) period: shows the roster with current marks, lets the
 * user mark each student or everyone at once, and saves with the idempotent bulk upsert (PUT .../attendance).
 */
export function AttendanceSheetPanel({ classId, schedule, canMark, termStart, termEnd }: AttendanceSheetPanelProps) {
  const t = useTranslations('academic.attendance');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const format = useLocaleFormat();
  const today = useTodayIso();
  const initialDate = termEnd && today > termEnd ? termEnd : termStart && today < termStart ? termStart : today;
  const [date, setDate] = useState(initialDate);
  const daySlots = useMemo(() => schedule.filter((s) => date && s.weekday === weekdayOf(date)), [schedule, date]);
  const [slotId, setSlotId] = useState('');
  useEffect(() => setSlotId(daySlots[0]?.id ?? ''), [daySlots]);

  const query = useApiQuery(`attendance|${classId}|${date}|${slotId}`, (api) =>
    date ? api.academic.classes.attendance(classId, date, slotId || null) : Promise.resolve(null),
  );
  const sheet = query.data ?? null;
  const [marks, setMarks] = useState<Marks>({});
  const [saved, setSaved] = useState(false);
  useEffect(() => {
    if (sheet) setMarks(toMarks(sheet));
    setSaved(false);
  }, [sheet]);

  const mutation = useApiMutation((api, s: AttendanceSheet, m: Marks) =>
    api.academic.classes.markAttendance(classId, {
      date: s.date,
      scheduleSlotId: s.scheduleSlotId,
      rowVersion: s.rowVersion,
      records: Object.entries(m)
        .filter(([, v]) => v.status)
        .map(([studentId, v]) => ({ studentId, status: v.status as AttendanceStatus, note: v.note.trim() || null })),
    }),
  );

  const setAll = (status: AttendanceStatus) =>
    setMarks((m) => Object.fromEntries(Object.entries(m).map(([k, v]) => [k, { ...v, status }])));
  const unmarked = Object.values(marks).filter((v) => !v.status).length;

  return (
    <Card aria-labelledby="attendance-heading">
      <h2 id="attendance-heading" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('title')}</h2>
      <FormGrid>
        <DateField label={t('date')} name="attendanceDate" value={date} onChange={setDate} required />
        <SelectField
          label={t('period')}
          name="scheduleSlotId"
          value={slotId}
          onChange={(e) => setSlotId(e.target.value)}
          hint={daySlots.length === 0 ? t('noPeriodsThatDay') : undefined}
          options={[
            { value: '', label: t('wholeDay') },
            ...daySlots.map((s) => ({ value: s.id, label: `${labels.time(s.startTime)}–${labels.time(s.endTime)}${s.room ? ` · ${s.room}` : ''}` })),
          ]}
        />
      </FormGrid>
      <div style={{ marginBlock: 12 }}>
        <ErrorAlert error={query.error} />
        <ErrorAlert error={mutation.error} onReload={() => { mutation.reset(); query.reload(); }} />
        {saved && <Alert tone="success" role="status">{t('saved')}</Alert>}
      </div>
      {query.loading && !sheet ? (
        <Spinner label={common('loading')} />
      ) : !sheet ? null : sheet.entries.length === 0 ? (
        <p>{t('noStudents')}</p>
      ) : (
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            setSaved(false);
            const result = await mutation.run(sheet, marks);
            if (result.ok) {
              query.setData(result.value);
              setSaved(true);
            }
          }}
        >
          <p style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }} data-testid="attendance-summary">
            {ATTENDANCE_STATUSES.map((s) => (
              <Badge key={s} tone={s === 'present' ? 'success' : s === 'absent' ? 'danger' : s === 'late' ? 'warning' : 'info'}>
                {labels.attendance(s)}: {format.number(sheet.summary[s])}
              </Badge>
            ))}
            <Badge tone="neutral">{t('unmarked')}: {format.number(sheet.summary.unmarked)}</Badge>
            {sheet.updatedAt && <span style={{ fontSize: '0.875rem' }}>{t('lastSaved', { when: format.dateTime(sheet.updatedAt) })}</span>}
          </p>
          {canMark && (
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBlockEnd: 12 }}>
              <Button type="button" variant="secondary" size="sm" onClick={() => setAll('present')}>{t('markAllPresent')}</Button>
              <Button type="button" variant="secondary" size="sm" onClick={() => setAll('absent')}>{t('markAllAbsent')}</Button>
            </div>
          )}
          <div style={{ overflowX: 'auto' }}>
            <table className="eemp-matrix" style={{ inlineSize: '100%', borderCollapse: 'collapse' }}>
              <caption className="eemp-visually-hidden">{t('caption', { date: format.calendarDate(sheet.date) })}</caption>
              <thead>
                <tr>
                  <th scope="col" style={{ textAlign: 'start' }}>{t('student')}</th>
                  <th scope="col" style={{ textAlign: 'start' }}>{t('status')}</th>
                  <th scope="col" style={{ textAlign: 'start' }}>{t('note')}</th>
                </tr>
              </thead>
              <tbody>
                {sheet.entries.map((entry) => {
                  const name = personName(entry);
                  const m = marks[entry.studentId] ?? { status: '', note: '' };
                  return (
                    <tr key={entry.studentId} data-student={entry.studentNumber}>
                      <th scope="row" style={{ textAlign: 'start', fontWeight: 500 }}>
                        {name} <span className="eemp-ltr" style={{ color: 'var(--eemp-color-fg-muted)' }}>{entry.studentNumber}</span>
                      </th>
                      <td>
                        <fieldset disabled={!canMark} style={{ border: 0, padding: 0, margin: 0, display: 'flex', gap: 10, flexWrap: 'wrap' }}>
                          <legend className="eemp-visually-hidden">{t('statusFor', { name })}</legend>
                          {ATTENDANCE_STATUSES.map((s) => (
                            <label key={s} style={{ display: 'inline-flex', gap: 4, alignItems: 'center' }}>
                              <input
                                type="radio"
                                name={`att-${entry.studentId}`}
                                value={s}
                                checked={m.status === s}
                                onChange={() => setMarks((all) => ({ ...all, [entry.studentId]: { ...m, status: s } }))}
                              />
                              {labels.attendance(s)}
                            </label>
                          ))}
                        </fieldset>
                      </td>
                      <td>
                        <input
                          aria-label={t('noteFor', { name })}
                          value={m.note}
                          maxLength={500}
                          disabled={!canMark}
                          onChange={(e) => setMarks((all) => ({ ...all, [entry.studentId]: { ...m, note: e.target.value } }))}
                          style={{ inlineSize: '100%', minInlineSize: '8rem', padding: '4px 6px', border: '1px solid var(--eemp-color-border)', borderRadius: 6, font: 'inherit', background: 'var(--eemp-color-surface)', color: 'inherit' }}
                        />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          {canMark && (
            <div style={{ display: 'flex', gap: 12, alignItems: 'center', marginBlockStart: 12, flexWrap: 'wrap' }}>
              <Button type="submit" loading={mutation.busy}>{mutation.busy ? common('saving') : t('save')}</Button>
              {unmarked > 0 && <span style={{ fontSize: '0.875rem' }}>{t('unmarkedHint', { count: unmarked })}</span>}
            </div>
          )}
        </form>
      )}
    </Card>
  );
}
