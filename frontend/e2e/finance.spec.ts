import { expect, test } from '@playwright/test';

import { ADMIN, signIn, WEB } from './helpers';

/**
 * Milestone 3 finance and workflow flows against the in-process mock backend (shared state: serial).
 * Each app process has its own mock, so admin and portal flows do not see each other's changes; the cross-app flows
 * (officer payment → guardian balance, results blocking) run against the real API in e2e-real/finance.real.spec.ts.
 * Mock seed: INV-000001 (ST-1001, رضا احمدی) 12,000,000 IRR in two installments, R-000001 5,000,000 cash;
 * finance.officer and finance.checker hold FinanceOfficer tenant-wide; demo.admin approves workflow steps in North.
 */
test.describe.configure({ mode: 'serial' });

const INVOICE_1 = 'f0000000-0000-4000-8000-000000000021';

test('nav is permission-driven: officer sees Finance, tenant admin does not', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'finance.officer');
  const nav = page.getByRole('navigation').first();
  await expect(nav.getByRole('link', { name: 'Invoices' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Finance settings' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Workflow definitions' })).toHaveCount(0);
  await page.context().clearCookies();
  await signIn(page, ADMIN, 'en', 'demo', 'demo.admin');
  await page.goto(`${ADMIN}/en`); // demo.admin prefers fa
  await expect(page.getByRole('navigation').first().getByRole('link', { name: 'Invoices' })).toHaveCount(0);
  await expect(page.getByRole('navigation').first().getByRole('link', { name: 'Workflow definitions' })).toBeVisible();
});

test('officer records a cash payment and gets a receipt number and PDF', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'finance.officer');
  await page.goto(`${ADMIN}/en/finance/invoices/${INVOICE_1}`);
  // Seed: 7,000,000 rials due (less when the server already ran this spec).
  await expect(page.getByTestId('invoice-balance')).toHaveText(/^[\d,]+ rials$/);
  const before = Number(await page.getByTestId('invoice-balance').getAttribute('data-amount'));
  await page.getByRole('link', { name: 'Record payment' }).click();
  await page.waitForURL(/\/en\/finance\/payments\/new\?studentId=/);
  await expect(page.locator('select[name="invoiceId"]')).toHaveValue(INVOICE_1);
  await page.locator('input[name="amount"]').fill('2,000,000');
  await page.getByRole('button', { name: 'Record payment' }).click();
  await page.waitForURL(/\/en\/finance\/payments\/[0-9a-f-]+\?notice=recorded/);
  await expect(page.getByTestId('payment-recorded')).toContainText(/Receipt R-\d{6}\./);
  const receipt = (await page.getByTestId('receipt-number').textContent())!;
  expect(receipt).toMatch(/^R-\d{6}$/);
  const download = page.waitForEvent('download');
  await page.getByTestId('receipt-pdf').click();
  expect((await download).suggestedFilename()).toBe(`receipt-${receipt}.pdf`);
  await page.goto(`${ADMIN}/en/finance/invoices/${INVOICE_1}`);
  await expect(page.getByTestId('invoice-balance')).toHaveAttribute('data-amount', String(before - 2_000_000));
});

