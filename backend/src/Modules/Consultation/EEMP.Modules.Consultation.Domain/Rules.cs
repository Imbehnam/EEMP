namespace EEMP.Modules.Consultation.Domain;

public static class ConsultationErrors
{
    public const string ConsultantExists = "consultation.consultant_exists";
    public const string UserNotFound = "consultation.user_not_found";
    public const string ConsultantInactive = "consultation.consultant_inactive";
    public const string BranchNotFound = "consultation.branch_not_found";
    public const string BranchNotServed = "consultation.branch_not_served";
    public const string SlotOverlap = "consultation.slot_overlap";
    public const string SlotInPast = "consultation.slot_in_past";
    public const string SlotUnavailable = "consultation.slot_unavailable";
    public const string SlotHasBookings = "consultation.slot_has_bookings";
    public const string OutsideBookingWindow = "consultation.outside_booking_window";
    public const string BookingLimitReached = "consultation.booking_limit_reached";
    public const string CancellationWindowPassed = "consultation.cancellation_window_passed";
    public const string InvalidTransition = "consultation.invalid_transition";
    public const string SessionNotStarted = "consultation.session_not_started";
    public const string NotesForbidden = "consultation.notes_forbidden";
    public const string NotBookingConsultant = "consultation.not_booking_consultant";
    public const string TicketClosed = "support.ticket_closed";
    public const string UnknownAssignee = "support.unknown_assignee";

    public static readonly IReadOnlyList<string> All =
    [
        ConsultantExists, UserNotFound, ConsultantInactive, BranchNotFound, BranchNotServed, SlotOverlap, SlotInPast, SlotUnavailable,
        SlotHasBookings, OutsideBookingWindow, BookingLimitReached, CancellationWindowPassed, InvalidTransition, SessionNotStarted,
        NotesForbidden, NotBookingConsultant, TicketClosed, UnknownAssignee,
    ];
}

/// <summary>Booking lifecycle (DL-092): requested → confirmed → completed | noShow; requested/confirmed → cancelled.</summary>
public static class BookingMachine
{
    /// <summary>Completion is possible from this long before the scheduled start (sessions may begin a little early).</summary>
    public static readonly TimeSpan EarlyCompletion = TimeSpan.FromMinutes(15);

    public static BookingStatus? Next(BookingStatus from, BookingAction action) => (from, action) switch
    {
        (BookingStatus.Requested, BookingAction.Confirm) => BookingStatus.Confirmed,
        (BookingStatus.Requested or BookingStatus.Confirmed, BookingAction.Cancel) => BookingStatus.Cancelled,
        (BookingStatus.Confirmed, BookingAction.Complete) => BookingStatus.Completed,
        (BookingStatus.Confirmed, BookingAction.NoShow) => BookingStatus.NoShow,
        _ => null,
    };

    /// <summary>Time rule for an otherwise valid transition; returns an error code or null.</summary>
    public static string? CheckTiming(BookingAction action, DateTime startsAt, DateTime now) => action switch
    {
        BookingAction.Complete when now < startsAt - EarlyCompletion => ConsultationErrors.SessionNotStarted,
        BookingAction.NoShow when now < startsAt => ConsultationErrors.SessionNotStarted,
        _ => null,
    };

    /// <summary>Last instant at which the requester may still cancel.</summary>
    public static DateTime CancelDeadline(DateTime startsAt, int cancellationWindowHours) => startsAt.AddHours(-cancellationWindowHours);

    public static bool RequesterMayCancel(Booking b, int cancellationWindowHours, DateTime now) =>
        b.IsActive && now <= CancelDeadline(b.StartsAt, cancellationWindowHours);
}

public static class SlotRules
{
    public const int MinMinutes = 15;
    public const int MaxMinutes = 240;
    public const int MaxRangeDays = 62;

    public static bool ValidLength(DateTime start, DateTime end)
    {
        var minutes = (end - start).TotalMinutes;
        return minutes >= MinMinutes && minutes <= MaxMinutes;
    }

    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd) => aStart < bEnd && bStart < aEnd;

    /// <summary>Bookable from the portal: open, in the future beyond the lead time and within the horizon.</summary>
    public static bool WithinBookingWindow(DateTime startsAt, DateTime now, int minLeadHours, int horizonDays) =>
        startsAt >= now.AddHours(minLeadHours) && startsAt <= now.AddDays(horizonDays);

    /// <summary>
    /// Expands a weekly pattern into UTC slot windows. Dates and times are wall-clock in <paramref name="zone"/>; a time that does not
    /// exist in the zone (DST gap) is skipped.
    /// </summary>
    public static IEnumerable<(DateTime Start, DateTime End)> Expand(DateOnly from, DateOnly to, IReadOnlySet<DayOfWeek> weekdays,
        TimeOnly startTime, TimeOnly endTime, int durationMinutes, TimeZoneInfo zone)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (!weekdays.Contains(d.DayOfWeek)) continue;
            for (var t = startTime; ; )
            {
                var end = t.AddMinutes(durationMinutes);
                if (end <= t || end > endTime) break; // wrapped past midnight or beyond the window
                var localStart = d.ToDateTime(t, DateTimeKind.Unspecified);
                var localEnd = d.ToDateTime(end, DateTimeKind.Unspecified);
                if (!zone.IsInvalidTime(localStart) && !zone.IsInvalidTime(localEnd))
                    yield return (TimeZoneInfo.ConvertTimeToUtc(localStart, zone), TimeZoneInfo.ConvertTimeToUtc(localEnd, zone));
                t = end;
            }
        }
    }
}

public static class TicketRules
{
    /// <summary>Status after a public message: staff reply waits on the requester; a requester reply reopens.</summary>
    public static TicketStatus AfterMessage(TicketStatus current, bool fromStaff, bool isInternal)
    {
        if (isInternal) return current;
        if (fromStaff) return TicketStatus.WaitingOnRequester;
        return current is TicketStatus.WaitingOnRequester or TicketStatus.Resolved ? TicketStatus.Open : current;
    }
}
