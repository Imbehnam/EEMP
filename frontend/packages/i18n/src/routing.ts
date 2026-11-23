import { defineRouting } from 'next-intl/routing';

import { defaultLocale, locales } from './locales';

export const routing = defineRouting({
  locales,
  defaultLocale,
  localePrefix: 'always',
  localeCookie: { name: 'NEXT_LOCALE', sameSite: 'lax', maxAge: 60 * 60 * 24 * 365 },
});
