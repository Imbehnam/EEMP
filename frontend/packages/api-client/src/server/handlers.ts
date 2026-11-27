import { NextResponse, type NextRequest } from 'next/server';

import { ErrorCodes } from '../problem';
import type { CurrentUser, TokenResponse } from '../types';
import { getBffConfig, type BffConfig } from './config';
import { verifyCsrf } from './csrf';
import { clearSessionCookies, readTokens, refreshTokens, setPreferenceCookies, setSessionCookies } from './tokens';

/** Backend routes that must never be reachable through the generic proxy (they handle raw tokens). */
const BLOCKED = new Set(['auth/login', 'auth/refresh', 'auth/logout']);
/**
 * x-forwarded-for carries the browser's address (Next.js fills it from the socket when no proxy did), so the
 * API's per-client auth rate limit does not lump every user behind this server into one bucket.
 */
const FORWARD_REQUEST_HEADERS = ['accept', 'accept-language', 'content-type', 'x-correlation-id', 'x-forwarded-for'];

/** Client-address header for direct BFF -> API calls (login/refresh/logout). */
export function forwardedFor(request: { headers: Headers }): Record<string, string> {
  const value = request.headers.get('x-forwarded-for');
  return value ? { 'X-Forwarded-For': value } : {};
}
const FORWARD_RESPONSE_HEADERS = ['content-type', 'x-correlation-id', 'location', 'x-total-count', 'content-disposition'];
/** Downloads (report-card/invoice/receipt PDFs, archives, CSV reports) are streamed through instead of buffered. */
const STREAMED_TYPES = ['application/pdf', 'application/zip', 'text/csv', 'application/octet-stream'];

function problemResponse(status: number, code: string, extra: Record<string, unknown> = {}) {
  return NextResponse.json(
    { title: code, status, code, params: {}, ...extra },
    { status, headers: { 'Content-Type': 'application/problem+json', 'Cache-Control': 'no-store' } },
  );
}

/** Relative, same-app paths only (prevents open redirects via ?next=). */
export function safeNextPath(value: string | null | undefined, fallback: string): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\') || value.startsWith('/api/')) {
    return fallback;
  }
  return value;
}

function correlationId(request: NextRequest) {
  return request.headers.get('x-correlation-id') ?? globalThis.crypto.randomUUID();
}

async function passThrough(upstream: Response): Promise<NextResponse> {
  const headers = new Headers({ 'Cache-Control': 'no-store' });
  for (const name of FORWARD_RESPONSE_HEADERS) {
    const value = upstream.headers.get(name);
    if (value) headers.set(name, value);
  }
  if (upstream.status === 204 || upstream.status === 304) return new NextResponse(null, { status: upstream.status, headers });
  const type = (upstream.headers.get('content-type') ?? '').split(';')[0]!.trim().toLowerCase();
  if (upstream.ok && upstream.body && STREAMED_TYPES.includes(type)) {
    headers.set('X-Content-Type-Options', 'nosniff');
    return new NextResponse(upstream.body, { status: upstream.status, headers });
  }
  return new NextResponse(await upstream.arrayBuffer(), { status: upstream.status, headers });
}

export interface AuthHandlerOptions {
  appId: string;
  /** Where to send users after a failed refresh, e.g. (next) => `/fa/login?next=...` */
  loginPath: (next: string) => string;
  /** Optional gate: reject logins this app does not serve (e.g. platform admins on the client portal). */
  acceptUser?: (user: CurrentUser) => boolean;
}

