/** DTOs for EEMP-012 consultation and support booking (work/eemp-consultation-contract.md, backend Consultation.Application/Dtos.cs). */
import type { Guid, RowVersion, UtcInstant } from './types';

export const SESSION_MODES = ['inPerson', 'online'] as const;
export type SessionMode = (typeof SESSION_MODES)[number];
export type SlotStatus = 'open' | 'cancelled';
export const BOOKING_STATUSES = ['requested', 'confirmed', 'completed', 'cancelled', 'noShow'] as const;
export type BookingStatus = (typeof BOOKING_STATUSES)[number];
/** Staff actions; the HTTP route of `noShow` is `no-show`. */
export type BookingAction = 'confirm' | 'cancel' | 'complete' | 'noShow';
export type CancelledBy = 'requester' | 'staff';
export const TICKET_CATEGORIES = ['general', 'academic', 'finance', 'technical', 'consultation'] as const;
export type TicketCategory = (typeof TICKET_CATEGORIES)[number];
export const TICKET_PRIORITIES = ['low', 'normal', 'high'] as const;
export type TicketPriority = (typeof TICKET_PRIORITIES)[number];
export const TICKET_STATUSES = ['open', 'inProgress', 'waitingOnRequester', 'resolved', 'closed'] as const;
export type TicketStatus = (typeof TICKET_STATUSES)[number];

export interface ConsultationSettings {
  cancellationWindowHours: number;
  bookingHorizonDays: number;
  minLeadHours: number;
  maxActiveBookingsPerStudent: number;
  /** null until the tenant saved its own policy (defaults apply). */
  rowVersion: RowVersion | null;
}
export type UpdateConsultationSettings = Omit<ConsultationSettings, 'rowVersion'> & { rowVersion: RowVersion | null };

export interface Consultant {
  id: Guid;
  userId: Guid;
  displayName: string;
  bio: string | null;
  specialties: string | null;
  isActive: boolean;
  branchIds: Guid[];
  rowVersion: RowVersion;
}
export interface CreateConsultantRequest {
  userId: Guid;
  displayName: string;
  bio?: string | null;
  specialties?: string | null;
  branchIds: Guid[];
}
export interface UpdateConsultantRequest {
  displayName: string;
  bio?: string | null;
  specialties?: string | null;
  branchIds: Guid[];
  isActive: boolean;
  rowVersion: RowVersion;
}

/** What the caller can do in the consultation area (UI hints; the API re-checks every call). */
export interface ConsultationMe {
  consultant: Consultant | null;
  canConsult: boolean;
  canManageConsultants: boolean;
  canReadBookings: boolean;
  canManageBookings: boolean;
  canReadNotes: boolean;
  canManageSettings: boolean;
}

export interface SlotBookingSummary {
  id: Guid;
  status: BookingStatus;
  studentName: string;
  topic: string;
}
export interface Slot {
  id: Guid;
  consultantId: Guid;
  consultantName: string;
  branchId: Guid;
  startsAt: UtcInstant;
  endsAt: UtcInstant;
  mode: SessionMode;
  location: string | null;
  status: SlotStatus;
  booking: SlotBookingSummary | null;
  rowVersion: RowVersion;
}
export interface SlotQuery {
  from?: UtcInstant;
  to?: UtcInstant;
  consultantId?: Guid;
  branchId?: Guid;
}
export interface CreateSlotRequest {
  consultantId: Guid;
  branchId: Guid;
  startsAt: UtcInstant;
  endsAt: UtcInstant;
  mode: SessionMode;
  location?: string | null;
}
export interface BulkSlotsRequest {
  consultantId: Guid;
  branchId: Guid;
  /** yyyy-MM-dd in the tenant calendar (Gregorian ISO). */
  fromDate: string;
  toDate: string;
  weekdays: string[];
  /** HH:mm, tenant time zone. */
  startTime: string;
  endTime: string;
  durationMinutes: number;
  mode: SessionMode;
  location?: string | null;
}
export interface BulkSlotsResult {
  created: number;
  skipped: number;
  slots: Slot[];
}

