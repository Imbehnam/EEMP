using EEMP.Modules.Consultation.Domain;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Consultation.Application.Fmt;

namespace EEMP.Modules.Consultation.Application;

/// <summary>
/// Student/guardian booking (DL-092). The relationship is the authorization: a guardian acts for linked students, a student for
/// themself. Only the student's own branch is bookable. The slot is reserved atomically by the unique filtered index on bookings.
/// </summary>
public sealed class PortalConsultationService(IConsultationDbContext db, ConsultationActor actor, IConsultationClock clock, IPeopleDirectory people)
{
    public const int MaxTopic = 200;
    public const int MaxNote = 1000;

    public async Task<IReadOnlyList<PortalConsultantDto>> ConsultantsAsync(Guid studentId, CancellationToken ct)
    {
        var student = await actor.PortalStudentAsync(studentId, ct);
        var settings = await SettingsService.CurrentAsync(db, ct);
        var consultants = await db.Consultants.AsNoTracking().Where(c => c.IsActive && c.Branches.Any(b => b.BranchId == student.BranchId))
            .OrderBy(c => c.DisplayName).ToListAsync(ct);
        var slots = await BookableQuery(student.BranchId, settings).ToListAsync(ct);
        return consultants.Select(c => new PortalConsultantDto(c.Id, c.DisplayName, c.Bio, c.Specialties,
            slots.Where(s => s.ConsultantId == c.Id).Select(s => (DateTime?)s.StartsAt).Min())).ToList();
    }

    public async Task<IReadOnlyList<PortalSlotDto>> SlotsAsync(Guid studentId, Guid? consultantId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var student = await actor.PortalStudentAsync(studentId, ct);
        var settings = await SettingsService.CurrentAsync(db, ct);
        var q = BookableQuery(student.BranchId, settings);
        if (consultantId is { } cid) q = q.Where(s => s.ConsultantId == cid);
        if (from is { } f) { var fu = f.ToUniversalTime(); q = q.Where(s => s.StartsAt >= fu); }
        if (to is { } t) { var tu = t.ToUniversalTime(); q = q.Where(s => s.StartsAt < tu); }
        var rows = await q.OrderBy(s => s.StartsAt).Take(500).ToListAsync(ct);
        var cids = rows.Select(r => r.ConsultantId).Distinct().ToList();
        var names = await db.Consultants.AsNoTracking().Where(c => cids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        return rows.Select(s => new PortalSlotDto(s.Id, s.ConsultantId, names.GetValueOrDefault(s.ConsultantId, ""), s.BranchId, s.StartsAt, s.EndsAt,
            Camel(s.Mode), s.Location)).ToList();
    }

    /// <summary>Open slots in the branch, of active consultants serving it, with no active booking, inside the booking window.</summary>
    private IQueryable<Slot> BookableQuery(Guid branchId, ConsultationSettings settings)
    {
        var now = clock.UtcNow;
        var earliest = now.AddHours(settings.MinLeadHours);
        var latest = now.AddDays(settings.BookingHorizonDays);
        var bookings = db.Bookings;
        var consultants = db.Consultants;
        return db.Slots.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.Status == SlotStatus.Open && s.StartsAt >= earliest && s.StartsAt <= latest)
            .Where(s => consultants.Any(c => c.Id == s.ConsultantId && c.IsActive && c.Branches.Any(b => b.BranchId == branchId)))
            .Where(s => !bookings.Any(b => b.SlotId == s.Id && b.Status != BookingStatus.Cancelled));
    }

    public async Task<PortalBookingDto> BookAsync(PortalBookRequest req, CancellationToken ct)
    {
        var topic = Clean(req.Topic);
        var note = Clean(req.Note);
        new Validator().RequiredText("topic", topic, MaxTopic).OptionalText("note", note, MaxNote).ThrowIfInvalid();
        var student = await actor.PortalStudentAsync(req.StudentId, ct);
        var settings = await SettingsService.CurrentAsync(db, ct);
        var slot = await db.Slots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SlotId, ct) ?? throw new NotFoundException();
        if (slot.BranchId != student.BranchId) throw new BusinessRuleException(ConsultationErrors.BranchNotServed);
        var consultant = await db.Consultants.AsNoTracking().Include(c => c.Branches).FirstOrDefaultAsync(c => c.Id == slot.ConsultantId, ct);
        if (slot.Status != SlotStatus.Open || consultant is null || !consultant.IsActive || !consultant.Serves(slot.BranchId))
            throw new ConflictException(ConsultationErrors.SlotUnavailable);
        var now = clock.UtcNow;
        if (!SlotRules.WithinBookingWindow(slot.StartsAt, now, settings.MinLeadHours, settings.BookingHorizonDays))
            throw new BusinessRuleException(ConsultationErrors.OutsideBookingWindow);
        if (await db.Bookings.AnyAsync(b => b.SlotId == slot.Id && b.Status != BookingStatus.Cancelled, ct))
            throw new ConflictException(ConsultationErrors.SlotUnavailable);
        var active = await db.Bookings.CountAsync(b => b.StudentId == student.Id && b.StartsAt > now
                                                       && (b.Status == BookingStatus.Requested || b.Status == BookingStatus.Confirmed), ct);
        if (active >= settings.MaxActiveBookingsPerStudent)
            throw new BusinessRuleException(ConsultationErrors.BookingLimitReached, P(("max", settings.MaxActiveBookingsPerStudent)));

