namespace EEMP.Modules.Consultation.Application;

// Settings
public sealed record SettingsDto(int CancellationWindowHours, int BookingHorizonDays, int MinLeadHours, int MaxActiveBookingsPerStudent, string? RowVersion);
public sealed record UpdateSettingsRequest(int CancellationWindowHours, int BookingHorizonDays, int MinLeadHours, int MaxActiveBookingsPerStudent, string? RowVersion);

// Consultants
public sealed record ConsultantDto(Guid Id, Guid UserId, string DisplayName, string? Bio, string? Specialties, bool IsActive,
    IReadOnlyList<Guid> BranchIds, string RowVersion);
public sealed record CreateConsultantRequest(Guid UserId, string? DisplayName, string? Bio, string? Specialties, IReadOnlyList<Guid>? BranchIds);
public sealed record UpdateConsultantRequest(string? DisplayName, string? Bio, string? Specialties, IReadOnlyList<Guid>? BranchIds, bool IsActive, string? RowVersion);

/// <summary>What the caller can do in the consultation area (drives the admin UI; the server re-checks every call).</summary>
public sealed record ConsultationMeDto(ConsultantDto? Consultant, bool CanConsult, bool CanManageConsultants, bool CanReadBookings,
    bool CanManageBookings, bool CanReadNotes, bool CanManageSettings);

// Slots
public sealed record SlotBookingSummaryDto(Guid Id, string Status, string StudentName, string Topic);
public sealed record SlotDto(Guid Id, Guid ConsultantId, string ConsultantName, Guid BranchId, DateTime StartsAt, DateTime EndsAt, string Mode,
    string? Location, string Status, SlotBookingSummaryDto? Booking, string RowVersion);
public sealed record CreateSlotRequest(Guid ConsultantId, Guid BranchId, DateTime? StartsAt, DateTime? EndsAt, string? Mode, string? Location);
public sealed record BulkSlotsRequest(Guid ConsultantId, Guid BranchId, string? FromDate, string? ToDate, IReadOnlyList<string>? Weekdays,
    string? StartTime, string? EndTime, int DurationMinutes, string? Mode, string? Location);
public sealed record BulkSlotsResult(int Created, int Skipped, IReadOnlyList<SlotDto> Slots);

// Bookings
public sealed record BookingDto(Guid Id, Guid SlotId, Guid ConsultantId, string ConsultantName, Guid BranchId, Guid StudentId, string StudentName,
    Guid BookedByUserId, DateTime StartsAt, DateTime EndsAt, string Mode, string? Location, string Topic, string? RequesterNote, string Status,
    DateTime? ConfirmedAt, DateTime? CompletedAt, DateTime? CancelledAt, string? CancelledBy, string? CancelReason, DateTime CreatedAt, string RowVersion);
public sealed record BookingDetailDto(BookingDto Booking, IReadOnlyList<string> AllowedActions, bool CanReadNotes, bool CanWriteNotes);
public sealed record BookingActionRequest(string? RowVersion, string? Reason);
public sealed record BookingListQuery(string? Scope, DateTime? From, DateTime? To, Guid? BranchId, Guid? ConsultantId, string? Status);
public sealed record NoteDto(Guid Id, Guid AuthorUserId, string AuthorName, string Body, DateTime CreatedAt);
public sealed record CreateNoteRequest(string? Body);

// Portal
public sealed record PortalConsultantDto(Guid Id, string DisplayName, string? Bio, string? Specialties, DateTime? NextSlotAt);
public sealed record PortalSlotDto(Guid Id, Guid ConsultantId, string ConsultantName, Guid BranchId, DateTime StartsAt, DateTime EndsAt, string Mode, string? Location);
public sealed record PortalBookRequest(Guid StudentId, Guid SlotId, string? Topic, string? Note);
public sealed record PortalBookingDto(Guid Id, Guid StudentId, string StudentName, string ConsultantName, Guid BranchId, DateTime StartsAt, DateTime EndsAt,
    string Mode, string? Location, string Topic, string? RequesterNote, string Status, string? CancelledBy, string? CancelReason, bool CanCancel,
    DateTime CancelDeadline, DateTime CreatedAt, string RowVersion);
public sealed record PortalCancelRequest(string? RowVersion, string? Reason);

// Support
public sealed record TicketSummaryDto(Guid Id, int Number, Guid BranchId, Guid StudentId, string StudentName, string Subject, string Category, string Priority,
    string Status, Guid? AssigneeUserId, string? AssigneeName, DateTime CreatedAt, DateTime LastMessageAt, string RowVersion);
public sealed record TicketMessageDto(Guid Id, string AuthorName, bool FromStaff, bool IsInternal, string Body, DateTime CreatedAt, bool Mine);
public sealed record TicketDetailDto(TicketSummaryDto Ticket, IReadOnlyList<TicketMessageDto> Messages, bool CanManage);
public sealed record CreateTicketRequest(Guid StudentId, string? Subject, string? Category, string? Body);
public sealed record TicketMessageRequest(string? Body, bool IsInternal);
public sealed record UpdateTicketRequest(string? Status, string? Priority, Guid? AssigneeUserId, string? RowVersion);
public sealed record TicketRowVersionRequest(string? RowVersion);
public sealed record TicketListQuery(string? Status, Guid? BranchId, string? Assignee);
