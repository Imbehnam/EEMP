using EEMP.Modules.Academic.Domain;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Academic.Application.Fmt;

namespace EEMP.Modules.Academic.Application;

/// <summary>
/// Student/guardian portal. The relationship is the authorization: a student sees themselves, a guardian their linked students.
/// Grades come only from published snapshots; report cards only from published versions. Anything else is "not found".
/// </summary>
public sealed class PortalAcademicService(IAcademicDbContext db, AcademicActor actor, IPeopleDirectory people, EEMP.Modules.Finance.Contracts.IFinanceStanding finance)
{
    private async Task EnsureStudentAsync(Guid studentId, CancellationToken ct)
    {
        if (!(await actor.PortalStudentIdsAsync(ct)).Contains(studentId)) throw new NotFoundException();
    }

    /// <summary>DL-065: when the tenant enables it, portal results are withheld while the student has an overdue balance.</summary>
    private async Task EnsureResultsVisibleAsync(Guid studentId, CancellationToken ct)
    {
        if (await finance.AreResultsBlockedAsync(studentId, ct))
            throw new ForbiddenException(EEMP.Modules.Finance.Contracts.FinanceStandingErrors.ResultsBlocked);
    }

    /// <summary>The requested term, else the term containing today, else null (= every term).</summary>
    private async Task<Guid?> TermFilterAsync(Guid? termId, IReadOnlyCollection<Guid> candidateTermIds, CancellationToken ct)
    {
        if (termId is not null) return termId;
        var today = await actor.TodayAsync(ct);
        var current = await db.Terms.AsNoTracking().Where(t => candidateTermIds.Contains(t.Id) && t.StartDate <= today && t.EndDate >= today)
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        return current;
    }

    private async Task<List<ClassSection>> StudentClassesAsync(Guid studentId, bool activeOnly, CancellationToken ct)
    {
        var q = db.Enrollments.AsNoTracking().Where(e => e.StudentId == studentId);
        q = activeOnly ? q.Where(e => e.Status == EnrollmentStatus.Active) : q.Where(e => e.Status != EnrollmentStatus.Withdrawn);
        var classIds = await q.Select(e => e.ClassId).ToListAsync(ct);
        return await db.Classes.AsNoTracking().Include(c => c.Schedule).Include(c => c.Teachers)
            .Where(c => classIds.Contains(c.Id) && c.Status != ClassStatus.Cancelled).ToListAsync(ct);
    }

    private async Task<(Dictionary<Guid, Course> Courses, Dictionary<Guid, Subject> Subjects)> CatalogAsync(IEnumerable<ClassSection> classes, CancellationToken ct)
    {
        var courseIds = classes.Select(c => c.CourseId).Distinct().ToList();
        var courses = await db.Courses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var subjectIds = courses.Values.Select(c => c.SubjectId).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        return (courses, subjects);
    }

    public async Task<IReadOnlyList<TimetableEntryDto>> TimetableAsync(Guid studentId, Guid? termId, CancellationToken ct)
    {
        await EnsureStudentAsync(studentId, ct);
        var classes = await StudentClassesAsync(studentId, activeOnly: true, ct);
        var term = await TermFilterAsync(termId, classes.Select(c => c.TermId).Distinct().ToList(), ct);
        if (term is { } t) classes = classes.Where(c => c.TermId == t).ToList();
        var (courses, subjects) = await CatalogAsync(classes, ct);
        var today = await actor.TodayAsync(ct);
        var teachers = await people.GetTeachersAsync(classes.SelectMany(c => c.Teachers).Select(a => a.TeacherId).Distinct().ToList(), ct);
        return classes.SelectMany(c => c.Schedule.Select(s => (c, s)))
            .OrderBy(x => ScheduleRules.WeekOrder(x.s.Weekday)).ThenBy(x => x.s.StartTime)
            .Select(x =>
            {
                var course = courses[x.c.CourseId];
                var names = x.c.Teachers.Where(a => a.IsActiveOn(today)).OrderBy(a => a.Role)
                    .Select(a => teachers.GetValueOrDefault(a.TeacherId)).Where(p => p is not null)
                    .Select(p => new PersonNameDto(p!.FirstName, p.LastName)).ToList();
                return new TimetableEntryDto(x.c.Id, x.c.Code, x.c.Name, course.Name, subjects.GetValueOrDefault(course.SubjectId)?.Name ?? "",
                    Camel(x.s.Weekday), Time(x.s.StartTime), Time(x.s.EndTime), x.s.Room ?? x.c.DefaultRoom, names);
            }).ToList();
    }

