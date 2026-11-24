import { expect, test, type Page } from '@playwright/test';

const ADMIN = 'http://localhost:3001';
const WEB = 'http://localhost:3000';
const PASSWORD = process.env.EEMP_MOCK_PASSWORD || 'eemp-mock';

async function signIn(page: Page, base: string, locale: string, tenant: string, user: string, password = PASSWORD, expectSuccess = true) {
  await page.goto(`${base}/${locale}/login`);
  await page.locator('input[name="tenantSlug"]').fill(tenant);
  await page.locator('input[name="userName"]').fill(user);
  await page.locator('input[name="password"]').fill(password);
  await page.locator('button[type="submit"]').click();
  // Login navigates with window.location; wait for it before the test navigates again.
  if (expectSuccess) await page.waitForURL((url) => !url.pathname.endsWith('/login'));
}

test.describe('Persian browser', () => {
  test.use({ locale: 'fa-IR' });
  test('anonymous users are sent to the RTL Persian login page', async ({ page }) => {
    await page.goto(`${ADMIN}/`);
    await expect(page).toHaveURL(/\/fa\/login/);
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('html')).toHaveAttribute('lang', 'fa');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('ورود');
  });
});

test('English browsers negotiate the English LTR login page', async ({ page }) => {
  await page.goto(`${ADMIN}/`);
  await expect(page).toHaveURL(/\/en\/login/);
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
});

test('wrong credentials show a translated error (no server text)', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'demo.admin', 'not-the-password', false);
  await expect(page.getByRole('alert').filter({ hasText: 'incorrect' })).toHaveText('The institute code, username or password is incorrect.');
});

test('admin login, then switch fa → en → ar (RTL/LTR)', async ({ page }) => {
  await signIn(page, ADMIN, 'fa', 'demo', 'demo.admin');
  await expect(page).toHaveURL(`${ADMIN}/fa`);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('خوش آمدید');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

  await page.getByTestId('locale-switcher').selectOption('en');
  await expect(page).toHaveURL(`${ADMIN}/en`);
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Welcome');

  await page.getByTestId('locale-switcher').selectOption('ar');
  await expect(page).toHaveURL(`${ADMIN}/ar`);
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('مرحبًا');
});

test('admin roles screen marks sensitive permissions', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'demo.admin');
  await page.goto(`${ADMIN}/en/roles`);
  await page.getByRole('link', { name: 'Branch manager' }).click();
  await expect(page.locator('input[data-permission="students.read"]')).toBeChecked();
  const row = page.getByRole('row', { name: /Students/ });
  await expect(row.getByText('Sensitive').first()).toBeVisible();
});

test('student portal shows own profile with a Jalali birth date', async ({ page }) => {
  await signIn(page, WEB, 'fa', 'demo', 'student.reza');
  await expect(page).toHaveURL(`${WEB}/fa`);
  await page.goto(`${WEB}/fa/profile`);
  await expect(page.getByTestId('birth-date')).toContainText('۱۳۹۱');
  await expect(page.getByTestId('birth-date')).toContainText('فروردین');
});

test('guardian sees linked students in English (LTR)', async ({ page }) => {
  await signIn(page, WEB, 'en', 'demo', 'guardian.ali');
  await page.goto(`${WEB}/en/students`);
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('My students');
  await expect(page.getByText('ST-1001')).toBeVisible();
  await expect(page.getByText('ST-1002')).toBeVisible();
});
