using EEMP.Modules.Academic.Domain;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Academic.Application.Fmt;

namespace EEMP.Modules.Academic.Application;

/// <summary>
/// Assessments, score entry and the grade workflow (draft → submitted → approved → published, return, reopen).
/// Assigned teachers may define assessments, enter scores and submit; approval and publication need grades.approve / grades.publish.
/// Every score change and transition is written to the append-only grade history.
/// </summary>
public sealed class AssessmentService(IAcademicDbContext db, ClassAccess classAccess, AcademicActor actor, IPeopleDirectory people)
{
    public async Task<IReadOnlyList<AssessmentDto>> ListAsync(Guid classId, CancellationToken ct)
    {
        await classAccess.AuthorizeAsync(classId, Permissions.GradesRead, true, Permissions.GradesRead, ct);
        var items = await db.Assessments.AsNoTracking().Where(a => a.ClassId == classId).ToListAsync(ct);
        return items.OrderBy(a => a.Date ?? DateOnly.MaxValue).ThenBy(a => a.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<AssessmentDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await FindAsync(id, Permissions.GradesRead, true, ct));

    public async Task<AssessmentDto> CreateAsync(Guid classId, AssessmentRequest req, CancellationToken ct)
    {
        var (type, date) = Validate(req, isNew: true);
        await classAccess.AuthorizeAsync(classId, Permissions.GradesManage, true, Permissions.GradesRead, ct);
        var a = new Assessment { ClassId = classId, Name = req.Name!.Trim(), Type = type, Weight = req.Weight!.Value, MaxScore = req.MaxScore!.Value, Date = date };
        db.Assessments.Add(a);
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<AssessmentDto> UpdateAsync(Guid id, AssessmentRequest req, CancellationToken ct)
    {
        var (type, date) = Validate(req, isNew: false);
        var a = await FindAsync(id, Permissions.GradesManage, true, ct, track: true, withResults: true);
        if (a.Status != AssessmentStatus.Draft) throw new ConflictException(AcademicErrors.AssessmentLocked);
        if (a.Results.Any(r => r.Score > req.MaxScore)) { new Validator().Add("maxScore", AcademicErrors.ScoreOutOfRange).ThrowIfInvalid(); }
        db.SetOriginalRowVersion(a, RowVersion(req.RowVersion));
        a.Name = req.Name!.Trim();
        a.Type = type;
        a.Weight = req.Weight!.Value;
        a.MaxScore = req.MaxScore!.Value;
        a.Date = date;
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var a = await FindAsync(id, Permissions.GradesManage, true, ct, track: true);
        if (a.Status != AssessmentStatus.Draft || a.EverPublished) throw new ConflictException(AcademicErrors.AssessmentLocked);
        db.Assessments.Remove(a);
        await db.SaveChangesAsync(ct);
    }

    private static (AssessmentType Type, DateOnly? Date) Validate(AssessmentRequest req, bool isNew)
    {
        var v = new Validator().RequiredText("name", req.Name, 200)
            .When(req.Weight is null, "weight", Validator.Required).When(req.Weight is <= 0 or > 100, "weight", Validator.InvalidValue)
            .When(req.MaxScore is null, "maxScore", Validator.Required).When(req.MaxScore is <= 0 or > 1000, "maxScore", Validator.InvalidValue);
        if (!isNew) v.RowVersion("rowVersion", req.RowVersion);
        var type = ParseEnum<AssessmentType>(req.Type, "type", v, required: true);
        var date = Validator.ParseDate(req.Date, "date", v);
        v.ThrowIfInvalid();
        return (type!.Value, date);
    }

    private async Task<Assessment> FindAsync(Guid id, string permission, bool teacherAllowed, CancellationToken ct, bool track = false, bool withResults = false)
    {
        IQueryable<Assessment> q = track ? db.Assessments : db.Assessments.AsNoTracking();
        if (withResults) q = q.Include(a => a.Results);
        var a = await q.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        await classAccess.AuthorizeAsync(a.ClassId, permission, teacherAllowed, Permissions.GradesRead, ct);
        return a;
    }

    // ---------- Scores ----------
    public async Task<ResultsDto> GetResultsAsync(Guid id, CancellationToken ct)
    {
        var a = await FindAsync(id, Permissions.GradesRead, true, ct, withResults: true);
        return await ResultsAsync(a, ct);
    }

    public async Task<ResultsDto> SaveResultsAsync(Guid id, ResultsRequest req, CancellationToken ct)
    {
        var v = new Validator().RowVersion("rowVersion", req.RowVersion);
        var entries = new List<(Guid StudentId, ResultMark Mark, decimal? Score, string? Comment, string? Reason, int Index)>();
        foreach (var (r, i) in (req.Results ?? []).Select((r, i) => (r, i)))
        {
            var f = $"results[{i}]";
            v.When(r.StudentId is null || r.StudentId == Guid.Empty, $"{f}.studentId", Validator.Required)
             .OptionalText($"{f}.comment", r.Comment, 500).OptionalText($"{f}.reason", r.Reason, 500);
            var mark = ParseEnum<ResultMark>(r.Mark, $"{f}.mark", v) ?? (r.Score is null ? null : ResultMark.Scored);
            if (mark is null && r.Score is null) v.Add($"{f}.mark", Validator.Required);
            if (mark == ResultMark.Scored && r.Score is null) v.Add($"{f}.score", Validator.Required);
            if (r.StudentId is { } sid && sid != Guid.Empty && mark is { } m)
                entries.Add((sid, m, m == ResultMark.Scored ? r.Score : null, Clean(r.Comment), Clean(r.Reason), i));
        }
        if (entries.Select(e => e.StudentId).Distinct().Count() != entries.Count) v.Add("results", Validator.InvalidValue);
        v.ThrowIfInvalid();

        var a = await FindAsync(id, Permissions.GradesManage, true, ct, track: true, withResults: true);
        if (a.Status != AssessmentStatus.Draft) throw new ConflictException(AcademicErrors.AssessmentLocked);
        foreach (var e in entries.Where(e => e.Score is { } s && (s < 0 || s > a.MaxScore)))
            v.Add($"results[{e.Index}].score", AcademicErrors.ScoreOutOfRange);
        v.ThrowIfInvalid();
        var enrolled = (await db.Enrollments.AsNoTracking().Where(e => e.ClassId == a.ClassId && e.Status == EnrollmentStatus.Active)
            .Select(e => e.StudentId).ToListAsync(ct)).ToHashSet();
        var bad = entries.FirstOrDefault(e => !enrolled.Contains(e.StudentId) && a.Results.All(r => r.StudentId != e.StudentId));
        if (bad.StudentId != Guid.Empty) throw new BusinessRuleException(AcademicErrors.StudentNotEnrolled, P(("studentId", bad.StudentId)));

        var now = actor.UtcNow;
        db.SetOriginalRowVersion(a, RowVersion(req.RowVersion));
        var changed = false;
        foreach (var e in entries)
        {
            var result = a.Results.FirstOrDefault(r => r.StudentId == e.StudentId);
            if (result is not null && result.Mark == e.Mark && result.Score == e.Score && result.Comment == e.Comment) continue;
            db.GradeAudit.Add(new GradeAuditEntry
            {
                AssessmentId = a.Id, StudentId = e.StudentId, Action = GradeAuditAction.ScoreChanged, OldScore = result?.Score, NewScore = e.Score,
                OldMark = result?.Mark, NewMark = e.Mark, Reason = e.Reason, ActorUserId = actor.UserId, OccurredAt = now,
            });
            if (result is null)
            {
                result = new AssessmentResult { AssessmentId = a.Id, StudentId = e.StudentId };
                a.Results.Add(result);
            }
            result.Mark = e.Mark;
            result.Score = e.Score;
            result.Comment = e.Comment;
            result.UpdatedAt = now;
            changed = true;
        }
        if (changed)
        {
            a.UpdatedAt = now; // bump the assessment rowversion: concurrent score sheets conflict
            await db.SaveChangesAsync(ct);
        }
        return await ResultsAsync(a, ct);
    }

    private async Task<ResultsDto> ResultsAsync(Assessment a, CancellationToken ct)
    {
        var enrolled = await db.Enrollments.AsNoTracking().Where(e => e.ClassId == a.ClassId && e.Status == EnrollmentStatus.Active)
            .Select(e => e.StudentId).ToListAsync(ct);
        var ids = enrolled.Union(a.Results.Select(r => r.StudentId)).ToList();
        var students = await people.GetStudentsAsync(ids, ct);
        var rows = ids.Select(id =>
        {
            var s = students.GetValueOrDefault(id);
            var r = a.Results.FirstOrDefault(x => x.StudentId == id);
            return new ResultRowDto(id, s?.FirstName ?? "", s?.LastName ?? "", s?.StudentNumber ?? "", r?.Score, Camel(r?.Mark), r?.Comment);
        }).OrderBy(r => r.LastName).ThenBy(r => r.FirstName).ToList();
        return new ResultsDto(ToDto(a), rows);
    }

    // ---------- Workflow ----------
    public async Task<AssessmentDto> TransitionAsync(Guid id, GradeAuditAction action, TransitionRequest req, CancellationToken ct)
    {
        var v = new Validator().RowVersion("rowVersion", req.RowVersion).OptionalText("reason", req.Reason, 500);
        if (AssessmentWorkflow.RequiresReason(action)) v.RequiredText("reason", req.Reason, 500);
        v.ThrowIfInvalid();
        var (permission, teacherAllowed) = action switch
        {
            GradeAuditAction.Submitted => (Permissions.GradesManage, true),
            GradeAuditAction.Published => (Permissions.GradesPublish, false),
            _ => (Permissions.GradesApprove, false),
        };
        var a = await FindAsync(id, permission, teacherAllowed, ct, track: true, withResults: true);
        if (action == GradeAuditAction.Submitted && a.Status == AssessmentStatus.Draft)
        {
            var enrolled = await db.Enrollments.AsNoTracking().Where(e => e.ClassId == a.ClassId && e.Status == EnrollmentStatus.Active)
                .Select(e => e.StudentId).ToListAsync(ct);
            var missing = enrolled.Count(s => a.Results.All(r => r.StudentId != s));
            if (missing > 0) throw new BusinessRuleException(AcademicErrors.ResultsIncomplete, P(("missing", missing)));
        }
        db.SetOriginalRowVersion(a, RowVersion(req.RowVersion));
        var now = actor.UtcNow;
        AssessmentWorkflow.Apply(a, action, now, Clean(req.Reason));
        a.UpdatedAt = now;
        db.GradeAudit.Add(new GradeAuditEntry { AssessmentId = a.Id, Action = action, Reason = Clean(req.Reason), ActorUserId = actor.UserId, OccurredAt = now });
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<IReadOnlyList<GradeHistoryDto>> HistoryAsync(Guid id, CancellationToken ct)
    {
        var a = await FindAsync(id, Permissions.GradesRead, true, ct);
        var rows = await db.GradeAudit.AsNoTracking().Where(h => h.AssessmentId == a.Id).ToListAsync(ct);
        return rows.OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).Select(h => new GradeHistoryDto(Camel(h.Action), h.StudentId, h.OldScore, h.NewScore,
            Camel(h.OldMark), Camel(h.NewMark), h.Reason, h.ActorUserId, h.OccurredAt)).ToList();
    }

    // ---------- Gradebook (staff/teacher view over live scores) ----------
    public async Task<GradebookDto> GradebookAsync(Guid classId, CancellationToken ct)
    {
        var cls = await classAccess.AuthorizeAsync(classId, Permissions.GradesRead, true, Permissions.GradesRead, ct);
        var scheme = await db.GradingSchemes.AsNoTracking().Include(s => s.Bands).FirstAsync(s => s.Id == cls.GradingSchemeId, ct);
        var assessments = (await db.Assessments.AsNoTracking().Include(a => a.Results).Where(a => a.ClassId == classId).ToListAsync(ct))
            .OrderBy(a => a.Date ?? DateOnly.MaxValue).ThenBy(a => a.CreatedAt).ToList();
        var enrolled = await db.Enrollments.AsNoTracking().Where(e => e.ClassId == classId && e.Status == EnrollmentStatus.Active)
            .Select(e => e.StudentId).ToListAsync(ct);
        var students = await people.GetStudentsAsync(enrolled, ct);
        var spec = GradeCalculator.Spec(scheme);
        var rows = enrolled.Select(sid =>
        {
            var s = students.GetValueOrDefault(sid);
            var scores = assessments.Select(a => (a, r: a.Results.FirstOrDefault(r => r.StudentId == sid))).ToList();
            var total = GradeCalculator.Total(scores.Select(x => new ScoreInput(x.a.Weight, x.a.MaxScore, x.r?.Score, x.r?.Mark)), spec);
            return new GradebookStudentDto(sid, s?.FirstName ?? "", s?.LastName ?? "", s?.StudentNumber ?? "",
                scores.Select(x => new GradebookScoreDto(x.a.Id, x.r?.Score, Camel(x.r?.Mark))).ToList(), Total(total));
        }).OrderBy(r => r.LastName).ThenBy(r => r.FirstName).ToList();
        return new GradebookDto(classId, CatalogService.Info(scheme), assessments.Select(ToDto).ToList(), rows);
    }

    public static GradeTotalDto? Total(GradeTotal? t) => t is null ? null : new GradeTotalDto(t.Value, t.Label, t.Passed);

    public static AssessmentDto ToDto(Assessment a) => new(a.Id, a.ClassId, a.Name, Camel(a.Type), a.Weight, a.MaxScore, Date(a.Date), Camel(a.Status),
        a.SubmittedAt, a.ApprovedAt, a.PublishedAt, a.ReturnReason, Rv(a.RowVersion));
}
