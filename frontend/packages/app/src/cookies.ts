/** Non-sensitive preference cookies shared by both apps (read by the server for SSR). */
export const TIME_ZONE_COOKIE = 'eemp_tz';
export const THEME_COOKIE = 'eemp_theme';
export const LOCALE_COOKIE = 'NEXT_LOCALE';
export const ONE_YEAR_SECONDS = 60 * 60 * 24 * 365;

export type ThemePreference = 'light' | 'dark' | 'system';
export const THEMES: readonly ThemePreference[] = ['light', 'dark', 'system'];

export function isTheme(value: unknown): value is ThemePreference {
  return typeof value === 'string' && (THEMES as readonly string[]).includes(value);
}

/** Client-side write of a preference cookie (never used for tokens: those are httpOnly). */
export function setPreferenceCookie(name: string, value: string) {
  if (typeof document === 'undefined') return;
  const secure = typeof location !== 'undefined' && location.protocol === 'https:' ? '; Secure' : '';
  document.cookie = `${name}=${encodeURIComponent(value)}; Path=/; Max-Age=${ONE_YEAR_SECONDS}; SameSite=Lax${secure}`;
}
