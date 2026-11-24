import { locales } from '@eemp/i18n';

/** Replace the locale prefix of a pathname (`/fa/users` + `?x=1` -> `/en/users?x=1`). */
export function switchLocalePath(pathname: string, search: string, nextLocale: string): string {
  const parts = pathname.split('/');
  if ((locales as readonly string[]).includes(parts[1] ?? '')) parts[1] = nextLocale;
  else parts.splice(1, 0, nextLocale);
  const path = parts.join('/').replace(/\/+$/, '') || `/${nextLocale}`;
  return `${path}${search}`;
}

/** Whether a nav href (without locale) is active for the current pathname. */
export function isActivePath(pathname: string, href: string): boolean {
  if (href === '/') return pathname === '/' || pathname === '';
  return pathname === href || pathname.startsWith(`${href}/`);
}
