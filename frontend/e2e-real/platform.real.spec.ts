import { expect, test } from '@playwright/test';

import { ADMIN, alertBox, PASSWORDS, signIn, unique } from './helpers';

test('platform admin: list, create, suspend and activate a tenant; suspended tenant cannot sign in', async ({ page, browser }) => {
  await signIn(page, ADMIN, 'en', null, 'platform.admin', PASSWORDS.platform);
  await page.goto(`${ADMIN}/en/tenants`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Tenants');
  await expect(page.getByRole('link', { name: 'Demo Institute' })).toBeVisible();

  const slug = `e2e-${unique()}`;
  const name = `E2E Institute ${slug}`;
  await page.getByRole('link', { name: 'New tenant' }).click();
  const box = (label: string) => page.getByRole('textbox', { name: label, exact: true });
  await box('Code').fill(slug);
  await box('Name').fill(name);
  await box('Username').fill('first.admin');
  await box('Display name').fill('First Admin');
  await page.getByLabel('Temporary password').fill('FirstAdmin123');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(name);

  // Suspend from the detail page.
  await page.getByRole('button', { name: 'Suspend' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Suspend' }).click();
  // The dialog's label follows the new status for a moment before it closes: wait for it to go away first.
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByRole('button', { name: 'Activate' })).toBeVisible();
  await expect(page.getByText('Suspended').first()).toBeVisible();

  // The new tenant's admin is blocked while suspended, with a translated message.
  const other = await browser.newPage();
  await signIn(other, ADMIN, 'en', slug, 'first.admin', 'FirstAdmin123', false);
  await expect(alertBox(other)).toContainText('suspended');

  await page.getByRole('button', { name: 'Activate' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Activate' }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByRole('button', { name: 'Suspend' })).toBeVisible();

  await signIn(other, ADMIN, 'en', slug, 'first.admin', 'FirstAdmin123');
  await expect(other).toHaveURL(/\/(fa|en)$/);

  // List shows the new tenant.
  await page.goto(`${ADMIN}/en/tenants`);
  await page.getByRole('searchbox').fill(slug);
  await expect(page.getByRole('link', { name })).toBeVisible();
});
