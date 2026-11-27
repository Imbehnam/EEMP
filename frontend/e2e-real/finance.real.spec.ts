import { expect, test } from '@playwright/test';

import { ADMIN, api, apiLogin, PASSWORDS, signIn, unique, WEB } from './helpers';

/**
 * Milestone 3 finance + EEMP-011 workflow against the REAL API (Development seed on SQLite):
 * INV-000001 for S0001 Sara Ahmadi (guardian.one / student.one), 12,000,000 IRR in two installments, R-000001 5,000,000 cash;
 * finance.officer (FinanceOfficer, tenant-wide); fake gateway enabled; sample workflow request "Sample request" (North,
 * requested by finance.officer) in branch.manager's inbox. Specs share the database: serial, amounts checked as deltas.
 */
test.describe.configure({ mode: 'serial' });

interface Paged<T> { items: T[] }
interface Role { id: string; code: string }
interface InvoiceSummary { id: string; number: string | null; studentNumber: string; balance: number; status: string }
interface Settings { rowVersion: string; blockResultsOnOverdue: boolean; [key: string]: unknown }

const officer = () => apiLogin('demo', 'finance.officer', PASSWORDS.demoUser);

async function invoice1() {
  const o = await officer();
  const list = await api<Paged<InvoiceSummary>>(o.accessToken, 'GET', '/finance/invoices?search=INV-000001');
  const inv = list.body.items.find((i) => i.number === 'INV-000001')!;
  expect(inv, 'seeded INV-000001').toBeTruthy();
  return inv;
}

async function setResultsBlocking(on: boolean) {
  const o = await officer();
  const current = await api<Settings>(o.accessToken, 'GET', '/finance/settings');
  const { currency: _c, availableGateways: _g, ...body } = current.body;
  const saved = await api(o.accessToken, 'PUT', '/finance/settings', { ...body, blockResultsOnOverdue: on });
  expect(saved.status).toBe(200);
}

test('results blocking: portal grades are withheld while the seeded invoice is overdue (staff unaffected)', async ({ page }) => {
  await setResultsBlocking(true);
  try {
    await signIn(page, WEB, 'en', 'demo', 'student.one', PASSWORDS.demoUser);
    await page.goto(`${WEB}/en/grades`);
    await expect(page.getByRole('heading', { name: 'Results withheld' })).toBeVisible();
    await page.getByTestId('results-blocked-invoices').click();
    await page.waitForURL(`${WEB}/en/invoices`);
    await expect(page.getByRole('link', { name: 'INV-000001' })).toBeVisible();
  } finally {
    await setResultsBlocking(false);
  }
});

test('officer records a cash payment in the console and gets the next receipt number', async ({ page }) => {
  const before = await invoice1();
  await signIn(page, ADMIN, 'en', 'demo', 'finance.officer', PASSWORDS.demoUser);
  await page.goto(`${ADMIN}/en/finance/invoices/${before.id}`);
  await expect(page.getByTestId('invoice-balance')).toHaveAttribute('data-amount', String(before.balance));
  await page.getByRole('link', { name: 'Record payment' }).click();
  await expect(page.locator('select[name="invoiceId"]')).toHaveValue(before.id);
  await page.locator('select[name="method"]').selectOption('pos');
  await page.locator('input[name="amount"]').fill('2,000,000');
  await page.locator('input[name="reference"]').fill(`POS-${unique()}`);
  await page.getByRole('button', { name: 'Record payment' }).click();
  await page.waitForURL(/\/en\/finance\/payments\/[0-9a-f-]+\?notice=recorded/);
  const receipt = (await page.getByTestId('receipt-number').textContent())!;
  expect(receipt).toMatch(/^R-\d{6}$/);
  expect(receipt).not.toBe('R-000001');
  await expect(page.getByTestId('payment-recorded')).toContainText(`Receipt ${receipt}.`);
  const download = page.waitForEvent('download');
  await page.getByTestId('receipt-pdf').click();
  const file = await download;
  expect(file.suggestedFilename()).toBe(`receipt-${receipt}.pdf`);
  const after = await invoice1();
  expect(after.balance).toBe(before.balance - 2_000_000);
});

test('guardian sees the invoice and pays the balance online through the fake gateway; the balance updates', async ({ page }) => {
  const before = await invoice1();
  expect(before.balance).toBeGreaterThan(0);
  await signIn(page, WEB, 'fa', 'demo', 'guardian.one', PASSWORDS.demoUser);
  await page.goto(`${WEB}/fa/invoices`);
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByTestId('portal-balance')).toHaveAttribute('data-amount', String(before.balance));
  await expect(page.getByTestId('portal-balance')).toHaveText(/^[۰-۹٬]+ ریال$/);
  await page.getByRole('link', { name: 'INV-000001' }).click();
  await page.getByTestId('pay-balance').click();
  await page.getByRole('dialog').getByRole('button', { name: 'ادامه و پرداخت' }).click();
  await page.waitForURL(/\/fa\/pay\/fake\?paymentId=/);
  await page.getByTestId('gateway-pay').click();
  await page.waitForURL(/\/fa\/pay\/result\?paymentId=/);
  await expect(page.getByTestId('payment-result')).toHaveAttribute('data-status', 'succeeded');
  await expect(page.getByRole('status')).toContainText(/R-[0-9]{6}/);
  const download = page.waitForEvent('download');
  await page.getByTestId('result-receipt').click();
  expect((await download).suggestedFilename()).toMatch(/^receipt-R-\d{6}\.pdf$/);
  await page.goto(`${WEB}/fa/invoices`);
  await expect(page.getByTestId('portal-balance')).toHaveAttribute('data-amount', '0');
  const after = await invoice1();
  expect(after.balance).toBe(0);
  expect(after.status).toBe('paid');
});