export interface Booking {
  id: Guid;
  slotId: Guid;
  consultantId: Guid;
  consultantName: string;
  branchId: Guid;
  studentId: Guid;
  studentName: string;
  bookedByUserId: Guid;
  startsAt: UtcInstant;
  endsAt: UtcInstant;
  mode: SessionMode;
  location: string | null;
  topic: string;
  requesterNote: string | null;
  status: BookingStatus;
  confirmedAt: UtcInstant | null;
  completedAt: UtcInstant | null;
  cancelledAt: UtcInstant | null;
  cancelledBy: CancelledBy | null;
  cancelReason: string | null;
  createdAt: UtcInstant;
  rowVersion: RowVersion;
}
export interface BookingDetail {
  booking: Booking;
  allowedActions: BookingAction[];
  canReadNotes: boolean;
  canWriteNotes: boolean;
}
export interface BookingListQuery {
  scope?: 'mine' | 'all';
  from?: UtcInstant;
  to?: UtcInstant;
  branchId?: Guid;
  consultantId?: Guid;
  status?: BookingStatus;
}
export interface BookingActionRequest {
  rowVersion: RowVersion;
  reason?: string | null;
}
/** Confidential consultation note (DL-093): only the session's consultant and consultation.notes.read holders. */
export interface ConsultationNote {
  id: Guid;
  authorUserId: Guid;
  authorName: string;
  body: string;
  createdAt: UtcInstant;
}

export interface PortalConsultant {
  id: Guid;
  displayName: string;
  bio: string | null;
  specialties: string | null;
  nextSlotAt: UtcInstant | null;
}
export interface PortalSlot {
  id: Guid;
  consultantId: Guid;
  consultantName: string;
  branchId: Guid;
  startsAt: UtcInstant;
  endsAt: UtcInstant;
  mode: SessionMode;
  location: string | null;
}
export interface PortalBookRequest {
  studentId: Guid;
  slotId: Guid;
  topic: string;
  note?: string | null;
}
/** Portal view of a booking: no notes and no staff user ids. */
export interface PortalBooking {
  id: Guid;
  studentId: Guid;
  studentName: string;
  consultantName: string;
  branchId: Guid;
  startsAt: UtcInstant;
  endsAt: UtcInstant;
  mode: SessionMode;
  location: string | null;
  topic: string;
  requesterNote: string | null;
  status: BookingStatus;
  cancelledBy: CancelledBy | null;
  cancelReason: string | null;
  canCancel: boolean;
  cancelDeadline: UtcInstant;
  createdAt: UtcInstant;
  rowVersion: RowVersion;
}

export interface TicketSummary {
  id: Guid;
  number: number;
  branchId: Guid;
  studentId: Guid;
  studentName: string;
  subject: string;
  category: TicketCategory;
  priority: TicketPriority;
  status: TicketStatus;
  /** Staff views only (null in the portal). */
  assigneeUserId: Guid | null;
  assigneeName: string | null;
  createdAt: UtcInstant;
  lastMessageAt: UtcInstant;
  rowVersion: RowVersion;
}
export interface TicketMessage {
  id: Guid;
  /** Empty for staff replies in the portal (the team answers, not a named person). */
  authorName: string;
  fromStaff: boolean;
  isInternal: boolean;
  body: string;
  createdAt: UtcInstant;
  mine: boolean;
}
export interface TicketDetail {
  ticket: TicketSummary;
  messages: TicketMessage[];
  canManage: boolean;
}
export interface CreateTicketRequest {
  studentId: Guid;
  subject: string;
  category: TicketCategory;
  body: string;
}
export interface TicketListQuery {
  status?: TicketStatus;
  branchId?: Guid;
  assignee?: 'me' | 'unassigned' | 'any';
}
export interface UpdateTicketRequest {
  status: TicketStatus;
  priority: TicketPriority;
  assigneeUserId: Guid | null;
  rowVersion: RowVersion;
}
