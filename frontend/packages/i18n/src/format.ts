import { getMetadata } from './locales';

export interface FormatContext {
  locale: string;
  timeZone: string;
}

/**
 * Instants (UTC from the API) are displayed in the user's IANA time zone using the locale's
 * calendar: Jalali (Solar Hijri) for fa, Gregorian for en/tr/ar.
 */
export function formatDateTime(
  value: string | number | Date,
  ctx: FormatContext,
  options: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' },
): string {
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat(getMetadata(ctx.locale).intlLocale, { timeZone: ctx.timeZone, ...options }).format(date);
}

export function formatInstantDate(value: string | number | Date, ctx: FormatContext): string {
  return formatDateTime(value, ctx, { dateStyle: 'medium' });
}

/**
 * Calendar dates (yyyy-mm-dd, e.g. birthDate) have no time zone. Format at UTC noon in UTC so the
 * day never shifts, while still converting to the locale calendar (e.g. 2012-04-03 → ۱۵ فروردین ۱۳۹۱).
 */
export function formatCalendarDate(isoDate: string | null | undefined, locale: string, style: 'medium' | 'long' | 'short' = 'medium'): string {
  if (!isoDate) return '';
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(isoDate);
  if (!match) return '';
  const date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]), 12));
  return new Intl.DateTimeFormat(getMetadata(locale).intlLocale, { dateStyle: style, timeZone: 'UTC' }).format(date);
}

export function formatNumber(value: number, locale: string, options?: Intl.NumberFormatOptions): string {
  return new Intl.NumberFormat(getMetadata(locale).intlLocale, options).format(value);
}

/** Money uses decimal strings from the API; never parse money into floats for arithmetic. */
export function formatMoney(amount: string, currency: string, locale: string): string {
  const numeric = Number(amount);
  if (!Number.isFinite(numeric)) return amount;
  return new Intl.NumberFormat(getMetadata(locale).intlLocale, {
    style: 'currency',
    currency,
    currencyDisplay: 'name',
    maximumFractionDigits: currency === 'IRR' ? 0 : 2,
  }).format(numeric);
}

export function calendarOf(locale: string) {
  return getMetadata(locale).calendar;
}
