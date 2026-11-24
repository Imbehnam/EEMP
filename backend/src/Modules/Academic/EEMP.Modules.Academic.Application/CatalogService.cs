using EEMP.Modules.Academic.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Academic.Application.Fmt;

namespace EEMP.Modules.Academic.Application;

/// <summary>
/// Tenant-wide academic catalog: years, terms, subjects, courses and grading schemes. Reading needs catalog.read in any scope;
/// changes need catalog.manage tenant-wide (the catalog is shared by every branch).
/// </summary>
public sealed class CatalogService(IAcademicDbContext db, IAccessControl access)
{
    private Task EnsureManageAsync(CancellationToken ct) => access.EnsureTenantWideAsync(Permissions.CatalogManage, ct);

    private static ConflictException Duplicate(string field) => new(ErrorCodes.DuplicateValue, P(("field", field)));

    // ---------- Academic years ----------
    public async Task<PagedResult<AcademicYearDto>> ListYearsAsync(PageRequest page, CancellationToken ct)
    {
        var q = db.AcademicYears.AsNoTracking();
        if (page.Search is { } s) q = q.Where(y => y.Name.Contains(s));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(y => y.StartDate).ThenBy(y => y.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<AcademicYearDto>(items.Select(ToDto).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<AcademicYearDto> GetYearAsync(Guid id, CancellationToken ct) => ToDto(await FindYearAsync(id, ct));

    public async Task<AcademicYearDto> SaveYearAsync(Guid? id, AcademicYearRequest req, CancellationToken ct)
    {
        await EnsureManageAsync(ct);
        var v = new Validator().RequiredText("name", req.Name, 100);
        if (id is not null) v.RowVersion("rowVersion", req.RowVersion);
        var start = RequiredDate(req.StartDate, "startDate", v);
        var end = RequiredDate(req.EndDate, "endDate", v);
        var status = ParseEnum<AcademicYearStatus>(req.Status, "status", v);
        if (start is { } s0 && end is { } e0 && s0 >= e0) v.Add("endDate", AcademicErrors.InvalidDateRange);
        v.ThrowIfInvalid();

        var year = id is { } yid ? await FindYearAsync(yid, ct, track: true) : new AcademicYear();
        var name = req.Name!.Trim();
        if (await db.AcademicYears.AnyAsync(y => y.Name == name && y.Id != year.Id, ct)) throw Duplicate("name");
        if (id is not null)
        {
            db.SetOriginalRowVersion(year, RowVersion(req.RowVersion));
            var terms = await db.Terms.AsNoTracking().Where(t => t.AcademicYearId == year.Id).ToListAsync(ct);
            if (terms.Any(t => t.StartDate < start!.Value || t.EndDate > end!.Value)) throw new BusinessRuleException(AcademicErrors.TermOutsideYear);
        }
        year.Name = name;
        year.StartDate = start!.Value;
        year.EndDate = end!.Value;
        year.Status = status ?? (id is null ? AcademicYearStatus.Planned : year.Status);
        if (id is null) db.AcademicYears.Add(year);
        await db.SaveChangesAsync(ct);
        return ToDto(year);
    }

    public async Task DeleteYearAsync(Guid id, CancellationToken ct)
    {
        await EnsureManageAsync(ct);
        var year = await FindYearAsync(id, ct, track: true);
        if (await db.Terms.AnyAsync(t => t.AcademicYearId == id, ct)) throw new ConflictException(AcademicErrors.YearInUse);
        db.AcademicYears.Remove(year);
        await db.SaveChangesAsync(ct);
    }

    private async Task<AcademicYear> FindYearAsync(Guid id, CancellationToken ct, bool track = false) =>
        await (track ? db.AcademicYears : db.AcademicYears.AsNoTracking()).FirstOrDefaultAsync(y => y.Id == id, ct) ?? throw new NotFoundException();

    public static AcademicYearDto ToDto(AcademicYear y) => new(y.Id, y.Name, Date(y.StartDate), Date(y.EndDate), Camel(y.Status), Rv(y.RowVersion));

    // ---------- Terms ----------
    public async Task<IReadOnlyList<TermDto>> ListTermsAsync(Guid? academicYearId, CancellationToken ct)
    {
        var q = db.Terms.AsNoTracking();
        if (academicYearId is { } y) q = q.Where(t => t.AcademicYearId == y);
        var items = await q.OrderBy(t => t.StartDate).ThenBy(t => t.SortOrder).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<TermDto> GetTermAsync(Guid id, CancellationToken ct) => ToDto(await FindTermAsync(id, ct));

    public async Task<TermDto> SaveTermAsync(Guid? id, TermRequest req, CancellationToken ct)
    {
        await EnsureManageAsync(ct);
        var v = new Validator().RequiredText("name", req.Name, 100);
        if (id is null) v.When(req.AcademicYearId is null || req.AcademicYearId == Guid.Empty, "academicYearId", Validator.Required);
        else v.RowVersion("rowVersion", req.RowVersion);
        var start = RequiredDate(req.StartDate, "startDate", v);
        var end = RequiredDate(req.EndDate, "endDate", v);
        if (start is { } s0 && end is { } e0 && s0 >= e0) v.Add("endDate", AcademicErrors.InvalidDateRange);
        v.ThrowIfInvalid();

        var term = id is { } tid ? await FindTermAsync(tid, ct, track: true) : new Term { AcademicYearId = req.AcademicYearId!.Value };
        var year = await db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == term.AcademicYearId, ct);
        if (year is null) { v.Add("academicYearId", AcademicErrors.YearNotFound); v.ThrowIfInvalid(); }
        if (start < year!.StartDate || end > year.EndDate) throw new BusinessRuleException(AcademicErrors.TermOutsideYear);
        var name = req.Name!.Trim();
        var siblings = await db.Terms.AsNoTracking().Where(t => t.AcademicYearId == term.AcademicYearId && t.Id != term.Id).ToListAsync(ct);
        if (siblings.Any(t => t.Name == name)) throw Duplicate("name");
        if (siblings.Any(t => t.StartDate <= end && start <= t.EndDate)) throw new ConflictException(AcademicErrors.TermOverlap);
        if (id is not null)
        {
            db.SetOriginalRowVersion(term, RowVersion(req.RowVersion));
            // Existing classes keep their dates meaningful: attendance must stay inside the term.
            var outside = await (from s in db.AttendanceSessions
                                 join c in db.Classes on s.ClassId equals c.Id
                                 where c.TermId == term.Id && (s.Date < start!.Value || s.Date > end!.Value)
                                 select s.Id).AnyAsync(ct);
            if (outside) throw new BusinessRuleException(AcademicErrors.DateOutsideTerm);
        }
        term.Name = name;
        term.StartDate = start!.Value;
        term.EndDate = end!.Value;
        term.SortOrder = req.SortOrder ?? (id is null ? siblings.Count + 1 : term.SortOrder);
        if (id is null) db.Terms.Add(term);
        await db.SaveChangesAsync(ct);
        return ToDto(term);
    }

    public async Task DeleteTermAsync(Guid id, CancellationToken ct)
    {
        await EnsureManageAsync(ct);
        var term = await FindTermAsync(id, ct, track: true);
        if (await db.Classes.AnyAsync(c => c.TermId == id, ct) || await db.ReportCards.AnyAsync(r => r.TermId == id, ct))
            throw new ConflictException(AcademicErrors.TermInUse);
        db.Terms.Remove(term);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Term> FindTermAsync(Guid id, CancellationToken ct, bool track = false) =>
        await (track ? db.Terms : db.Terms.AsNoTracking()).FirstOrDefaultAsync(y => y.Id == id, ct) ?? throw new NotFoundException();

    public static TermDto ToDto(Term t) => new(t.Id, t.AcademicYearId, t.Name, Date(t.StartDate), Date(t.EndDate), t.SortOrder, Rv(t.RowVersion));

    // ---------- Subjects ----------
    public async Task<PagedResult<SubjectDto>> ListSubjectsAsync(PageRequest page, CancellationToken ct)
    {
        var q = db.Subjects.AsNoTracking();
        if (page.Search is { } s) q = q.Where(x => x.Name.Contains(s) || x.Code.Contains(s));
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.Code).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<SubjectDto>(items.Select(ToDto).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<SubjectDto> GetSubjectAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Subjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException());

    public async Task<SubjectDto> SaveSubjectAsync(Guid? id, SubjectRequest req, CancellationToken ct)
    {
        await EnsureManageAsync(ct);
        var v = new Validator().RequiredText("code", req.Code, 30).RequiredText("name", req.Name, 200).OptionalText("description", req.Description, 1000);
        if (id is not null) v.RowVersion("rowVersion", req.RowVersion);
        v.ThrowIfInvalid();
        var subject = id is { } sid ? await db.Subjects.FirstOrDefaultAsync(x => x.Id == sid, ct) ?? throw new NotFoundException() : new Subject();
        var code = req.Code!.Trim();
        if (await db.Subjects.AnyAsync(x => x.Code == code && x.Id != subject.Id, ct)) throw Duplicate("code");
        if (id is not null) db.SetOriginalRowVersion(subject, RowVersion(req.RowVersion));
        subject.Code = code;
        subject.Name = req.Name!.Trim();
        subject.Description = Clean(req.Description);
        if (id is null) db.Subjects.Add(subject);
        await db.SaveChangesAsync(ct);
        return ToDto(subject);
    }

    public async Task DeleteSubjectAsync(Guid id, CancellationToken ct)
    {
        await EnsureManageAsync(ct);
        var subject = await db.Subjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        if (await db.Courses.AnyAsync(c => c.SubjectId == id, ct)) throw new ConflictException(AcademicErrors.SubjectInUse);
        db.Subjects.Remove(subject);
        await db.SaveChangesAsync(ct);
    }

    public static SubjectDto ToDto(Subject s) => new(s.Id, s.Code, s.Name, s.Description, Rv(s.RowVersion));

    // ---------- Courses ----------
    public async Task<PagedResult<CourseDto>> ListCoursesAsync(PageRequest page, Guid? subjectId, CancellationToken ct)
    {
        var q = db.Courses.AsNoTracking();
        if (subjectId is { } sid) q = q.Where(x => x.SubjectId == sid);
        if (page.Search is { } s) q = q.Where(x => x.Name.Contains(s) || x.Code.Contains(s));
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.Code).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<CourseDto>(items.Select(ToDto).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<CourseDto> GetCourseAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException());

    public async Task<CourseDto> SaveCourseAsync(Guid? id, CourseRequest req, CancellationToken ct)
    {
        await EnsureManageAsync(ct);
        var v = new Validator().RequiredText("code", req.Code, 30).RequiredText("name", req.Name, 200)
            .OptionalText("gradeLevel", req.GradeLevel, 50).OptionalText("description", req.Description, 1000)
            .When(req.SubjectId is null || req.SubjectId == Guid.Empty, "subjectId", Validator.Required);
        if (id is not null) v.RowVersion("rowVersion", req.RowVersion);
        v.ThrowIfInvalid();
        if (!await db.Subjects.AnyAsync(s => s.Id == req.SubjectId, ct)) v.Add("subjectId", AcademicErrors.SubjectNotFound);
        if (req.GradingSchemeId is { } gs && !await db.GradingSchemes.AnyAsync(s => s.Id == gs, ct)) v.Add("gradingSchemeId", AcademicErrors.GradingSchemeNotFound);
        v.ThrowIfInvalid();

        var course = id is { } cid ? await db.Courses.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw new NotFoundException() : new Course();
        var code = req.Code!.Trim();
        if (await db.Courses.AnyAsync(x => x.Code == code && x.Id != course.Id, ct)) throw Duplicate("code");
        if (id is not null) db.SetOriginalRowVersion(course, RowVersion(req.RowVersion));
        course.SubjectId = req.SubjectId!.Value;
        course.Code = code;
        course.Name = req.Name!.Trim();
        course.GradeLevel = Clean(req.GradeLevel);
        course.Description = Clean(req.Description);
        course.GradingSchemeId = req.GradingSchemeId;
        if (id is null) db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        return ToDto(course);
    }

    public async Task DeleteCourseAsync(Guid id, CancellationToken ct)
    {
        await EnsureManageAsync(ct);
        var course = await db.Courses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        if (await db.Classes.AnyAsync(c => c.CourseId == id, ct)) throw new ConflictException(AcademicErrors.CourseInUse);
        db.Courses.Remove(course);
        await db.SaveChangesAsync(ct);
    }

    public static CourseDto ToDto(Course c) => new(c.Id, c.SubjectId, c.Code, c.Name, c.GradeLevel, c.Description, c.GradingSchemeId, Rv(c.RowVersion));

    // ---------- Grading schemes ----------
    /// <summary>Creates the built-in schemes (IR20 default, PCT, LETTER) the first time a tenant needs them. Idempotent.</summary>
    public async Task EnsureDefaultSchemesAsync(CancellationToken ct)
    {
        if (await db.GradingSchemes.AnyAsync(ct)) return;
        foreach (var s in DefaultGradingSchemes.Create()) db.GradingSchemes.Add(s);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request created them concurrently (unique tenant+code); use theirs.
            db.ResetTracking();
        }
    }

    public async Task<GradingScheme> DefaultSchemeAsync(CancellationToken ct)
    {
        await EnsureDefaultSchemesAsync(ct);
        return await db.GradingSchemes.AsNoTracking().Include(s => s.Bands).OrderByDescending(s => s.IsDefault).ThenBy(s => s.Code).FirstAsync(ct);
    }

    public async Task<IReadOnlyList<GradingSchemeDto>> ListSchemesAsync(CancellationToken ct)
    {
        await EnsureDefaultSchemesAsync(ct);
        var items = await db.GradingSchemes.AsNoTracking().Include(s => s.Bands).OrderByDescending(s => s.IsDefault).ThenBy(s => s.Code).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<GradingSchemeDto> GetSchemeAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.GradingSchemes.AsNoTracking().Include(s => s.Bands).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException());

    public async Task<GradingSchemeDto> SaveSchemeAsync(Guid? id, GradingSchemeRequest req, CancellationToken ct)
    {
        await EnsureManageAsync(ct);
        await EnsureDefaultSchemesAsync(ct);
        var v = new Validator().RequiredText("code", req.Code, 30).RequiredText("name", req.Name, 200);
        if (id is not null) v.RowVersion("rowVersion", req.RowVersion);
        var kind = ParseEnum<GradingSchemeKind>(req.Kind, "kind", v, required: true);
        v.When(req.MinScore is null, "minScore", Validator.Required).When(req.MaxScore is null, "maxScore", Validator.Required)
         .When(req.PassScore is null, "passScore", Validator.Required);
        if (req.MinScore is { } min && req.MaxScore is { } max)
        {
            v.When(min < 0 || max > 1000 || min >= max, "maxScore", Validator.InvalidValue);
            v.When(req.PassScore is { } pass && (pass < min || pass > max), "passScore", Validator.InvalidValue);
            if (req.Bands is { } bands)
            {
                v.When(bands.Any(b => string.IsNullOrWhiteSpace(b.Label) || b.Label.Length > 30), "bands", Validator.InvalidValue);
                v.When(bands.Any(b => b.MinValue < min || b.MinValue > max), "bands", Validator.InvalidValue);
                v.When(bands.Select(b => b.MinValue).Distinct().Count() != bands.Count, "bands", Validator.InvalidValue);
            }
        }
        v.When(req.DecimalPlaces is < 0 or > 4, "decimalPlaces", Validator.InvalidValue);
        v.ThrowIfInvalid();

        var scheme = id is { } sid
            ? await db.GradingSchemes.Include(s => s.Bands).FirstOrDefaultAsync(x => x.Id == sid, ct) ?? throw new NotFoundException()
            : new GradingScheme();
        var code = req.Code!.Trim();
        if (await db.GradingSchemes.AnyAsync(x => x.Code == code && x.Id != scheme.Id, ct)) throw Duplicate("code");
        if (id is not null) db.SetOriginalRowVersion(scheme, RowVersion(req.RowVersion));
        scheme.Code = code;
        scheme.Name = req.Name!.Trim();
        scheme.Kind = kind!.Value;
        scheme.MinScore = req.MinScore!.Value;
        scheme.MaxScore = req.MaxScore!.Value;
        scheme.PassScore = req.PassScore!.Value;
        scheme.DecimalPlaces = req.DecimalPlaces ?? scheme.DecimalPlaces;
        scheme.MissingCountsAsZero = req.MissingCountsAsZero ?? scheme.MissingCountsAsZero;
        if (req.Bands is { } newBands)
        {
            db.GradeBands.RemoveRange(scheme.Bands);
            scheme.Bands = newBands.Select(b => new GradeBand { GradingSchemeId = scheme.Id, Label = b.Label.Trim(), MinValue = b.MinValue }).ToList();
        }
        if (req.IsDefault == true && !scheme.IsDefault)
        {
            foreach (var other in await db.GradingSchemes.Where(s => s.IsDefault && s.Id != scheme.Id).ToListAsync(ct)) other.IsDefault = false;
            scheme.IsDefault = true;
        }
        if (id is null) db.GradingSchemes.Add(scheme);
        await db.SaveChangesAsync(ct);
        return ToDto(scheme);
    }

    public async Task DeleteSchemeAsync(Guid id, CancellationToken ct)
    {
        await EnsureManageAsync(ct);
        var scheme = await db.GradingSchemes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        if (scheme.IsDefault || await db.Courses.AnyAsync(c => c.GradingSchemeId == id, ct) || await db.Classes.AnyAsync(c => c.GradingSchemeId == id, ct))
            throw new ConflictException(AcademicErrors.GradingSchemeInUse);
        db.GradingSchemes.Remove(scheme);
        await db.SaveChangesAsync(ct);
    }

    public static GradingSchemeDto ToDto(GradingScheme s) => new(s.Id, s.Code, s.Name, Camel(s.Kind), s.MinScore, s.MaxScore, s.PassScore, s.DecimalPlaces,
        s.MissingCountsAsZero, s.IsDefault, s.Bands.OrderByDescending(b => b.MinValue).Select(b => new GradeBandDto(b.Label, b.MinValue)).ToList(), Rv(s.RowVersion));

    public static SchemeInfoDto Info(GradingScheme s) => new(s.Code, s.Name, Camel(s.Kind), s.MinScore, s.MaxScore, s.PassScore, s.DecimalPlaces);
}
