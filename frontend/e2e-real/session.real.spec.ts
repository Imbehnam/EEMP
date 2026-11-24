import { expect, test } from '@playwright/test';

import { ADMIN, API, apiLogin, cookieValue, PASSWORDS, signIn, WEB } from './helpers';

async function refreshStatus(refreshToken: string) {
  const response = await fetch(`${API}/api/v1/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  });
  return { status: response.status, code: ((await response.json().catch(() => ({}))) as { code?: string }).code };
}

test('access token expiry -> transparent refresh with rotation; old refresh token is rejected', async ({ page }) => {
  const probe = await apiLogin('demo', 'student.one', PASSWORDS.demoUser);
  const lifetime = (Date.parse((probe as unknown as { accessTokenExpiresAt: string }).accessTokenExpiresAt) - Date.now()) / 1000;
  test.skip(lifetime > 120, `API access tokens live ${Math.round(lifetime)}s; start the API with Jwt__AccessTokenMinutes=1`);
  test.setTimeout(150_000);

  await signIn(page, WEB, 'en', 'demo', 'student.one', PASSWORDS.demoUser);
  const firstRefresh = await cookieValue(page, 'eemp_web_rt');
  const firstAccess = await cookieValue(page, 'eemp_web_at');
  expect(firstRefresh).toBeTruthy();
  expect(firstAccess).toBeTruthy();

  // The access cookie lives (expiresAt - 30s); wait until the browser drops it.
  await expect.poll(async () => cookieValue(page, 'eemp_web_at'), { timeout: 100_000, intervals: [2_000] }).toBeUndefined();

  // Server-rendered page: needs-refresh -> /api/auth/refresh -> back to the page with new cookies.
  await page.goto(`${WEB}/en/profile`);
  await expect(page).toHaveURL(`${WEB}/en/profile`);
  await expect(page.getByText('Sara Ahmadi')).toBeVisible();
  const secondRefresh = await cookieValue(page, 'eemp_web_rt');
  expect(secondRefresh).toBeTruthy();
  expect(secondRefresh).not.toBe(firstRefresh);
  expect(await cookieValue(page, 'eemp_web_at')).not.toBe(firstAccess);

  // BFF path: an invalid access cookie -> 401 upstream -> one refresh -> retried call succeeds.
  await page.context().addCookies([{ name: 'eemp_web_at', value: 'garbage.token.value', url: WEB }]);
  const profile = await page.evaluate(async () => {
    const r = await fetch('/api/bff/portal/me/profile', { headers: { Accept: 'application/json' } });
    return { status: r.status, kind: ((await r.json()) as { kind?: string }).kind };
  });
  expect(profile).toEqual({ status: 200, kind: 'student' });
  const thirdRefresh = await cookieValue(page, 'eemp_web_rt');
  expect(thirdRefresh).not.toBe(secondRefresh);

  // Re-using a rotated (old) refresh token is refused.
  const reuse = await refreshStatus(firstRefresh!);
  expect(reuse.status).toBe(401);
});

test('logout revokes the refresh token and clears cookies', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'demo.admin', PASSWORDS.demoAdmin);
  const refreshToken = await cookieValue(page, 'eemp_admin_rt');
  expect(refreshToken).toBeTruthy();
  await page.goto(`${ADMIN}/en`); // login lands on the user's saved language (fa)
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/en\/login\?reason=signedOut/);
  await expect(page.getByText('You’ve been signed out.')).toBeVisible();
  expect(await cookieValue(page, 'eemp_admin_rt')).toBeUndefined();
  expect(await cookieValue(page, 'eemp_admin_at')).toBeUndefined();
  expect((await refreshStatus(refreshToken!)).status).toBe(401);
  await page.goto(`${ADMIN}/en/branches`);
  await expect(page).toHaveURL(/\/en\/login/);
});
