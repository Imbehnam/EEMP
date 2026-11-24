import { expect, test } from '@playwright/test';

import { alertBox, PASSWORDS, signIn, WEB } from './helpers';

test('student.one sees own profile with a Jalali birth date (fa, RTL)', async ({ page }) => {
  await signIn(page, WEB, 'fa', 'demo', 'student.one', PASSWORDS.demoUser);
  await expect(page).toHaveURL(`${WEB}/fa`);
  await page.goto(`${WEB}/fa/profile`);
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByText('Sara Ahmadi')).toBeVisible();
  await expect(page.getByText('S0001')).toBeVisible();
  // 2012-03-21 = 1 Farvardin 1391
  await expect(page.getByTestId('birth-date')).toContainText('۱۳۹۱');
  await expect(page.getByTestId('birth-date')).toContainText('فروردین');
  // Student has no guardian-only page link.
  await expect(page.getByRole('link', { name: 'دانش‌آموزان من' })).toHaveCount(0);
});

test('guardian.one sees linked students (en, LTR)', async ({ page }) => {
  await signIn(page, WEB, 'en', 'demo', 'guardian.one', PASSWORDS.demoUser);
  await page.goto(`${WEB}/en/students`);
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('My students');
  const card = page.getByLabel('Sara Ahmadi');
  await expect(card).toBeVisible();
  await expect(card.getByText('S0001')).toBeVisible();
  await expect(card.getByText('Father')).toBeVisible();
  await expect(card.getByText('You are the primary contact')).toBeVisible();
  await page.goto(`${WEB}/en/profile`);
  await expect(page.getByText('Reza Ahmadi')).toBeVisible();
});

test('teacher.one sees own profile with subjects text', async ({ page }) => {
  await signIn(page, WEB, 'en', 'demo', 'teacher.one', PASSWORDS.demoUser);
  await page.goto(`${WEB}/en/profile`);
  await expect(page.getByText('Mina Rahimi')).toBeVisible();
  await expect(page.getByText('Mathematics')).toBeVisible();
});

test('an account without a person record gets the translated not-linked state in the portal', async ({ page }) => {
  // demo.admin has no person record: the profile page shows the translated not-linked state.
  await signIn(page, WEB, 'en', 'demo', 'demo.admin', PASSWORDS.demoAdmin, false);
  await page.waitForLoadState('networkidle');
  if (page.url().endsWith('/login')) {
    await expect(alertBox(page)).toBeVisible();
  } else {
    await page.goto(`${WEB}/en/profile`);
    await expect(page.getByText('No profile is linked to your account.').first()).toBeVisible();
  }
});
