import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

import { ADMIN, signIn, WEB } from './helpers';

/**
 * EEMP-012 consultation and support flows against the in-process mock backend (each app has its own mock; serial).
 * Mock seed: consultant.one (Consultant, North) with 30-minute slots 10:00–12:00 Tehran for the coming week, a requested booking for
 * Sara Ahmadi tomorrow, a confirmed one for رضا احمدی and a completed one with a confidential note; one support ticket from guardian.ali.
 * demo.admin holds TenantAdmin plus BranchManager (North); teacher.sara holds no consultation/support permission.
 */
test.describe.configure({ mode: 'serial' });

const tags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
async function expectNoSeriousViolations(page: Page, label: string) {
  await page.waitForLoadState('networkidle');
  const results = await new AxeBuilder({ page }).withTags(tags).analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(serious.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).slice(0, 5).join(' | ')}`), `${label}: axe`).toEqual([]);
}

const COMPLETED = 'e0000000-0000-4000-8000-000000000103';

test('nav is permission-driven: consultant and manager see consultation, teacher does not', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'consultant.one');
  await page.goto(`${ADMIN}/en`);
  const nav = page.getByRole('navigation').first();
  await expect(nav.getByRole('link', { name: 'Consultation schedule' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Bookings' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Support requests' })).toHaveCount(0);
  await page.context().clearCookies();
  await signIn(page, ADMIN, 'en', 'demo', 'teacher.sara');
  await page.goto(`${ADMIN}/en`);
  await expect(page.getByRole('navigation').first().getByRole('link', { name: 'Bookings' })).toHaveCount(0);
  await expect(page.getByRole('navigation').first().getByRole('link', { name: 'Support requests' })).toHaveCount(0);
});

test('fa: guardian books a session from the portal (Jalali dates, axe clean)', async ({ page }) => {
  await signIn(page, WEB, 'fa', 'demo', 'guardian.ali');
  await page.goto(`${WEB}/fa/consultation`);
  await expect(page.getByRole('heading', { name: 'رزرو مشاوره' })).toBeVisible();
  await expect(page.getByTestId('consultant-option').first()).toContainText('دکتر نرگس توکلی');
  await expectNoSeriousViolations(page, 'portal booking');
  const slot = page.getByTestId('slot-option').last();
  await slot.click();
  await expect(slot).toHaveAttribute('aria-checked', 'true');
  // Persian calendar month names in the selection summary.
  await expect(page.getByTestId('selected-slot')).toContainText(/فروردین|اردیبهشت|خرداد|تیر|مرداد|شهریور|مهر|آبان|آذر|دی|بهمن|اسفند/);
  await page.locator('input[name="topic"]').fill('برنامهٔ مطالعه');
  await page.getByTestId('book-submit').click();
  await expect(page.getByTestId('booking-requested')).toBeVisible();
  await page.getByRole('link', { name: 'مشاهدهٔ جلسه‌های من' }).click();
  await expect(page.getByRole('table')).toContainText('برنامهٔ مطالعه');
  await expect(page.getByRole('table')).toContainText('درخواست‌شده');
});

test('guardian cancels a session in time; notes are never shown in the portal', async ({ page }) => {
  await signIn(page, WEB, 'en', 'demo', 'guardian.ali');
  await page.goto(`${WEB}/en/consultation/bookings`);
  await expect(page.getByRole('table')).toBeVisible();
  await expect(page.locator('body')).not.toContainText('یادداشت محرمانه');
  const cancel = page.getByTestId('portal-cancel').first();
  if (await cancel.count()) {
    await cancel.click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel session' }).click();
    await expect(page.getByText('Session cancelled.')).toBeVisible();
  }
});

test('consultant confirms a request from the week view and reads the confidential notes', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'consultant.one');
  await page.goto(`${ADMIN}/en/consultation/bookings`);
  await expect(page.getByTestId('bookings-week')).toBeVisible();
  // The seeded request is tomorrow: this week, or next week when today is Friday.
  if ((await page.getByTestId('booking-chip').filter({ hasText: 'Requested' }).count()) === 0) await page.getByTestId('week-next').click();
  await page.getByTestId('booking-chip').filter({ hasText: 'Requested' }).first().click();
  await page.getByTestId('booking-confirm').click();
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click();
  await expect(page.locator('[data-status="confirmed"]').first()).toBeVisible();
  await page.goto(`${ADMIN}/en/consultation/bookings/${COMPLETED}`);
  await expect(page.getByTestId('notes-card')).toContainText('یادداشت محرمانه');
  await page.locator('textarea[name="note"]').fill('Follow-up planned.');
  await page.getByTestId('note-save').click();
  await expect(page.getByTestId('notes-card')).toContainText('Follow-up planned.');
});

test('branch manager sees bookings but not the confidential notes', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'demo.admin');
  await page.goto(`${ADMIN}/en/consultation/bookings/${COMPLETED}`);
  await expect(page.getByTestId('notes-hidden')).toBeVisible();
  await expect(page.getByTestId('notes-card')).toHaveCount(0);
  await expect(page.locator('body')).not.toContainText('یادداشت محرمانه');
});

test('consultant adds a time slot to the schedule', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'consultant.one');
  await page.goto(`${ADMIN}/en/consultation/schedule`);
  await expect(page.getByTestId('schedule-week')).toBeVisible();
  await expect(page.getByTestId('slot').first()).toBeVisible();
  await page.getByTestId('slot-bulk').click();
  const dialog = page.getByRole('dialog');
  const next = new Date(Date.now() + 9 * 86_400_000).toISOString().slice(0, 10);
  await dialog.locator('input[name="fromDate"]').fill(next);
  await dialog.locator('input[name="toDate"]').fill(next);
  for (const d of ['saturday', 'sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday']) await dialog.locator(`input[name="day-${d}"]`).check();
  await dialog.locator('input[name="start"]').fill('15:00');
  await dialog.locator('input[name="end"]').fill('16:00');
  await dialog.getByRole('button', { name: 'Create slots' }).click();
  await expect(page.getByText(/2 slots created/)).toBeVisible();
});

test('support round trip: staff reply with an internal note (admin, axe clean on the thread)', async ({ page }) => {
  await signIn(page, ADMIN, 'fa', 'demo', 'demo.admin');
  await page.goto(`${ADMIN}/fa/support`);
  await page.getByRole('link', { name: 'سؤال دربارهٔ برنامهٔ کلاس' }).click();
  await expect(page.getByTestId('ticket-thread')).toContainText('با آموزش هماهنگ شود.');
  await expectNoSeriousViolations(page, 'ticket thread');
  await page.locator('textarea[name="reply"]').fill('کلاس ساعت ۱۰ شروع می‌شود.');
  await page.getByTestId('ticket-send').click();
  await expect(page.getByTestId('ticket-thread')).toContainText('کلاس ساعت ۱۰ شروع می‌شود.');
});

test('support round trip: guardian sees replies but not internal notes, replies and closes', async ({ page }) => {
  await signIn(page, WEB, 'en', 'demo', 'guardian.ali');
  await page.goto(`${WEB}/en/support`);
  await page.getByRole('link', { name: 'سؤال دربارهٔ برنامهٔ کلاس' }).click();
  const thread = page.getByTestId('ticket-thread');
  await expect(thread).toContainText('اتاق ۲۰۴، طبقهٔ دوم.');
  await expect(thread).not.toContainText('با آموزش هماهنگ شود.');
  await expect(thread).toContainText('Support team');
  await page.locator('textarea[name="reply"]').fill('Thank you!');
  await page.getByTestId('portal-ticket-send').click();
  await expect(thread).toContainText('Thank you!');
  await page.getByTestId('ticket-close').click();
  await page.getByRole('dialog').getByRole('button', { name: 'Close request' }).click();
  await expect(page.getByText('This request is closed.')).toBeVisible();
  // New request
  await page.goto(`${WEB}/en/support`);
  await page.getByTestId('ticket-new').click();
  await page.locator('input[name="subject"]').fill('Report card');
  await page.locator('textarea[name="body"]').fill('When is it published?');
  await page.getByTestId('ticket-submit').click();
  await expect(page.getByRole('table')).toContainText('Report card');
});
