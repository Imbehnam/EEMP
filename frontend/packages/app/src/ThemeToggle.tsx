'use client';

import { useTranslations } from 'next-intl';
import { useId, useState } from 'react';

import { isTheme, setPreferenceCookie, THEME_COOKIE, THEMES, type ThemePreference } from './cookies';

/** Light / dark / system theme. Persisted in a cookie so the server renders the right theme. */
export function ThemeToggle({ initial = 'system' }: { initial?: ThemePreference }) {
  const t = useTranslations('common');
  const id = useId();
  const [theme, setTheme] = useState<ThemePreference>(initial);
  const labels: Record<ThemePreference, string> = { light: t('themeLight'), dark: t('themeDark'), system: t('themeSystem') };
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
      <label htmlFor={id} style={{ fontSize: '0.8125rem' }}>{t('theme')}</label>
      <select
        id={id}
        className="eemp-compact-select"
        value={theme}
        onChange={(event) => {
          const next = event.target.value;
          if (!isTheme(next)) return;
          setTheme(next);
          document.documentElement.dataset.theme = next;
          setPreferenceCookie(THEME_COOKIE, next);
        }}
      >
        {THEMES.map((value) => (
          <option key={value} value={value}>{labels[value]}</option>
        ))}
      </select>
    </span>
  );
}
