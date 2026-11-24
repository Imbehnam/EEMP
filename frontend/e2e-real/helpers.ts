import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { expect, type Page } from '@playwright/test';

export const API = process.env.EEMP_E2E_API ?? 'http://127.0.0.1:5080';
export const ADMIN = 'http://localhost:3001';
export const WEB = 'http://localhost:3000';

/** Development-seed credentials (backend Seed__* settings; dev only, never production secrets). */
export const PASSWORDS = {
  platform: process.env.EEMP_E2E_PLATFORM_PASSWORD ?? 'Platform123',
  demoAdmin: process.env.EEMP_E2E_DEMO_ADMIN_PASSWORD ?? 'DemoAdmin123',
  demoUser: process.env.EEMP_E2E_DEMO_USER_PASSWORD ?? 'DemoUser123',
};

/** Short unique suffix so reruns against the same database do not collide. */
export const unique = () => `${Date.now().toString(36)}${Math.floor(Math.random() * 1296).toString(36)}`;

export async function signIn(
  page: Page,
  base: string,
  locale: string,
  tenant: string | null,
  user: string,
  password: string,
  expectSuccess = true,
) {
  await page.goto(`${base}/${locale}/login`);
  if (tenant === null) {
    await page.getByRole('checkbox').first().check();
  } else {
    await page.locator('input[name="tenantSlug"]').fill(tenant);
  }
  await page.locator('input[name="userName"]').fill(user);
  await page.locator('input[name="password"]').fill(password);
  await page.locator('button[type="submit"]').click();
  if (expectSuccess) await page.waitForURL((url) => !url.pathname.endsWith('/login'));
}

export const signInDemoAdmin = (page: Page, locale = 'en') => signIn(page, ADMIN, locale, 'demo', 'demo.admin', PASSWORDS.demoAdmin);
export const signInBranchManager = (page: Page, locale = 'en') =>
  signIn(page, ADMIN, locale, 'demo', 'branch.manager', PASSWORDS.demoUser);

/** Direct API call (outside the browser) for setup and for asserting server-side state. */
export async function apiLogin(tenantSlug: string | null, userName: string, password: string) {
  const response = await fetch(`${API}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tenantSlug, userName, password }),
  });
  expect(response.status, `login ${userName}`).toBe(200);
  return (await response.json()) as { accessToken: string; refreshToken: string; user: { id: string } };
}

export async function api<T = unknown>(token: string, method: string, path: string, body?: unknown, locale = 'en') {
  const response = await fetch(`${API}/api/v1${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      'Accept-Language': locale,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  return { status: response.status, body: (text ? JSON.parse(text) : null) as T };
}

/** Cookie value for an app's session cookie (names come from @eemp/api-client cookies). */
export async function cookieValue(page: Page, name: string) {
  return (await page.context().cookies()).find((c) => c.name === name)?.value;
}

/** Exact UI translation (from @eemp/i18n messages) for a dotted key, e.g. msg('en', 'errors.auth.invalid_credentials'). */
const messageCache = new Map<string, Record<string, unknown>>();
export function msg(locale: 'fa' | 'en' | 'tr' | 'ar', key: string): string {
  let doc = messageCache.get(locale);
  if (!doc) {
    doc = JSON.parse(readFileSync(join(__dirname, '..', 'packages', 'i18n', 'messages', `${locale}.json`), 'utf8')) as Record<string, unknown>;
    messageCache.set(locale, doc);
  }
  const value = key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], doc);
  if (typeof value !== 'string') throw new Error(`missing message ${locale}:${key}`);
  return value;
}

export const textbox = (page: Page, name: string) => page.getByRole('textbox', { name, exact: true });

/** The app's own alert (Next.js also renders an empty role=alert route announcer). */
export const alertBox = (page: Page) => page.locator('[role="alert"]:not(#__next-route-announcer__)');
