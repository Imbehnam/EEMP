import { NextRequest } from 'next/server';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { getSharedMockBackend } from '../src/mock';
import { buildCsp, createAppProxy } from '../src/server/proxy';
import { createAuthHandlers, createBffProxy, safeNextPath, verifyCsrf, getBffConfig } from '../src/server';
import type { CurrentUser } from '../src';

const ORIGIN = 'http://localhost:3001';
const CSRF = 'a'.repeat(64);

function req(path: string, init: { method?: string; cookies?: Record<string, string>; headers?: Record<string, string>; body?: unknown } = {}) {
  const headers = new Headers({ host: 'localhost:3001', ...init.headers });
  const cookie = Object.entries(init.cookies ?? {}).map(([k, v]) => `${k}=${v}`).join('; ');
  if (cookie) headers.set('cookie', cookie);
  if (init.body !== undefined) headers.set('content-type', 'application/json');
  return new NextRequest(`${ORIGIN}${path}`, {
    method: init.method ?? 'GET',
    headers,
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
  });
}

const setCookies = (response: Response) => response.headers.getSetCookie();
const cookieValue = (response: Response, name: string) =>
  setCookies(response).find((c) => c.startsWith(`${name}=`))?.split(';')[0]!.slice(name.length + 1);

beforeEach(() => {
  process.env.EEMP_API_MOCK = 'true';
  getSharedMockBackend().reset();
});
afterEach(() => {
  delete process.env.EEMP_API_MOCK;
});

const handlers = createAuthHandlers({
  appId: 'admin',
  loginPath: (next) => `/fa/login?next=${encodeURIComponent(next)}`,
  acceptUser: (u: CurrentUser) => u.isPlatformAdmin || u.permissions.length > 0,
});
const proxy = createBffProxy({ appId: 'admin' });
const ctx = (...path: string[]) => ({ params: Promise.resolve({ path }) });

async function signIn(userName = 'demo.admin', tenantSlug: string | null = 'demo') {
  const response = await handlers.login(
    req('/api/auth/login', {
      method: 'POST',
      cookies: { eemp_admin_csrf: CSRF },
      headers: { 'x-csrf-token': CSRF, origin: ORIGIN },
      body: { tenantSlug, userName, password: 'eemp-mock' },
    }),
  );
  return response;
}

describe('CSRF', () => {
  const cfg = () => getBffConfig('admin');
  it('allows safe methods and requires the double-submit token on mutations', () => {
    expect(verifyCsrf(cfg(), req('/api/bff/branches'))).toBe(true);
    expect(verifyCsrf(cfg(), req('/api/bff/branches', { method: 'POST', cookies: { eemp_admin_csrf: CSRF } }))).toBe(false);
    expect(verifyCsrf(cfg(), req('/api/bff/branches', { method: 'POST', cookies: { eemp_admin_csrf: CSRF }, headers: { 'x-csrf-token': 'b'.repeat(64) } }))).toBe(false);
    expect(verifyCsrf(cfg(), req('/api/bff/branches', { method: 'POST', cookies: { eemp_admin_csrf: CSRF }, headers: { 'x-csrf-token': CSRF } }))).toBe(true);
  });
  it('rejects cross-origin mutations even with a matching token', () => {
    expect(
      verifyCsrf(cfg(), req('/api/bff/branches', { method: 'POST', cookies: { eemp_admin_csrf: CSRF }, headers: { 'x-csrf-token': CSRF, origin: 'https://evil.example' } })),
    ).toBe(false);
  });
});

describe('auth handlers', () => {
  it('login sets httpOnly session cookies plus eemp_tz / NEXT_LOCALE and never returns tokens', async () => {
    const response = await signIn();
    expect(response.status).toBe(200);
    const body = await response.json();
    expect(body).not.toHaveProperty('accessToken');
    expect(body).not.toHaveProperty('refreshToken');
    const cookies = setCookies(response);
    expect(cookies.find((c) => c.startsWith('eemp_admin_at='))).toMatch(/HttpOnly/i);
    expect(cookies.find((c) => c.startsWith('eemp_admin_rt='))).toMatch(/SameSite=lax/i);
    expect(decodeURIComponent(cookieValue(response, 'eemp_tz') ?? '')).toBe('Asia/Tehran');
    expect(cookieValue(response, 'NEXT_LOCALE')).toBe('fa');
    expect(cookies.find((c) => c.startsWith('eemp_tz='))).not.toMatch(/HttpOnly/i);
  });

  it('login without CSRF token is rejected', async () => {
    const response = await handlers.login(req('/api/auth/login', { method: 'POST', body: { tenantSlug: 'demo', userName: 'demo.admin', password: 'eemp-mock' } }));
    expect(response.status).toBe(403);
    expect((await response.json()).code).toBe('security.csrf_failed');
  });

  it('rejects accounts the app does not serve', async () => {
    const response = await signIn('student.reza');
    expect(response.status).toBe(403);
    expect((await response.json()).code).toBe('auth.portal_not_allowed');
    expect(cookieValue(response, 'eemp_admin_at')).toBeUndefined();
  });

  it('refresh rotates tokens and redirects; failure clears cookies and goes to login', async () => {
    const login = await signIn();
    const rt = cookieValue(login, 'eemp_admin_rt')!;
    const ok = await handlers.refresh(req('/api/auth/refresh?next=%2Ffa%2Fbranches', { cookies: { eemp_admin_rt: rt } }));
    expect(ok.status).toBe(307);
    expect(ok.headers.get('location')).toBe(`${ORIGIN}/fa/branches`);
    expect(cookieValue(ok, 'eemp_admin_rt')).not.toBe(rt);

    const fail = await handlers.refresh(req('/api/auth/refresh?next=%2F%2Fevil.example', { cookies: { eemp_admin_rt: 'bogus' } }));
    expect(fail.headers.get('location')).toBe(`${ORIGIN}/fa/login?next=%2F`);
    expect(cookieValue(fail, 'eemp_admin_at')).toBe('');
  });
});

