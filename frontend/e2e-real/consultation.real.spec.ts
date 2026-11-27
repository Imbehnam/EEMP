import { expect, test } from '@playwright/test';

import { ADMIN, api, apiLogin, PASSWORDS, signIn, unique, WEB } from './helpers';

/**
 * EEMP-012 against the real API (dev seed: consultant.one serves North with the demo booking policy minLeadHours = 0; guardian.one is
 * linked to Sara (S0001, North); student.one is Sara; branch.manager manages North bookings and support but holds no notes permission).
 * Flow: the consultant publishes a slot starting in two minutes → the guardian books it in the portal → the consultant confirms and
 * completes it with a confidential note in the admin console → the student and the branch manager cannot read the note.
 */
test.describe.configure({ mode: 'serial' });

interface Me { consultant: { id: string; branchIds: string[] } | null; canConsult: boolean }
interface PortalBooking { id: string; topic: string; status: string; startsAt: string }

test('guardian books → consultant confirms → completes with a confidential note; student and manager cannot read it', async ({ browser }) => {
  const topic = `E2E consult ${unique()}`;
  const consultant = await apiLogin('demo', 'consultant.one', PASSWORDS.demoUser);
  const me = await api<Me>(consultant.accessToken, 'GET', '/consultation/me');
  expect(me.status).toBe(200);
  expect(me.body.canConsult).toBe(true);
  const consultantId = me.body.consultant!.id;
  const branchId = me.body.consultant!.branchIds[0]!;
  // Two minutes ahead, rounded to the minute: the earliest bookable time, so it is the first offered slot.
  const start = new Date(Math.ceil((Date.now() + 120_000) / 60_000) * 60_000);
  const slot = await api<{ id: string }>(consultant.accessToken, 'POST', '/consultation/slots', {
    consultantId, branchId, startsAt: start.toISOString(), endsAt: new Date(start.getTime() + 30 * 60_000).toISOString(), mode: 'inPerson', location: 'E2E room',
  });
  expect(slot.status, JSON.stringify(slot.body)).toBe(201);

  // Guardian books it in the portal.
  const guardianCtx = await browser.newContext();
  const guardian = await guardianCtx.newPage();
  await signIn(guardian, WEB, 'en', 'demo', 'guardian.one', PASSWORDS.demoUser);
  await guardian.goto(`${WEB}/en/consultation`);
  await expect(guardian.getByTestId('consultant-option').first()).toBeVisible();
  await guardian.getByTestId('slot-option').first().click();
  await expect(guardian.getByTestId('selected-slot')).toContainText('E2E room');
  await guardian.locator('input[name="topic"]').fill(topic);
  await guardian.getByTestId('book-submit').click();
  await expect(guardian.getByTestId('booking-requested')).toBeVisible();

  const guardianApi = await apiLogin('demo', 'guardian.one', PASSWORDS.demoUser);
  const mine = await api<PortalBooking[]>(guardianApi.accessToken, 'GET', '/portal/consultation/bookings');
  const booking = mine.body.find((b) => b.topic === topic)!;
  expect(booking.status).toBe('requested');
  // The slot is taken: a second booking of it is refused by the server.
  const again = await api<{ code: string }>(guardianApi.accessToken, 'POST', '/portal/consultation/bookings', {
    studentId: (await api<{ profile: { id: string } }>((await apiLogin('demo', 'student.one', PASSWORDS.demoUser)).accessToken, 'GET', '/portal/me/profile')).body.profile.id,
    slotId: slot.body.id, topic: 'again',
  });
  expect(again.status).toBe(409);
  expect(again.body.code).toBe('consultation.slot_unavailable');

  // Consultant confirms, completes and writes a confidential note in the admin console.
  const adminCtx = await browser.newContext();
  const admin = await adminCtx.newPage();
  await signIn(admin, ADMIN, 'en', 'demo', 'consultant.one', PASSWORDS.demoUser);
  await admin.goto(`${ADMIN}/en/consultation/bookings/${booking.id}`);
  await admin.getByTestId('booking-confirm').click();
  await admin.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click();
  await expect(admin.getByTestId('booking-complete')).toBeVisible();
  await admin.getByTestId('booking-complete').click();
  await admin.getByRole('dialog').getByRole('button', { name: 'Mark completed' }).click();
  await expect(admin.locator('[data-status="completed"]').first()).toBeVisible();
  const secret = `Confidential ${unique()}`;
  await admin.locator('textarea[name="note"]').fill(secret);
  await admin.getByTestId('note-save').click();
  await expect(admin.getByTestId('notes-card')).toContainText(secret);

  // The student sees the completed session in the portal, never the note; the notes endpoint does not exist for them.
  const studentCtx = await browser.newContext();
  const studentPage = await studentCtx.newPage();
  await signIn(studentPage, WEB, 'en', 'demo', 'student.one', PASSWORDS.demoUser);
  await studentPage.goto(`${WEB}/en/consultation/bookings`);
  await expect(studentPage.getByRole('table')).toContainText(topic);
  await expect(studentPage.getByRole('table')).toContainText('Completed');
  await expect(studentPage.locator('body')).not.toContainText(secret);
  const student = await apiLogin('demo', 'student.one', PASSWORDS.demoUser);
  expect((await api(student.accessToken, 'GET', `/consultation/bookings/${booking.id}/notes`)).status).toBe(404);
  const portalJson = JSON.stringify((await api(student.accessToken, 'GET', '/portal/consultation/bookings')).body);
  expect(portalJson).not.toContain(secret);

  // Branch manager: booking metadata yes, notes no (403); tenant admin: nothing (404).
  const manager = await apiLogin('demo', 'branch.manager', PASSWORDS.demoUser);
  expect((await api(manager.accessToken, 'GET', `/consultation/bookings/${booking.id}`)).status).toBe(200);
  const managerNotes = await api<{ code: string }>(manager.accessToken, 'GET', `/consultation/bookings/${booking.id}/notes`);
  expect(managerNotes.status).toBe(403);
  expect(managerNotes.body.code).toBe('consultation.notes_forbidden');
  const tenantAdmin = await apiLogin('demo', 'demo.admin', PASSWORDS.demoAdmin);
  expect((await api(tenantAdmin.accessToken, 'GET', `/consultation/bookings/${booking.id}`)).status).toBe(404);
  const managerCtx = await browser.newContext();
  const managerPage = await managerCtx.newPage();
  await signIn(managerPage, ADMIN, 'en', 'demo', 'branch.manager', PASSWORDS.demoUser);
  await managerPage.goto(`${ADMIN}/en/consultation/bookings/${booking.id}`);
  await expect(managerPage.getByTestId('notes-hidden')).toBeVisible();
  await expect(managerPage.locator('body')).not.toContainText(secret);

  await Promise.all([guardianCtx.close(), adminCtx.close(), studentCtx.close(), managerCtx.close()]);
});

