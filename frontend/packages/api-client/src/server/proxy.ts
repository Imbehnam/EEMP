import { NextResponse, type NextRequest } from 'next/server';

import { cookieNames } from '../cookies';
import { newCsrfToken } from './csrf';

export interface AppProxyOptions {
  appId: string;
  locales: readonly string[];
  defaultLocale: string;
  /** Paths (after the locale prefix) reachable without a session, e.g. ["/login"]. */
  publicPaths: readonly string[];
  /** The next-intl middleware (locale negotiation, prefix redirects). */
  intl: (request: NextRequest) => NextResponse | Response;
  isDev?: boolean;
  cookieSecure?: boolean;
}

export function buildCsp(nonce: string, isDev: boolean): string {
  const directives: Record<string, string[]> = {
    'default-src': ["'self'"],
    'script-src': ["'self'", `'nonce-${nonce}'`, "'strict-dynamic'", ...(isDev ? ["'unsafe-eval'"] : [])],
    // Emotion receives the nonce; 'unsafe-inline' remains for React style attributes (see FRONTEND_IMPLEMENTATION.md).
    'style-src': ["'self'", "'unsafe-inline'"],
    'img-src': ["'self'", 'data:', 'blob:'],
    'font-src': ["'self'", 'data:'],
    'connect-src': ["'self'", ...(isDev ? ['ws:', 'wss:'] : [])],
    'frame-ancestors': ["'none'"],
    'form-action': ["'self'"],
    'base-uri': ["'self'"],
    'object-src': ["'none'"],
  };
  const csp = Object.entries(directives).map(([k, v]) => `${k} ${v.join(' ')}`);
  if (!isDev) csp.push('upgrade-insecure-requests');
  return csp.join('; ');
}

/**
 * Next.js 16 `proxy` (formerly middleware): locale routing via next-intl, a per-request CSP nonce,
 * the CSRF double-submit cookie, and a coarse session gate. The gate only checks cookie presence
 * for navigation UX; layouts re-validate with the backend and the backend authorizes every call.
 */
export function createAppProxy(options: AppProxyOptions) {
  const names = cookieNames(options.appId);
  const isDev = options.isDev ?? process.env.NODE_ENV !== 'production';

  return function proxy(request: NextRequest) {
    const nonce = btoa(globalThis.crypto.randomUUID());
    const csp = buildCsp(nonce, isDev);
    // next-intl copies request headers into its NextResponse.next({ request: { headers } }),
    // which makes the nonce available to server components via headers().
    request.headers.set('x-nonce', nonce);
    request.headers.set('content-security-policy', csp);
    // Original path for server layouts (post-login / refresh return path). Never trusted for auth.
    request.headers.set('x-eemp-pathname', `${request.nextUrl.pathname}${request.nextUrl.search}`);

    const { pathname, search } = request.nextUrl;
    const [, first = '', ...rest] = pathname.split('/');
    const locale = options.locales.includes(first) ? first : null;

    let response: NextResponse | Response | undefined;
    if (locale) {
      const subPath = `/${rest.join('/')}`.replace(/\/+$/, '') || '/';
      const isPublic = options.publicPaths.some((p) => subPath === p || subPath.startsWith(`${p}/`));
      const hasAccess = !!request.cookies.get(names.access)?.value;
      const hasRefresh = !!request.cookies.get(names.refresh)?.value;
      if (!isPublic && !hasAccess) {
        const next = `${pathname}${search}`;
        const target = hasRefresh
          ? `/api/auth/refresh?next=${encodeURIComponent(next)}`
          : `/${locale}/login?next=${encodeURIComponent(next)}`;
        response = NextResponse.redirect(new URL(target, request.url));
      } else if (isPublic && hasAccess && subPath === '/login') {
        response = NextResponse.redirect(new URL(`/${locale}`, request.url));
      }
    }
    response ??= options.intl(request);

    response.headers.set('Content-Security-Policy', csp);
    if (!request.cookies.get(names.csrf)?.value && response instanceof NextResponse) {
      response.cookies.set(names.csrf, newCsrfToken(), {
        httpOnly: false, // read by our JS to echo in X-CSRF-Token (double submit)
        sameSite: 'strict',
        secure: options.cookieSecure ?? !isDev,
        path: '/',
      });
    }
    return response;
  };
}

export const PROXY_MATCHER = ['/((?!api|_next|_vercel|favicon.ico|.*\\..*).*)'];
