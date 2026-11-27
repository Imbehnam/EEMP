using System.Globalization;
using System.Linq.Expressions;
using EEMP.Modules.Consultation.Domain;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Consultation.Application.Fmt;

namespace EEMP.Modules.Consultation.Application;

/// <summary>
/// Availability slots. A consultant manages their own slots in branches where they hold consultation.consult and that their profile
/// serves; holders of consultation.consultants.manage manage any consultant's slots in their branches.
/// </summary>
public sealed class SlotService(IConsultationDbContext db, ConsultationActor actor, IConsultationClock clock, IPeopleDirectory people)
{
    public const int MaxLocation = 200;

    public async Task<IReadOnlyList<SlotDto>> ListAsync(DateTime? from, DateTime? to, Guid? consultantId, Guid? branchId, CancellationToken ct)
    {
        var v = new Validator();
        var (start, end) = Range(from, to, v);
        v.ThrowIfInvalid();
        var filter = await VisibleAsync(ct);
        var q = db.Slots.AsNoTracking().Where(filter).Where(s => s.StartsAt < end && s.EndsAt > start);
        if (consultantId is { } cid) q = q.Where(s => s.ConsultantId == cid);
        if (branchId is { } bid) q = q.Where(s => s.BranchId == bid);
        var rows = await q.OrderBy(s => s.StartsAt).Take(1000).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<SlotDto> CreateAsync(CreateSlotRequest req, CancellationToken ct)
    {
        var location = Clean(req.Location);
        var v = new Validator().OptionalText("location", location, MaxLocation);
        if (req.StartsAt is null) v.Add("startsAt", Validator.Required);
        if (req.EndsAt is null) v.Add("endsAt", Validator.Required);
        if (!TryEnum<SessionMode>(req.Mode, out var mode)) v.Add("mode", Validator.InvalidValue);
        var startsAt = req.StartsAt?.ToUniversalTime() ?? default;
        var endsAt = req.EndsAt?.ToUniversalTime() ?? default;
        if (req.StartsAt is not null && req.EndsAt is not null && !SlotRules.ValidLength(startsAt, endsAt)) v.Add("endsAt", Validator.InvalidValue);
        v.ThrowIfInvalid();

        var consultant = await AuthorizeWriteAsync(req.ConsultantId, req.BranchId, ct);
        if (startsAt < clock.UtcNow) throw new BusinessRuleException(ConsultationErrors.SlotInPast);
        if (await db.Slots.AnyAsync(s => s.ConsultantId == consultant.Id && s.Status == SlotStatus.Open && s.StartsAt < endsAt && s.EndsAt > startsAt, ct))
            throw new ConflictException(ConsultationErrors.SlotOverlap);
        var slot = new Slot { ConsultantId = consultant.Id, BranchId = req.BranchId, StartsAt = startsAt, EndsAt = endsAt, Mode = mode, Location = location };
        db.Slots.Add(slot);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([slot], ct))[0];
    }

    public async Task<BulkSlotsResult> BulkAsync(BulkSlotsRequest req, CancellationToken ct)
    {
        var location = Clean(req.Location);
        var v = new Validator().OptionalText("location", location, MaxLocation);
        var fromDate = Validator.ParseDate(req.FromDate, "fromDate", v);
        var toDate = Validator.ParseDate(req.ToDate, "toDate", v);
        if (req.FromDate is null) v.Add("fromDate", Validator.Required);
        if (req.ToDate is null) v.Add("toDate", Validator.Required);
        if (fromDate is { } f && toDate is { } t && (t < f || t.DayNumber - f.DayNumber > SlotRules.MaxRangeDays)) v.Add("toDate", Validator.InvalidValue);
        var startTime = ParseTime(req.StartTime, "startTime", v);
        var endTime = ParseTime(req.EndTime, "endTime", v);
        if (startTime is { } st && endTime is { } et && et <= st) v.Add("endTime", Validator.InvalidValue);
        if (req.DurationMinutes is < SlotRules.MinMinutes or > SlotRules.MaxMinutes) v.Add("durationMinutes", Validator.InvalidValue);
        if (!TryEnum<SessionMode>(req.Mode, out var mode)) v.Add("mode", Validator.InvalidValue);
        var days = new HashSet<DayOfWeek>();
        foreach (var w in req.Weekdays ?? [])
            if (TryEnum<DayOfWeek>(w, out var d)) days.Add(d);
            else v.Add("weekdays", Validator.InvalidValue);
        if (days.Count == 0) v.Add("weekdays", Validator.Required);
        v.ThrowIfInvalid();

        var consultant = await AuthorizeWriteAsync(req.ConsultantId, req.BranchId, ct);
        var tz = await actor.TimeZoneAsync(ct);
        var now = clock.UtcNow;
        var windows = SlotRules.Expand(fromDate!.Value, toDate!.Value, days, startTime!.Value, endTime!.Value, req.DurationMinutes, tz).ToList();
        if (windows.Count == 0) return new BulkSlotsResult(0, 0, []);
        var min = windows.Min(w => w.Start);
        var max = windows.Max(w => w.End);
        var existing = await db.Slots.AsNoTracking()
            .Where(s => s.ConsultantId == consultant.Id && s.Status == SlotStatus.Open && s.StartsAt < max && s.EndsAt > min)
            .Select(s => new { s.StartsAt, s.EndsAt }).ToListAsync(ct);
        var created = new List<Slot>();
        var skipped = 0;
        foreach (var (start, end) in windows)
        {
            if (start < now || existing.Any(e => SlotRules.Overlaps(e.StartsAt, e.EndsAt, start, end))) { skipped++; continue; }
            created.Add(new Slot { ConsultantId = consultant.Id, BranchId = req.BranchId, StartsAt = start, EndsAt = end, Mode = mode, Location = location });
        }
        db.Slots.AddRange(created);
        await db.SaveChangesAsync(ct);
        return new BulkSlotsResult(created.Count, skipped, await ToDtosAsync(created, ct));
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException();
        if (!await actor.IsOwnAsync(slot.ConsultantId, slot.BranchId, ct)
            && !(await actor.ScopeAsync(Permissions.ConsultationConsultantsManage, ct)).Includes(slot.BranchId))
        {
            if (await CanSeeAsync(slot, ct)) throw new ForbiddenException();
            throw new NotFoundException();
        }
        if (slot.Status == SlotStatus.Cancelled) return;
        if (await db.Bookings.AnyAsync(b => b.SlotId == id && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Confirmed), ct))
            throw new ConflictException(ConsultationErrors.SlotHasBookings);
        slot.Status = SlotStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> CanSeeAsync(Slot slot, CancellationToken ct) =>
        (await actor.AnyScopeAsync(ct, Permissions.ConsultationBookingsRead, Permissions.ConsultationBookingsManage)).Includes(slot.BranchId);

