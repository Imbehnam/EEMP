import { beforeEach, describe, expect, it } from 'vitest';

import { CONSULTATION_IDS } from '../src/mock/consultation';
import { createMockBackend, DEFAULT_MOCK_PASSWORD, MOCK_API_BASE_URL, MOCK_IDS } from '../src/mock';
import type {
  BookingDetail, ConsultationMe, ConsultationNote, CurrentUser, Paged, PortalBooking, PortalConsultant, PortalSlot, Slot, TicketDetail, TicketSummary, TokenResponse,
} from '../src';

let backend: ReturnType<typeof createMockBackend>;

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const call = async <T = any>(method: string, path: string, token: string | null, body?: unknown) => {
  const response = await backend.fetch(`${MOCK_API_BASE_URL}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  return { status: response.status, body: (text ? JSON.parse(text) : null) as T };
};

const login = async (userName: string) => {
  const r = await backend.fetch(`${MOCK_API_BASE_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tenantSlug: 'demo', userName, password: DEFAULT_MOCK_PASSWORD }),
  });
  expect(r.status).toBe(200);
  return ((await r.json()) as TokenResponse).accessToken;
};

beforeEach(() => {
  backend = createMockBackend();
});

describe('mock consultation: permissions', () => {
  it('tenant admin role holds no consultation/support code; the consultant has the sessions permission and a profile', async () => {
    const consultant = await login('consultant.one');
    const me = await call<ConsultationMe>('GET', '/consultation/me', consultant);
    expect(me.body.canConsult).toBe(true);
    expect(me.body.consultant?.id).toBe(CONSULTATION_IDS.consultant);
    const user = await call<CurrentUser>('GET', '/auth/me', consultant);
    expect(user.body.permissions).toContain('consultation.sessions.manage');
    const teacher = await login('teacher.sara');
    expect((await call('GET', '/consultation/bookings', teacher)).status).toBe(403);
    expect((await call('GET', '/support/tickets', teacher)).status).toBe(403);
  });
});

