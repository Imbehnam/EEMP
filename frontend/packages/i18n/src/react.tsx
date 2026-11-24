'use client';

import { useLocale, useTimeZone, useTranslations } from 'next-intl';
import { useMemo } from 'react';

import { translateError, translateFieldErrors, type ErrorLike } from './errors';
import { formatCalendarDate, formatDateTime, formatInstantDate, formatNumber } from './format';
import { getMetadata } from './locales';

/** Locale-aware formatting bound to the active locale and the user's time zone. */
export function useLocaleFormat() {
  const locale = useLocale();
  const timeZone = useTimeZone() ?? 'UTC';
  return useMemo(
    () => ({
      locale,
      timeZone,
      metadata: getMetadata(locale),
      dateTime: (value: string | number | Date, options?: Intl.DateTimeFormatOptions) =>
        formatDateTime(value, { locale, timeZone }, options),
      instantDate: (value: string | number | Date) => formatInstantDate(value, { locale, timeZone }),
      calendarDate: (value: string | null | undefined) => formatCalendarDate(value, locale),
      number: (value: number, options?: Intl.NumberFormatOptions) => formatNumber(value, locale, options),
    }),
    [locale, timeZone],
  );
}

/** Translates API error codes (problem+json `code`, field error codes). */
export function useErrorTranslator() {
  const t = useTranslations('errors');
  return useMemo(
    () => ({
      message: (error: ErrorLike) => translateError(t as never, error),
      fields: (fieldErrors: Record<string, string[]>) => translateFieldErrors(t as never, fieldErrors),
      code: (code: string) => translateError(t as never, { code }),
    }),
    [t],
  );
}
