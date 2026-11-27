/**
 * Mock of the EEMP-012 consultation and support API (development and tests only; NOT a security boundary).
 * Mirrors the contract: consultant profiles, availability slots, portal booking in the student's branch with one active booking per
 * slot, staff lifecycle (confirm/cancel/complete/no-show), confidential notes (own consultant or consultation.notes.read only),
 * cancellation window, and support tickets with internal notes hidden from the portal.
 */
import type {
  Booking, BookingAction, BookingStatus, Consultant, ConsultationNote, ConsultationSettings, Slot, TicketCategory, TicketMessage, TicketPriority,
  TicketStatus, TicketSummary,
} from '../consultation-types';
import { assertValid, HttpError, json, paginate, uuid } from './http';
import type { MockTenantData, MockUser } from './seed';

type MockBooking = Omit<Booking, 'consultantName' | 'studentName'>;
type MockTicket = Omit<TicketSummary, 'studentName' | 'assigneeName'> & { createdByUserId: string };
type MockMessage = Omit<TicketMessage, 'authorName' | 'mine'> & { ticketId: string; authorUserId: string };

export interface MockConsultation {
  settings: ConsultationSettings;
  consultants: Consultant[];
  slots: Omit<Slot, 'consultantName' | 'booking'>[];
  bookings: MockBooking[];
  notes: (ConsultationNote & { bookingId: string })[];
  tickets: MockTicket[];
  messages: MockMessage[];
}

export const CONSULTATION_IDS = {
  consultant: 'e0000000-0000-4000-8000-000000000001',
  bookingRequested: 'e0000000-0000-4000-8000-000000000101',
  bookingConfirmed: 'e0000000-0000-4000-8000-000000000102',
  bookingCompleted: 'e0000000-0000-4000-8000-000000000103',
  ticket: 'e0000000-0000-4000-8000-000000000201',
} as const;

const DEFAULT_SETTINGS = (): ConsultationSettings => ({ cancellationWindowHours: 24, bookingHorizonDays: 30, minLeadHours: 0, maxActiveBookingsPerStudent: 3, rowVersion: null });

export const emptyConsultation = (): MockConsultation => ({
  settings: DEFAULT_SETTINGS(), consultants: [], slots: [], bookings: [], notes: [], tickets: [], messages: [],
});

const HOUR = 3_600_000;
const iso = (ms: number) => new Date(ms).toISOString();

/**
 * Demo data relative to "now": slots 10:00–12:00 Tehran (06:30–08:30 UTC) for the next seven days, a requested booking for Sara
 * tomorrow, a confirmed one for Reza the day after, a completed session with a note yesterday and an open ticket.
 */
