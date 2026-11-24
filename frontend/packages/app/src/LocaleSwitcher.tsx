'use client';

import { localeMetadata, locales } from '@eemp/i18n';
import { useLocale, useTranslations } from 'next-intl';
import { useId } from 'react';

import { LOCALE_COOKIE, setPreferenceCookie } from './cookies';

/** Replace the locale prefix of a pathname (`/fa/users?x=1` -> `/en/users?x=1`). */
export function switchLocalePath(pathname: string, search: string, nextLocale: string): string {
  const parts = pathname.split('/');
  if ((locales as readonly string[]).includes(parts[1] ?? '')) parts[1] = nextLocale;
  else parts.splice(1, 0, nextLocale);
  const path = parts.join('/').replace(/\/+$/, '') || `/${nextLocale}`;
  return `${path}${search}`;
}

/**
 * Language switcher. Uses a full navigation because direction (RTL/LTR), font and the Emotion
 * cache all change with the locale. Option labels are native language names with their own `lang`.
 */
export function LocaleSwitcher() {
  const t = useTranslations('common');
  const locale = useLocale();
  const id = useId();
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
      <label htmlFor={id} style={{ fontSize: '0.8125rem' }}>{t('language')}</label>
      <select
        id={id}
        className="eemp-compact-select"
        value={locale}
        data-testid="locale-switcher"
        onChange={(event) => {
          const next = event.target.value;
          setPreferenceCookie(LOCALE_COOKIE, next);
          window.location.assign(switchLocalePath(window.location.pathname, window.location.search, next));
        }}
      >
        {locales.map((code) => (
          <option key={code} value={code} lang={code}>{localeMetadata[code].nativeName}</option>
        ))}
      </select>
    </span>
  );
}
