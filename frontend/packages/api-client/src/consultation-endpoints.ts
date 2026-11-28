import type { ApiClient } from './client';
import type {
  Booking, BookingAction, BookingActionRequest, BookingDetail, BookingListQuery, BulkSlotsRequest, BulkSlotsResult, Consultant, ConsultationMe,
  ConsultationNote, ConsultationSettings, CreateConsultantRequest, CreateSlotRequest, CreateTicketRequest, PortalBookRequest, PortalBooking,
  PortalConsultant, PortalSlot, Slot, SlotQuery, TicketDetail, TicketListQuery, TicketSummary, UpdateConsultantRequest, UpdateConsultationSettings,
  UpdateTicketRequest,
} from './consultation-types';
import type { Guid, PageQuery, Paged, RowVersion } from './types';

const id = (value: Guid) => encodeURIComponent(value);
const route = (action: BookingAction) => (action === 'noShow' ? 'no-show' : action);

/** Typed endpoints for EEMP-012 consultation and support booking (paths relative to /api/v1). */
export function createConsultationEndpoints(client: ApiClient) {
  return {
    settings: {
      get: () => client.get<ConsultationSettings>('/consultation/settings'),
      update: (body: UpdateConsultationSettings) => client.put<ConsultationSettings>('/consultation/settings', body),
    },
    me: () => client.get<ConsultationMe>('/consultation/me'),
    consultants: {
      list: (query: { branchId?: Guid; includeInactive?: boolean } = {}) =>
        client.get<Consultant[]>('/consultation/consultants', { query: { branchId: query.branchId, includeInactive: query.includeInactive } }),
      get: (consultantId: Guid) => client.get<Consultant>(`/consultation/consultants/${id(consultantId)}`),
      create: (body: CreateConsultantRequest) => client.post<Consultant>('/consultation/consultants', body),
      update: (consultantId: Guid, body: UpdateConsultantRequest) => client.put<Consultant>(`/consultation/consultants/${id(consultantId)}`, body),
    },
    slots: {
      list: (query: SlotQuery = {}) =>
        client.get<Slot[]>('/consultation/slots', { query: { from: query.from, to: query.to, consultantId: query.consultantId, branchId: query.branchId } }),
      create: (body: CreateSlotRequest) => client.post<Slot>('/consultation/slots', body),
      bulk: (body: BulkSlotsRequest) => client.post<BulkSlotsResult>('/consultation/slots/bulk', body),
      /** Cancels an open slot (409 consultation.slot_has_bookings while a booking is active). */
      remove: (slotId: Guid) => client.delete(`/consultation/slots/${id(slotId)}`),
    },
    bookings: {
      list: (query: PageQuery & BookingListQuery = {}) =>
        client.get<Paged<Booking>>('/consultation/bookings', {
          query: {
            page: query.page, pageSize: query.pageSize, scope: query.scope, from: query.from, to: query.to, branchId: query.branchId,
            consultantId: query.consultantId, status: query.status,
          },
        }),
      get: (bookingId: Guid) => client.get<BookingDetail>(`/consultation/bookings/${id(bookingId)}`),
      act: (bookingId: Guid, action: BookingAction, body: BookingActionRequest) =>
        client.post<BookingDetail>(`/consultation/bookings/${id(bookingId)}/${route(action)}`, body),
      notes: (bookingId: Guid) => client.get<ConsultationNote[]>(`/consultation/bookings/${id(bookingId)}/notes`),
      addNote: (bookingId: Guid, body: string) => client.post<ConsultationNote>(`/consultation/bookings/${id(bookingId)}/notes`, { body }),
    },
    portal: {
      consultants: (studentId: Guid) => client.get<PortalConsultant[]>('/portal/consultation/consultants', { query: { studentId } }),
      slots: (studentId: Guid, query: { consultantId?: Guid; from?: string; to?: string } = {}) =>
        client.get<PortalSlot[]>('/portal/consultation/slots', { query: { studentId, consultantId: query.consultantId, from: query.from, to: query.to } }),
      bookings: (studentId?: Guid) => client.get<PortalBooking[]>('/portal/consultation/bookings', { query: { studentId } }),
      book: (body: PortalBookRequest) => client.post<PortalBooking>('/portal/consultation/bookings', body),
      cancel: (bookingId: Guid, rowVersion: RowVersion, reason?: string | null) =>
        client.post<PortalBooking>(`/portal/consultation/bookings/${id(bookingId)}/cancel`, { rowVersion, reason }),
    },
  };
}

/** Support tickets: portal requesters and branch staff. */
export function createSupportEndpoints(client: ApiClient) {
  return {
    tickets: {
      list: (query: PageQuery & TicketListQuery = {}) =>
        client.get<Paged<TicketSummary>>('/support/tickets', {
          query: { page: query.page, pageSize: query.pageSize, search: query.search, status: query.status, branchId: query.branchId, assignee: query.assignee },
        }),
      get: (ticketId: Guid) => client.get<TicketDetail>(`/support/tickets/${id(ticketId)}`),
      reply: (ticketId: Guid, body: string, isInternal: boolean) =>
        client.post<TicketDetail>(`/support/tickets/${id(ticketId)}/messages`, { body, isInternal }),
      update: (ticketId: Guid, body: UpdateTicketRequest) => client.put<TicketDetail>(`/support/tickets/${id(ticketId)}`, body),
    },
    portal: {
      list: () => client.get<TicketSummary[]>('/portal/support/tickets'),
      create: (body: CreateTicketRequest) => client.post<TicketDetail>('/portal/support/tickets', body),
      get: (ticketId: Guid) => client.get<TicketDetail>(`/portal/support/tickets/${id(ticketId)}`),
      reply: (ticketId: Guid, body: string) => client.post<TicketDetail>(`/portal/support/tickets/${id(ticketId)}/messages`, { body }),
      close: (ticketId: Guid, rowVersion: RowVersion) => client.post<TicketDetail>(`/portal/support/tickets/${id(ticketId)}/close`, { rowVersion }),
    },
  };
}

export type ConsultationEndpoints = ReturnType<typeof createConsultationEndpoints>;
export type SupportEndpoints = ReturnType<typeof createSupportEndpoints>;