export function createConsultationSeed(rv: () => string, ids: {
  branchNorth: string; userConsultant: string; userGuardian: string; student1: string; student2: string; userDemoAdmin: string;
}, nowMs = Date.now()): MockConsultation {
  const day0 = Date.UTC(new Date(nowMs).getUTCFullYear(), new Date(nowMs).getUTCMonth(), new Date(nowMs).getUTCDate());
  const consultant: Consultant = {
    id: CONSULTATION_IDS.consultant, userId: ids.userConsultant, displayName: 'دکتر نرگس توکلی', bio: 'مشاور تحصیلی (برنامه‌ریزی درسی، انتخاب رشته)',
    specialties: 'برنامه‌ریزی درسی؛ انتخاب رشته', isActive: true, branchIds: [ids.branchNorth], rowVersion: rv(),
  };
  const slots: MockConsultation['slots'] = [];
  for (let d = -1; d <= 7; d++) {
    for (let k = 0; k < 4; k++) {
      const start = day0 + d * 24 * HOUR + 6.5 * HOUR + k * 0.5 * HOUR;
      slots.push({ id: uuid(), consultantId: consultant.id, branchId: ids.branchNorth, startsAt: iso(start), endsAt: iso(start + 0.5 * HOUR), mode: k === 3 ? 'online' : 'inPerson', location: k === 3 ? null : 'اتاق مشاوره ۱', status: 'open', rowVersion: rv() });
    }
  }
  const slotAt = (day: number, k: number) => slots[(day + 1) * 4 + k]!;
  const booking = (id: string, slot: (typeof slots)[number], studentId: string, status: BookingStatus, topic: string): MockBooking => ({
    id, slotId: slot.id, consultantId: consultant.id, branchId: slot.branchId, studentId, bookedByUserId: ids.userGuardian, startsAt: slot.startsAt,
    endsAt: slot.endsAt, mode: slot.mode, location: slot.location, topic, requesterNote: null, status,
    confirmedAt: status === 'requested' ? null : iso(nowMs - 2 * 24 * HOUR), completedAt: status === 'completed' ? slot.endsAt : null,
    cancelledAt: null, cancelledBy: null, cancelReason: null, createdAt: iso(nowMs - 3 * 24 * HOUR), rowVersion: rv(),
  });
  const ticketCreated = iso(nowMs - 26 * HOUR);
  return {
    settings: DEFAULT_SETTINGS(),
    consultants: [consultant],
    slots,
    bookings: [
      booking(CONSULTATION_IDS.bookingRequested, slotAt(1, 0), ids.student2, 'requested', 'انتخاب درس‌های ترم بعد'),
      booking(CONSULTATION_IDS.bookingConfirmed, slotAt(2, 1), ids.student1, 'confirmed', 'برنامهٔ مطالعهٔ امتحانات'),
      booking(CONSULTATION_IDS.bookingCompleted, slotAt(-1, 2), ids.student1, 'completed', 'مشاورهٔ تحصیلی'),
    ],
    notes: [{
      id: uuid(), bookingId: CONSULTATION_IDS.bookingCompleted, authorUserId: ids.userConsultant, authorName: '', body: 'یادداشت محرمانه: پیگیری دو هفتهٔ بعد.',
      createdAt: slotAt(-1, 2).endsAt,
    }],
    tickets: [{
      id: CONSULTATION_IDS.ticket, number: 1, branchId: ids.branchNorth, studentId: ids.student1, subject: 'سؤال دربارهٔ برنامهٔ کلاس', category: 'academic',
      priority: 'normal', status: 'waitingOnRequester', assigneeUserId: null, createdAt: ticketCreated, lastMessageAt: iso(nowMs - 20 * HOUR),
      rowVersion: rv(), createdByUserId: ids.userGuardian,
    }],
    messages: [
      { id: uuid(), ticketId: CONSULTATION_IDS.ticket, authorUserId: ids.userGuardian, fromStaff: false, isInternal: false, body: 'کلاس ریاضی چهارشنبه در کدام اتاق است؟', createdAt: ticketCreated },
      { id: uuid(), ticketId: CONSULTATION_IDS.ticket, authorUserId: ids.userDemoAdmin, fromStaff: true, isInternal: true, body: 'با آموزش هماهنگ شود.', createdAt: iso(nowMs - 22 * HOUR) },
      { id: uuid(), ticketId: CONSULTATION_IDS.ticket, authorUserId: ids.userDemoAdmin, fromStaff: true, isInternal: false, body: 'اتاق ۲۰۴، طبقهٔ دوم.', createdAt: iso(nowMs - 20 * HOUR) },
    ],
  };
}

export interface ConsultationContext {
  user: MockUser;
  data: MockTenantData;
  scope: (permission: string) => Set<string> | null;
  now: () => number;
  nextVersion: () => string;
  userName: (id: string) => string;
  userExists: (id: string) => boolean;
}

const notFound = () => new HttpError(404, 'resource.not_found');
const str = (v: unknown) => (typeof v === 'string' ? v.trim() : '');
const has = (scope: Set<string> | null, branchId: string) => scope === null || scope.has(branchId);
const empty = (scope: Set<string> | null) => scope !== null && scope.size === 0;
const ACTIVE: BookingStatus[] = ['requested', 'confirmed'];

function next(from: BookingStatus, action: BookingAction): BookingStatus | null {
  if (from === 'requested' && action === 'confirm') return 'confirmed';
  if ((from === 'requested' || from === 'confirmed') && action === 'cancel') return 'cancelled';
  if (from === 'confirmed' && action === 'complete') return 'completed';
  if (from === 'confirmed' && action === 'noShow') return 'noShow';
  return null;
}

function rowVersion(current: string | null, body: Record<string, unknown>) {
  if (typeof body.rowVersion !== 'string' || body.rowVersion === '') throw new HttpError(400, 'validation.failed', { errors: { rowVersion: ['validation.required'] } });
  if (current !== null && body.rowVersion !== current) throw new HttpError(409, 'concurrency.conflict');
}

