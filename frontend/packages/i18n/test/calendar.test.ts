import { describe, expect, it } from 'vitest';

import {
  addDaysIso, calendarToIso, formatDateInput, fromJalali, isJalaliLeapYear, isoToCalendar, isoWeekdayIndex, monthGrid,
  normalizeDigits, parseDateInput, todayIso, toJalali, weekdayOfIso, weekdayOrder,
} from '../src';

/** Reference: Intl's persian calendar (ICU). */
function intlJalali(iso: string) {
  const [y, m, d] = iso.split('-').map(Number) as [number, number, number];
  const parts = new Intl.DateTimeFormat('en-u-ca-persian-nu-latn', { timeZone: 'UTC', year: 'numeric', month: 'numeric', day: 'numeric' })
    .formatToParts(new Date(Date.UTC(y, m - 1, d, 12)));
  const get = (t: string) => Number(parts.find((p) => p.type === t)!.value.replace(/\D/g, ''));
  return { year: get('year'), month: get('month'), day: get('day') };
}

describe('Jalali ⇄ Gregorian conversion', () => {
  it('matches known dates from the contract and Nowruz', () => {
    expect(toJalali({ year: 2026, month: 11, day: 24 })).toEqual({ year: 1405, month: 9, day: 3 });
    expect(fromJalali({ year: 1405, month: 9, day: 3 })).toEqual({ year: 2026, month: 11, day: 24 });
    expect(toJalali({ year: 2024, month: 3, day: 20 })).toEqual({ year: 1403, month: 1, day: 1 });
    expect(toJalali({ year: 2026, month: 9, day: 23 })).toEqual({ year: 1405, month: 7, day: 1 });
    expect(isJalaliLeapYear(1403)).toBe(true);
    expect(isJalaliLeapYear(1404)).toBe(false);
  });

  it('agrees with Intl (persian calendar) for every day from 2000 to 2040 and round-trips', () => {
    let iso = '2000-01-01';
    while (iso < '2040-12-31') {
      const j = isoToCalendar(iso, 'persian')!;
      expect(j, iso).toEqual(intlJalali(iso));
      expect(calendarToIso(j, 'persian'), iso).toBe(iso);
      iso = addDaysIso(iso, 1)!;
    }
  });
});

describe('date input parsing and formatting', () => {
  it('fa uses Jalali with Persian digits; other locales use ISO Gregorian', () => {
    expect(formatDateInput('2026-11-24', 'fa')).toBe('۱۴۰۵/۰۹/۰۳');
    expect(formatDateInput('2026-11-24', 'en')).toBe('2026-11-24');
    expect(formatDateInput('2026-11-24', 'ar')).toBe('2026-11-24');
    expect(parseDateInput('۱۴۰۵/۰۹/۰۳', 'fa')).toBe('2026-11-24');
    expect(parseDateInput('1405-9-3', 'fa')).toBe('2026-11-24');
    expect(parseDateInput('2026-11-24', 'tr')).toBe('2026-11-24');
    expect(parseDateInput('٢٠٢٦/١١/٢٤', 'ar')).toBe('2026-11-24');
  });

  it('rejects dates that do not exist in the locale calendar', () => {
    expect(parseDateInput('1404/12/30', 'fa')).toBeNull(); // 1404 is not leap
    expect(parseDateInput('1403/12/30', 'fa')).toBe('2025-03-20');
    expect(parseDateInput('2026-02-29', 'en')).toBeNull();
    expect(parseDateInput('1405/07/31', 'fa')).toBeNull();
    expect(parseDateInput('abc', 'en')).toBeNull();
    expect(normalizeDigits('۱۲٣')).toBe('123');
  });
});

describe('weeks start on Saturday', () => {
  it('computes weekdays', () => {
    expect(weekdayOfIso('2026-11-21')).toBe('saturday');
    expect(weekdayOfIso('2026-11-23')).toBe('monday');
    expect(isoWeekdayIndex('2026-11-22')).toBe(0);
    expect(['monday', 'saturday', 'friday', 'sunday'].sort((a, b) => weekdayOrder(a) - weekdayOrder(b))).toEqual(['saturday', 'sunday', 'monday', 'friday']);
  });

  it('builds a Saturday-first Jalali month grid', () => {
    const weeks = monthGrid('persian', 1405, 9);
    for (const week of weeks) {
      expect(week).toHaveLength(7);
      expect(weekdayOfIso(week[0]!.iso)).toBe('saturday');
    }
    const inMonth = weeks.flat().filter((d) => d.inMonth);
    expect(inMonth).toHaveLength(30);
    expect(inMonth[0]!.iso).toBe(calendarToIso({ year: 1405, month: 9, day: 1 }, 'persian'));
    expect(monthGrid('gregory', 2026, 2).flat().filter((d) => d.inMonth)).toHaveLength(28);
  });

  it('reads today in a time zone', () => {
    expect(todayIso('Asia/Tehran', new Date('2026-11-24T21:00:00Z'))).toBe('2026-11-25');
    expect(todayIso('UTC', new Date('2026-11-24T21:00:00Z'))).toBe('2026-11-24');
  });
});
