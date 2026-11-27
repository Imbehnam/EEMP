'use client';

import { localeMetadata, locales } from '@eemp/i18n';
import { useLocale, useTranslations } from 'next-intl';
import { useId } from 'react';

import { LOCALE_COOKIE, setPreferenceCookie } from './cookies';
import { switchLocalePath } from './paths';

/**
 * Language switcher. Uses a full navigation because direction (RTL/LTR), font and the Emotion
 * cache all change with the locale. Option labels are native language names with their own `lang`.
 */
export function LocaleSwitcher() {
  const t = useTranslations('common');
  const locale = useLocale();
  const id = useId();
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
      <label htmlFor={id} className="eemp-hide-sm eemp-compact-label">{t('language')}</label>
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
