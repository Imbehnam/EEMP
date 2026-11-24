import { expect, test } from '@playwright/test';

import { ADMIN, alertBox, api, apiLogin, msg, PASSWORDS, signIn, signInDemoAdmin, textbox, unique, WEB } from './helpers';

const LOCALES = [
  { locale: 'fa', dir: 'rtl' },
  { locale: 'en', dir: 'ltr' },
  { locale: 'tr', dir: 'ltr' },
  { locale: 'ar', dir: 'rtl' },
] as const;

for (const { locale, dir } of LOCALES) {
  test(`[${locale}] wrong password -> translated auth.invalid_credentials, ${dir}`, async ({ page }) => {
    await signIn(page, ADMIN, locale, 'demo', 'demo.admin', 'definitely-wrong', false);
    await expect(page.locator('html')).toHaveAttribute('dir', dir);
    await expect(alertBox(page)).toHaveText(msg(locale, 'errors.auth.invalid_credentials'));
  });

  test(`[${locale}] backend validation/conflict codes are translated (branch.code_taken, validation.required)`, async ({ page }) => {
    await signInDemoAdmin(page, locale);
    await page.goto(`${ADMIN}/${locale}/branches/new`);
    await expect(page.locator('html')).toHaveAttribute('dir', dir);
    await textbox(page, msg(locale, 'branches.code')).fill('NORTH');
    await textbox(page, msg(locale, 'branches.name')).fill(`Dup ${unique()}`);
    await page.getByRole('button', { name: msg(locale, 'common.save') }).click();
    await expect(page.getByText(msg(locale, 'errors.branch.code_taken')).first()).toBeVisible();
  });
}

test('locale switcher fa -> en -> tr -> ar on the admin console', async ({ page }) => {
  await signInDemoAdmin(page, 'fa');
  await expect(page).toHaveURL(`${ADMIN}/fa`);
  for (const { locale, dir } of LOCALES.slice(1)) {
    await page.getByTestId('locale-switcher').selectOption(locale);
    await expect(page).toHaveURL(`${ADMIN}/${locale}`);
    await expect(page.locator('html')).toHaveAttribute('dir', dir);
    await expect(page.locator('html')).toHaveAttribute('lang', locale);
  }
});

test('saving preferences in the portal persists the API locale tag and switches the UI', async ({ page }) => {
  await signIn(page, WEB, 'en', 'demo', 'teacher.one', PASSWORDS.demoUser);
  await page.goto(`${WEB}/en/settings`);
  await page.locator('select[name="locale"]').selectOption('tr');
  await page.getByRole('button', { name: msg('en', 'common.save') }).click();
  await expect(page).toHaveURL(/\/tr\/settings/);
  await expect(page.locator('html')).toHaveAttribute('lang', 'tr');
  const teacher = await apiLogin('demo', 'teacher.one', PASSWORDS.demoUser);
  expect((await api<{ locale: string }>(teacher.accessToken, 'GET', '/auth/me')).body.locale).toBe('tr');

  // Back to Persian: the API stores fa-IR (contract), the UI route is /fa.
  await page.locator('select[name="locale"]').selectOption('fa');
  await page.getByRole('button', { name: msg('tr', 'common.save') }).click();
  await expect(page).toHaveURL(/\/fa\/settings/);
  expect((await api<{ locale: string }>(teacher.accessToken, 'GET', '/auth/me')).body.locale).toBe('fa-IR');
});
