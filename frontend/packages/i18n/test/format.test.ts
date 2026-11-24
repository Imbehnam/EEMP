import { describe, expect, it } from 'vitest';

import {
  calendarOf, formatCalendarDate, formatDateTime, formatNumber, getDirection, toSupportedLocale, translateErrorCode,
  type ErrorTranslator,
} from '../src';

describe('Persian (Jalali) calendar formatting', () => {
  it('formats calendar dates in the Solar Hijri calendar with Persian digits for fa', () => {
    const text = formatCalendarDate('2012-04-03', 'fa', 'long');
    expect(text).toContain('۱۳۹۱');
    expect(text).toContain('فروردین');
    expect(text).toContain('۱۵');
  });

  it('keeps calendar dates stable regardless of time zone (no day shift)', () => {
    expect(formatCalendarDate('2024-03-20', 'fa', 'long')).toContain('۱۴۰۳');
    expect(formatCalendarDate('2024-03-20', 'fa', 'long')).toContain('فروردین');
    expect(formatCalendarDate('2024-03-19', 'fa', 'long')).toContain('اسفند');
  });

  it('uses the Gregorian calendar for en, tr and ar', () => {
    expect(formatCalendarDate('2012-04-03', 'en')).toBe('Apr 3, 2012');
    expect(formatCalendarDate('2012-04-03', 'tr', 'long')).toContain('2012');
    expect(formatCalendarDate('2012-04-03', 'ar', 'long')).toContain('٢٠١٢');
    expect(calendarOf('ar')).toBe('gregory');
    expect(calendarOf('fa')).toBe('persian');
  });

  it('returns empty text for invalid or missing dates', () => {
    expect(formatCalendarDate('2012/04/03', 'fa')).toBe('');
    expect(formatCalendarDate(null, 'fa')).toBe('');
  });
});

describe('instant formatting in the user time zone', () => {
  const instant = '2026-03-20T20:45:00Z'; // 00:15 on 21 March in Tehran (UTC+03:30) = 1 Farvardin 1405
  it('converts UTC instants into the user zone and calendar', () => {
    const fa = formatDateTime(instant, { locale: 'fa', timeZone: 'Asia/Tehran' }, { dateStyle: 'long', timeStyle: 'short' });
    expect(fa).toContain('۱۴۰۵');
    expect(fa).toContain('فروردین');
    expect(fa).toContain('۰:۱۵');
    const en = formatDateTime(instant, { locale: 'en', timeZone: 'UTC' }, { dateStyle: 'medium', timeStyle: 'short', hourCycle: 'h23' });
    expect(en).toBe('Mar 20, 2026, 20:45');
  });

  it('formats numbers with the locale numbering system', () => {
    expect(formatNumber(1234, 'fa')).toBe('۱٬۲۳۴');
    expect(formatNumber(1234, 'en')).toBe('1,234');
  });
});

describe('locale helpers', () => {
  it('maps backend locales and directions', () => {
    expect(toSupportedLocale('fa-IR')).toBe('fa');
    expect(toSupportedLocale('de-DE', 'en')).toBe('en');
    expect(getDirection('ar')).toBe('rtl');
    expect(getDirection('tr')).toBe('ltr');
  });
});

describe('error code translation', () => {
  const messages: Record<string, string> = { 'auth.invalid_credentials': 'Wrong', unknown: 'Unknown ({code})' };
  const t = Object.assign((key: string, values?: Record<string, unknown>) => messages[key]!.replace('{code}', String(values?.code ?? '')), {
    has: (key: string) => key in messages,
  }) as ErrorTranslator;

  it('translates known codes and never shows server text for unknown ones', () => {
    expect(translateErrorCode(t, 'auth.invalid_credentials')).toBe('Wrong');
    expect(translateErrorCode(t, 'some.new_code')).toBe('Unknown (some.new_code)');
  });
});