test('discount maker-checker: the maker cannot approve; a second officer approves; then the invoice is issued', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'finance.officer');
  await page.goto(`${ADMIN}/en/finance/invoices/new`);
  await page.getByRole('searchbox').first().fill('Sara');
  await page.locator('select[name="studentId"]').selectOption({ label: 'Sara Ahmadi (ST-1002)' });
  await page.getByRole('button', { name: 'Add fee item' }).click();
  await page.locator('select[name="lines[0].feeItemId"]').selectOption({ label: 'Tuition - Term 1' });
  await page.getByRole('button', { name: 'Create draft' }).click();
  await page.waitForURL(/\/en\/finance\/invoices\/[0-9a-f-]+\?notice=created/);
  const invoiceUrl = page.url().split('?')[0]!;
  await expect(page.getByTestId('invoice-total')).toHaveText('12,000,000 rials');

  await page.getByRole('button', { name: 'Request discount' }).click();
  await page.locator('select[name="discountKind"]').selectOption('scholarship');
  await page.locator('input[name="discountValue"]').fill('25');
  await page.locator('textarea[name="reason"]').fill('Merit scholarship');
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Discount requested' })).toBeVisible();
  // UX hint only (the API answers 403 finance.maker_checker): no approve button on one's own request.
  await expect(page.getByTestId('own-request')).toBeVisible();
  await expect(page.getByTestId('discount-approve')).toHaveCount(0);

  await page.context().clearCookies();
  await signIn(page, ADMIN, 'en', 'demo', 'finance.checker');
  await page.goto(`${ADMIN}/en/finance/approvals`);
  const row = page.getByRole('row').filter({ hasText: 'Merit scholarship' });
  await expect(row).toContainText('3,000,000 rials');
  await row.getByTestId('approval-approve').click();
  await page.getByRole('dialog').getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Approved.' })).toBeVisible();

  await page.goto(invoiceUrl);
  await expect(page.getByTestId('invoice-total')).toHaveText('9,000,000 rials');
  await page.getByRole('button', { name: 'Issue' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Issue' }).click();
  await expect(page.getByRole('status').filter({ hasText: /Invoice INV-\d{6} issued\./ })).toBeVisible();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(/Invoice INV-\d{6}/);
});

test('fa: guardian sees the invoice in rials with Persian digits and pays online through the fake gateway', async ({ page }) => {
  await signIn(page, WEB, 'fa', 'demo', 'guardian.ali');
  await page.goto(`${WEB}/fa/invoices`);
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByTestId('portal-balance')).toHaveText(/^[۰-۹٬]+ ریال$/);
  await page.getByRole('link', { name: 'INV-000001' }).click();
  await page.waitForURL(new RegExp(`/fa/invoices/${INVOICE_1}`));
  await page.getByTestId('pay-balance').click();
  await expect(page.getByRole('dialog')).toContainText(' ریال');
  await page.getByRole('dialog').getByRole('button', { name: 'ادامه و پرداخت' }).click();
  await page.waitForURL(/\/fa\/pay\/fake\?paymentId=/);
  await page.getByTestId('gateway-pay').click();
  await page.waitForURL(/\/fa\/pay\/result\?paymentId=/);
  await expect(page.getByTestId('payment-result')).toHaveAttribute('data-status', 'succeeded');
  await expect(page.getByRole('status')).toContainText('رسید R-');
  await page.goto(`${WEB}/fa/invoices`);
  await expect(page.getByTestId('portal-balance')).toHaveText('۰ ریال');
});

test('workflow: approver sees the request in the inbox and approves it with a comment', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'demo.admin');
  await page.goto(`${ADMIN}/en/workflow/inbox`); // explicit locale (demo.admin prefers fa)
  await page.getByRole('link', { name: 'Sample request' }).click();
  await page.waitForURL(/\/en\/workflow\/instances\//);
  // The requester never sees their own request in the inbox; demo.admin's own request is not listed.
  await page.getByTestId('wf-reject').click();
  await page.getByRole('dialog').getByRole('button', { name: 'Reject' }).click();
  await expect(page.getByRole('dialog')).toBeVisible(); // comment required: nothing sent
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByTestId('wf-approve').click();
  await page.locator('textarea[name="comment"]').fill('Looks good');
  await page.getByRole('dialog').getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByTestId('wf-done')).toHaveText('Approved.');
  await expect(page.getByRole('row').filter({ hasText: 'Looks good' })).toContainText('Approved');
  await page.goto(`${ADMIN}/en/workflow/inbox`);
  await expect(page.getByText('Nothing is waiting for you.')).toBeVisible();
});
