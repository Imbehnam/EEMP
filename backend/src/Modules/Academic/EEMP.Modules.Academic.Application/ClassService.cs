using EEMP.Modules.Academic.Domain;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Academic.Application.Fmt;

namespace EEMP.Modules.Academic.Application;

public sealed record ClassListQuery(Guid? TermId, Guid? BranchId, Guid? CourseId, Guid? TeacherId, string? Status);

/// <summary>Classes/sections with schedule slots and effective-dated teacher assignments (branch-scoped).</summary>
public sealed class ClassService(
    IAcademicDbContext db, IAccessControl access, ClassAccess classAccess, AcademicActor actor, CatalogService catalog,
    IBranchDirectory branches, IPeopleDirectory people)
{
    public async Task<PagedResult<ClassDto>> ListAsync(PageRequest page, ClassListQuery query, CancellationToken ct)
    {
        var scope = await access.GetBranchScopeAsync(Permissions.ClassesRead, ct);
        IQueryable<ClassSection> q = db.Classes.AsNoTracking();
        if (!scope.AllBranches)
        {
            var ids = scope.BranchIds.ToList();
            q = q.Where(c => ids.Contains(c.BranchId));
        }
        if (query.TermId is { } t) q = q.Where(c => c.TermId == t);
        if (query.BranchId is { } b) q = q.Where(c => c.BranchId == b);
        if (query.CourseId is { } co) q = q.Where(c => c.CourseId == co);
        if (query.TeacherId is { } te) q = q.Where(c => c.Teachers.Any(a => a.TeacherId == te));
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var v = new Validator();
            var st = ParseEnum<ClassStatus>(query.Status, "status", v);
            v.ThrowIfInvalid();
            q = q.Where(c => c.Status == st);
        }
        if (page.Search is { } s) q = q.Where(c => c.Name.Contains(s) || c.Code.Contains(s));
        var total = await q.CountAsync(ct);
        var items = await q.Include(c => c.Schedule).Include(c => c.Teachers)
            .OrderBy(c => c.Code).ThenBy(c => c.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<ClassDto>(await ToDtosAsync(items, ct), page.Page, page.PageSize, total);
    }

    public async Task<ClassDto> GetAsync(Guid id, CancellationToken ct)
    {
        var cls = await classAccess.AuthorizeAsync(id, Permissions.ClassesRead, true, Permissions.ClassesRead, ct, includeDetails: true);
        return (await ToDtosAsync([cls], ct))[0];
    }

    public async Task<ClassDto> CreateAsync(ClassRequest req, CancellationToken ct)
    {
        var v = Validate(req, isNew: true);
        v.ThrowIfInvalid();
        await access.EnsureBranchAsync(Permissions.ClassesManage, req.BranchId!.Value, ct);
        if (!await branches.ExistsAsync(req.BranchId!.Value, ct)) v.Add("branchId", AcademicErrors.BranchNotFound);
        if (!await db.Terms.AnyAsync(t => t.Id == req.TermId, ct)) v.Add("termId", AcademicErrors.TermNotFound);
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CourseId, ct);
        if (course is null) v.Add("courseId", AcademicErrors.CourseNotFound);
        if (req.GradingSchemeId is { } gs && !await db.GradingSchemes.AnyAsync(s => s.Id == gs, ct)) v.Add("gradingSchemeId", AcademicErrors.GradingSchemeNotFound);
        v.ThrowIfInvalid();

        var code = req.Code!.Trim();
        if (await db.Classes.AnyAsync(c => c.TermId == req.TermId && c.Code == code, ct))
            throw new ConflictException(ErrorCodes.DuplicateValue, P(("field", "code")));
        var schemeId = req.GradingSchemeId ?? course!.GradingSchemeId ?? (await catalog.DefaultSchemeAsync(ct)).Id;
        var status = ParseEnum<ClassStatus>(req.Status, "status", new Validator()) ?? ClassStatus.Planned;
        var cls = new ClassSection
        {
            TermId = req.TermId!.Value, CourseId = req.CourseId!.Value, BranchId = req.BranchId!.Value, Code = code, Name = req.Name!.Trim(),
            Capacity = req.Capacity!.Value, Status = status, GradingSchemeId = schemeId, DefaultRoom = Clean(req.DefaultRoom),
        };
        db.Classes.Add(cls);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([cls], ct))[0];
    }

    public async Task<ClassDto> UpdateAsync(Guid id, ClassRequest req, CancellationToken ct)
    {
        var v = Validate(req, isNew: false);
        v.ThrowIfInvalid();
        var cls = await classAccess.AuthorizeAsync(id, Permissions.ClassesManage, false, Permissions.ClassesRead, ct, track: true, includeDetails: true);
        if (req.GradingSchemeId is { } gs && gs != cls.GradingSchemeId)
        {
            if (!await db.GradingSchemes.AnyAsync(s => s.Id == gs, ct)) v.Add("gradingSchemeId", AcademicErrors.GradingSchemeNotFound);
            // The scheme is part of how published grades were computed; it is fixed once assessments exist.
            else if (await db.Assessments.AnyAsync(a => a.ClassId == id, ct)) v.Add("gradingSchemeId", Validator.InvalidValue);
        }
        v.ThrowIfInvalid();
        var code = req.Code!.Trim();
        if (code != cls.Code && await db.Classes.AnyAsync(c => c.TermId == cls.TermId && c.Code == code && c.Id != id, ct))
            throw new ConflictException(ErrorCodes.DuplicateValue, P(("field", "code")));
        if (req.Capacity!.Value < cls.EnrolledCount) throw new BusinessRuleException(AcademicErrors.CapacityBelowEnrollment);
        db.SetOriginalRowVersion(cls, RowVersion(req.RowVersion));
        cls.Code = code;
        cls.Name = req.Name!.Trim();
        cls.Capacity = req.Capacity.Value;
        cls.Status = ParseEnum<ClassStatus>(req.Status, "status", new Validator()) ?? cls.Status;
        cls.GradingSchemeId = req.GradingSchemeId ?? cls.GradingSchemeId;
        cls.DefaultRoom = Clean(req.DefaultRoom);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([cls], ct))[0];
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var cls = await classAccess.AuthorizeAsync(id, Permissions.ClassesManage, false, Permissions.ClassesRead, ct, track: true);
        if (await db.Enrollments.AnyAsync(e => e.ClassId == id, ct) || await db.AttendanceSessions.AnyAsync(s => s.ClassId == id, ct) ||
            await db.Assessments.AnyAsync(a => a.ClassId == id, ct))
            throw new ConflictException(AcademicErrors.ClassInUse);
        db.Classes.Remove(cls);
        await db.SaveChangesAsync(ct);
    }

    private static Validator Validate(ClassRequest req, bool isNew)
    {
        var v = new Validator().RequiredText("code", req.Code, 30).RequiredText("name", req.Name, 200).OptionalText("defaultRoom", req.DefaultRoom, 50)
            .When(req.Capacity is null, "capacity", Validator.Required)
            .When(req.Capacity is < 1 or > 1000, "capacity", Validator.InvalidValue);
        if (isNew)
            v.When(req.TermId is null || req.TermId == Guid.Empty, "termId", Validator.Required)
             .When(req.CourseId is null || req.CourseId == Guid.Empty, "courseId", Validator.Required)
             .When(req.BranchId is null || req.BranchId == Guid.Empty, "branchId", Validator.Required);
        else v.RowVersion("rowVersion", req.RowVersion);
        ParseEnum<ClassStatus>(req.Status, "status", v);
        return v;
    }

    // ---------- Schedule ----------
    public async Task<ClassDto> SetScheduleAsync(Guid id, ScheduleRequest req, CancellationToken ct)
    {
        var v = new Validator().RowVersion("rowVersion", req.RowVersion);
        var slots = new List<(ScheduleSlotRequest Req, WeeklySlot Slot)>();
        foreach (var (s, i) in (req.Slots ?? []).Select((s, i) => (s, i)))
        {
            var f = $"slots[{i}]";
            var day = ParseEnum<DayOfWeek>(s.Weekday, $"{f}.weekday", v, required: true);
            var start = ParseTime(s.StartTime, $"{f}.startTime", v);
            var end = ParseTime(s.EndTime, $"{f}.endTime", v);
            v.OptionalText($"{f}.room", s.Room, 50);
            if (start is { } st && end is { } en && st >= en) v.Add($"{f}.endTime", AcademicErrors.InvalidTimeRange);
            if (day is { } d && start is { } a && end is { } b && a < b) slots.Add((s, new WeeklySlot(d, a, b)));
        }
        for (var i = 0; i < slots.Count; i++)
            for (var j = i + 1; j < slots.Count; j++)
                if (ScheduleRules.Overlaps(slots[i].Slot, slots[j].Slot)) v.Add("slots", AcademicErrors.SlotOverlap);
        v.ThrowIfInvalid();

        var cls = await classAccess.AuthorizeAsync(id, Permissions.ClassesManage, false, Permissions.ClassesRead, ct, track: true, includeDetails: true);
        var newSlots = slots.Select(s => (Slot: s.Slot, Room: Clean(s.Req.Room) ?? cls.DefaultRoom)).ToList();
        await EnsureNoConflictsAsync(cls, newSlots, cls.Teachers.Select(t => t.TeacherId).ToList(), checkStudents: true, ct);

        db.SetOriginalRowVersion(cls, RowVersion(req.RowVersion));
        db.ScheduleSlots.RemoveRange(cls.Schedule);
        cls.Schedule = newSlots.Select(s => new ScheduleSlot
        {
            ClassId = cls.Id, Weekday = s.Slot.Weekday, StartTime = s.Slot.Start, EndTime = s.Slot.End, Room = s.Room == cls.DefaultRoom ? null : s.Room,
        }).ToList();
        cls.UpdatedAt = actor.UtcNow; // bump the class rowversion
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([cls], ct))[0];
    }

    /// <summary>Room, teacher and enrolled-student conflicts against other non-cancelled classes of the same term.</summary>
    private async Task EnsureNoConflictsAsync(ClassSection cls, List<(WeeklySlot Slot, string? Room)> slots, IReadOnlyCollection<Guid> teacherIds,
        bool checkStudents, CancellationToken ct)
    {
        if (slots.Count == 0) return;
        var mine = slots.Select(s => s.Slot).ToList();
        var others = await db.Classes.AsNoTracking().Include(c => c.Schedule).Include(c => c.Teachers)
            .Where(c => c.TermId == cls.TermId && c.Id != cls.Id && c.Status != ClassStatus.Cancelled).ToListAsync(ct);

        foreach (var other in others.Where(o => o.BranchId == cls.BranchId))
            foreach (var os in other.Schedule)
            {
                var room = os.Room ?? other.DefaultRoom;
                if (room is null) continue;
                var w = new WeeklySlot(os.Weekday, os.StartTime, os.EndTime);
                if (slots.Any(s => s.Room is not null && string.Equals(s.Room, room, StringComparison.OrdinalIgnoreCase) && ScheduleRules.Overlaps(s.Slot, w)))
                    throw new ConflictException(AcademicErrors.RoomConflict, P(("classId", other.Id)));
            }

        foreach (var other in others)
        {
            var shared = other.Teachers.Select(t => t.TeacherId).Intersect(teacherIds).ToList();
            if (shared.Count > 0 && ScheduleRules.AnyOverlap(mine, other.Schedule.Select(Weekly)))
                throw new ConflictException(AcademicErrors.TeacherConflict, P(("classId", other.Id), ("teacherId", shared[0])));
        }

        if (!checkStudents) return;
        var students = await db.Enrollments.AsNoTracking().Where(e => e.ClassId == cls.Id && e.Status == EnrollmentStatus.Active).Select(e => e.StudentId).ToListAsync(ct);
        if (students.Count == 0) return;
        var otherIds = others.Select(o => o.Id).ToList();
        var otherEnrollments = await db.Enrollments.AsNoTracking()
            .Where(e => otherIds.Contains(e.ClassId) && students.Contains(e.StudentId) && e.Status == EnrollmentStatus.Active).ToListAsync(ct);
        foreach (var e in otherEnrollments)
        {
            var other = others.First(o => o.Id == e.ClassId);
            if (ScheduleRules.AnyOverlap(mine, other.Schedule.Select(Weekly)))
                throw new ConflictException(AcademicErrors.ScheduleConflict, P(("classId", other.Id), ("studentId", e.StudentId)));
        }
    }

    public static WeeklySlot Weekly(ScheduleSlot s) => new(s.Weekday, s.StartTime, s.EndTime);

    // ---------- Teacher assignments ----------
    public async Task<ClassDto> SetTeachersAsync(Guid id, TeachersRequest req, CancellationToken ct)
    {
        var v = new Validator().RowVersion("rowVersion", req.RowVersion);
        var parsed = new List<TeacherAssignment>();
        foreach (var (a, i) in (req.Assignments ?? []).Select((a, i) => (a, i)))
        {
            var f = $"assignments[{i}]";
            v.When(a.TeacherId is null || a.TeacherId == Guid.Empty, $"{f}.teacherId", Validator.Required);
            var role = ParseEnum<TeacherRole>(a.Role, $"{f}.role", v) ?? TeacherRole.Primary;
            var start = Validator.ParseDate(a.StartDate, $"{f}.startDate", v);
            var end = Validator.ParseDate(a.EndDate, $"{f}.endDate", v);
            if (start is { } s && end is { } e && s > e) v.Add($"{f}.endDate", AcademicErrors.InvalidDateRange);
            if (a.TeacherId is { } tid && tid != Guid.Empty)
                parsed.Add(new TeacherAssignment { ClassId = id, TeacherId = tid, Role = role, StartDate = start, EndDate = end });
        }
        if (parsed.Select(p => p.TeacherId).Distinct().Count() != parsed.Count) v.Add("assignments", Validator.InvalidValue);
        v.ThrowIfInvalid();

        var cls = await classAccess.AuthorizeAsync(id, Permissions.ClassesManage, false, Permissions.ClassesRead, ct, track: true, includeDetails: true);
        var teachers = await people.GetTeachersAsync(parsed.Select(p => p.TeacherId).ToList(), ct);
        foreach (var (p, i) in parsed.Select((p, i) => (p, i)))
            if (!teachers.ContainsKey(p.TeacherId)) v.Add($"assignments[{i}].teacherId", AcademicErrors.TeacherNotFound);
        v.ThrowIfInvalid();
        if (teachers.Values.Any(t => t.BranchId != cls.BranchId)) throw new BusinessRuleException(AcademicErrors.TeacherBranchMismatch);

        var slots = cls.Schedule.Select(s => (Weekly(s), s.Room ?? cls.DefaultRoom)).ToList();
        await EnsureNoConflictsAsync(cls, slots, parsed.Select(p => p.TeacherId).ToList(), checkStudents: false, ct);

        db.SetOriginalRowVersion(cls, RowVersion(req.RowVersion));
        // Keep row identity for unchanged teachers (assignment history ids stay stable).
        foreach (var existing in cls.Teachers.Where(t => parsed.All(p => p.TeacherId != t.TeacherId)).ToList())
        {
            db.TeacherAssignments.Remove(existing);
            cls.Teachers.Remove(existing);
        }
        foreach (var p in parsed)
        {
            var existing = cls.Teachers.FirstOrDefault(t => t.TeacherId == p.TeacherId);
            if (existing is null) cls.Teachers.Add(p);
            else
            {
                existing.Role = p.Role;
                existing.StartDate = p.StartDate;
                existing.EndDate = p.EndDate;
            }
        }
        cls.UpdatedAt = actor.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([cls], ct))[0];
    }

    // ---------- Roster ----------
    public async Task<IReadOnlyList<RosterEntryDto>> RosterAsync(Guid id, bool includeInactive, CancellationToken ct)
    {
        await classAccess.AuthorizeAsync(id, Permissions.EnrollmentsRead, true, Permissions.EnrollmentsRead, ct);
        var q = db.Enrollments.AsNoTracking().Where(e => e.ClassId == id);
        if (!includeInactive) q = q.Where(e => e.Status == EnrollmentStatus.Active);
        var rows = await q.ToListAsync(ct);
        var students = await people.GetStudentsAsync(rows.Select(r => r.StudentId).ToList(), ct);
        return rows.Select(r =>
        {
            var s = students.GetValueOrDefault(r.StudentId);
            return new RosterEntryDto(r.Id, r.StudentId, s?.FirstName ?? "", s?.LastName ?? "", s?.StudentNumber ?? "", Camel(r.Status), Date(r.EnrolledOn));
        }).OrderBy(r => r.LastName).ThenBy(r => r.FirstName).ToList();
    }

    // ---------- Teacher portal ----------
    public async Task<IReadOnlyList<TeacherClassDto>> MyClassesAsync(Guid? termId, CancellationToken ct)
    {
        var teacherId = await actor.TeacherIdAsync(ct);
        if (teacherId is null) return [];
        var today = await actor.TodayAsync(ct);
        var assignments = (await db.TeacherAssignments.AsNoTracking().Where(a => a.TeacherId == teacherId).ToListAsync(ct))
            .Where(a => a.IsActiveOn(today)).ToList();
        var classIds = assignments.Select(a => a.ClassId).ToList();
        var q = db.Classes.AsNoTracking().Include(c => c.Schedule).Where(c => classIds.Contains(c.Id));
        if (termId is { } t) q = q.Where(c => c.TermId == t);
        var classes = await q.ToListAsync(ct);
        var courseIds = classes.Select(c => c.CourseId).Distinct().ToList();
        var courses = await db.Courses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var subjectIds = courses.Values.Select(c => c.SubjectId).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var termIds = classes.Select(c => c.TermId).Distinct().ToList();
        var terms = await db.Terms.AsNoTracking().Where(x => termIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return classes.OrderBy(c => c.Code).Select(c =>
        {
            var course = courses[c.CourseId];
            return new TeacherClassDto(c.Id, c.Code, c.Name, course.Name, subjects.GetValueOrDefault(course.SubjectId)?.Name ?? "", c.TermId, terms[c.TermId].Name,
                c.BranchId, Camel(assignments.First(a => a.ClassId == c.Id).Role), c.EnrolledCount, Camel(c.Status), Slots(c));
        }).ToList();
    }

    public static IReadOnlyList<ScheduleSlotDto> Slots(ClassSection c) => c.Schedule
        .OrderBy(s => ScheduleRules.WeekOrder(s.Weekday)).ThenBy(s => s.StartTime)
        .Select(s => new ScheduleSlotDto(s.Id, Camel(s.Weekday), Time(s.StartTime), Time(s.EndTime), s.Room ?? c.DefaultRoom)).ToList();

    private async Task<IReadOnlyList<ClassDto>> ToDtosAsync(IReadOnlyList<ClassSection> classes, CancellationToken ct)
    {
        var termIds = classes.Select(c => c.TermId).Distinct().ToList();
        var terms = await db.Terms.AsNoTracking().Where(t => termIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var courseIds = classes.Select(c => c.CourseId).Distinct().ToList();
        var courses = await db.Courses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var teachers = await people.GetTeachersAsync(classes.SelectMany(c => c.Teachers).Select(t => t.TeacherId).Distinct().ToList(), ct);
        var today = await actor.TodayAsync(ct);
        return classes.Select(c => new ClassDto(c.Id, c.TermId, terms.GetValueOrDefault(c.TermId)?.AcademicYearId ?? Guid.Empty, c.CourseId,
            courses.GetValueOrDefault(c.CourseId)?.SubjectId ?? Guid.Empty, c.BranchId, c.Code, c.Name, c.Capacity, c.EnrolledCount, Camel(c.Status),
            c.GradingSchemeId, c.DefaultRoom, Slots(c),
            c.Teachers.OrderBy(t => t.Role).Select(t =>
            {
                var p = teachers.GetValueOrDefault(t.TeacherId);
                return new TeacherAssignmentDto(t.Id, t.TeacherId, p?.FirstName ?? "", p?.LastName ?? "", Camel(t.Role), Date(t.StartDate), Date(t.EndDate), t.IsActiveOn(today));
            }).ToList(), Rv(c.RowVersion))).ToList();
    }
}