describe('mock consultation: booking flow', () => {
  it('guardian books for a linked student; the consultant confirms; notes stay confidential', async () => {
    const guardian = await login('guardian.ali');
    const consultants = await call<PortalConsultant[]>('GET', `/portal/consultation/consultants?studentId=${MOCK_IDS.student2}`, guardian);
    expect(consultants.body.map((c) => c.id)).toContain(CONSULTATION_IDS.consultant);
    const slots = await call<PortalSlot[]>('GET', `/portal/consultation/slots?studentId=${MOCK_IDS.student2}`, guardian);
    expect(slots.status).toBe(200);
    const slot = slots.body.at(-1)!;
    const booked = await call<PortalBooking>('POST', '/portal/consultation/bookings', guardian, { studentId: MOCK_IDS.student2, slotId: slot.id, topic: 'Study plan' });
    expect(booked.status).toBe(201);
    expect(booked.body.status).toBe('requested');
    expect(booked.body).not.toHaveProperty('notes');
    // The same slot cannot be booked twice.
    const again = await call('POST', '/portal/consultation/bookings', guardian, { studentId: MOCK_IDS.student2, slotId: slot.id, topic: 'Again' });
    expect(again.status).toBe(409);
    expect(again.body.code).toBe('consultation.slot_unavailable');
    // Not linked → not found.
    const other = await call('POST', '/portal/consultation/bookings', guardian, { studentId: MOCK_IDS.student3, slotId: slot.id, topic: 'x' });
    expect(other.status).toBe(404);

    const consultant = await login('consultant.one');
    const detail = await call<BookingDetail>('GET', `/consultation/bookings/${booked.body.id}`, consultant);
    expect(detail.body.allowedActions).toEqual(['confirm', 'cancel']);
    const confirmed = await call<BookingDetail>('POST', `/consultation/bookings/${booked.body.id}/confirm`, consultant, { rowVersion: detail.body.booking.rowVersion });
    expect(confirmed.body.booking.status).toBe('confirmed');
    const early = await call('POST', `/consultation/bookings/${booked.body.id}/complete`, consultant, { rowVersion: confirmed.body.booking.rowVersion });
    expect(early.body.code).toBe('consultation.session_not_started');

    // Branch manager (demo.admin holds BranchManager in North) sees metadata, not notes; the student has no staff access.
    const manager = await login('demo.admin');
    expect((await call<BookingDetail>('GET', `/consultation/bookings/${CONSULTATION_IDS.bookingCompleted}`, manager)).body.canReadNotes).toBe(false);
    const notes = await call('GET', `/consultation/bookings/${CONSULTATION_IDS.bookingCompleted}/notes`, manager);
    expect(notes.status).toBe(403);
    expect(notes.body.code).toBe('consultation.notes_forbidden');
    const student = await login('student.reza');
    expect((await call('GET', `/consultation/bookings/${CONSULTATION_IDS.bookingCompleted}/notes`, student)).status).toBe(404);
    const ownNotes = await call<ConsultationNote[]>('GET', `/consultation/bookings/${CONSULTATION_IDS.bookingCompleted}/notes`, consultant);
    expect(ownNotes.body).toHaveLength(1);
    // The portal list never carries notes.
    const portal = await call<PortalBooking[]>('GET', '/portal/consultation/bookings', student);
    expect(JSON.stringify(portal.body)).not.toContain('محرمانه');
  });

  it('enforces the cancellation window for families but not for staff', async () => {
    const guardian = await login('guardian.ali');
    const tomorrow = (await call<PortalBooking[]>('GET', '/portal/consultation/bookings', guardian)).body.find((b) => b.id === CONSULTATION_IDS.bookingRequested)!;
    const cancel = await call('POST', `/portal/consultation/bookings/${tomorrow.id}/cancel`, guardian, { rowVersion: tomorrow.rowVersion });
    if (tomorrow.canCancel) expect(cancel.status).toBe(200);
    else expect(cancel.body.code).toBe('consultation.cancellation_window_passed');
    const manager = await login('demo.admin');
    const detail = await call<BookingDetail>('GET', `/consultation/bookings/${CONSULTATION_IDS.bookingConfirmed}`, manager);
    const staff = await call<BookingDetail>('POST', `/consultation/bookings/${CONSULTATION_IDS.bookingConfirmed}/cancel`, manager, { rowVersion: detail.body.booking.rowVersion, reason: 'Sick' });
    expect(staff.body.booking.status).toBe('cancelled');
    expect(staff.body.booking.cancelledBy).toBe('staff');
  });

  it('lets the consultant publish a weekly pattern and lists slots with booking summaries', async () => {
    const consultant = await login('consultant.one');
    const day = new Date(Date.now() + 10 * 86_400_000).toISOString().slice(0, 10);
    const bulk = await call('POST', '/consultation/slots/bulk', consultant, {
      consultantId: CONSULTATION_IDS.consultant, branchId: MOCK_IDS.branchNorth, fromDate: day, toDate: day,
      weekdays: ['saturday', 'sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday'], startTime: '14:00', endTime: '16:00', durationMinutes: 60, mode: 'online',
    });
    expect(bulk.status).toBe(200);
    expect(bulk.body.created).toBe(2);
    const listed = await call<Slot[]>('GET', `/consultation/slots?from=${new Date(Date.now() - 86_400_000).toISOString()}&to=${new Date(Date.now() + 7 * 86_400_000).toISOString()}`, consultant);
    expect(listed.body.some((s) => s.booking?.id === CONSULTATION_IDS.bookingRequested)).toBe(true);
    const south = await call('POST', '/consultation/slots', consultant, {
      consultantId: CONSULTATION_IDS.consultant, branchId: MOCK_IDS.branchSouth, startsAt: new Date(Date.now() + 86_400_000).toISOString(),
      endsAt: new Date(Date.now() + 86_400_000 + 1_800_000).toISOString(), mode: 'inPerson',
    });
    expect(south.status).toBe(403);
  });
});

describe('mock support tickets', () => {
  it('round-trips a ticket and hides internal notes from the portal', async () => {
    const guardian = await login('guardian.ali');
    const created = await call<TicketDetail>('POST', '/portal/support/tickets', guardian, { studentId: MOCK_IDS.student2, subject: 'Fees', category: 'finance', body: 'Question' });
    expect(created.status).toBe(201);
    const manager = await login('demo.admin');
    const inbox = await call<Paged<TicketSummary>>('GET', '/support/tickets', manager);
    expect(inbox.body.items.map((t) => t.id)).toContain(created.body.ticket.id);
    await call('POST', `/support/tickets/${created.body.ticket.id}/messages`, manager, { body: 'internal', isInternal: true });
    const replied = await call<TicketDetail>('POST', `/support/tickets/${created.body.ticket.id}/messages`, manager, { body: 'Answer', isInternal: false });
    expect(replied.body.ticket.status).toBe('waitingOnRequester');
    const view = await call<TicketDetail>('GET', `/portal/support/tickets/${created.body.ticket.id}`, guardian);
    expect(view.body.messages.map((m) => m.body)).toEqual(['Question', 'Answer']);
    expect(view.body.messages[1]!.authorName).toBe('');
    const south = await login('manager.south');
    expect((await call('GET', `/support/tickets/${created.body.ticket.id}`, south)).status).toBe(404);
    const closed = await call<TicketDetail>('POST', `/portal/support/tickets/${created.body.ticket.id}/close`, guardian, { rowVersion: view.body.ticket.rowVersion });
    expect(closed.body.ticket.status).toBe('closed');
    expect((await call('POST', `/portal/support/tickets/${created.body.ticket.id}/messages`, guardian, { body: 'more' })).body.code).toBe('support.ticket_closed');
  });
});