    public async Task<PortalAttendanceDto> AttendanceAsync(Guid studentId, Guid? termId, string? from, string? to, CancellationToken ct)
    {
        var v = new Validator();
        var f = Validator.ParseDate(from, "from", v);
        var t = Validator.ParseDate(to, "to", v);
        v.ThrowIfInvalid();
        await EnsureStudentAsync(studentId, ct);
        var q = from r in db.AttendanceRecords.AsNoTracking()
                join s in db.AttendanceSessions on r.SessionId equals s.Id
                join c in db.Classes on s.ClassId equals c.Id
                where r.StudentId == studentId
                select new { r.Status, r.Note, s.Date, s.ScheduleSlotId, ClassId = c.Id, ClassName = c.Name, c.TermId };
        if (termId is { } term) q = q.Where(x => x.TermId == term);
        if (f is { } ff) q = q.Where(x => x.Date >= ff);
        if (t is { } tt) q = q.Where(x => x.Date <= tt);
        var rows = await q.OrderByDescending(x => x.Date).ToListAsync(ct);
        var summary = new RcAttendance(rows.Count(r => r.Status == AttendanceStatus.Present), rows.Count(r => r.Status == AttendanceStatus.Absent),
            rows.Count(r => r.Status == AttendanceStatus.Late), rows.Count(r => r.Status == AttendanceStatus.Excused));
        return new PortalAttendanceDto(summary, rows.Select(r => new PortalAttendanceRecordDto(Date(r.Date), r.ClassId, r.ClassName,
            r.ScheduleSlotId == Guid.Empty ? null : r.ScheduleSlotId, Camel(r.Status), r.Note)).ToList());
    }

    public async Task<IReadOnlyList<PortalClassGradesDto>> GradesAsync(Guid studentId, Guid? termId, CancellationToken ct)
    {
        await EnsureStudentAsync(studentId, ct);
        await EnsureResultsVisibleAsync(studentId, ct);
        var classes = await StudentClassesAsync(studentId, activeOnly: false, ct);
        var term = await TermFilterAsync(termId, classes.Select(c => c.TermId).Distinct().ToList(), ct);
        if (term is { } tf) classes = classes.Where(c => c.TermId == tf).ToList();
        var (courses, subjects) = await CatalogAsync(classes, ct);
        var classIds = classes.Select(c => c.Id).ToList();
        // Published snapshot only: assessments that have ever been published, scores as last published.
        var assessments = await db.Assessments.AsNoTracking().Where(a => classIds.Contains(a.ClassId) && a.PublishedAt != null).ToListAsync(ct);
        var assessmentIds = assessments.Select(a => a.Id).ToList();
        var results = await db.AssessmentResults.AsNoTracking()
            .Where(r => assessmentIds.Contains(r.AssessmentId) && r.StudentId == studentId && r.IsPublished).ToListAsync(ct);
        var schemeIds = classes.Select(c => c.GradingSchemeId).Distinct().ToList();
        var schemes = await db.GradingSchemes.AsNoTracking().Include(s => s.Bands).Where(s => schemeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        return classes.OrderBy(c => c.Code).Select(c =>
        {
            var course = courses[c.CourseId];
            var scheme = schemes[c.GradingSchemeId];
            var rows = assessments.Where(a => a.ClassId == c.Id).OrderBy(a => a.Date ?? DateOnly.MaxValue).ThenBy(a => a.CreatedAt)
                .Select(a => (a, r: results.FirstOrDefault(r => r.AssessmentId == a.Id))).ToList();
            var total = GradeCalculator.Total(rows.Select(x => new ScoreInput(x.a.PublishedWeight ?? x.a.Weight, x.a.PublishedMaxScore ?? x.a.MaxScore,
                x.r?.PublishedScore, x.r?.PublishedMark)), GradeCalculator.Spec(scheme));
            return new PortalClassGradesDto(c.Id, c.Name, course.Name, subjects.GetValueOrDefault(course.SubjectId)?.Name ?? "", c.TermId, CatalogService.Info(scheme),
                rows.Select(x => new PortalAssessmentDto(x.a.Id, x.a.Name, Camel(x.a.Type), x.a.PublishedWeight ?? x.a.Weight, x.a.PublishedMaxScore ?? x.a.MaxScore,
                    x.r?.PublishedScore, Camel(x.r?.PublishedMark), x.a.PublishedAt)).ToList(),
                AssessmentService.Total(total));
        }).ToList();
    }

    public async Task<IReadOnlyList<PortalReportCardSummaryDto>> ReportCardsAsync(Guid studentId, CancellationToken ct)
    {
        await EnsureStudentAsync(studentId, ct);
        await EnsureResultsVisibleAsync(studentId, ct);
        var cards = await db.ReportCards.AsNoTracking().Where(r => r.StudentId == studentId && r.PublishedVersion != null).ToListAsync(ct);
        var termIds = cards.Select(c => c.TermId).ToList();
        var terms = await db.Terms.AsNoTracking().Where(t => termIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        return cards.OrderByDescending(c => terms.GetValueOrDefault(c.TermId)?.StartDate)
            .Select(c => new PortalReportCardSummaryDto(c.Id, c.TermId, terms.GetValueOrDefault(c.TermId)?.Name ?? "", c.PublishedVersion!.Value, c.PublishedAt)).ToList();
    }

    public async Task<PortalReportCardDto> ReportCardAsync(Guid id, CancellationToken ct)
    {
        var card = await db.ReportCards.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException();
        await EnsureStudentAsync(card.StudentId, ct);
        await EnsureResultsVisibleAsync(card.StudentId, ct);
        if (card.PublishedVersion is not { } published) throw new NotFoundException();
        var version = await db.ReportCardVersions.AsNoTracking().FirstAsync(v => v.ReportCardId == id && v.VersionNo == published, ct);
        return new PortalReportCardDto(card.Id, card.StudentId, card.TermId, version.VersionNo, version.PublishedAt, ReportCardService.Content(version));
    }
}