export function createAuthHandlers(options: AuthHandlerOptions) {
  const config = () => getBffConfig(options.appId);

  /** POST /api/auth/login — exchanges credentials for httpOnly cookies; returns only the user. */
  async function login(request: NextRequest) {
    const cfg = config();
    if (!verifyCsrf(cfg, request)) return problemResponse(403, ErrorCodes.csrfFailed);
    let payload: { tenantSlug?: string | null; userName?: string; password?: string };
    try {
      payload = (await request.json()) as typeof payload;
    } catch {
      return problemResponse(400, ErrorCodes.validationFailed);
    }
    const upstream = await cfg.fetch(`${cfg.apiBaseUrl}/auth/login`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'application/json',
        'Accept-Language': request.headers.get('accept-language') ?? 'fa',
        'X-Correlation-Id': correlationId(request),
        ...forwardedFor(request),
      },
      body: JSON.stringify({
        tenantSlug: payload.tenantSlug ? String(payload.tenantSlug).trim().toLowerCase() : null,
        userName: String(payload.userName ?? '').trim(),
        password: String(payload.password ?? ''),
      }),
      cache: 'no-store',
    }).catch(() => null);
    if (!upstream) return problemResponse(502, ErrorCodes.network);
    if (!upstream.ok) return passThrough(upstream);

    const tokens = (await upstream.json()) as TokenResponse;
    if (options.acceptUser && !options.acceptUser(tokens.user)) {
      // Revoke the just-issued session; this portal does not serve this account type.
      await cfg.fetch(`${cfg.apiBaseUrl}/auth/logout`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${tokens.accessToken}` },
        body: JSON.stringify({ refreshToken: tokens.refreshToken }),
      }).catch(() => undefined);
      return problemResponse(403, 'auth.portal_not_allowed');
    }
    const response = NextResponse.json({ user: tokens.user }, { headers: { 'Cache-Control': 'no-store' } });
    setSessionCookies(cfg, response, tokens);
    setPreferenceCookies(cfg, response, tokens.user);
    return response;
  }

  /** POST /api/auth/logout — revokes the refresh token server-side and clears cookies. */
  async function logout(request: NextRequest) {
    const cfg = config();
    if (!verifyCsrf(cfg, request)) return problemResponse(403, ErrorCodes.csrfFailed);
    const { accessToken, refreshToken } = readTokens(cfg, request.cookies);
    if (refreshToken) {
      await cfg.fetch(`${cfg.apiBaseUrl}/auth/logout`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...forwardedFor(request),
          ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
        },
        body: JSON.stringify({ refreshToken }),
        cache: 'no-store',
      }).catch(() => undefined);
    }
    const response = new NextResponse(null, { status: 204 });
    clearSessionCookies(cfg, response);
    return response;
  }

  /**
   * GET /api/auth/refresh?next=/fa/... — used by the proxy and server components when the
   * access cookie has expired. Rotates tokens, then redirects back (or to login on failure).
   */
  async function refresh(request: NextRequest) {
    const cfg = config();
    const next = safeNextPath(request.nextUrl.searchParams.get('next'), '/');
    const { refreshToken } = readTokens(cfg, request.cookies);
    const result = refreshToken ? await refreshTokens(cfg, refreshToken, forwardedFor(request)) : null;
    if (!result?.ok) {
      const response = NextResponse.redirect(new URL(options.loginPath(next), request.url));
      clearSessionCookies(cfg, response);
      return response;
    }
    const response = NextResponse.redirect(new URL(next, request.url));
    setSessionCookies(cfg, response, result.tokens);
    return response;
  }

  return { login, logout, refresh };
}

/**
 * Catch-all BFF proxy: /api/bff/<path> -> <EEMP_API_BASE_URL>/<path>.
 * Attaches the bearer token from the httpOnly cookie, refreshes once on expiry/401,
 * enforces CSRF on mutations, and passes problem+json through unchanged.
 */
export function createBffProxy(options: { appId: string }) {
  async function handler(request: NextRequest, context: { params: Promise<{ path?: string[] }> }) {
    const cfg: BffConfig = getBffConfig(options.appId);
    const { path = [] } = await context.params;
    const joined = path.map(encodeURIComponent).join('/');
    if (!joined || BLOCKED.has(joined) || path.some((s) => s === '..' || s === '.')) {
      return problemResponse(404, ErrorCodes.notFound);
    }
    if (!verifyCsrf(cfg, request)) return problemResponse(403, ErrorCodes.csrfFailed);

    const target = `${cfg.apiBaseUrl}/${joined}${request.nextUrl.search}`;
    const method = request.method.toUpperCase();
    const body = method === 'GET' || method === 'HEAD' ? undefined : await request.text();
    const baseHeaders: Record<string, string> = {};
    for (const name of FORWARD_REQUEST_HEADERS) {
      const value = request.headers.get(name);
      if (value) baseHeaders[name] = value;
    }
    baseHeaders['x-correlation-id'] ??= globalThis.crypto.randomUUID();

    let { accessToken } = readTokens(cfg, request.cookies);
    const { refreshToken } = readTokens(cfg, request.cookies);
    let rotated: TokenResponse | undefined;

    const tryRefresh = async () => {
      if (!refreshToken) return false;
      const result = await refreshTokens(cfg, refreshToken, forwardedFor(request));
      if (!result.ok) return false;
      rotated = result.tokens;
      accessToken = result.tokens.accessToken;
      return true;
    };

    const send = () =>
      cfg.fetch(target, {
        method,
        headers: { ...baseHeaders, ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}) },
        body: body || undefined,
        cache: 'no-store',
        redirect: 'manual',
      });

    if (!accessToken && !(await tryRefresh())) {
      const response = problemResponse(401, ErrorCodes.unauthenticated);
      clearSessionCookies(cfg, response);
      return response;
    }

    let upstream: Response;
    try {
      upstream = await send();
      if (upstream.status === 401 && !rotated && (await tryRefresh())) upstream = await send();
    } catch {
      return problemResponse(502, ErrorCodes.network);
    }

    const response = await passThrough(upstream);
    if (upstream.status === 401) clearSessionCookies(cfg, response);
    else if (rotated) setSessionCookies(cfg, response, rotated);
    return response;
  }

  return { GET: handler, POST: handler, PUT: handler, DELETE: handler, PATCH: handler };
}