test('discount maker-checker: the maker sees no approve action; a second officer approves; the invoice is issued', async ({ page }) => {
  // A second FinanceOfficer (checker), created by the tenant admin through the API.
  const admin = await apiLogin('demo', 'demo.admin', PASSWORDS.demoAdmin);
  const roles = await api<Role[]>(admin.accessToken, 'GET', '/roles');
  const financeRole = roles.body.find((r) => r.code === 'FinanceOfficer')!;
  const checkerName = `checker.${unique()}`;
  const created = await api<{ id: string }>(admin.accessToken, 'POST', '/users', {
    userName: checkerName, displayName: 'Finance Checker', email: null, phone: null, password: 'Checker1234', locale: 'en', timeZone: null,
  });
  expect(created.status).toBeLessThan(300);
  expect((await api(admin.accessToken, 'PUT', `/users/${created.body.id}/assignments`, [{ roleId: financeRole.id, branchId: null }])).status).toBeLessThan(300);

  await signIn(page, ADMIN, 'en', 'demo', 'finance.officer', PASSWORDS.demoUser);
  await page.goto(`${ADMIN}/en/finance/invoices/new`);
  await page.getByRole('searchbox').first().fill('S0002');
  await page.locator('select[name="studentId"]').selectOption({ label: 'Ali Karimi (S0002)' });
  await page.getByRole('button', { name: 'Add other charge' }).click();
  await page.locator('input[name="lines[0].description"]').fill('Workshop');
  await page.locator('input[name="lines[0].unitPrice"]').fill('۴٬۰۰۰٬۰۰۰');
  await page.getByRole('button', { name: 'Create draft' }).click();
  await page.waitForURL(/\/en\/finance\/invoices\/[0-9a-f-]+\?notice=created/);
  const invoiceUrl = page.url().split('?')[0]!;
  await expect(page.getByTestId('invoice-total')).toHaveAttribute('data-amount', '4000000');
  await page.getByRole('button', { name: 'Request discount' }).click();
  await page.locator('select[name="discountKind"]').selectOption('scholarship');
  await page.locator('input[name="discountValue"]').fill('25');
  const reason = `Merit ${unique()}`;
  await page.locator('textarea[name="reason"]').fill(reason);
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Discount requested' })).toBeVisible();
  await expect(page.getByTestId('own-request')).toBeVisible();
  await expect(page.getByTestId('discount-approve')).toHaveCount(0);
  await page.goto(`${ADMIN}/en/finance/approvals`);
  await expect(page.getByRole('row').filter({ hasText: reason }).getByTestId('own-request')).toBeVisible();

  await page.context().clearCookies();
  await signIn(page, ADMIN, 'en', 'demo', checkerName, 'Checker1234');
  await page.goto(`${ADMIN}/en/finance/approvals`);
  const row = page.getByRole('row').filter({ hasText: reason });
  await expect(row).toContainText('1,000,000 rials');
  await row.getByTestId('approval-approve').click();
  await page.getByRole('dialog').getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Approved.' })).toBeVisible();

  await page.goto(invoiceUrl);
  await expect(page.getByTestId('invoice-total')).toHaveAttribute('data-amount', '3000000');
  await page.getByRole('button', { name: 'Issue' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Issue' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(/^Invoice INV-\d{6}$/);
  const download = page.waitForEvent('download');
  await page.getByTestId('invoice-pdf').click();
  expect((await download).suggestedFilename()).toMatch(/^invoice-INV-\d{6}\.pdf$/);
});

test('workflow: branch.manager approves the seeded request from the inbox; the requester cannot see it in their inbox', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'finance.officer', PASSWORDS.demoUser);
  await page.goto(`${ADMIN}/en/workflow/inbox`);
  await expect(page.getByText('Nothing is waiting for you.')).toBeVisible();
  await page.goto(`${ADMIN}/en/workflow/requests`);
  await expect(page.getByRole('link', { name: 'Sample request' })).toBeVisible();

  await page.context().clearCookies();
  await signIn(page, ADMIN, 'en', 'demo', 'branch.manager', PASSWORDS.demoUser);
  await page.goto(`${ADMIN}/en/workflow/inbox`);
  await page.getByRole('link', { name: 'Sample request' }).click();
  await page.waitForURL(/\/en\/workflow\/instances\//);
  await expect(page.getByTestId('wf-approve')).toBeVisible();
  await page.getByTestId('wf-approve').click();
  await page.locator('textarea[name="comment"]').fill('Approved in e2e');
  await page.getByRole('dialog').getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByTestId('wf-done')).toHaveText('Approved.');
  await expect(page.getByRole('row').filter({ hasText: 'Approved in e2e' })).toBeVisible();
  await page.goto(`${ADMIN}/en/workflow/inbox`);
  await expect(page.getByRole('link', { name: 'Sample request' })).toHaveCount(0);
});
