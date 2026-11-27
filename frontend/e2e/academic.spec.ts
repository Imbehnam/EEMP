import { expect, test } from '@playwright/test';

import { ADMIN, signIn, WEB } from './helpers';

/**
 * Milestone 2 academic flows against the in-process mock backend (shared state, so this file runs serially).
 * Mock seed: MATH-7A (North) taught by teacher.sara with ST-1001 (رضا احمدی) and ST-1002 (Sara Ahmadi);
 * Quiz 1 published, Midterm draft (only ST-1001 scored).
 */
test.describe.configure({ mode: 'serial' });

const MATH_7A = 'e0000000-0000-4000-8000-000000000051';
const STUDENT_2 = 'd0000000-0000-4000-8000-000000000002';

test('fa: academic year dates are entered and shown in the Jalali calendar', async ({ page }) => {
  await signIn(page, ADMIN, 'fa', 'demo', 'demo.admin');
  await page.goto(`${ADMIN}/fa/academic/years/new`);
  await page.locator('input[name="name"]').fill(`۱۴۰۶-۱۴۰۷-${Date.now() % 1000}`);
  await page.locator('input[name="startDate"]').fill('1406/07/01');
  await page.locator('input[name="endDate"]').fill('۱۴۰۷/۰۳/۳۱');
  // The API receives ISO Gregorian dates.
  await expect(page.locator('input[name="startDate"]')).toHaveAttribute('data-iso', '2027-09-23');
  await expect(page.locator('input[name="endDate"]')).toHaveAttribute('data-iso', '2028-06-20');
  await page.locator('button[type="submit"]').click();
  await page.waitForURL(/\/fa\/academic\/years\/[0-9a-f-]+\?notice=created/);
  await expect(page.locator('input[name="startDate"]')).toHaveValue('۱۴۰۶/۰۷/۰۱');
  // An invalid Jalali date (1406 is not a leap year) is rejected client-side.
  await page.locator('input[name="endDate"]').fill('1406/12/30');
  await expect(page.getByText('یک تاریخ معتبر با قالب نشان‌داده‌شده وارد کنید.')).toBeVisible();
});

test('schedule overlaps and capacity errors are shown translated', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'demo.admin');
  await page.goto(`${ADMIN}/en/academic/classes/${MATH_7A}`);
  await expect(page.getByTestId('schedule')).toContainText('Saturday');
  await page.getByRole('button', { name: 'Edit schedule' }).click();
  await page.getByRole('button', { name: 'Add period' }).click();
  await page.getByLabel('Starts').last().fill('08:30');
  await page.getByLabel('Ends').last().fill('09:00');
  await page.getByRole('button', { name: 'Save schedule' }).click();
  await expect(page.getByText('Two periods of this class overlap.')).toBeVisible();
  await page.getByRole('button', { name: 'Cancel' }).last().click();

  await page.locator('input[name="capacity"]').fill('1');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Capacity' })).toHaveText(/Capacity can’t be lower than the number of enrolled students/);
});

test('admin takes attendance with bulk mark (idempotent save)', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'demo.admin');
  await page.goto(`${ADMIN}/en/academic/classes/${MATH_7A}?tab=attendance`);
  await page.locator('input[name="attendanceDate"]').fill('2026-11-23');
  await expect(page.locator('select[name="scheduleSlotId"]')).not.toHaveValue('');
  await page.getByRole('button', { name: 'Mark everyone present' }).click();
  await page.getByRole('button', { name: 'Save attendance' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Attendance saved.' })).toBeVisible();
  await expect(page.getByTestId('attendance-summary')).toContainText('Present: 2');
  await page.getByRole('button', { name: 'Save attendance' }).click();
  await expect(page.getByTestId('attendance-summary')).toContainText('Present: 2');
});

