import { readFileSync } from 'node:fs';

import { expect, test, type Page } from '@playwright/test';

import { ADMIN, alertBox, api, apiLogin, msg, PASSWORDS, signIn, unique, WEB } from './helpers';

/**
 * Milestone 2 academic flows against the real API and its Development seed:
 * MATH-7A (North) taught by teacher.one (Mina Rahimi) with Sara Ahmadi (S0001, linked to guardian.one) and Ali Karimi (S0002);
 * Quiz 1 published, Midterm draft. branch.manager holds every academic permission in North; demo.admin only catalog/classes.
 * The specs share one database, so they run in order.
 */
test.describe.configure({ mode: 'serial' });

interface ClassRow { id: string; code: string; schedule: { id: string; weekday: string }[] }

async function mathClass() {
  const { accessToken } = await apiLogin('demo', 'branch.manager', PASSWORDS.demoUser);
  const list = await api<{ items: ClassRow[] }>(accessToken, 'GET', '/classes?search=MATH-7A');
  const cls = list.body.items.find((c) => c.code === 'MATH-7A');
  expect(cls, 'seeded MATH-7A').toBeTruthy();
  return { manager: accessToken, cls: cls! };
}

/** Latest Saturday or Monday (MATH-7A meets then) not after today in Tehran and inside Term 1 (2026-09-23 → 2027-01-20). */
function attendanceDate() {
  const fmt = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Tehran', year: 'numeric', month: '2-digit', day: '2-digit' });
  let day = new Date(`${fmt.format(new Date())}T12:00:00Z`);
  const termEnd = new Date('2027-01-20T12:00:00Z');
  if (day > termEnd) day = termEnd;
  while (![6, 1].includes(day.getUTCDay())) day = new Date(day.getTime() - 86_400_000);
  return { iso: day.toISOString().slice(0, 10), weekday: day.getUTCDay() === 6 ? 'saturday' : 'monday' };
}