export function consultationRoutes(ctx: ConsultationContext, seg: string[], method: string, url: URL, body: Record<string, unknown>): Response | null {
  const isPortal = seg[0] === 'portal' && (seg[1] === 'consultation' || seg[1] === 'support');
  if (seg[0] !== 'consultation' && seg[0] !== 'support' && !isPortal) return null;
  const c = ctx.data.consultation;
  const { user } = ctx;
  const now = ctx.now();
  const nv = ctx.nextVersion;
  const sc = (p: string) => ctx.scope(p);
  const myConsultant = c.consultants.find((x) => x.userId === user.id) ?? null;
  const own = (consultantId: string, branchId: string) => myConsultant?.id === consultantId && has(sc('consultation.sessions.manage'), branchId) && !empty(sc('consultation.sessions.manage'));
  const studentName = (id: string) => {
    const s = ctx.data.students.find((x) => x.id === id);
    return s ? `${s.firstName} ${s.lastName}` : '';
  };
  const consultantName = (id: string) => c.consultants.find((x) => x.id === id)?.displayName ?? '';
  const portalStudents = () => {
    const ids = new Set<string>();
    const guardian = ctx.data.guardians.find((g) => g.userId === user.id);
    if (guardian) ctx.data.students.filter((s) => s.guardians.some((g) => g.guardianId === guardian.id)).forEach((s) => ids.add(s.id));
    const self = ctx.data.students.find((s) => s.userId === user.id);
    if (self) ids.add(self.id);
    return ids;
  };
  const bookingDto = (b: MockBooking): Booking => ({ ...b, consultantName: consultantName(b.consultantId), studentName: studentName(b.studentId) });
  const deadline = (b: MockBooking) => Date.parse(b.startsAt) - c.settings.cancellationWindowHours * HOUR;
  const portalDto = (b: MockBooking) => ({
    id: b.id, studentId: b.studentId, studentName: studentName(b.studentId), consultantName: consultantName(b.consultantId), branchId: b.branchId,
    startsAt: b.startsAt, endsAt: b.endsAt, mode: b.mode, location: b.location, topic: b.topic, requesterNote: b.requesterNote, status: b.status,
    cancelledBy: b.cancelledBy, cancelReason: b.cancelReason, canCancel: ACTIVE.includes(b.status) && now <= deadline(b), cancelDeadline: iso(deadline(b)),
    createdAt: b.createdAt, rowVersion: b.rowVersion,
  });
  const slotDto = (s: MockConsultation['slots'][number]): Slot => {
    const b = c.bookings.find((x) => x.slotId === s.id && x.status !== 'cancelled');
    return { ...s, consultantName: consultantName(s.consultantId), booking: b ? { id: b.id, status: b.status, studentName: studentName(b.studentId), topic: b.topic } : null };
  };
  const anyScope = (...perms: string[]) => {
    const out = new Set<string>();
    for (const p of perms) {
      const s = sc(p);
      if (s === null) return null;
      s.forEach((x) => out.add(x));
    }
    return out;
  };
  const bookable = (branchId: string) => c.slots.filter((s) => {
    const t = Date.parse(s.startsAt);
    const consultant = c.consultants.find((x) => x.id === s.consultantId);
    return s.branchId === branchId && s.status === 'open' && consultant?.isActive && consultant.branchIds.includes(branchId)
      && t >= now + c.settings.minLeadHours * HOUR && t <= now + c.settings.bookingHorizonDays * 24 * HOUR
      && !c.bookings.some((b) => b.slotId === s.id && b.status !== 'cancelled');
  }).sort((a, b) => a.startsAt.localeCompare(b.startsAt));

  /* ---------------- portal consultation ---------------- */
  if (seg[0] === 'portal' && seg[1] === 'consultation') {
    const student = (id: string | null) => {
      const s = id && portalStudents().has(id) ? ctx.data.students.find((x) => x.id === id) : undefined;
      if (!s) throw notFound();
      return s;
    };
    if (seg[2] === 'consultants' && method === 'GET') {
      const s = student(url.searchParams.get('studentId'));
      const slots = bookable(s.branchId);
      return json(200, c.consultants.filter((x) => x.isActive && x.branchIds.includes(s.branchId)).map((x) => ({
        id: x.id, displayName: x.displayName, bio: x.bio, specialties: x.specialties, nextSlotAt: slots.find((sl) => sl.consultantId === x.id)?.startsAt ?? null,
      })));
    }
    if (seg[2] === 'slots' && method === 'GET') {
      const s = student(url.searchParams.get('studentId'));
      const cid = url.searchParams.get('consultantId');
      return json(200, bookable(s.branchId).filter((x) => !cid || x.consultantId === cid).map((x) => ({
        id: x.id, consultantId: x.consultantId, consultantName: consultantName(x.consultantId), branchId: x.branchId, startsAt: x.startsAt, endsAt: x.endsAt, mode: x.mode, location: x.location,
      })));
    }
    if (seg[2] === 'bookings' && seg.length === 3 && method === 'GET') {
      const ids = portalStudents();
      const sid = url.searchParams.get('studentId');
      if (sid && !ids.has(sid)) throw notFound();
      return json(200, c.bookings.filter((b) => (sid ? b.studentId === sid : ids.has(b.studentId))).sort((a, b) => b.startsAt.localeCompare(a.startsAt)).map(portalDto));
    }
    if (seg[2] === 'bookings' && seg.length === 3 && method === 'POST') {
      const errors: Record<string, string[]> = {};
      if (!str(body.topic)) errors.topic = ['validation.required'];
      else if (str(body.topic).length > 200) errors.topic = ['validation.max_length'];
      assertValid(errors);
      const s = student(str(body.studentId) || null);
      const slot = c.slots.find((x) => x.id === body.slotId);
      if (!slot) throw notFound();
      if (slot.branchId !== s.branchId) throw new HttpError(422, 'consultation.branch_not_served');
      if (slot.status !== 'open' || c.bookings.some((b) => b.slotId === slot.id && b.status !== 'cancelled')) throw new HttpError(409, 'consultation.slot_unavailable');
      const t = Date.parse(slot.startsAt);
      if (t < now + c.settings.minLeadHours * HOUR || t > now + c.settings.bookingHorizonDays * 24 * HOUR) throw new HttpError(422, 'consultation.outside_booking_window');
      if (c.bookings.filter((b) => b.studentId === s.id && ACTIVE.includes(b.status) && Date.parse(b.startsAt) > now).length >= c.settings.maxActiveBookingsPerStudent) {
        throw new HttpError(422, 'consultation.booking_limit_reached');
      }
      const b: MockBooking = {
        id: uuid(), slotId: slot.id, consultantId: slot.consultantId, branchId: slot.branchId, studentId: s.id, bookedByUserId: user.id, startsAt: slot.startsAt,
        endsAt: slot.endsAt, mode: slot.mode, location: slot.location, topic: str(body.topic), requesterNote: str(body.note) || null, status: 'requested',
        confirmedAt: null, completedAt: null, cancelledAt: null, cancelledBy: null, cancelReason: null, createdAt: iso(now), rowVersion: nv(),
      };
      c.bookings.push(b);
      return json(201, portalDto(b));
    }
    if (seg[2] === 'bookings' && seg[4] === 'cancel' && method === 'POST') {
      const b = c.bookings.find((x) => x.id === seg[3]);
      if (!b || !portalStudents().has(b.studentId)) throw notFound();
      rowVersion(b.rowVersion, body);
      if (!ACTIVE.includes(b.status)) throw new HttpError(409, 'consultation.invalid_transition');
      if (now > deadline(b)) throw new HttpError(422, 'consultation.cancellation_window_passed');
      Object.assign(b, { status: 'cancelled', cancelledAt: iso(now), cancelledBy: 'requester', cancelReason: str(body.reason) || null, rowVersion: nv() });
      return json(200, portalDto(b));
    }
    throw notFound();
  }

  /* ---------------- support (portal + staff) ---------------- */
  const ticketDto = (t: MockTicket, staff: boolean): TicketSummary => ({
    id: t.id, number: t.number, branchId: t.branchId, studentId: t.studentId, studentName: studentName(t.studentId), subject: t.subject, category: t.category,
    priority: t.priority, status: t.status, assigneeUserId: staff ? t.assigneeUserId : null, assigneeName: staff && t.assigneeUserId ? ctx.userName(t.assigneeUserId) : null,
    createdAt: t.createdAt, lastMessageAt: t.lastMessageAt, rowVersion: t.rowVersion,
  });
  const detail = (t: MockTicket, staff: boolean, canManage: boolean) => ({
    ticket: ticketDto(t, staff),
    messages: c.messages.filter((m) => m.ticketId === t.id && (staff || !m.isInternal)).map((m) => ({
      id: m.id, authorName: !staff && m.fromStaff ? '' : ctx.userName(m.authorUserId), fromStaff: m.fromStaff, isInternal: m.isInternal, body: m.body,
      createdAt: m.createdAt, mine: m.authorUserId === user.id,
    })),
    canManage,
  });
  const addMessage = (t: MockTicket, text: string, fromStaff: boolean, isInternal: boolean) => {
    c.messages.push({ id: uuid(), ticketId: t.id, authorUserId: user.id, fromStaff, isInternal, body: text, createdAt: iso(now) });
    if (!isInternal) {
      t.lastMessageAt = iso(now);
      t.status = fromStaff ? 'waitingOnRequester' : t.status === 'waitingOnRequester' || t.status === 'resolved' ? 'open' : t.status;
    }
    t.rowVersion = nv();
  };
  const bodyText = () => {
    const text = str(body.body);
    if (!text) throw new HttpError(400, 'validation.failed', { errors: { body: ['validation.required'] } });
    return text;
  };

  if (seg[0] === 'portal' && seg[1] === 'support' && seg[2] === 'tickets') {
    const mine = (id: string) => {
      const t = c.tickets.find((x) => x.id === id && x.createdByUserId === user.id);
      if (!t) throw notFound();
      return t;
    };
    if (seg.length === 3 && method === 'GET') {
      return json(200, c.tickets.filter((t) => t.createdByUserId === user.id).sort((a, b) => b.lastMessageAt.localeCompare(a.lastMessageAt)).map((t) => ticketDto(t, false)));
    }
    if (seg.length === 3 && method === 'POST') {
      const errors: Record<string, string[]> = {};
      if (!str(body.subject)) errors.subject = ['validation.required'];
      if (!str(body.body)) errors.body = ['validation.required'];
      if (!['general', 'academic', 'finance', 'technical', 'consultation'].includes(str(body.category))) errors.category = ['validation.invalid_value'];
      assertValid(errors);
      const sid = str(body.studentId);
      const s = portalStudents().has(sid) ? ctx.data.students.find((x) => x.id === sid) : undefined;
      if (!s) throw notFound();
      const t: MockTicket = {
        id: uuid(), number: Math.max(0, ...c.tickets.map((x) => x.number)) + 1, branchId: s.branchId, studentId: s.id, subject: str(body.subject),
        category: str(body.category) as TicketCategory, priority: 'normal', status: 'open', assigneeUserId: null, createdAt: iso(now), lastMessageAt: iso(now),
        rowVersion: nv(), createdByUserId: user.id,
      };
      c.tickets.push(t);
      c.messages.push({ id: uuid(), ticketId: t.id, authorUserId: user.id, fromStaff: false, isInternal: false, body: str(body.body), createdAt: iso(now) });
      return json(201, detail(t, false, false));
    }
    const t = mine(seg[3]!);
    if (seg.length === 4 && method === 'GET') return json(200, detail(t, false, false));
    if (seg[4] === 'messages' && method === 'POST') {
      const text = bodyText();
      if (t.status === 'closed') throw new HttpError(409, 'support.ticket_closed');
      addMessage(t, text, false, false);
      return json(200, detail(t, false, false));
    }
    if (seg[4] === 'close' && method === 'POST') {
      rowVersion(t.rowVersion, body);
      Object.assign(t, { status: 'closed', rowVersion: nv() });
      return json(200, detail(t, false, false));
    }
    throw notFound();
  }

  if (seg[0] === 'support' && seg[1] === 'tickets') {
    const read = sc('support.tickets.read');
    const manageScope = sc('support.tickets.manage');
    if (seg.length === 2 && method === 'GET') {
      if (empty(read)) throw new HttpError(403, 'authorization.forbidden');
      const status = url.searchParams.get('status');
      const assignee = url.searchParams.get('assignee');
      const rows = c.tickets.filter((t) => has(read, t.branchId) && (!status || t.status === status)
        && (assignee === 'me' ? t.assigneeUserId === user.id : assignee === 'unassigned' ? t.assigneeUserId === null : true))
        .sort((a, b) => b.lastMessageAt.localeCompare(a.lastMessageAt)).map((t) => ticketDto(t, true));
      return json(200, paginate(rows, url, (t) => [t.subject, String(t.number), t.studentName]));
    }
    const t = c.tickets.find((x) => x.id === seg[2]);
    if (!t || (!has(read, t.branchId) || empty(read)) && (!has(manageScope, t.branchId) || empty(manageScope))) throw notFound();
    const canManage = has(manageScope, t.branchId) && !empty(manageScope);
    if (seg.length === 3 && method === 'GET') return json(200, detail(t, true, canManage));
    if (!canManage) throw new HttpError(403, 'authorization.forbidden');
    if (seg[3] === 'messages' && method === 'POST') {
      const text = bodyText();
      if (t.status === 'closed') throw new HttpError(409, 'support.ticket_closed');
      addMessage(t, text, true, body.isInternal === true);
      return json(200, detail(t, true, true));
    }
    if (seg.length === 3 && method === 'PUT') {
      rowVersion(t.rowVersion, body);
      const status = str(body.status) as TicketStatus;
      if (t.status === 'closed' && status !== 'closed') throw new HttpError(409, 'support.ticket_closed');
      const assignee = typeof body.assigneeUserId === 'string' && body.assigneeUserId ? body.assigneeUserId : null;
      if (assignee && !ctx.userExists(assignee)) throw new HttpError(400, 'validation.failed', { errors: { assigneeUserId: ['support.unknown_assignee'] } });
      Object.assign(t, { status, priority: str(body.priority) as TicketPriority, assigneeUserId: assignee, rowVersion: nv() });
      return json(200, detail(t, true, true));
    }
    throw notFound();
  }

  /* ---------------- staff consultation ---------------- */
  const sub = seg[1];
  if (sub === 'settings') {
    if (method === 'GET') return json(200, c.settings);
    if (method === 'PUT') {
      if (sc('consultation.consultants.manage') !== null) throw new HttpError(403, 'authorization.forbidden_scope');
      rowVersion(c.settings.rowVersion, body);
      const n = (k: string, min: number, max: number) => {
        const v = Number(body[k]);
        if (!Number.isInteger(v) || v < min || v > max) throw new HttpError(400, 'validation.failed', { errors: { [k]: ['validation.invalid_value'] } });
        return v;
      };
      c.settings = {
        cancellationWindowHours: n('cancellationWindowHours', 0, 336), bookingHorizonDays: n('bookingHorizonDays', 1, 180), minLeadHours: n('minLeadHours', 0, 168),
        maxActiveBookingsPerStudent: n('maxActiveBookingsPerStudent', 1, 20), rowVersion: nv(),
      };
      return json(200, c.settings);
    }
  }
  if (sub === 'me' && method === 'GET') {
    const not = (p: string) => !empty(sc(p));
    return json(200, {
      consultant: myConsultant, canConsult: !!myConsultant?.isActive && not('consultation.sessions.manage'),
      canManageConsultants: not('consultation.consultants.manage'), canReadBookings: not('consultation.bookings.read'),
      canManageBookings: not('consultation.bookings.manage'), canReadNotes: not('consultation.notes.read'),
      canManageSettings: sc('consultation.consultants.manage') === null,
    });
  }
  const oversight = anyScope('consultation.consultants.manage', 'consultation.bookings.read', 'consultation.bookings.manage');
  if (sub === 'consultants') {
    const manage = sc('consultation.consultants.manage');
    const validate = () => {
      const errors: Record<string, string[]> = {};
      if (!str(body.displayName)) errors.displayName = ['validation.required'];
      const branchIds = Array.isArray(body.branchIds) ? (body.branchIds as string[]) : [];
      if (branchIds.length === 0) errors.branchIds = ['validation.required'];
      else if (branchIds.some((b) => !ctx.data.branches.some((x) => x.id === b))) errors.branchIds = ['consultation.branch_not_found'];
      assertValid(errors);
      if (branchIds.some((b) => !has(manage, b)) || empty(manage)) throw new HttpError(403, 'authorization.forbidden_scope');
      return branchIds;
    };
    if (seg.length === 2 && method === 'GET') {
      if (empty(oversight) && !myConsultant) throw new HttpError(403, 'authorization.forbidden');
      const bid = url.searchParams.get('branchId');
      const inactive = url.searchParams.get('includeInactive') === 'true';
      return json(200, c.consultants.filter((x) => (x.id === myConsultant?.id || (!empty(oversight) && x.branchIds.some((b) => has(oversight, b))))
        && (inactive || x.isActive) && (!bid || x.branchIds.includes(bid))));
    }
    if (seg.length === 2 && method === 'POST') {
      const branchIds = validate();
      if (!ctx.userExists(str(body.userId))) throw new HttpError(400, 'validation.failed', { errors: { userId: ['consultation.user_not_found'] } });
      if (c.consultants.some((x) => x.userId === body.userId)) throw new HttpError(409, 'consultation.consultant_exists');
      const x: Consultant = {
        id: uuid(), userId: str(body.userId), displayName: str(body.displayName), bio: str(body.bio) || null, specialties: str(body.specialties) || null,
        isActive: true, branchIds, rowVersion: nv(),
      };
      c.consultants.push(x);
      return json(201, x);
    }
    const x = c.consultants.find((y) => y.id === seg[2]);
    if (!x || (x.userId !== user.id && (empty(oversight) || !x.branchIds.some((b) => has(oversight, b))))) throw notFound();
    if (seg.length === 3 && method === 'GET') return json(200, x);
    if (seg.length === 3 && method === 'PUT') {
      rowVersion(x.rowVersion, body);
      const branchIds = validate();
      if (x.branchIds.some((b) => !has(manage, b))) throw new HttpError(403, 'authorization.forbidden_scope');
      Object.assign(x, { displayName: str(body.displayName), bio: str(body.bio) || null, specialties: str(body.specialties) || null, isActive: body.isActive !== false, branchIds, rowVersion: nv() });
      return json(200, x);
    }
    throw notFound();
  }
  if (sub === 'slots') {
    const consult = sc('consultation.sessions.manage');
    const visible = (s: { consultantId: string; branchId: string }) => (!empty(oversight) && has(oversight, s.branchId)) || own(s.consultantId, s.branchId);
    if (empty(oversight) && (!myConsultant || empty(consult))) throw new HttpError(403, 'authorization.forbidden');
    if (seg.length === 2 && method === 'GET') {
      const from = Date.parse(url.searchParams.get('from') ?? '') || now - 24 * HOUR;
      const to = Date.parse(url.searchParams.get('to') ?? '') || from + 8 * 24 * HOUR;
      const cid = url.searchParams.get('consultantId');
      const bid = url.searchParams.get('branchId');
      return json(200, c.slots.filter((s) => visible(s) && Date.parse(s.startsAt) < to && Date.parse(s.endsAt) > from && (!cid || s.consultantId === cid) && (!bid || s.branchId === bid))
        .sort((a, b) => a.startsAt.localeCompare(b.startsAt)).map(slotDto));
    }
    const writable = (consultantId: string, branchId: string) => {
      const x = c.consultants.find((y) => y.id === consultantId);
      if (!x) throw notFound();
      if (!own(x.id, branchId) && !(has(sc('consultation.consultants.manage'), branchId) && !empty(sc('consultation.consultants.manage')))) {
        throw new HttpError(403, 'authorization.forbidden_scope');
      }
      if (!x.isActive) throw new HttpError(422, 'consultation.consultant_inactive');
      if (!x.branchIds.includes(branchId)) throw new HttpError(422, 'consultation.branch_not_served');
      return x;
    };
    const overlaps = (consultantId: string, start: number, end: number) =>
      c.slots.some((s) => s.consultantId === consultantId && s.status === 'open' && Date.parse(s.startsAt) < end && Date.parse(s.endsAt) > start);
    if (seg.length === 2 && method === 'POST') {
      const start = Date.parse(str(body.startsAt));
      const end = Date.parse(str(body.endsAt));
      const errors: Record<string, string[]> = {};
      if (Number.isNaN(start)) errors.startsAt = ['validation.required'];
      if (Number.isNaN(end) || end - start < 15 * 60_000 || end - start > 240 * 60_000) errors.endsAt = ['validation.invalid_value'];
      if (!['inPerson', 'online'].includes(str(body.mode))) errors.mode = ['validation.invalid_value'];
      assertValid(errors);
      const x = writable(str(body.consultantId), str(body.branchId));
      if (start < now) throw new HttpError(422, 'consultation.slot_in_past');
      if (overlaps(x.id, start, end)) throw new HttpError(409, 'consultation.slot_overlap');
      const s = { id: uuid(), consultantId: x.id, branchId: str(body.branchId), startsAt: iso(start), endsAt: iso(end), mode: str(body.mode) as Slot['mode'], location: str(body.location) || null, status: 'open' as const, rowVersion: nv() };
      c.slots.push(s);
      return json(201, slotDto(s));
    }
    if (seg[2] === 'bulk' && method === 'POST') {
      const x = writable(str(body.consultantId), str(body.branchId));
      const days = new Set((Array.isArray(body.weekdays) ? body.weekdays : []).map((d) => ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday'].indexOf(String(d))));
      const [sh, sm] = str(body.startTime).split(':').map(Number);
      const [eh, em] = str(body.endTime).split(':').map(Number);
      const duration = Number(body.durationMinutes);
      const errors: Record<string, string[]> = {};
      if (!/^\d{4}-\d{2}-\d{2}$/.test(str(body.fromDate))) errors.fromDate = ['validation.required'];
      if (!/^\d{4}-\d{2}-\d{2}$/.test(str(body.toDate))) errors.toDate = ['validation.required'];
      if (days.size === 0 || days.has(-1)) errors.weekdays = ['validation.required'];
      if (!(duration >= 15 && duration <= 240)) errors.durationMinutes = ['validation.invalid_value'];
      if (sh === undefined || Number.isNaN(sh) || eh === undefined || Number.isNaN(eh) || eh * 60 + (em ?? 0) <= sh * 60 + (sm ?? 0)) errors.endTime = ['validation.invalid_value'];
      assertValid(errors);
      const created: MockConsultation['slots'] = [];
      let skipped = 0;
      // Tehran is UTC+03:30 all year (no DST since 2022).
      const offset = 3.5 * HOUR;
      for (let d = Date.parse(`${str(body.fromDate)}T00:00:00Z`); d <= Date.parse(`${str(body.toDate)}T00:00:00Z`); d += 24 * HOUR) {
        if (!days.has(new Date(d).getUTCDay())) continue;
        for (let m = sh! * 60 + (sm ?? 0); m + duration <= eh! * 60 + (em ?? 0); m += duration) {
          const start = d + m * 60_000 - offset;
          const end = start + duration * 60_000;
          if (start < now || overlaps(x.id, start, end)) { skipped++; continue; }
          const s = { id: uuid(), consultantId: x.id, branchId: str(body.branchId), startsAt: iso(start), endsAt: iso(end), mode: str(body.mode) as Slot['mode'], location: str(body.location) || null, status: 'open' as const, rowVersion: nv() };
          c.slots.push(s);
          created.push(s);
        }
      }
      return json(200, { created: created.length, skipped, slots: created.map(slotDto) });
    }
    if (seg.length === 3 && method === 'DELETE') {
      const s = c.slots.find((y) => y.id === seg[2]);
      if (!s || !visible(s)) throw notFound();
      if (!own(s.consultantId, s.branchId) && !(has(sc('consultation.consultants.manage'), s.branchId) && !empty(sc('consultation.consultants.manage')))) {
        throw new HttpError(403, 'authorization.forbidden');
      }
      if (c.bookings.some((b) => b.slotId === s.id && ACTIVE.includes(b.status))) throw new HttpError(409, 'consultation.slot_has_bookings');
      s.status = 'cancelled';
      return json(204, null);
    }
    throw notFound();
  }
  if (sub === 'bookings') {
    const read = anyScope('consultation.bookings.read', 'consultation.bookings.manage', 'consultation.notes.read');
    const manage = sc('consultation.bookings.manage');
    const notesRead = sc('consultation.notes.read');
    const access = (b: MockBooking) => ({
      own: own(b.consultantId, b.branchId),
      manage: !empty(manage) && has(manage, b.branchId),
      notes: !empty(notesRead) && has(notesRead, b.branchId),
      visible: own(b.consultantId, b.branchId) || (!empty(read) && has(read, b.branchId)),
    });
    if (seg.length === 2 && method === 'GET') {
      const scope = url.searchParams.get('scope') ?? 'all';
      if (empty(read) && !myConsultant) throw new HttpError(403, 'authorization.forbidden');
      const from = Date.parse(url.searchParams.get('from') ?? '');
      const to = Date.parse(url.searchParams.get('to') ?? '');
      const status = url.searchParams.get('status');
      const cid = url.searchParams.get('consultantId');
      const rows = c.bookings.filter((b) => (scope === 'mine' ? access(b).own : access(b).visible)
        && (Number.isNaN(from) || Date.parse(b.endsAt) > from) && (Number.isNaN(to) || Date.parse(b.startsAt) < to)
        && (!status || b.status === status) && (!cid || b.consultantId === cid))
        .sort((a, b) => a.startsAt.localeCompare(b.startsAt)).map(bookingDto);
      return json(200, paginate(rows, url, (b) => [b.studentName, b.topic]));
    }
    const b = c.bookings.find((x) => x.id === seg[2]);
    if (!b || !access(b).visible) throw notFound();
    const a = access(b);
    const detail = () => ({
      booking: bookingDto(b),
      allowedActions: a.own || a.manage ? (['confirm', 'cancel', 'complete', 'noShow'] as BookingAction[]).filter((x) => next(b.status, x)) : [],
      canReadNotes: a.own || a.notes,
      canWriteNotes: a.own && (b.status === 'confirmed' || b.status === 'completed'),
    });
    if (seg.length === 3 && method === 'GET') return json(200, detail());
    if (seg[3] === 'notes') {
      if (method === 'GET') {
        if (!a.own && !a.notes) throw new HttpError(403, 'consultation.notes_forbidden');
        return json(200, c.notes.filter((n) => n.bookingId === b.id).map(({ bookingId: _b, ...n }) => ({ ...n, authorName: ctx.userName(n.authorUserId) })));
      }
      if (method === 'POST') {
        if (!str(body.body)) throw new HttpError(400, 'validation.failed', { errors: { body: ['validation.required'] } });
        if (!a.own) throw new HttpError(403, 'consultation.not_booking_consultant');
        if (b.status !== 'confirmed' && b.status !== 'completed') throw new HttpError(409, 'consultation.invalid_transition');
        const n = { id: uuid(), bookingId: b.id, authorUserId: user.id, authorName: ctx.userName(user.id), body: str(body.body), createdAt: iso(now) };
        c.notes.push(n);
        const { bookingId: _b, ...dto } = n;
        return json(201, dto);
      }
    }
    const actions: Record<string, BookingAction> = { confirm: 'confirm', cancel: 'cancel', complete: 'complete', 'no-show': 'noShow' };
    const action = actions[seg[3] ?? ''];
    if (action && method === 'POST') {
      rowVersion(b.rowVersion, body);
      if (!a.own && !a.manage) throw new HttpError(403, 'authorization.forbidden');
      const to = next(b.status, action);
      if (!to) throw new HttpError(409, 'consultation.invalid_transition');
      const start = Date.parse(b.startsAt);
      if ((action === 'complete' && now < start - 15 * 60_000) || (action === 'noShow' && now < start)) throw new HttpError(422, 'consultation.session_not_started');
      b.status = to;
      if (to === 'confirmed') b.confirmedAt = iso(now);
      if (to === 'completed') b.completedAt = iso(now);
      if (to === 'cancelled') Object.assign(b, { cancelledAt: iso(now), cancelledBy: 'staff', cancelReason: str(body.reason) || null });
      b.rowVersion = nv();
      return json(200, detail());
    }
    throw notFound();
  }
  throw notFound();
}