        var booking = new Booking
        {
            SlotId = slot.Id, ConsultantId = slot.ConsultantId, BranchId = slot.BranchId, StudentId = student.Id, BookedByUserId = actor.UserId,
            StartsAt = slot.StartsAt, EndsAt = slot.EndsAt, Mode = slot.Mode, Location = slot.Location, Topic = topic!, RequesterNote = note,
        };
        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // Lost the race for the slot: the unique filtered index (TenantId, SlotId) WHERE Status <> 'Cancelled' rejected the insert.
            db.ResetTracking();
            throw new ConflictException(ConsultationErrors.SlotUnavailable);
        }
        return (await ToDtosAsync([booking], settings, ct))[0];
    }

    public async Task<IReadOnlyList<PortalBookingDto>> ListAsync(Guid? studentId, CancellationToken ct)
    {
        var ids = await actor.PortalStudentIdsAsync(ct);
        if (studentId is { } sid)
        {
            if (!ids.Contains(sid)) throw new NotFoundException();
            ids = new HashSet<Guid> { sid };
        }
        var list = ids.ToList();
        var settings = await SettingsService.CurrentAsync(db, ct);
        var rows = await db.Bookings.AsNoTracking().Where(b => list.Contains(b.StudentId)).OrderByDescending(b => b.StartsAt).Take(200).ToListAsync(ct);
        return await ToDtosAsync(rows, settings, ct);
    }

    public async Task<PortalBookingDto> CancelAsync(Guid id, PortalCancelRequest req, CancellationToken ct)
    {
        var reason = Clean(req.Reason);
        new Validator().RowVersion("rowVersion", req.RowVersion).OptionalText("reason", reason, BookingService.MaxReason).ThrowIfInvalid();
        var ids = await actor.PortalStudentIdsAsync(ct);
        var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null || !ids.Contains(b.StudentId)) throw new NotFoundException();
        var settings = await SettingsService.CurrentAsync(db, ct);
        if (BookingMachine.Next(b.Status, BookingAction.Cancel) is null)
            throw new ConflictException(ConsultationErrors.InvalidTransition, P(("from", Camel(b.Status)), ("action", "cancel")));
        var now = clock.UtcNow;
        if (!BookingMachine.RequesterMayCancel(b, settings.CancellationWindowHours, now))
            throw new BusinessRuleException(ConsultationErrors.CancellationWindowPassed, P(("hours", settings.CancellationWindowHours)));
        db.SetOriginalRowVersion(b, Convert.FromBase64String(req.RowVersion!));
        BookingService.Apply(b, BookingStatus.Cancelled, actor.UserId, now, reason, CancelledBy.Requester);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([b], settings, ct))[0];
    }

    private async Task<List<PortalBookingDto>> ToDtosAsync(List<Booking> rows, ConsultationSettings settings, CancellationToken ct)
    {
        var cids = rows.Select(r => r.ConsultantId).Distinct().ToList();
        var names = await db.Consultants.AsNoTracking().Where(c => cids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        var students = await people.GetStudentsAsync(rows.Select(r => r.StudentId).Distinct().ToList(), ct);
        var now = clock.UtcNow;
        return rows.Select(b => new PortalBookingDto(b.Id, b.StudentId, StudentName(students.GetValueOrDefault(b.StudentId)),
            names.GetValueOrDefault(b.ConsultantId, ""), b.BranchId, b.StartsAt, b.EndsAt, Camel(b.Mode), b.Location, b.Topic, b.RequesterNote,
            Camel(b.Status), Camel(b.CancelledBy), b.CancelReason, BookingMachine.RequesterMayCancel(b, settings.CancellationWindowHours, now),
            BookingMachine.CancelDeadline(b.StartsAt, settings.CancellationWindowHours), b.CreatedAt, Rv(b.RowVersion))).ToList();
    }
}