test('teacher.one marks attendance for the assigned class (bulk mark, idempotent save)', async ({ page }) => {
  const { manager, cls } = await mathClass();
  const { iso, weekday } = attendanceDate();
  await signIn(page, WEB, 'en', 'demo', 'teacher.one', PASSWORDS.demoUser);
  await page.goto(`${WEB}/en/classes`);
  await page.getByRole('link', { name: 'Mathematics 7 - A' }).click();
  await expect(page.getByRole('heading', { name: 'Attendance', level: 2 })).toBeVisible();
  await page.locator('input[name="attendanceDate"]').fill(iso);
  await expect(page.locator('select[name="scheduleSlotId"]')).not.toHaveValue('');
  await page.getByRole('button', { name: 'Mark everyone present' }).click();
  await page.getByRole('group', { name: 'Attendance for Ali Karimi' }).getByRole('radio', { name: 'Late' }).check();
  await page.getByRole('button', { name: 'Save attendance' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Attendance saved.' })).toBeVisible();
  await page.getByRole('button', { name: 'Save attendance' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Attendance saved.' })).toBeVisible();

  // Server state: one session for that date and period, Sara present, Ali late.
  const slot = cls.schedule.find((s) => s.weekday === weekday)!;
  const sheet = await api<{ summary: Record<string, number> }>(manager, 'GET', `/classes/${cls.id}/attendance?date=${iso}&scheduleSlotId=${slot.id}`);
  expect(sheet.status).toBe(200);
  expect(sheet.body.summary).toMatchObject({ present: 1, late: 1, unmarked: 0 });
  const sessions = await api<{ date: string }[]>(manager, 'GET', `/classes/${cls.id}/attendance/sessions?from=${iso}&to=${iso}`);
  expect(sessions.body.filter((s) => s.date === iso)).toHaveLength(1);
});

test('grade entry → submit (teacher) → approve and publish (branch manager) → guardian sees only published scores', async ({ browser }) => {
  const name = `Quiz ${unique()}`;
  const teacher = await browser.newPage();
  await signIn(teacher, WEB, 'en', 'demo', 'teacher.one', PASSWORDS.demoUser);
  await teacher.goto(`${WEB}/en/classes`);
  await teacher.getByRole('link', { name: 'Mathematics 7 - A' }).click();
  await teacher.getByRole('link', { name: 'Assessments' }).click();
  await teacher.getByRole('button', { name: 'New assessment' }).click();
  await teacher.locator('input[name="name"]').fill(name);
  await teacher.locator('input[name="maxScore"]').fill('20');
  await teacher.getByRole('button', { name: 'Create' }).click();
  await teacher.getByRole('link', { name }).click();
  await teacher.waitForURL(/\/assessments\/[0-9a-f-]+$/);
  const assessmentId = teacher.url().split('/').pop()!;
  await teacher.getByLabel('Score for Sara Ahmadi').fill('19');
  await teacher.getByLabel('Score for Ali Karimi').fill('14.5');
  await teacher.getByRole('button', { name: 'Save scores' }).click();
  await expect(teacher.getByText('2 scores saved.')).toBeVisible();
  await expect(teacher.locator('[data-action="approve"]')).toHaveCount(0);
  await teacher.locator('[data-action="submit"]').click();
  await teacher.getByRole('dialog').getByRole('button', { name: 'Submit for approval' }).click();
  await expect(teacher.getByTestId('assessment-status')).toHaveText('Submitted');

  // Not published yet: the guardian does not see it (nor the seeded draft Midterm).
  const guardian = await browser.newPage();
  await signIn(guardian, WEB, 'en', 'demo', 'guardian.one', PASSWORDS.demoUser);
  await guardian.goto(`${WEB}/en/grades`);
  await expect(guardian.getByRole('rowheader', { name: 'Quiz 1', exact: true })).toBeVisible();
  await expect(guardian.getByRole('rowheader', { name })).toHaveCount(0);
  await expect(guardian.getByRole('rowheader', { name: 'Midterm' })).toHaveCount(0);

  const manager = await browser.newPage();
  await signIn(manager, ADMIN, 'en', 'demo', 'branch.manager', PASSWORDS.demoUser);
  await manager.goto(`${ADMIN}/en/academic/assessments/${assessmentId}`);
  await manager.locator('[data-action="approve"]').click();
  await manager.getByRole('dialog').getByRole('button', { name: 'Approve' }).click();
  await expect(manager.getByTestId('assessment-status')).toHaveText('Approved');
  await manager.locator('[data-action="publish"]').click();
  await manager.getByRole('dialog').getByRole('button', { name: 'Publish' }).click();
  await expect(manager.getByTestId('assessment-status')).toHaveText('Published');
  await expect(manager.getByRole('cell', { name: 'Published' }).first()).toBeVisible(); // grade history

  await guardian.reload();
  const row = guardian.getByRole('row', { name: new RegExp(name) });
  await expect(row).toBeVisible();
  await expect(row.getByRole('cell', { name: '19 / 20' })).toBeVisible();
  await expect(guardian.getByRole('rowheader', { name: 'Midterm' })).toHaveCount(0);
});

test('guardian cannot read an unlinked student; tenant admin sees no grade data', async ({ page }) => {
  const { manager, cls } = await mathClass();
  const roster = await api<{ studentId: string; studentNumber: string }[]>(manager, 'GET', `/classes/${cls.id}/roster`);
  const ali = roster.body.find((r) => r.studentNumber === 'S0002')!;
  const { accessToken: guardian } = await apiLogin('demo', 'guardian.one', PASSWORDS.demoUser);
  expect((await api(guardian, 'GET', `/portal/me/students/${ali.studentId}/grades`)).status).toBe(404);
  const { accessToken: admin } = await apiLogin('demo', 'demo.admin', PASSWORDS.demoAdmin);
  expect((await api(admin, 'GET', `/classes/${cls.id}/gradebook`)).status).toBe(403);

  // The UI hides what the role cannot use (UX only; the API above is the authority).
  await signIn(page, ADMIN, 'en', 'demo', 'demo.admin', PASSWORDS.demoAdmin);
  await page.goto(`${ADMIN}/en/academic/classes/${cls.id}`);
  await expect(page.getByRole('heading', { name: 'Mathematics 7 - A', level: 1 })).toBeVisible();
  const sections = page.getByRole('navigation', { name: 'Class sections' });
  await expect(sections.getByRole('link', { name: 'Overview' })).toBeVisible();
  await expect(sections.getByRole('link', { name: 'Assessments' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Report cards' })).toHaveCount(0);
});

test('fa: a term is entered with Jalali dates and an overlap is reported in Persian', async ({ page }) => {
  await signIn(page, ADMIN, 'fa', 'demo', 'demo.admin', PASSWORDS.demoAdmin);
  await page.goto(`${ADMIN}/fa/academic/years`);
  await page.getByRole('link', { name: '1405-1406' }).click();
  await page.getByRole('button', { name: 'افزودن نیم‌سال' }).click();
  await page.locator('input[name="termName"]').fill(`Extra ${unique()}`);
  await page.locator('input[name="termStart"]').fill('1405/08/01');
  await page.locator('input[name="termEnd"]').fill('۱۴۰۵/۰۸/۱۰');
  await expect(page.locator('input[name="termStart"]')).toHaveAttribute('data-iso', '2026-10-23');
  await page.getByRole('form', { name: 'افزودن نیم‌سال' }).getByRole('button', { name: 'ذخیره', exact: true }).click();
  await expect(alertBox(page).filter({ hasText: msg('fa', 'errors.academic.term_overlap') })).toBeVisible();
  // Existing terms are listed with Jalali dates (Term 1 starts 1 Mehr 1405).
  await expect(page.getByRole('row', { name: /Term 1/ })).toContainText('مهر ۱۴۰۵');
});

test('report cards: branch manager generates and publishes; guardian opens the published card', async ({ browser }) => {
  const manager = await browser.newPage();
  await signIn(manager, ADMIN, 'en', 'demo', 'branch.manager', PASSWORDS.demoUser);
  await manager.goto(`${ADMIN}/en/academic/report-cards`);
  await expect(manager.locator('select[name="term"]')).not.toHaveValue('');
  await manager.getByRole('button', { name: 'Generate' }).click();
  await manager.getByRole('dialog').getByRole('button', { name: 'Generate' }).click();
  await expect(manager.getByRole('status').filter({ hasText: /Created \d+/ })).toBeVisible();
  await expect(manager.getByRole('link', { name: 'Sara Ahmadi' })).toBeVisible();
  await manager.getByRole('button', { name: 'Publish all drafts' }).click();
  await manager.getByRole('dialog').getByRole('button', { name: 'Publish all drafts' }).click();
  await expect(manager.getByRole('status').filter({ hasText: /published/ })).toBeVisible();

  const guardian = await browser.newPage();
  await signIn(guardian, WEB, 'en', 'demo', 'guardian.one', PASSWORDS.demoUser);
  await guardian.goto(`${WEB}/en/report-cards`);
  await guardian.getByRole('link', { name: 'Term 1' }).click();
  await expect(guardian.getByRole('heading', { name: 'Sara Ahmadi', level: 2 })).toBeVisible();
  await expect(guardian.getByText('(Mathematics 7 - A)')).toBeVisible();
  await expect(guardian.getByTestId('rc-average')).not.toHaveText('—');
});

/** Clicks a download button and returns the saved file's name and bytes. */
async function downloadFrom(page: Page, testId: string) {
  const [download] = await Promise.all([page.waitForEvent('download'), page.getByTestId(testId).click()]);
  return { name: download.suggestedFilename(), bytes: readFileSync((await download.path())!) };
}

test('report-card PDFs: manager downloads a version and the class ZIP; guardian downloads the published card (fa)', async ({ browser }) => {
  // Runs after the publish spec above (serial): Term 1 cards are published.
  const manager = await browser.newPage();
  await signIn(manager, ADMIN, 'en', 'demo', 'branch.manager', PASSWORDS.demoUser);
  await manager.goto(`${ADMIN}/en/academic/report-cards`);
  await expect(manager.locator('select[name="term"]')).not.toHaveValue('');
  const { cls } = await mathClass();
  await manager.locator('select[name="class"]').selectOption(cls.id);
  const zip = await downloadFrom(manager, 'rc-archive');
  expect(zip.name).toMatch(/^report-cards-[0-9a-f]{8}\.zip$/);
  expect(zip.bytes.subarray(0, 4).toString('latin1')).toBe('PK\u0003\u0004');
  expect(zip.bytes.toString('latin1')).toContain('S0001-v');

  await manager.getByRole('link', { name: 'Sara Ahmadi' }).click();
  const pdf = await downloadFrom(manager, 'rc-pdf');
  expect(pdf.name).toMatch(/^report-card-[0-9a-f]{8}-v\d+\.pdf$/);
  expect(pdf.bytes.subarray(0, 5).toString('latin1')).toBe('%PDF-');
  expect(pdf.bytes.length).toBeGreaterThan(5_000); // embedded font subset + content

  const guardian = await browser.newPage();
  await signIn(guardian, WEB, 'fa', 'demo', 'guardian.one', PASSWORDS.demoUser);
  await guardian.goto(`${WEB}/fa/report-cards`);
  await guardian.getByRole('link', { name: 'Term 1' }).click();
  await expect(guardian.getByTestId('rc-pdf')).toHaveText('دریافت PDF');
  const portal = await downloadFrom(guardian, 'rc-pdf');
  expect(portal.name).toMatch(/^report-card-[0-9a-f]{8}-v\d+\.pdf$/);
  expect(portal.bytes.subarray(0, 5).toString('latin1')).toBe('%PDF-');
});
