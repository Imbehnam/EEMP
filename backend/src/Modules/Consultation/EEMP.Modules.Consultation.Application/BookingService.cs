using System.Linq.Expressions;
using EEMP.Modules.Consultation.Domain;
using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Consultation.Application.Fmt;

namespace EEMP.Modules.Consultation.Application;

/// <summary>
/// Staff/consultant view of bookings and the confidential notes (DL-092, DL-093). A booking is visible to its consultant and to holders
/// of bookings.read/bookings.manage/notes.read in its branch; anyone else gets 404. Notes are a separate resource with stricter rules.
/// </summary>
public sealed class BookingService(IConsultationDbContext db, ConsultationActor actor, IConsultationClock clock, IPeopleDirectory people,
    IUserDirectory users, INotesAccessLog notesLog)
{
    public const int MaxReason = 500;
    public const int MaxNote = 4000;

    private sealed record Access(bool Visible, bool Own, bool Manage, bool NotesRead);

    private async Task<Access> EvaluateAsync(Booking b, CancellationToken ct)
    {
        var own = await actor.IsOwnAsync(b.ConsultantId, b.BranchId, ct);
        var manage = (await actor.ScopeAsync(Permissions.ConsultationBookingsManage, ct)).Includes(b.BranchId);
        var notes = (await actor.ScopeAsync(Permissions.ConsultationNotesRead, ct)).Includes(b.BranchId);
        var read = (await actor.ScopeAsync(Permissions.ConsultationBookingsRead, ct)).Includes(b.BranchId);
        return new Access(own || manage || notes || read, own, manage, notes);
    }

    private async Task<(Booking Booking, Access Access)> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = track ? db.Bookings : db.Bookings.AsNoTracking();
        var b = await q.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var a = await EvaluateAsync(b, ct);
        if (!a.Visible) throw new NotFoundException();
        return (b, a);
    }

    public async Task<PagedResult<BookingDto>> ListAsync(PageRequest page, BookingListQuery query, CancellationToken ct)
    {
        var v = new Validator();
        BookingStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (TryEnum<BookingStatus>(query.Status, out var s)) status = s;
            else v.Add("status", Validator.InvalidValue);
        }
        var scope = string.IsNullOrWhiteSpace(query.Scope) ? "all" : query.Scope;
        if (scope is not ("all" or "mine")) v.Add("scope", Validator.InvalidValue);
        v.ThrowIfInvalid();

        Expression<Func<Booking, bool>> filter = b => false;
        var me = await actor.ConsultantAsync(ct);
        var consult = await actor.ScopeAsync(Permissions.ConsultationSessionsManage, ct);
        if (me is not null && !consult.IsEmpty)
        {
            var myId = me.Id;
            filter = ScopeFilter.Covers<Booking>(consult, b => b.BranchId).AndAlso(b => b.ConsultantId == myId);
        }
        if (scope == "all")
        {
            var read = await actor.AnyScopeAsync(ct, Permissions.ConsultationBookingsRead, Permissions.ConsultationBookingsManage, Permissions.ConsultationNotesRead);
            if (read.IsEmpty && (me is null || consult.IsEmpty)) throw new ForbiddenException();
            filter = filter.Or(ScopeFilter.Covers<Booking>(read, b => b.BranchId));
        }
        else if (me is null || consult.IsEmpty) throw new ForbiddenException();

        var q = db.Bookings.AsNoTracking().Where(filter);
        if (query.From is { } from) { var f = from.ToUniversalTime(); q = q.Where(b => b.EndsAt > f); }
        if (query.To is { } to) { var t = to.ToUniversalTime(); q = q.Where(b => b.StartsAt < t); }
        if (query.BranchId is { } bid) q = q.Where(b => b.BranchId == bid);
        if (query.ConsultantId is { } cid) q = q.Where(b => b.ConsultantId == cid);
        if (status is { } st) q = q.Where(b => b.Status == st);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(b => b.StartsAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<BookingDto>(await ToDtosAsync(rows, ct), page.Page, page.PageSize, total);
    }

    public async Task<BookingDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var (b, a) = await LoadAsync(id, false, ct);
        return await DetailAsync(b, a, ct);
    }

    public async Task<BookingDetailDto> ActAsync(Guid id, BookingAction action, BookingActionRequest req, CancellationToken ct)
    {
        var reason = Clean(req.Reason);
        new Validator().RowVersion("rowVersion", req.RowVersion).OptionalText("reason", reason, MaxReason).ThrowIfInvalid();
        var (b, a) = await LoadAsync(id, true, ct);
        if (!a.Own && !a.Manage) throw new ForbiddenException();
        var next = BookingMachine.Next(b.Status, action)
                   ?? throw new ConflictException(ConsultationErrors.InvalidTransition, P(("from", Camel(b.Status)), ("action", Camel(action))));
        var now = clock.UtcNow;
        if (BookingMachine.CheckTiming(action, b.StartsAt, now) is { } error) throw new BusinessRuleException(error);
        db.SetOriginalRowVersion(b, Convert.FromBase64String(req.RowVersion!));
        Apply(b, next, actor.UserId, now, reason, CancelledBy.Staff);
        await db.SaveChangesAsync(ct);
        return await DetailAsync(b, a, ct);
    }

    internal static void Apply(Booking b, BookingStatus next, Guid userId, DateTime now, string? reason, CancelledBy by)
    {
        b.Status = next;
        b.StatusChangedByUserId = userId;
        switch (next)
        {
            case BookingStatus.Confirmed: b.ConfirmedAt = now; break;
            case BookingStatus.Completed: b.CompletedAt = now; break;
            case BookingStatus.Cancelled:
                b.CancelledAt = now;
                b.CancelledByUserId = userId;
                b.CancelledBy = by;
                b.CancelReason = reason;
                break;
        }
    }

    public async Task<IReadOnlyList<NoteDto>> NotesAsync(Guid id, CancellationToken ct)
    {
        var (b, a) = await LoadAsync(id, false, ct);
        if (!a.Own && !a.NotesRead) throw new ForbiddenException(ConsultationErrors.NotesForbidden);
        var notes = await db.BookingNotes.AsNoTracking().Where(n => n.BookingId == b.Id).OrderBy(n => n.CreatedAt).ToListAsync(ct);
        await notesLog.NotesReadAsync(b.Id, notes.Count, ct);
        var names = await users.GetDisplayNamesAsync(notes.Select(n => n.AuthorUserId).Distinct().ToList(), ct);
        return notes.Select(n => new NoteDto(n.Id, n.AuthorUserId, names.GetValueOrDefault(n.AuthorUserId, ""), n.Body, n.CreatedAt)).ToList();
    }

    public async Task<NoteDto> AddNoteAsync(Guid id, CreateNoteRequest req, CancellationToken ct)
    {
        var body = Clean(req.Body);
        new Validator().RequiredText("body", body, MaxNote).ThrowIfInvalid();
        var (b, a) = await LoadAsync(id, false, ct);
        if (!a.Own) throw new ForbiddenException(ConsultationErrors.NotBookingConsultant);
        if (b.Status is not (BookingStatus.Confirmed or BookingStatus.Completed))
            throw new ConflictException(ConsultationErrors.InvalidTransition, P(("from", Camel(b.Status)), ("action", "note")));
        var note = new BookingNote { BookingId = b.Id, AuthorUserId = actor.UserId, Body = body!, CreatedAt = clock.UtcNow };
        db.BookingNotes.Add(note);
        await db.SaveChangesAsync(ct);
        var names = await users.GetDisplayNamesAsync([note.AuthorUserId], ct);
        return new NoteDto(note.Id, note.AuthorUserId, names.GetValueOrDefault(note.AuthorUserId, ""), note.Body, note.CreatedAt);
    }

    private async Task<BookingDetailDto> DetailAsync(Booking b, Access a, CancellationToken ct)
    {
        var actions = new List<string>();
        if (a.Own || a.Manage)
            foreach (var action in Enum.GetValues<BookingAction>())
                if (BookingMachine.Next(b.Status, action) is not null) actions.Add(Camel(action));
        var canWrite = a.Own && b.Status is BookingStatus.Confirmed or BookingStatus.Completed;
        return new BookingDetailDto((await ToDtosAsync([b], ct))[0], actions, a.Own || a.NotesRead, canWrite);
    }

    internal async Task<List<BookingDto>> ToDtosAsync(List<Booking> rows, CancellationToken ct)
    {
        var cids = rows.Select(r => r.ConsultantId).Distinct().ToList();
        var names = await db.Consultants.AsNoTracking().Where(c => cids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        var students = await people.GetStudentsAsync(rows.Select(r => r.StudentId).Distinct().ToList(), ct);
        return rows.Select(b => new BookingDto(b.Id, b.SlotId, b.ConsultantId, names.GetValueOrDefault(b.ConsultantId, ""), b.BranchId, b.StudentId,
            StudentName(students.GetValueOrDefault(b.StudentId)), b.BookedByUserId, b.StartsAt, b.EndsAt, Camel(b.Mode), b.Location, b.Topic, b.RequesterNote,
            Camel(b.Status), b.ConfirmedAt, b.CompletedAt, b.CancelledAt, Camel(b.CancelledBy), b.CancelReason, b.CreatedAt, Rv(b.RowVersion))).ToList();
    }
}