    /// <summary>The consultant whose slots may be written for the branch, or 403/404/422.</summary>
    private async Task<Consultant> AuthorizeWriteAsync(Guid consultantId, Guid branchId, CancellationToken ct)
    {
        var consultant = await db.Consultants.AsNoTracking().Include(c => c.Branches).FirstOrDefaultAsync(c => c.Id == consultantId, ct);
        var manage = await actor.ScopeAsync(Permissions.ConsultationConsultantsManage, ct);
        var own = consultant is not null && await actor.IsOwnAsync(consultant.Id, branchId, ct);
        if (!own && !manage.Includes(branchId))
        {
            if (consultant is null || !consultant.Branches.Any(b => manage.Includes(b.BranchId))) throw new NotFoundException();
            throw new ForbiddenException(ErrorCodes.ForbiddenScope);
        }
        if (consultant is null) throw new NotFoundException();
        if (!consultant.IsActive) throw new BusinessRuleException(ConsultationErrors.ConsultantInactive);
        if (!consultant.Serves(branchId)) throw new BusinessRuleException(ConsultationErrors.BranchNotServed);
        return consultant;
    }

    /// <summary>Slots the caller may see: own (consultant) or in branches with manage/read permissions.</summary>
    internal async Task<Expression<Func<Slot, bool>>> VisibleAsync(CancellationToken ct)
    {
        var scope = await actor.AnyScopeAsync(ct, Permissions.ConsultationConsultantsManage, Permissions.ConsultationBookingsRead,
            Permissions.ConsultationBookingsManage);
        Expression<Func<Slot, bool>> filter = ScopeFilter.Covers<Slot>(scope, s => s.BranchId);
        var me = await actor.ConsultantAsync(ct);
        var consult = await actor.ScopeAsync(Permissions.ConsultationConsult, ct);
        if (me is not null && !consult.IsEmpty)
        {
            var myId = me.Id;
            filter = filter.Or(ScopeFilter.Covers<Slot>(consult, s => s.BranchId).AndAlso(s => s.ConsultantId == myId));
        }
        if (scope.IsEmpty && (me is null || consult.IsEmpty)) throw new ForbiddenException();
        return filter;
    }

    private async Task<List<SlotDto>> ToDtosAsync(List<Slot> rows, CancellationToken ct)
    {
        var consultantIds = rows.Select(r => r.ConsultantId).Distinct().ToList();
        var names = await db.Consultants.AsNoTracking().Where(c => consultantIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        var slotIds = rows.Select(r => r.Id).ToList();
        var bookings = await db.Bookings.AsNoTracking().Where(b => slotIds.Contains(b.SlotId) && b.Status != BookingStatus.Cancelled).ToListAsync(ct);
        var students = await people.GetStudentsAsync(bookings.Select(b => b.StudentId).Distinct().ToList(), ct);
        var byslot = bookings.GroupBy(b => b.SlotId).ToDictionary(g => g.Key, g => g.First());
        return rows.Select(s =>
        {
            var b = byslot.GetValueOrDefault(s.Id);
            return new SlotDto(s.Id, s.ConsultantId, names.GetValueOrDefault(s.ConsultantId, ""), s.BranchId, s.StartsAt, s.EndsAt, Camel(s.Mode), s.Location,
                Camel(s.Status), b is null ? null : new SlotBookingSummaryDto(b.Id, Camel(b.Status), StudentName(students.GetValueOrDefault(b.StudentId)), b.Topic),
                Rv(s.RowVersion));
        }).ToList();
    }

    internal static (DateTime Start, DateTime End) Range(DateTime? from, DateTime? to, Validator v)
    {
        var start = from?.ToUniversalTime() ?? DateTime.UtcNow.Date.AddDays(-1);
        var end = to?.ToUniversalTime() ?? start.AddDays(8);
        if (end <= start || (end - start).TotalDays > SlotRules.MaxRangeDays) v.Add("to", Validator.InvalidValue);
        return (start, end);
    }

    private static TimeOnly? ParseTime(string? value, string field, Validator v)
    {
        if (string.IsNullOrWhiteSpace(value)) { v.Add(field, Validator.Required); return null; }
        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return t;
        v.Add(field, Validator.InvalidFormat);
        return null;
    }
}

internal static class ExpressionAnd
{
    public static Expression<Func<T, bool>> AndAlso<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var p = left.Parameters[0];
        var body = new Swap(right.Parameters[0], p).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, body), p);
    }

    private sealed class Swap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