// The admin and web servers each hold their own in-process mock, so the cross-app flow (teacher submits in the portal,
// manager publishes in the console, guardian sees it) is covered against the real API in e2e-real/academic.real.spec.ts.
test('portal: teacher enters and submits scores; the guardian still sees published scores only', async ({ browser }) => {
  test.setTimeout(90_000);
  const teacher = await browser.newPage();
  await signIn(teacher, WEB, 'en', 'demo', 'teacher.sara');
  await teacher.goto(`${WEB}/en/classes`);
  await teacher.getByRole('link', { name: 'Mathematics 7 - A' }).click();
  await teacher.getByRole('link', { name: 'Assessments' }).click();
  await teacher.getByRole('link', { name: 'Midterm' }).click();
  // Out-of-range scores are blocked in the browser (the API also rejects them: academic.score_out_of_range).
  await teacher.getByLabel('Score for Sara Ahmadi').fill('25');
  await teacher.getByRole('button', { name: 'Save scores' }).click();
  expect(await teacher.getByLabel('Score for Sara Ahmadi').evaluate((e) => (e as HTMLInputElement).validity.valid)).toBe(false);
  await teacher.getByLabel('Score for Sara Ahmadi').fill('12');
  await teacher.locator('input[name="reason"]').fill('Paper marked');
  await teacher.getByRole('button', { name: 'Save scores' }).click();
  await expect(teacher.getByText('1 score saved.')).toBeVisible();
  await expect(teacher.getByRole('cell', { name: 'Paper marked' })).toBeVisible();
  await expect(teacher.locator('[data-action="approve"]')).toHaveCount(0); // teachers never approve or publish
  await teacher.locator('[data-action="submit"]').click();
  await teacher.getByRole('dialog').getByRole('button', { name: 'Submit for approval' }).click();
  await expect(teacher.getByTestId('assessment-status')).toHaveText('Submitted');
  await expect(teacher.getByLabel('Score for Sara Ahmadi')).toBeDisabled();

  const guardian = await browser.newPage();
  await signIn(guardian, WEB, 'en', 'demo', 'guardian.ali');
  await guardian.goto(`${WEB}/en/grades?student=${STUDENT_2}`);
  await expect(guardian.getByRole('rowheader', { name: 'Quiz 1' })).toBeVisible();
  await expect(guardian.getByRole('rowheader', { name: 'Midterm' })).toHaveCount(0);
});

test('console: scores entered, submitted, approved and published by a branch manager', async ({ page }) => {
  await signIn(page, ADMIN, 'en', 'demo', 'demo.admin');
  await page.goto(`${ADMIN}/en/academic/classes/${MATH_7A}?tab=assessments`);
  await page.getByRole('link', { name: 'Midterm' }).click();
  await page.getByLabel('Score for Sara Ahmadi').fill('12');
  await page.getByRole('button', { name: 'Save scores' }).click();
  await expect(page.getByText('1 score saved.')).toBeVisible();
  for (const [action, label, status] of [['submit', 'Submit for approval', 'Submitted'], ['approve', 'Approve', 'Approved'], ['publish', 'Publish', 'Published']]) {
    await page.locator(`[data-action="${action}"]`).click();
    await page.getByRole('dialog').getByRole('button', { name: label }).click();
    await expect(page.getByTestId('assessment-status')).toHaveText(status!);
  }
  await expect(page.locator('[data-action="reopen"]')).toBeVisible();
  await page.goto(`${ADMIN}/en/academic/classes/${MATH_7A}?tab=gradebook`);
  await expect(page.getByRole('row', { name: /Sara Ahmadi/ }).getByTestId('gradebook-total')).toContainText('13'); // (15/20·1 + 12/20·2)/3·20
});

test('student sees a Saturday-first timetable in Persian', async ({ page }) => {
  await signIn(page, WEB, 'fa', 'demo', 'student.reza');
  await page.goto(`${WEB}/fa/timetable`);
  const days = page.getByTestId('timetable').locator('h2');
  await expect(days).toHaveText(['شنبه', 'دوشنبه']);
  await expect(page.getByTestId('timetable')).toContainText('۰۸:۰۰');
});

test('academic navigation is hidden without permissions', async ({ page }) => {
  await signIn(page, WEB, 'en', 'demo', 'teacher.sara');
  await page.goto(`${WEB}/en`); // sign-in lands on the user's preferred language (fa)
  await expect(page.getByRole('link', { name: 'My classes' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Grades' })).toHaveCount(0);
});
