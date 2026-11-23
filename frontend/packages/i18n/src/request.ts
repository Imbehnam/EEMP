import { hasLocale } from 'next-intl';
import { getRequestConfig } from 'next-intl/server';

import { defaultTimeZone } from './locales';
import { routing } from './routing';

export const loadMessages = async (locale: string) => {
  switch (locale) {
    case 'en':
      return (await import('../messages/en.json')).default;
    case 'tr':
      return (await import('../messages/tr.json')).default;
    case 'ar':
      return (await import('../messages/ar.json')).default;
    default:
      return (await import('../messages/fa.json')).default;
  }
};

/**
 * Shared next-intl request config. Apps re-export it from src/i18n/request.ts.
 * The time zone comes from the user's preference cookie (set after login / preference change),
 * otherwise the tenant default (Asia/Tehran).
 */
export const requestConfig = getRequestConfig(async ({ requestLocale }) => {
  const requested = await requestLocale;
  const locale = hasLocale(routing.locales, requested) ? requested : routing.defaultLocale;
  let timeZone = defaultTimeZone;
  try {
    const { cookies } = await import('next/headers');
    const tz = (await cookies()).get('eemp_tz')?.value;
    if (tz) {
      new Intl.DateTimeFormat('en', { timeZone: tz });
      timeZone = tz;
    }
  } catch {
    // outside a request scope or invalid zone: keep default
  }
  return {
    locale,
    timeZone,
    messages: await loadMessages(locale),
    onError(error) {
      if (process.env.NODE_ENV !== 'production') console.error(error.message);
    },
    getMessageFallback({ namespace, key }) {
      return [namespace, key].filter(Boolean).join('.');
    },
  };
});