test('support ticket round trip: guardian asks, branch staff reply with an internal note, guardian sees only the reply', async ({ browser }) => {
  const subject = `E2E ticket ${unique()}`;
  const guardianCtx = await browser.newContext();
  const guardian = await guardianCtx.newPage();
  await signIn(guardian, WEB, 'en', 'demo', 'guardian.one', PASSWORDS.demoUser);
  await guardian.goto(`${WEB}/en/support`);
  await guardian.getByTestId('ticket-new').click();
  await guardian.locator('input[name="subject"]').fill(subject);
  await guardian.locator('textarea[name="body"]').fill('Is there class on Thursday?');
  await guardian.getByTestId('ticket-submit').click();
  await expect(guardian.getByRole('link', { name: subject })).toBeVisible();

  const staffCtx = await browser.newContext();
  const staff = await staffCtx.newPage();
  await signIn(staff, ADMIN, 'en', 'demo', 'branch.manager', PASSWORDS.demoUser);
  await staff.goto(`${ADMIN}/en/support`);
  await staff.getByRole('link', { name: subject }).click();
  await expect(staff.getByTestId('ticket-thread')).toContainText('Is there class on Thursday?');
  const internal = `internal ${unique()}`;
  await staff.locator('textarea[name="reply"]').fill(internal);
  await staff.locator('input[name="internal"]').check();
  await staff.getByTestId('ticket-send').click();
  await expect(staff.getByTestId('ticket-thread')).toContainText(internal);
  await staff.locator('textarea[name="reply"]').fill('No class on Thursday.');
  await staff.getByTestId('ticket-send').click();
  await expect(staff.getByTestId('ticket-thread')).toContainText('No class on Thursday.');

  await guardian.getByRole('link', { name: subject }).click();
  const thread = guardian.getByTestId('ticket-thread');
  await expect(thread).toContainText('No class on Thursday.');
  await expect(thread).toContainText('Support team');
  await expect(thread).not.toContainText(internal);
  await guardian.locator('textarea[name="reply"]').fill('Thanks!');
  await guardian.getByTestId('portal-ticket-send').click();
  await expect(thread).toContainText('Thanks!');

  // The student did not create it: not visible to them.
  const student = await apiLogin('demo', 'student.one', PASSWORDS.demoUser);
  const list = await api<{ subject: string }[]>(student.accessToken, 'GET', '/portal/support/tickets');
  expect(list.body.some((t) => t.subject === subject)).toBe(false);
  await Promise.all([guardianCtx.close(), staffCtx.close()]);
});
