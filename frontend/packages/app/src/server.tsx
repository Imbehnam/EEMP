import 'server-only';

import { createServerEndpoints, getBffConfig, getServerSession, readTokens, safeNextPath } from '@eemp/api-client/server';
import type { CurrentUser } from '@eemp/api-client';
import { getDirection, getMetadata } from '@eemp/i18n';
import { routing } from '@eemp/i18n/routing';
import { hasLocale, NextIntlClientProvider } from 'next-intl';
import { setRequestLocale } from 'next-intl/server';
import { cookies, headers } from 'next/headers';
import { notFound, redirect } from 'next/navigation';
import type { ReactNode } from 'react';

import { isTheme, THEME_COOKIE, type ThemePreference } from './cookies';
import { AppProviders } from './Providers';

/** Header set by the app proxy with the original pathname (for post-login/refresh return paths). */
export const PATHNAME_HEADER = 'x-eemp-pathname';

export async function getRequestNonce(): Promise<string | undefined> {
  return (await headers()).get('x-nonce') ?? undefined;
}

export async function getThemePreference(): Promise<ThemePreference> {
  const value = (await cookies()).get(THEME_COOKIE)?.value;
  return isTheme(value) ? value : 'system';
}

/** Validates the [locale] segment and enables static locale for next-intl in this request. */
export function resolveLocale(locale: string): string {
  if (!hasLocale(routing.locales, locale)) notFound();
  setRequestLocale(locale);
  return locale;
}

/** The <html> document for a locale: lang/dir, theme, nonce-aware Emotion, next-intl client provider. */
export async function LocaleDocument({ locale, children }: { locale: string; children: ReactNode }) {
  const dir = getDirection(locale);
  const [nonce, theme] = await Promise.all([getRequestNonce(), getThemePreference()]);
  return (
    <html lang={locale} dir={dir} data-theme={theme} data-calendar={getMetadata(locale).calendar} suppressHydrationWarning>
      <body>
        <NextIntlClientProvider>
          <AppProviders dir={dir} nonce={nonce}>
            {children}
          </AppProviders>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}

/**
 * Resolves the signed-in user for an authenticated layout, or redirects to refresh/login.
 * UX gate only; the backend authorizes every API call.
 */
export async function requireUser(appId: string, locale: string): Promise<CurrentUser> {
  const session = await getServerSession(appId, locale);
  if (session.status === 'authenticated') return session.user;
  const current = safeNextPath((await headers()).get(PATHNAME_HEADER), `/${locale}`);
  if (session.status === 'needs-refresh') redirect(`/api/auth/refresh?next=${encodeURIComponent(current)}`);
  redirect(`/${locale}/login?next=${encodeURIComponent(current)}`);
}

/** Typed endpoints for server components, using the caller's access token (null when signed out). */
export async function getServerApi(appId: string, locale: string) {
  const { accessToken } = readTokens(getBffConfig(appId), await cookies());
  return accessToken ? createServerEndpoints(appId, accessToken, locale) : null;
}

export function isMockMode(appId: string): boolean {
  return getBffConfig(appId).mock;
}

export { safeNextPath };
