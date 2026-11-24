using EEMP.Modules.Academic.Domain;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Academic.Application.Fmt;

namespace EEMP.Modules.Academic.Application;

public sealed record EnrollmentListQuery(Guid? ClassId, Guid? StudentId, Guid? TermId, string? Status);

/// <summary>Enrollment lifecycle with capacity, branch and timetable-conflict rules. Authorization is on the class's branch.</summary>
public sealed class EnrollmentService(IAcademicDbContext db, IAccessControl access, ClassAccess classAccess, AcademicActor actor, IPeopleDirectory people)
{
    public async Task<PagedResult<EnrollmentDto>> ListAsync(PageRequest page, EnrollmentListQuery query, CancellationToken ct)
    {
        var scope = await access.GetBranchScopeAsync(Permissions.EnrollmentsRead, ct);
        var q = from e in db.Enrollments.AsNoTracking()
                join c in db.Classes on e.ClassId equals c.Id
                select new { e, c.BranchId, c.TermId };
        if (!scope.AllBranches)
        {
            var ids = scope.BranchIds.ToList();
            q = q.Where(x => ids.Contains(x.BranchId));
        }
        if (query.ClassId is { } cid) q = q.Where(x => x.e.ClassId == cid);
        if (query.StudentId is { } sid) q = q.Where(x => x.e.StudentId == sid);
        if (query.TermId is { } tid) q = q.Where(x => x.TermId == tid);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var v = new Validator();
            var st = ParseEnum<EnrollmentStatus>(query.Status, "status", v);
            v.ThrowIfInvalid();
            q = q.Where(x => x.e.Status == st);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.e.EnrolledOn).ThenBy(x => x.e.Id).Skip(page.Skip).Take(page.PageSize).Select(x => x.e).ToListAsync(ct);
        return new PagedResult<EnrollmentDto>(await ToDtosAsync(rows, ct), page.Page, page.PageSize, total);
    }

    public async Task<EnrollmentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        await classAccess.AuthorizeAsync(e.ClassId, Permissions.EnrollmentsRead, false, Permissions.EnrollmentsRead, ct);
        return (await ToDtosAsync([e], ct))[0];
    }

    public async Task<EnrollmentDto> EnrollAsync(EnrollRequest req, CancellationToken ct)
    {
        var v = new Validator()
            .When(req.ClassId is null || req.ClassId == Guid.Empty, "classId", Validator.Required)
            .When(req.StudentId is null || req.StudentId == Guid.Empty, "studentId", Validator.Required);
        var enrolledOn = Validator.ParseDate(req.EnrolledOn, "enrolledOn", v);
        v.ThrowIfInvalid();

        var cls = await classAccess.AuthorizeAsync(req.ClassId!.Value, Permissions.EnrollmentsManage, false, Permissions.EnrollmentsRead, ct,
            track: true, includeDetails: true);
        var student = (await people.GetStudentsAsync([req.StudentId!.Value], ct)).GetValueOrDefault(req.StudentId!.Value);
        if (student is null) { v.Add("studentId", AcademicErrors.StudentNotFound); v.ThrowIfInvalid(); }
        if (student!.BranchId != cls.BranchId) throw new BusinessRuleException(AcademicErrors.StudentBranchMismatch);
        if (!cls.IsOpenForEnrollment) throw new BusinessRuleException(AcademicErrors.ClassNotOpen);

        var existing = await db.Enrollments.FirstOrDefaultAsync(e => e.ClassId == cls.Id && e.StudentId == student.Id, ct);
        if (existing?.Status == EnrollmentStatus.Active) throw new ConflictException(AcademicErrors.AlreadyEnrolled);
        if (cls.EnrolledCount >= cls.Capacity) throw new ConflictException(AcademicErrors.ClassFull);
        await EnsureNoTimetableConflictAsync(cls, student.Id, ct);

        var today = await actor.TodayAsync(ct);
        var enrollment = existing ?? new Enrollment { ClassId = cls.Id, StudentId = student.Id };
        enrollment.Status = EnrollmentStatus.Active;
        enrollment.EnrolledOn = enrolledOn ?? today;
        enrollment.WithdrawnOn = null;
        if (existing is null) db.Enrollments.Add(enrollment);
        // Capacity guard: the class row is updated with its rowversion, so a concurrent enrollment for the last seat fails with 409.
        cls.EnrolledCount++;
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([enrollment], ct))[0];
    }

    public async Task<EnrollmentDto> WithdrawAsync(Guid id, WithdrawRequest req, CancellationToken ct)
    {
        var v = new Validator().RowVersion("rowVersion", req.RowVersion);
        var withdrawnOn = Validator.ParseDate(req.WithdrawnOn, "withdrawnOn", v);
        v.ThrowIfInvalid();
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var cls = await classAccess.AuthorizeAsync(e.ClassId, Permissions.EnrollmentsManage, false, Permissions.EnrollmentsRead, ct, track: true);
        if (e.Status != EnrollmentStatus.Active)
            throw new ConflictException(AcademicErrors.InvalidTransition, P(("from", Camel(e.Status)), ("to", Camel(EnrollmentStatus.Withdrawn))));
        var on = withdrawnOn ?? await actor.TodayAsync(ct);
        if (on < e.EnrolledOn) { v.Add("withdrawnOn", AcademicErrors.InvalidDateRange); v.ThrowIfInvalid(); }
        db.SetOriginalRowVersion(e, RowVersion(req.RowVersion));
        e.Status = EnrollmentStatus.Withdrawn;
        e.WithdrawnOn = on;
        cls.EnrolledCount = Math.Max(0, cls.EnrolledCount - 1);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([e], ct))[0];
    }

    private async Task EnsureNoTimetableConflictAsync(ClassSection cls, Guid studentId, CancellationToken ct)
    {
        if (cls.Schedule.Count == 0) return;
        var mine = cls.Schedule.Select(ClassService.Weekly).ToList();
        var otherClassIds = await (from e in db.Enrollments
                                   join c in db.Classes on e.ClassId equals c.Id
                                   where e.StudentId == studentId && e.Status == EnrollmentStatus.Active && c.TermId == cls.TermId && c.Id != cls.Id
                                         && c.Status != ClassStatus.Cancelled
                                   select c.Id).ToListAsync(ct);
        if (otherClassIds.Count == 0) return;
        var slots = await db.ScheduleSlots.AsNoTracking().Where(s => otherClassIds.Contains(s.ClassId)).ToListAsync(ct);
        foreach (var g in slots.GroupBy(s => s.ClassId))
            if (ScheduleRules.AnyOverlap(mine, g.Select(ClassService.Weekly)))
                throw new ConflictException(AcademicErrors.ScheduleConflict, P(("classId", g.Key)));
    }

    private async Task<IReadOnlyList<EnrollmentDto>> ToDtosAsync(IReadOnlyList<Enrollment> rows, CancellationToken ct)
    {
        var students = await people.GetStudentsAsync(rows.Select(r => r.StudentId).ToList(), ct);
        return rows.Select(e =>
        {
            var s = students.GetValueOrDefault(e.StudentId);
            return new EnrollmentDto(e.Id, e.ClassId, e.StudentId, s?.FirstName ?? "", s?.LastName ?? "", s?.StudentNumber ?? "", Camel(e.Status),
                Date(e.EnrolledOn), Date(e.WithdrawnOn), Rv(e.RowVersion));
        }).ToList();
    }
}
