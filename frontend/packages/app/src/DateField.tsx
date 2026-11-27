'use client';

import {
  addDaysIso, formatDateInput, getMetadata, inputCalendarOf, isoToCalendar, monthGrid, parseDateInput, todayIso, type DateCalendar,
} from '@eemp/i18n';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Button, DirectionalGlyph, TextField } from '@eemp/ui';
import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useId, useMemo, useState } from 'react';

export interface DateFieldProps {
  label: string;
  /** ISO Gregorian `yyyy-MM-dd`, or '' when empty. */
  value: string;
  onChange: (iso: string) => void;
  name?: string;
  error?: string;
  hint?: string;
  required?: boolean;
  disabled?: boolean;
  id?: string;
}

/**
 * Calendar-date input (OD-020). The API value is always ISO Gregorian; the user types and reads the date in the
 * locale calendar: Jalali `1405/09/03` for fa (Persian or Latin digits accepted), ISO `2026-11-24` otherwise.
 * A month picker (weeks start on Saturday) offers the same dates.
 */
export function DateField({ label, value, onChange, name, error, hint, required, disabled, id }: DateFieldProps) {
  const t = useTranslations('dateField');
  const locale = useLocale();
  const format = useLocaleFormat();
  const calendar = inputCalendarOf(locale);
  const [text, setText] = useState(() => formatDateInput(value, locale));
  const [invalid, setInvalid] = useState(false);
  const [open, setOpen] = useState(false);

  // Keep the text in sync when the value changes from outside (e.g. picker, reset, reload).
  useEffect(() => {
    if (parseDateInput(text, locale) !== (value || null)) {
      setText(formatDateInput(value, locale));
      setInvalid(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value, locale]);

  const commit = (raw: string) => {
    setText(raw);
    if (raw.trim() === '') {
      setInvalid(false);
      onChange('');
      return;
    }
    const iso = parseDateInput(raw, locale);
    setInvalid(!iso);
    if (iso) onChange(iso);
  };

  const example = calendar === 'persian' ? formatDateInput('2026-11-24', locale) : '2026-11-24';
  const preview = value && !invalid ? format.calendarDate(value) : '';
  const hintText = [hint, preview || t('format', { example })].filter(Boolean).join(' · ');

  return (
    <div>
      <TextField
        id={id}
        name={name}
        label={label}
        inputMode="numeric"
        dir="ltr"
        autoComplete="off"
        required={required}
        disabled={disabled}
        value={text}
        placeholder={example}
        hint={hintText}
        error={invalid ? t('invalid') : error}
        data-iso={value || undefined}
        onChange={(e) => commit(e.target.value)}
      />
      {!disabled && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          aria-expanded={open}
          onClick={() => setOpen((o) => !o)}
          style={{ marginBlockStart: 4 }}
        >
          {open ? t('closeCalendar') : t('openCalendar')}
        </Button>
      )}
      {open && (
        <MonthPicker
          calendar={calendar}
          value={value}
          onPick={(iso) => {
            setText(formatDateInput(iso, locale));
            setInvalid(false);
            onChange(iso);
            setOpen(false);
          }}
        />
      )}
    </div>
  );
}

function MonthPicker({ calendar, value, onPick }: { calendar: DateCalendar; value: string; onPick: (iso: string) => void }) {
  const t = useTranslations('dateField');
  const locale = useLocale();
  const format = useLocaleFormat();
  const headingId = useId();
  const today = todayIso(format.timeZone);
  const start = isoToCalendar(value || today, calendar)!;
  const [view, setView] = useState({ year: start.year, month: start.month });
  const weeks = useMemo(() => monthGrid(calendar, view.year, view.month), [calendar, view]);
  const intlLocale = getMetadata(locale).intlLocale;
  const firstIso = weeks.flat().find((d) => d.inMonth)?.iso ?? today;
  const noon = (iso: string) => new Date(`${iso}T12:00:00Z`);
  const title = new Intl.DateTimeFormat(intlLocale, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(noon(firstIso));
  const weekdayShort = new Intl.DateTimeFormat(intlLocale, { weekday: 'short', timeZone: 'UTC' });
  const weekdayLong = new Intl.DateTimeFormat(intlLocale, { weekday: 'long', timeZone: 'UTC' });
  const step = (delta: number) =>
    setView((v) => {
      const m = v.month + delta;
      return m < 1 ? { year: v.year - 1, month: 12 } : m > 12 ? { year: v.year + 1, month: 1 } : { year: v.year, month: m };
    });

  return (
    <div
      role="group"
      aria-labelledby={headingId}
      style={{ marginBlockStart: 8, padding: 8, border: '1px solid var(--eemp-color-border)', borderRadius: 8, maxInlineSize: '22rem' }}
    >
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <Button type="button" variant="ghost" size="sm" onClick={() => step(-1)} aria-label={t('previousMonth')}>
          <DirectionalGlyph towards="start" kind="chevron" />
        </Button>
        <strong id={headingId} aria-live="polite">{title}</strong>
        <Button type="button" variant="ghost" size="sm" onClick={() => step(1)} aria-label={t('nextMonth')}>
          <DirectionalGlyph towards="end" kind="chevron" />
        </Button>
      </div>
      <table style={{ inlineSize: '100%', borderCollapse: 'collapse', textAlign: 'center' }}>
        <thead>
          <tr>
            {weeks[0]?.map((d) => (
              <th key={d.iso} scope="col" abbr={weekdayLong.format(noon(d.iso))} style={{ fontSize: '0.75rem', fontWeight: 600, padding: 2 }}>
                {weekdayShort.format(noon(d.iso))}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {weeks.map((week) => (
            <tr key={week[0]!.iso}>
              {week.map((d) => (
                <td key={d.iso} style={{ padding: 1 }}>
                  <button
                    type="button"
                    onClick={() => onPick(d.iso)}
                    aria-label={format.calendarDate(d.iso)}
                    aria-pressed={d.iso === value}
                    aria-current={d.iso === today ? 'date' : undefined}
                    data-iso={d.iso}
                    style={{
                      inlineSize: '2.25rem', blockSize: '2.25rem', borderRadius: 6, cursor: 'pointer', font: 'inherit',
                      border: d.iso === today ? '1px solid var(--eemp-color-primary)' : '1px solid transparent',
                      background: d.iso === value ? 'var(--eemp-color-primary)' : 'transparent',
                      color: d.iso === value ? 'var(--eemp-color-primary-fg)' : d.inMonth ? 'inherit' : 'var(--eemp-color-fg-muted)',
                    }}
                  >
                    {format.number(d.day)}
                  </button>
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      <Button type="button" variant="secondary" size="sm" onClick={() => onPick(today)}>{t('today')}</Button>
    </div>
  );
}

/** Today in the user's zone (ISO). */
export function useTodayIso() {
  const format = useLocaleFormat();
  return todayIso(format.timeZone);
}

export { addDaysIso };
