import { expect, test } from '@playwright/test';

import { ADMIN, api, apiLogin, PASSWORDS, signIn, signInDemoAdmin, textbox, unique } from './helpers';

test.describe.configure({ mode: 'serial' });

test('demo.admin edits the organization (central office)', async ({ page }) => {
  await signInDemoAdmin(page);
  await page.goto(`${ADMIN}/en/organization`);
  const phone = `+98 21 ${Math.floor(1000 + Math.random() * 8999)} 0000`;
  await textbox(page, 'Legal name').fill('Demo Institute Group LLC');
  await textbox(page, 'Phone').fill(phone);
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Changes saved.' })).toBeVisible();
  await page.reload();
  await expect(textbox(page, 'Phone')).toHaveValue(phone);
  await expect(page.getByRole('combobox', { name: 'Default language' })).toHaveValue('fa');
});

test('demo.admin creates, edits and deletes a branch; duplicate code is a translated field error', async ({ page }) => {
  await signInDemoAdmin(page);
  const code = `E${unique().toUpperCase()}`.slice(0, 12);
  await page.goto(`${ADMIN}/en/branches/new`);
  await textbox(page, 'Code').fill(code);
  await textbox(page, 'Name').fill(`Branch ${code}`);
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Created successfully.' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 1 })).toContainText(`Branch ${code}`);
  const detailUrl = page.url();

  await textbox(page, 'Name').fill(`Branch ${code} (renamed)`);
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Changes saved.' })).toBeVisible();

  // Same code again -> 409 branch.code_taken, shown as a translated message (not a concurrency warning).
  await page.goto(`${ADMIN}/en/branches/new`);
  await textbox(page, 'Code').fill(code);
  await textbox(page, 'Name').fill('Duplicate');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText('This branch code is already in use.').first()).toBeVisible();
  await expect(page.getByText('Someone else changed this record')).toHaveCount(0);

  await page.goto(detailUrl);
  await page.getByRole('button', { name: 'Delete' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
  await expect(page).toHaveURL(/\/en\/branches(\?notice=deleted)?$/);
  await expect(page.getByRole('link', { name: `Branch ${code} (renamed)` })).toHaveCount(0);
});

test('demo.admin creates a role with permissions, a user, and assigns the role on one branch', async ({ page, browser }) => {
  await signInDemoAdmin(page);
  const suffix = unique();
  const roleCode = `Registrar${suffix}`;
  await page.goto(`${ADMIN}/en/roles/new`);
  await textbox(page, 'Code').fill(roleCode);
  await textbox(page, 'Name').fill(`Registrar ${suffix}`);
  await page.locator('input[data-permission="students.read"]').check();
  await page.locator('input[data-permission="guardians.read"]').check();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Created successfully.' })).toBeVisible();
  await expect(page.locator('input[data-permission="students.read"]')).toBeChecked();
  await expect(page.locator('input[data-permission="students.manage"]')).not.toBeChecked();
  // Role code is immutable after creation.
  await expect(textbox(page, 'Code')).toHaveAttribute('readonly', '');

  const userName = `registrar.${suffix}`;
  await page.goto(`${ADMIN}/en/users/new`);
  await textbox(page, 'Username').fill(userName);
  await textbox(page, 'Display name').fill(`Registrar ${suffix}`);
  await page.getByLabel('Temporary password').fill('Registrar123');
  await page.getByRole('main').getByRole('combobox', { name: 'Language' }).selectOption('fa');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Created successfully.' })).toBeVisible();

  await page.getByRole('button', { name: 'Add assignment' }).click();
  const row = page.getByRole('listitem', { name: 'Assignment 1' });
  await row.getByRole('combobox', { name: 'Role' }).selectOption({ label: `Registrar ${suffix}` });
  await row.getByRole('combobox', { name: 'Branch' }).selectOption({ label: 'North Branch' });
  await page.getByRole('button', { name: 'Save assignments' }).click();
  await expect(page.getByText('Assignments saved.')).toBeVisible();

  // The new user signs in and gets exactly that branch-scoped permission set.
  const session = await apiLogin('demo', userName, 'Registrar123');
  const me = await api<{ permissions: string[]; scopes: { roleCode: string; branchId: string | null }[]; locale: string }>(session.accessToken, 'GET', '/auth/me');
  expect(me.body.permissions).toEqual(expect.arrayContaining(['students.read', 'guardians.read']));
  expect(me.body.permissions).not.toContain('students.manage');
  expect(me.body.scopes).toEqual([{ roleCode, branchId: expect.any(String) }]);
  expect(me.body.locale).toBe('fa-IR');

  const other = await browser.newPage();
  await signIn(other, ADMIN, 'en', 'demo', userName, 'Registrar123');
  await expect(other).toHaveURL(`${ADMIN}/fa`);
  await other.close();
});

test('tenant admin cannot reach platform tenant management (UI state + API 403)', async ({ page }) => {
  await signInDemoAdmin(page);
  await page.goto(`${ADMIN}/en/tenants`);
  await expect(page.getByText(/don’t have access|wasn’t found|not found/i).first()).toBeVisible();
  const admin = await apiLogin('demo', 'demo.admin', PASSWORDS.demoAdmin);
  expect((await api(admin.accessToken, 'GET', '/platform/tenants')).status).toBe(403);
});
