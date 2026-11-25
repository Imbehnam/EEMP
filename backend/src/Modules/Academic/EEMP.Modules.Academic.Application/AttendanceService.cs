using EEMP.Modules.Academic.Domain;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Academic.Application.Fmt;

namespace EEMP.Modules.Academic.Application;

public sealed record AttendanceListQuery(Guid? StudentId, Guid? ClassId, string? From, string? To, string? Status);

/// <summary>Attendance sheets per class/date/slot. Bulk marking is an idempotent upsert; assigned teachers may take attendance.</summary>
public sealed class AttendanceService(IAcademicDbContext db, IAccessControl access, ClassAccess classAccess, AcademicActor actor, IPeopleDirectory people)
{
    public async Task<AttendanceSheetDto> GetSheetAsync(Guid classId, string? date, Guid? scheduleSlotId, CancellationToken ct)
    {
        var v = new Validator();
        var d = RequiredDate(date, "date", v);
        v.ThrowIfInvalid();
        var cls = await classAccess.AuthorizeAsync(classId, Permissions.AttendanceRead, true, Permissions.AttendanceRead, ct);
        var slotKey = scheduleSlotId ?? Guid.Empty;
        var session = await db.AttendanceSessions.AsNoTracking().Include(s => s.Records)
            .FirstOrDefaultAsync(s => s.ClassId == cls.Id && s.Date == d && s.ScheduleSlotId == slotKey, ct);
        return await SheetAsync(cls.Id, d!.Value, slotKey, session, ct);
    }

