using System.Text.Json;
using EEMP.Modules.Academic.Domain;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Academic.Application.Fmt;

namespace EEMP.Modules.Academic.Application;

public sealed record ReportCardListQuery(Guid? TermId, Guid? ClassId, Guid? StudentId, Guid? BranchId, string? Status);

/// <summary>
/// Versioned report cards (student × term). Generation snapshots approved/published scores into an immutable version;
/// regenerating a published card creates a new draft version and keeps history. Branch scope = the card's branch.
/// </summary>
public sealed class ReportCardService(IAcademicDbContext db, IAccessControl access, ClassAccess classAccess, AcademicActor actor,
    CatalogService catalog, IPeopleDirectory people)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<GenerateReportCardsResult> GenerateAsync(GenerateReportCardsRequest req, CancellationToken ct)
    {
        var v = new Validator().When(req.TermId is null || req.TermId == Guid.Empty, "termId", Validator.Required);
        v.ThrowIfInvalid();
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.TermId, ct);
        if (term is null) { v.Add("termId", AcademicErrors.TermNotFound); v.ThrowIfInvalid(); }
        var scope = await access.GetBranchScopeAsync(Permissions.ReportCardsManage, ct);
        if (scope.IsEmpty) throw new ForbiddenException(ErrorCodes.ForbiddenScope);
        if (req.BranchId is { } bid && !scope.Includes(bid)) throw new ForbiddenException(ErrorCodes.ForbiddenScope);
        if (req.ClassId is { } cid)
        {
            var c = await classAccess.AuthorizeAsync(cid, Permissions.ReportCardsManage, false, Permissions.ReportCardsRead, ct);
            if (c.TermId != term!.Id) { v.Add("classId", Validator.InvalidValue); v.ThrowIfInvalid(); }
        }

        var classes = await db.Classes.AsNoTracking().Where(c => c.TermId == term!.Id && c.Status != ClassStatus.Cancelled).ToListAsync(ct);
        var targetClasses = classes.Where(c => scope.Includes(c.BranchId) && (req.BranchId is null || c.BranchId == req.BranchId) &&
                                               (req.ClassId is null || c.Id == req.ClassId)).Select(c => c.Id).ToList();
        var allClassIds = classes.Select(c => c.Id).ToList();
        var enrollments = await db.Enrollments.AsNoTracking()
            .Where(e => allClassIds.Contains(e.ClassId) && e.Status != EnrollmentStatus.Withdrawn).ToListAsync(ct);
        var studentIds = enrollments.Where(e => targetClasses.Contains(e.ClassId)).Select(e => e.StudentId).Distinct().ToList();
        var students = await people.GetStudentsAsync(studentIds, ct);

        var builder = await SnapshotBuilder.CreateAsync(db, catalog, term!, classes, ct);
        var now = actor.UtcNow;
        int created = 0, regenerated = 0, unchanged = 0;
        var cards = new List<ReportCard>();
        foreach (var sid in studentIds)
        {
            if (!students.TryGetValue(sid, out var student) || !scope.Includes(student.BranchId)) continue;
            var studentClasses = enrollments.Where(e => e.StudentId == sid).Select(e => e.ClassId).ToList();
            var content = await builder.BuildAsync(student, studentClasses, now, ct);
            var json = JsonSerializer.Serialize(content, Json);
            var card = await db.ReportCards.Include(r => r.Versions).FirstOrDefaultAsync(r => r.StudentId == sid && r.TermId == term!.Id, ct);
            if (card is null)
            {
                card = new ReportCard { StudentId = sid, TermId = term!.Id, BranchId = student.BranchId, CurrentVersion = 1, Status = ReportCardStatus.Draft };
                card.Versions.Add(new ReportCardVersion { ReportCardId = card.Id, VersionNo = 1, Content = json, GeneratedAt = now, GeneratedByUserId = actor.UserId });
                card.AverageValue = content.Average?.Value;
                db.ReportCards.Add(card);
                created++;
            }
            else
            {
                var current = card.Versions.First(x => x.VersionNo == card.CurrentVersion);
                if (SameContent(current.Content, content)) { unchanged++; cards.Add(card); continue; }
                if (current.Status == ReportCardVersionStatus.Draft)
                {
                    current.Content = json;
                    current.GeneratedAt = now;
                    current.GeneratedByUserId = actor.UserId;
                }
                else
                {
                    // Published versions are immutable: a correction is a new draft version.
                    card.CurrentVersion++;
                    card.Versions.Add(new ReportCardVersion
                    {
                        ReportCardId = card.Id, VersionNo = card.CurrentVersion, Content = json, GeneratedAt = now, GeneratedByUserId = actor.UserId,
                    });
                    card.Status = ReportCardStatus.Draft;
                }
                card.BranchId = student.BranchId;
                card.AverageValue = content.Average?.Value;
                card.UpdatedAt = now;
                regenerated++;
            }
            cards.Add(card);
        }
        await db.SaveChangesAsync(ct);
        return new GenerateReportCardsResult(created, regenerated, unchanged, await SummariesAsync(cards, ct));
    }

    private static bool SameContent(string stored, ReportCardContent fresh)
    {
        var old = JsonSerializer.Deserialize<ReportCardContent>(stored, Json);
        return old is not null && JsonSerializer.Serialize(old with { GeneratedAt = default }, Json) == JsonSerializer.Serialize(fresh with { GeneratedAt = default }, Json);
    }

    public async Task<PagedResult<ReportCardSummaryDto>> ListAsync(PageRequest page, ReportCardListQuery query, CancellationToken ct)
    {
        var v = new Validator();
        var status = ParseEnum<ReportCardStatus>(query.Status, "status", v);
        v.ThrowIfInvalid();
        var scope = await access.GetBranchScopeAsync(Permissions.ReportCardsRead, ct);
        var q = db.ReportCards.AsNoTracking();
        if (!scope.AllBranches)
        {
            var ids = scope.BranchIds.ToList();
            q = q.Where(r => ids.Contains(r.BranchId));
        }
        if (query.TermId is { } t) q = q.Where(r => r.TermId == t);
        if (query.StudentId is { } s) q = q.Where(r => r.StudentId == s);
        if (query.BranchId is { } b) q = q.Where(r => r.BranchId == b);
        if (status is { } st) q = q.Where(r => r.Status == st);
        if (query.ClassId is { } cid)
        {
            var sids = db.Enrollments.Where(e => e.ClassId == cid).Select(e => e.StudentId);
            var termOfClass = db.Classes.Where(c => c.Id == cid).Select(c => c.TermId);
            q = q.Where(r => sids.Contains(r.StudentId) && termOfClass.Contains(r.TermId));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(r => r.Versions).OrderBy(r => r.TermId).ThenBy(r => r.StudentId).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<ReportCardSummaryDto>(await SummariesAsync(items, ct), page.Page, page.PageSize, total);
    }

    public async Task<ReportCardDto> GetAsync(Guid id, CancellationToken ct)
    {
        var card = await FindAsync(id, Permissions.ReportCardsRead, ct);
        var summary = (await SummariesAsync([card], ct))[0];
        var current = card.Versions.First(v => v.VersionNo == card.CurrentVersion);
        return new ReportCardDto(summary.Id, summary.StudentId, summary.FirstName, summary.LastName, summary.StudentNumber, summary.TermId, summary.BranchId,
            summary.Status, summary.CurrentVersion, summary.PublishedVersion, summary.GeneratedAt, summary.PublishedAt, summary.Average, summary.RowVersion,
            card.Versions.OrderBy(v => v.VersionNo).Select(v => new ReportCardVersionInfoDto(v.VersionNo, Camel(v.Status), v.GeneratedAt, v.PublishedAt)).ToList(),
            Content(current));
    }

    public async Task<ReportCardVersionDto> GetVersionAsync(Guid id, int versionNo, CancellationToken ct)
    {
        var card = await FindAsync(id, Permissions.ReportCardsRead, ct);
        var version = card.Versions.FirstOrDefault(v => v.VersionNo == versionNo) ?? throw new NotFoundException();
        return new ReportCardVersionDto(version.VersionNo, Camel(version.Status), version.GeneratedAt, version.PublishedAt, Content(version));
    }

    public async Task<ReportCardDto> PublishAsync(Guid id, TransitionRequest req, CancellationToken ct)
    {
        new Validator().RowVersion("rowVersion", req.RowVersion).ThrowIfInvalid();
        var card = await FindAsync(id, Permissions.ReportCardsPublish, ct, track: true);
        db.SetOriginalRowVersion(card, RowVersion(req.RowVersion));
        Publish(card, actor.UtcNow, actor.UserId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PublishCountDto> PublishBatchAsync(PublishReportCardsRequest req, CancellationToken ct)
    {
        var v = new Validator().When(req.TermId is null || req.TermId == Guid.Empty, "termId", Validator.Required);
        v.ThrowIfInvalid();
        var scope = await access.GetBranchScopeAsync(Permissions.ReportCardsPublish, ct);
        if (scope.IsEmpty) throw new ForbiddenException(ErrorCodes.ForbiddenScope);
        if (req.BranchId is { } bid && !scope.Includes(bid)) throw new ForbiddenException(ErrorCodes.ForbiddenScope);
        if (req.ClassId is { } classId) await classAccess.AuthorizeAsync(classId, Permissions.ReportCardsPublish, false, Permissions.ReportCardsRead, ct);
        var q = db.ReportCards.Include(r => r.Versions).Where(r => r.TermId == req.TermId && r.Status == ReportCardStatus.Draft);
        if (req.BranchId is { } b) q = q.Where(r => r.BranchId == b);
        if (req.ClassId is { } cid)
        {
            var sids = db.Enrollments.Where(e => e.ClassId == cid).Select(e => e.StudentId);
            q = q.Where(r => sids.Contains(r.StudentId));
        }
        var cards = (await q.ToListAsync(ct)).Where(c => scope.Includes(c.BranchId)).ToList();
        var now = actor.UtcNow;
        foreach (var card in cards) Publish(card, now, actor.UserId);
        await db.SaveChangesAsync(ct);
        return new PublishCountDto(cards.Count);
    }

    private static void Publish(ReportCard card, DateTime now, Guid? userId)
    {
        var current = card.Versions.First(v => v.VersionNo == card.CurrentVersion);
        if (current.Status != ReportCardVersionStatus.Draft)
            throw new ConflictException(AcademicErrors.InvalidTransition, P(("from", Camel(current.Status)), ("to", Camel(ReportCardVersionStatus.Published))));
        foreach (var prev in card.Versions.Where(v => v.Status == ReportCardVersionStatus.Published)) prev.Status = ReportCardVersionStatus.Superseded;
        current.Status = ReportCardVersionStatus.Published;
        current.PublishedAt = now;
        current.PublishedByUserId = userId;
        card.Status = ReportCardStatus.Published;
        card.PublishedVersion = current.VersionNo;
        card.PublishedAt = now;
        card.UpdatedAt = now;
    }

    private async Task<ReportCard> FindAsync(Guid id, string permission, CancellationToken ct, bool track = false)
    {
        IQueryable<ReportCard> q = track ? db.ReportCards : db.ReportCards.AsNoTracking();
        var card = await q.Include(r => r.Versions).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException();
        if ((await access.GetBranchScopeAsync(permission, ct)).Includes(card.BranchId)) return card;
        if ((await access.GetBranchScopeAsync(Permissions.ReportCardsRead, ct)).Includes(card.BranchId)) throw new ForbiddenException(ErrorCodes.ForbiddenScope);
        throw new NotFoundException();
    }

    public static ReportCardContent Content(ReportCardVersion v) => JsonSerializer.Deserialize<ReportCardContent>(v.Content, Json)!;

    private async Task<IReadOnlyList<ReportCardSummaryDto>> SummariesAsync(IReadOnlyList<ReportCard> cards, CancellationToken ct)
    {
        var students = await people.GetStudentsAsync(cards.Select(c => c.StudentId).ToList(), ct);
        return cards.Select(c =>
        {
            var s = students.GetValueOrDefault(c.StudentId);
            var current = c.Versions.FirstOrDefault(v => v.VersionNo == c.CurrentVersion);
            var avg = current is null ? null : Content(current).Average;
            return new ReportCardSummaryDto(c.Id, c.StudentId, s?.FirstName ?? "", s?.LastName ?? "", s?.StudentNumber ?? "", c.TermId, c.BranchId,
                Camel(c.Status), c.CurrentVersion, c.PublishedVersion, current?.GeneratedAt ?? c.CreatedAt, c.PublishedAt, avg, Rv(c.RowVersion));
        }).ToList();
    }
}

/// <summary>Builds report-card snapshots for one term from approved/published assessments and attendance.</summary>
internal sealed class SnapshotBuilder
{
    private readonly IAcademicDbContext _db;
    private readonly Term _term;
    private readonly AcademicYear _year;
    private readonly GradingScheme _default;
    private readonly Dictionary<Guid, ClassSection> _classes;
    private readonly Dictionary<Guid, Course> _courses;
    private readonly Dictionary<Guid, Subject> _subjects;
    private readonly Dictionary<Guid, GradingScheme> _schemes;
    private readonly Dictionary<Guid, List<Assessment>> _assessments;

    private SnapshotBuilder(IAcademicDbContext db, Term term, AcademicYear year, GradingScheme def, Dictionary<Guid, ClassSection> classes,
        Dictionary<Guid, Course> courses, Dictionary<Guid, Subject> subjects, Dictionary<Guid, GradingScheme> schemes, Dictionary<Guid, List<Assessment>> assessments)
    {
        _db = db; _term = term; _year = year; _default = def; _classes = classes; _courses = courses; _subjects = subjects; _schemes = schemes; _assessments = assessments;
    }

    public static async Task<SnapshotBuilder> CreateAsync(IAcademicDbContext db, CatalogService catalog, Term term, IReadOnlyList<ClassSection> classes, CancellationToken ct)
    {
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == term.AcademicYearId, ct);
        var def = await catalog.DefaultSchemeAsync(ct);
        var courseIds = classes.Select(c => c.CourseId).Distinct().ToList();
        var courses = await db.Courses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var subjectIds = courses.Values.Select(c => c.SubjectId).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var schemeIds = classes.Select(c => c.GradingSchemeId).Distinct().ToList();
        var schemes = await db.GradingSchemes.AsNoTracking().Include(s => s.Bands).Where(s => schemeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var classIds = classes.Select(c => c.Id).ToList();
        var counted = new[] { AssessmentStatus.Approved, AssessmentStatus.Published };
        var assessments = (await db.Assessments.AsNoTracking().Include(a => a.Results)
                .Where(a => classIds.Contains(a.ClassId) && counted.Contains(a.Status)).ToListAsync(ct))
            .GroupBy(a => a.ClassId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.Date ?? DateOnly.MaxValue).ThenBy(a => a.CreatedAt).ToList());
        return new SnapshotBuilder(db, term, year, def, classes.ToDictionary(c => c.Id), courses, subjects, schemes, assessments);
    }

    public async Task<ReportCardContent> BuildAsync(StudentSummary student, IReadOnlyList<Guid> classIds, DateTime now, CancellationToken ct)
    {
        var lines = new List<RcLine>();
        var normalized = new List<decimal>();
        foreach (var cls in classIds.Select(id => _classes[id]).OrderBy(c => c.Code))
        {
            var scheme = _schemes[cls.GradingSchemeId];
            var course = _courses[cls.CourseId];
            var items = _assessments.GetValueOrDefault(cls.Id) ?? [];
            var rows = items.Select(a => (a, r: a.Results.FirstOrDefault(r => r.StudentId == student.Id))).ToList();
            var total = GradeCalculator.Total(rows.Select(x => new ScoreInput(x.a.Weight, x.a.MaxScore, x.r?.Score, x.r?.Mark)), GradeCalculator.Spec(scheme));
            if (total is not null) normalized.Add(total.Normalized);
            lines.Add(new RcLine(cls.Id, cls.Code, cls.Name, course.Name, _subjects.GetValueOrDefault(course.SubjectId)?.Name ?? "", CatalogService.Info(scheme),
                AssessmentService.Total(total),
                rows.Select(x => new RcAssessment(x.a.Name, Camel(x.a.Type), x.a.Weight, x.a.MaxScore, x.r?.Score, Camel(x.r?.Mark))).ToList()));
        }
        var average = normalized.Count == 0 ? null
            : AssessmentService.Total(GradeCalculator.FromNormalized(normalized.Average(), GradeCalculator.Spec(_default)));

        var ids = classIds.ToList();
        var marks = await (from r in _db.AttendanceRecords.AsNoTracking()
                           join s in _db.AttendanceSessions on r.SessionId equals s.Id
                           where r.StudentId == student.Id && ids.Contains(s.ClassId)
                           select r.Status).ToListAsync(ct);
        var attendance = new RcAttendance(marks.Count(m => m == AttendanceStatus.Present), marks.Count(m => m == AttendanceStatus.Absent),
            marks.Count(m => m == AttendanceStatus.Late), marks.Count(m => m == AttendanceStatus.Excused));

        return new ReportCardContent(GradeCalculator.Formula, now, new RcStudent(student.Id, student.FirstName, student.LastName, student.StudentNumber),
            new RcRef(_year.Id, _year.Name), new RcTerm(_term.Id, _term.Name, Date(_term.StartDate), Date(_term.EndDate)), CatalogService.Info(_default),
            lines, average, attendance);
    }
}
