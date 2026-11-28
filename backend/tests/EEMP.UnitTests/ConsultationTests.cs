using EEMP.Modules.Consultation.Application;
using EEMP.Modules.Consultation.Domain;

namespace EEMP.UnitTests;

public class ConsultationRuleTests
{
    private static readonly DateTime Start = new(2026, 12, 1, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(BookingStatus.Requested, BookingAction.Confirm, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Requested, BookingAction.Cancel, BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Confirmed, BookingAction.Cancel, BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Confirmed, BookingAction.Complete, BookingStatus.Completed)]
    [InlineData(BookingStatus.Confirmed, BookingAction.NoShow, BookingStatus.NoShow)]
    public void Valid_booking_transitions(BookingStatus from, BookingAction action, BookingStatus to) =>
        Assert.Equal(to, BookingMachine.Next(from, action));

    [Theory]
    [InlineData(BookingStatus.Requested, BookingAction.Complete)]
    [InlineData(BookingStatus.Requested, BookingAction.NoShow)]
    [InlineData(BookingStatus.Confirmed, BookingAction.Confirm)]
    [InlineData(BookingStatus.Completed, BookingAction.Cancel)]
    [InlineData(BookingStatus.Cancelled, BookingAction.Confirm)]
    [InlineData(BookingStatus.NoShow, BookingAction.Complete)]
    public void Invalid_booking_transitions_are_rejected(BookingStatus from, BookingAction action) =>
        Assert.Null(BookingMachine.Next(from, action));

    [Fact]
    public void Completion_is_allowed_from_shortly_before_start_and_no_show_only_after_start()
    {
        Assert.Equal(ConsultationErrors.SessionNotStarted, BookingMachine.CheckTiming(BookingAction.Complete, Start, Start.AddMinutes(-16)));
        Assert.Null(BookingMachine.CheckTiming(BookingAction.Complete, Start, Start.AddMinutes(-15)));
        Assert.Equal(ConsultationErrors.SessionNotStarted, BookingMachine.CheckTiming(BookingAction.NoShow, Start, Start.AddMinutes(-1)));
        Assert.Null(BookingMachine.CheckTiming(BookingAction.NoShow, Start, Start));
        Assert.Null(BookingMachine.CheckTiming(BookingAction.Cancel, Start, Start.AddDays(-5)));
    }

    [Fact]
    public void Requester_may_cancel_only_until_the_window_and_only_active_bookings()
    {
        var b = new Booking { StartsAt = Start, Status = BookingStatus.Confirmed };
        Assert.Equal(Start.AddHours(-24), BookingMachine.CancelDeadline(Start, 24));
        Assert.True(BookingMachine.RequesterMayCancel(b, 24, Start.AddHours(-24)));
        Assert.False(BookingMachine.RequesterMayCancel(b, 24, Start.AddHours(-23.9)));
        Assert.True(BookingMachine.RequesterMayCancel(b, 0, Start));
        b.Status = BookingStatus.Completed;
        Assert.False(BookingMachine.RequesterMayCancel(b, 24, Start.AddDays(-3)));
    }

    [Fact]
    public void Slot_length_overlap_and_booking_window()
    {
        Assert.True(SlotRules.ValidLength(Start, Start.AddMinutes(15)));
        Assert.False(SlotRules.ValidLength(Start, Start.AddMinutes(14)));
        Assert.False(SlotRules.ValidLength(Start, Start.AddMinutes(241)));
        Assert.True(SlotRules.Overlaps(Start, Start.AddMinutes(30), Start.AddMinutes(29), Start.AddMinutes(60)));
        Assert.False(SlotRules.Overlaps(Start, Start.AddMinutes(30), Start.AddMinutes(30), Start.AddMinutes(60)));
        var now = Start.AddHours(-3);
        Assert.True(SlotRules.WithinBookingWindow(Start, now, 2, 30));
        Assert.False(SlotRules.WithinBookingWindow(Start, now, 4, 30));
        Assert.False(SlotRules.WithinBookingWindow(Start.AddDays(31), now, 2, 30));
    }

    [Fact]
    public void Weekly_pattern_expands_in_the_tenant_time_zone()
    {
        var tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        // 2026-12-05 is a Saturday. 10:00–12:00 Tehran (UTC+03:30) in 30-minute steps = 4 slots per selected day.
        var slots = SlotRules.Expand(new DateOnly(2026, 12, 5), new DateOnly(2026, 12, 11),
            new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Monday }, new TimeOnly(10, 0), new TimeOnly(12, 0), 30, tehran).ToList();
        Assert.Equal(8, slots.Count);
        Assert.Equal(new DateTime(2026, 12, 5, 6, 30, 0, DateTimeKind.Utc), slots[0].Start);
        Assert.Equal(new DateTime(2026, 12, 5, 7, 0, 0, DateTimeKind.Utc), slots[0].End);
        Assert.Equal(new DateTime(2026, 12, 7, 8, 0, 0, DateTimeKind.Utc), slots[^1].Start);
        Assert.All(slots, s => Assert.Equal(DateTimeKind.Utc, s.Start.Kind));
        // A duration that does not fit leaves no partial slot.
        Assert.Single(SlotRules.Expand(new DateOnly(2026, 12, 5), new DateOnly(2026, 12, 5), new HashSet<DayOfWeek> { DayOfWeek.Saturday },
            new TimeOnly(10, 0), new TimeOnly(11, 30), 45, tehran).Skip(1));
    }

    [Theory]
    [InlineData(TicketStatus.Open, true, false, TicketStatus.WaitingOnRequester)]
    [InlineData(TicketStatus.InProgress, true, true, TicketStatus.InProgress)]
    [InlineData(TicketStatus.WaitingOnRequester, false, false, TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved, false, false, TicketStatus.Open)]
    [InlineData(TicketStatus.InProgress, false, false, TicketStatus.InProgress)]
    public void Ticket_status_follows_the_conversation(TicketStatus current, bool fromStaff, bool isInternal, TicketStatus expected) =>
        Assert.Equal(expected, TicketRules.AfterMessage(current, fromStaff, isInternal));

    [Theory]
    [InlineData("inPerson", true)]
    [InlineData("online", true)]
    [InlineData("1", false)]
    [InlineData("phone", false)]
    [InlineData(null, false)]
    public void Enum_values_parse_strictly_from_camel_case(string? value, bool ok) =>
        Assert.Equal(ok, Fmt.TryEnum<SessionMode>(value, out _));
}
