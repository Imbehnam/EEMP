using EEMP.SharedKernel.Domain;

namespace EEMP.Modules.Consultation.Domain;

public enum SessionMode { InPerson, Online }

public enum SlotStatus { Open, Cancelled }

public enum BookingStatus { Requested, Confirmed, Completed, Cancelled, NoShow }

public enum BookingAction { Confirm, Cancel, Complete, NoShow }

public enum CancelledBy { Requester, Staff }

public enum TicketCategory { General, Academic, Finance, Technical, Consultation }

public enum TicketPriority { Low, Normal, High }

public enum TicketStatus { Open, InProgress, WaitingOnRequester, Resolved, Closed }

/// <summary>Per-tenant booking policy (one row, created lazily with defaults).</summary>
public sealed class ConsultationSettings : TenantEntity
{
    public const int DefaultCancellationWindowHours = 24;
    public const int DefaultBookingHorizonDays = 30;
    public const int DefaultMinLeadHours = 2;
    public const int DefaultMaxActiveBookings = 3;

    public int CancellationWindowHours { get; set; } = DefaultCancellationWindowHours;
    public int BookingHorizonDays { get; set; } = DefaultBookingHorizonDays;
    public int MinLeadHours { get; set; } = DefaultMinLeadHours;
    public int MaxActiveBookingsPerStudent { get; set; } = DefaultMaxActiveBookings;
}

/// <summary>
/// A staff user who offers consultation sessions. The profile alone grants nothing: acting as a consultant also needs
/// <c>consultation.sessions.manage</c> in the branch (DL-091).
/// </summary>
public sealed class Consultant : TenantEntity
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? Specialties { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ConsultantBranch> Branches { get; set; } = [];

    public bool Serves(Guid branchId) => Branches.Any(b => b.BranchId == branchId);
}

/// <summary>A branch the consultant serves.</summary>
public sealed class ConsultantBranch : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ConsultantId { get; set; }
    public Guid BranchId { get; set; }
}

/// <summary>A published availability window for one one-to-one session (capacity 1, DL-092).</summary>
public sealed class Slot : TenantEntity
{
    public Guid ConsultantId { get; set; }
    public Guid BranchId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public SessionMode Mode { get; set; }
    public string? Location { get; set; }
    public SlotStatus Status { get; set; } = SlotStatus.Open;
}

/// <summary>
/// A reservation of a slot for a student. Scheduling metadata only: confidential notes live in <see cref="BookingNote"/>.
/// At most one non-cancelled booking per slot (unique filtered index).
/// </summary>
public sealed class Booking : TenantEntity
{
    public Guid SlotId { get; set; }
    public Guid ConsultantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid StudentId { get; set; }
    public Guid BookedByUserId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public SessionMode Mode { get; set; }
    public string? Location { get; set; }
    public string Topic { get; set; } = string.Empty;
    /// <summary>Message from the family to the consultant. Not confidential: the family wrote it and sees it.</summary>
    public string? RequesterNote { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Requested;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public CancelledBy? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
    public Guid? StatusChangedByUserId { get; set; }

    public bool IsActive => Status is BookingStatus.Requested or BookingStatus.Confirmed;
}

/// <summary>Confidential consultation note (DL-093). Append-only; readable only by the booking's consultant and notes.read holders.</summary>
public sealed class BookingNote : ITenantOwned, IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BookingId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Support request from the portal about a linked student; handled by branch staff (DL-094).</summary>
public sealed class Ticket : TenantEntity
{
    public int Number { get; set; }
    public Guid BranchId { get; set; }
    public Guid StudentId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public TicketCategory Category { get; set; }
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public Guid? AssigneeUserId { get; set; }
    public DateTime LastMessageAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public sealed class TicketMessage : ITenantOwned, IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Guid AuthorUserId { get; set; }
    public bool FromStaff { get; set; }
    public string Body { get; set; } = string.Empty;
    /// <summary>Staff-only note; never returned to the portal.</summary>
    public bool IsInternal { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
