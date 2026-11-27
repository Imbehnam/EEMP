import { type Page } from '@playwright/test';

export const ADMIN = `http://localhost:${process.env.EEMP_E2E_ADMIN_PORT ?? 3001}`;
export const WEB = `http://localhost:${process.env.EEMP_E2E_WEB_PORT ?? 3000}`;
export const PASSWORD = process.env.EEMP_MOCK_PASSWORD || 'eemp-mock';

export async function signIn(page: Page, base: string, locale: string, tenant: string, user: string, password = PASSWORD, expectSuccess = true) {
  await page.goto(`${base}/${locale}/login`);
  await page.locator('input[name="tenantSlug"]').fill(tenant);
  await page.locator('input[name="userName"]').fill(user);
  await page.locator('input[name="password"]').fill(password);
  await page.locator('button[type="submit"]').click();
  // Login navigates with window.location; wait for it before the test navigates again.
  if (expectSuccess) await page.waitForURL((url) => !url.pathname.endsWith('/login'));
}