describe('BFF proxy', () => {
  it('returns 401 problem without a session', async () => {
    const response = await proxy.GET(req('/api/bff/branches'), ctx('branches'));
    expect(response.status).toBe(401);
    expect(response.headers.get('content-type')).toContain('application/problem+json');
  });

  it('attaches the bearer token from the cookie and passes data through', async () => {
    const at = cookieValue(await signIn(), 'eemp_admin_at')!;
    const response = await proxy.GET(req('/api/bff/branches?pageSize=1', { cookies: { eemp_admin_at: at } }), ctx('branches'));
    expect(response.status).toBe(200);
    const body = await response.json();
    expect(body).toMatchObject({ page: 1, pageSize: 1, totalCount: 2 });
  });

  it('refreshes transparently when only the refresh cookie remains', async () => {
    const rt = cookieValue(await signIn(), 'eemp_admin_rt')!;
    const response = await proxy.GET(req('/api/bff/auth/me', { cookies: { eemp_admin_rt: rt } }), ctx('auth', 'me'));
    expect(response.status).toBe(200);
    expect(cookieValue(response, 'eemp_admin_at')).toMatch(/^mock-at-/);
  });

  it('enforces CSRF on mutations and blocks raw-token auth routes', async () => {
    const at = cookieValue(await signIn(), 'eemp_admin_at')!;
    const noCsrf = await proxy.POST(req('/api/bff/branches', { method: 'POST', cookies: { eemp_admin_at: at }, body: { code: 'X', name: 'X' } }), ctx('branches'));
    expect(noCsrf.status).toBe(403);
    const withCsrf = await proxy.POST(
      req('/api/bff/branches', { method: 'POST', cookies: { eemp_admin_at: at, eemp_admin_csrf: CSRF }, headers: { 'x-csrf-token': CSRF }, body: { code: 'X1', name: 'X' } }),
      ctx('branches'),
    );
    expect(withCsrf.status).toBe(201);
    const blocked = await proxy.POST(
      req('/api/bff/auth/login', { method: 'POST', cookies: { eemp_admin_csrf: CSRF }, headers: { 'x-csrf-token': CSRF }, body: {} }),
      ctx('auth', 'login'),
    );
    expect(blocked.status).toBe(404);
    const traversal = await proxy.GET(req('/api/bff/x', { cookies: { eemp_admin_at: at } }), ctx('..', 'platform'));
    expect(traversal.status).toBe(404);
  });
});

describe('safeNextPath', () => {
  it('only allows same-app relative paths', () => {
    expect(safeNextPath('/fa/users', '/')).toBe('/fa/users');
    for (const bad of ['//evil.example', 'https://evil.example', '/\\evil', '/api/bff/x', null]) expect(safeNextPath(bad, '/fa')).toBe('/fa');
  });
});

describe('app proxy (CSP nonce)', () => {
  it('adds a per-request nonce CSP, forwards x-nonce to the request and sets the CSRF cookie', async () => {
    const { NextResponse } = await import('next/server');
    let seen: Headers | undefined;
    const proxyFn = createAppProxy({
      appId: 'admin', locales: ['fa', 'en'], defaultLocale: 'fa', publicPaths: ['/login'], isDev: false,
      intl: (request) => {
        seen = request.headers;
        return NextResponse.next({ request: { headers: request.headers } });
      },
    });
    const response = proxyFn(req('/fa/login'));
    const csp = response.headers.get('content-security-policy')!;
    const nonce = seen!.get('x-nonce')!;
    expect(csp).toContain(`'nonce-${nonce}'`);
    expect(csp).toContain("frame-ancestors 'none'");
    expect(cookieValue(response, 'eemp_admin_csrf')).toHaveLength(64);
  });

  it('redirects anonymous users to login and expired sessions through refresh', () => {
    const proxyFn = createAppProxy({
      appId: 'admin', locales: ['fa'], defaultLocale: 'fa', publicPaths: ['/login'], isDev: false,
      intl: () => new Response(null),
    });
    expect(proxyFn(req('/fa/users')).headers.get('location')).toBe(`${ORIGIN}/fa/login?next=%2Ffa%2Fusers`);
    expect(proxyFn(req('/fa/users', { cookies: { eemp_admin_rt: 'x' } })).headers.get('location')).toBe(
      `${ORIGIN}/api/auth/refresh?next=%2Ffa%2Fusers`,
    );
  });

  it('builds a strict production CSP', () => {
    const csp = buildCsp('n1', false);
    expect(csp).not.toContain('unsafe-eval');
    expect(csp).toContain('upgrade-insecure-requests');
  });
});
