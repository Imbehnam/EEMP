'use client';

import type { GradeTotal, ScheduleSlot } from '@eemp/api-client';
import { weekdayOrder } from '@eemp/i18n';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Badge, type Tone } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useMemo } from 'react';

/** Translated labels for academic enums (ICU selects with an `other` fallback for unknown values). */
export function useAcademicLabels() {
  const t = useTranslations('academic.enums');
  const format = useLocaleFormat();
  return useMemo(() => {
    const number = (value: number | null | undefined, places = 2) =>
      value === null || value === undefined ? '' : format.number(value, { maximumFractionDigits: places });
    return {
      weekday: (value: string) => t('weekday', { value }),
      attendance: (value: string) => t('attendance', { value }),
      assessmentType: (value: string) => t('assessmentType', { value }),
      assessmentStatus: (value: string) => t('assessmentStatus', { value }),
      mark: (value: string) => t('mark', { value }),
      classStatus: (value: string) => t('classStatus', { value }),
      yearStatus: (value: string) => t('yearStatus', { value }),
      teacherRole: (value: string) => t('teacherRole', { value }),
      schemeKind: (value: string) => t('schemeKind', { value }),
      band: (value: string) => t('band', { value }),
      reportCardStatus: (value: string) => t('reportCardStatus', { value }),
      historyAction: (value: string) => t('historyAction', { value }),
      enrollmentStatus: (value: string) => t('enrollmentStatus', { value }),
      number,
      /** HH:mm wall-clock time in the locale's digits (times are not instants: no zone conversion). */
      time: (hhmm: string) => {
        const m = /^(\d{2}):(\d{2})/.exec(hhmm);
        return m ? `${format.number(Number(m[1]), { minimumIntegerDigits: 2 })}:${format.number(Number(m[2]), { minimumIntegerDigits: 2 })}` : hhmm;
      },
      total: (total: GradeTotal | null | undefined, places = 2) =>
        total ? `${number(total.value, places)}${total.label ? ` · ${t('band', { value: total.label })}` : ''}` : '',
    };
  }, [t, format]);
}

export const personName = (p: { firstName: string; lastName: string }) => `${p.firstName} ${p.lastName}`.trim();

const STATUS_TONE: Record<string, Tone> = { draft: 'neutral', submitted: 'info', approved: 'warning', published: 'success' };

export function AssessmentStatusBadge({ status }: { status: string }) {
  const labels = useAcademicLabels();
  return <Badge tone={STATUS_TONE[status] ?? 'neutral'} data-status={status}>{labels.assessmentStatus(status)}</Badge>;
}

export function PassBadge({ total }: { total: GradeTotal | null | undefined }) {
  const t = useTranslations('academic.common');
  if (!total) return null;
  return <Badge tone={total.passed ? 'success' : 'danger'}>{total.passed ? t('passed') : t('failed')}</Badge>;
}

/** Slots sorted Saturday-first, e.g. "Saturday 08:00–09:30 (101)". */
export function ScheduleText({ slots }: { slots: ScheduleSlot[] }) {
  const t = useTranslations('academic.common');
  const labels = useAcademicLabels();
  if (slots.length === 0) return <>{t('noSchedule')}</>;
  return (
    <ul style={{ margin: 0, paddingInlineStart: 18 }}>
      {[...slots].sort((a, b) => weekdayOrder(a.weekday) - weekdayOrder(b.weekday) || a.startTime.localeCompare(b.startTime)).map((s) => (
        <li key={s.id}>
          {t('slot', { weekday: labels.weekday(s.weekday), start: labels.time(s.startTime), end: labels.time(s.endTime) })}
          {s.room ? ` · ${t('roomShort', { room: s.room })}` : ''}
        </li>
      ))}
    </ul>
  );
}