    public async Task<AttendanceSheetDto> MarkAsync(Guid classId, AttendanceBulkRequest req, CancellationToken ct)
    {
        var v = new Validator();
        var date = RequiredDate(req.Date, "date", v);
        if (req.RowVersion is not null) v.RowVersion("rowVersion", req.RowVersion);
        var marks = new List<(Guid StudentId, AttendanceStatus Status, string? Note)>();
        foreach (var (r, i) in (req.Records ?? []).Select((r, i) => (r, i)))
        {
            v.When(r.StudentId is null || r.StudentId == Guid.Empty, $"records[{i}].studentId", Validator.Required);
            var st = ParseEnum<AttendanceStatus>(r.Status, $"records[{i}].status", v, required: true);
            v.OptionalText($"records[{i}].note", r.Note, 500);
            if (r.StudentId is { } sid && sid != Guid.Empty && st is { } s) marks.Add((sid, s, Clean(r.Note)));
        }
        if (marks.Select(m => m.StudentId).Distinct().Count() != marks.Count) v.Add("records", Validator.InvalidValue);
        v.ThrowIfInvalid();

        var cls = await classAccess.AuthorizeAsync(classId, Permissions.AttendanceManage, true, Permissions.AttendanceRead, ct, includeDetails: true);
        var term = await db.Terms.AsNoTracking().FirstAsync(t => t.Id == cls.TermId, ct);
        if (!term.Contains(date!.Value)) throw new BusinessRuleException(AcademicErrors.DateOutsideTerm);
        if (date.Value > await actor.TodayAsync(ct)) throw new BusinessRuleException(AcademicErrors.AttendanceFutureDate);
        var slotKey = Guid.Empty;
        if (req.ScheduleSlotId is { } slotId && slotId != Guid.Empty)
        {
            var slot = cls.Schedule.FirstOrDefault(s => s.Id == slotId);
            if (slot is null) { v.Add("scheduleSlotId", AcademicErrors.SlotNotFound); v.ThrowIfInvalid(); }
            if (slot!.Weekday != date.Value.DayOfWeek) throw new BusinessRuleException(AcademicErrors.SlotWeekdayMismatch);
            slotKey = slotId;
        }
        var enrolled = (await db.Enrollments.AsNoTracking().Where(e => e.ClassId == cls.Id && e.Status == EnrollmentStatus.Active)
            .Select(e => e.StudentId).ToListAsync(ct)).ToHashSet();
        var notEnrolled = marks.FirstOrDefault(m => !enrolled.Contains(m.StudentId));
        if (notEnrolled.StudentId != Guid.Empty) throw new BusinessRuleException(AcademicErrors.StudentNotEnrolled, P(("studentId", notEnrolled.StudentId)));

        // Idempotent upsert. A concurrent first write for the same class/date/slot hits the unique index; retry once against the winner.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var session = await UpsertAsync(cls.Id, date.Value, slotKey, req.RowVersion, marks, ct);
                return await SheetAsync(cls.Id, date.Value, slotKey, session, ct);
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && attempt == 0)
            {
                db.ResetTracking();
            }
        }
    }

    private async Task<AttendanceSession> UpsertAsync(Guid classId, DateOnly date, Guid slotKey, string? rowVersion,
        List<(Guid StudentId, AttendanceStatus Status, string? Note)> marks, CancellationToken ct)
    {
        var now = actor.UtcNow;
        var session = await db.AttendanceSessions.Include(s => s.Records)
            .FirstOrDefaultAsync(s => s.ClassId == classId && s.Date == date && s.ScheduleSlotId == slotKey, ct);
        if (session is null)
        {
            session = new AttendanceSession { ClassId = classId, Date = date, ScheduleSlotId = slotKey, TakenByUserId = actor.UserId };
            db.AttendanceSessions.Add(session);
        }
        else if (rowVersion is not null)
            db.SetOriginalRowVersion(session, RowVersion(rowVersion));

        var changed = false;
        foreach (var (studentId, status, note) in marks)
        {
            var record = session.Records.FirstOrDefault(r => r.StudentId == studentId);
            if (record is null)
            {
                session.Records.Add(new AttendanceRecord { SessionId = session.Id, StudentId = studentId, Status = status, Note = note, MarkedByUserId = actor.UserId, MarkedAt = now });
                changed = true;
            }
            else if (record.Status != status || record.Note != note)
            {
                record.Status = status;
                record.Note = note;
                record.MarkedByUserId = actor.UserId;
                record.MarkedAt = now;
                changed = true;
            }
        }
        if (changed)
        {
            if (session.CreatedAt != default) { session.TakenByUserId = actor.UserId; session.UpdatedAt = now; }
            await db.SaveChangesAsync(ct);
        }
        else if (rowVersion is not null && session.CreatedAt != default && Rv(session.RowVersion) != rowVersion)
            throw new DbUpdateConcurrencyException();
        return session;
    }

    private async Task<AttendanceSheetDto> SheetAsync(Guid classId, DateOnly date, Guid slotKey, AttendanceSession? session, CancellationToken ct)
    {
        var enrolled = await db.Enrollments.AsNoTracking().Where(e => e.ClassId == classId && e.Status == EnrollmentStatus.Active).Select(e => e.StudentId).ToListAsync(ct);
        var records = session?.Records ?? [];
        var ids = enrolled.Union(records.Select(r => r.StudentId)).ToList();
        var students = await people.GetStudentsAsync(ids, ct);
        var entries = ids.Select(id =>
        {
            var s = students.GetValueOrDefault(id);
            var r = records.FirstOrDefault(x => x.StudentId == id);
            return new AttendanceEntryDto(id, s?.FirstName ?? "", s?.LastName ?? "", s?.StudentNumber ?? "", Camel(r?.Status), r?.Note);
        }).OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToList();
        var summary = new AttendanceSummaryDto(
            records.Count(r => r.Status == AttendanceStatus.Present), records.Count(r => r.Status == AttendanceStatus.Absent),
            records.Count(r => r.Status == AttendanceStatus.Late), records.Count(r => r.Status == AttendanceStatus.Excused),
            entries.Count(e => e.Status is null));
        return new AttendanceSheetDto(session?.Id, classId, Date(date), slotKey == Guid.Empty ? null : slotKey, session?.TakenByUserId,
            session is null ? null : session.UpdatedAt ?? session.CreatedAt, session is null ? null : Rv(session.RowVersion), summary, entries);
    }

    public async Task<IReadOnlyList<AttendanceSessionDto>> SessionsAsync(Guid classId, string? from, string? to, CancellationToken ct)
    {
        var v = new Validator();
        var f = Validator.ParseDate(from, "from", v);
        var t = Validator.ParseDate(to, "to", v);
        v.ThrowIfInvalid();
        await classAccess.AuthorizeAsync(classId, Permissions.AttendanceRead, true, Permissions.AttendanceRead, ct);
        var q = db.AttendanceSessions.AsNoTracking().Include(s => s.Records).Where(s => s.ClassId == classId);
        if (f is { } ff) q = q.Where(s => s.Date >= ff);
        if (t is { } tt) q = q.Where(s => s.Date <= tt);
        var sessions = await q.OrderByDescending(s => s.Date).ToListAsync(ct);
        return sessions.Select(s => new AttendanceSessionDto(s.Id, Date(s.Date), s.ScheduleSlotId == Guid.Empty ? null : s.ScheduleSlotId,
            s.Records.Count(r => r.Status == AttendanceStatus.Present), s.Records.Count(r => r.Status == AttendanceStatus.Absent),
            s.Records.Count(r => r.Status == AttendanceStatus.Late), s.Records.Count(r => r.Status == AttendanceStatus.Excused), Rv(s.RowVersion))).ToList();
    }

    public async Task<PagedResult<AttendanceRecordDto>> ListRecordsAsync(PageRequest page, AttendanceListQuery query, CancellationToken ct)
    {
        var v = new Validator();
        var f = Validator.ParseDate(query.From, "from", v);
        var t = Validator.ParseDate(query.To, "to", v);
        var status = ParseEnum<AttendanceStatus>(query.Status, "status", v);
        v.ThrowIfInvalid();
        var scope = await access.GetBranchScopeAsync(Permissions.AttendanceRead, ct);
        var q = from r in db.AttendanceRecords.AsNoTracking()
                join s in db.AttendanceSessions on r.SessionId equals s.Id
                join c in db.Classes on s.ClassId equals c.Id
                select new { r, s.ClassId, s.Date, s.ScheduleSlotId, c.BranchId };
        if (!scope.AllBranches)
        {
            var ids = scope.BranchIds.ToList();
            q = q.Where(x => ids.Contains(x.BranchId));
        }
        if (query.StudentId is { } sid) q = q.Where(x => x.r.StudentId == sid);
        if (query.ClassId is { } cid) q = q.Where(x => x.ClassId == cid);
        if (f is { } ff) q = q.Where(x => x.Date >= ff);
        if (t is { } tt) q = q.Where(x => x.Date <= tt);
        if (status is { } st) q = q.Where(x => x.r.Status == st);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Date).ThenBy(x => x.r.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<AttendanceRecordDto>(rows.Select(x => new AttendanceRecordDto(x.r.Id, x.r.SessionId, x.ClassId, x.r.StudentId, Date(x.Date),
            x.ScheduleSlotId == Guid.Empty ? null : x.ScheduleSlotId, Camel(x.r.Status), x.r.Note)).ToList(), page.Page, page.PageSize, total);
    }
}
