import { createAppProxy } from '@eemp/api-client/proxy';
import { routing } from '@eemp/i18n/routing';
import createMiddleware from 'next-intl/middleware';

const intl = createMiddleware(routing);

export const proxy = createAppProxy({
  appId: 'admin',
  locales: routing.locales,
  defaultLocale: routing.defaultLocale,
  publicPaths: ['/login'],
  intl,
});

export const config = {
  // Everything except API routes, Next internals and static files.
  matcher: ['/((?!api|_next|_vercel|favicon.ico|.*\\..*).*)'],
};
