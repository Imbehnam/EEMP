using System.Globalization;
using System.Text.Json;
using EEMP.Modules.Academic.Contracts;
using EEMP.Modules.Academic.Domain;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Academic.Application;

public interface IAcademicDbContext
{
    DbSet<AcademicYear> AcademicYears { get; }
    DbSet<Term> Terms { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Course> Courses { get; }
    DbSet<GradingScheme> GradingSchemes { get; }
    DbSet<GradeBand> GradeBands { get; }
    DbSet<ClassSection> Classes { get; }
    DbSet<ScheduleSlot> ScheduleSlots { get; }
    DbSet<TeacherAssignment> TeacherAssignments { get; }
    DbSet<Enrollment> Enrollments { get; }
    DbSet<AttendanceSession> AttendanceSessions { get; }
    DbSet<AttendanceRecord> AttendanceRecords { get; }
    DbSet<Assessment> Assessments { get; }
    DbSet<AssessmentResult> AssessmentResults { get; }
    DbSet<GradeAuditEntry> GradeAudit { get; }
    DbSet<ReportCard> ReportCards { get; }
    DbSet<ReportCardVersion> ReportCardVersions { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class;
    /// <summary>Detaches all tracked entities (used to retry an idempotent upsert after a unique-key race).</summary>
    void ResetTracking();
}

/// <summary>Formatting/parsing helpers matching the M1 JSON conventions.</summary>
public static class Fmt
{
    public static string Camel<TEnum>(TEnum value) where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    public static string? Camel<TEnum>(TEnum? value) where TEnum : struct, Enum => value is { } v ? Camel(v) : null;
    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string? Date(DateOnly? d) => d is { } v ? Date(v) : null;
    public static string Time(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
    public static string Rv(byte[] rv) => Convert.ToBase64String(rv);
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static TEnum? ParseEnum<TEnum>(string? value, string field, Validator v, bool required = false) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) v.Add(field, Validator.Required);
            return null;
        }
        if (!int.TryParse(value, out _) && Enum.TryParse<TEnum>(value, ignoreCase: true, out var e) && Enum.IsDefined(e)) return e;
        v.Add(field, Validator.InvalidValue);
        return null;
    }

    public static DateOnly? RequiredDate(string? value, string field, Validator v)
    {
        if (string.IsNullOrWhiteSpace(value)) { v.Add(field, Validator.Required); return null; }
        return Validator.ParseDate(value, field, v);
    }

    public static TimeOnly? ParseTime(string? value, string field, Validator v)
    {
        if (string.IsNullOrWhiteSpace(value)) { v.Add(field, Validator.Required); return null; }
        if (TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return t;
        v.Add(field, Validator.InvalidFormat);
        return null;
    }

    public static byte[] RowVersion(string? value) => Convert.FromBase64String(value!);

    public static Dictionary<string, object?> P(params (string Key, object? Value)[] items) => items.ToDictionary(i => i.Key, i => i.Value);
}

/// <summary>Per-request academic actor context: tenant-local "today" and the caller's person relationships.</summary>
public sealed class AcademicActor(ITenantContext tenant, IPeopleDirectory people, IOrganizationSettings settings, TimeProvider clock)
{
    private DateOnly? _today;
    private (bool Loaded, Guid? Id) _teacher;

    public Guid? UserId => tenant.UserId;

    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        if (_today is { } t) return t;
        var tzId = await settings.GetTimeZoneAsync(ct);
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(tzId, out var found) ? found : TimeZoneInfo.Utc;
        return (_today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime)).Value;
    }

    public async Task<Guid?> TeacherIdAsync(CancellationToken ct)
    {
        if (_teacher.Loaded) return _teacher.Id;
        var id = tenant.UserId is { } uid ? await people.FindTeacherIdForUserAsync(uid, ct) : null;
        _teacher = (true, id);
        return id;
    }

    /// <summary>Students the caller may see in the portal: themselves (student) and linked students (guardian).</summary>
    public async Task<IReadOnlySet<Guid>> PortalStudentIdsAsync(CancellationToken ct)
    {
        var uid = tenant.UserId ?? throw new UnauthorizedException();
        var ids = new HashSet<Guid>(await people.GetGuardianStudentIdsForUserAsync(uid, ct));
        if (await people.FindStudentIdForUserAsync(uid, ct) is { } own) ids.Add(own);
        return ids;
    }
}

/// <summary>
/// Class-level authorization: the caller holds <c>permission</c> in the class's branch, or (when allowed for the operation)
/// is a teacher with an active assignment on the class. Invisible classes are reported as not found.
/// </summary>
public sealed class ClassAccess(IAcademicDbContext db, IAccessControl access, AcademicActor actor)
{
    public async Task<bool> IsAssignedTeacherAsync(Guid classId, CancellationToken ct)
    {
        var teacherId = await actor.TeacherIdAsync(ct);
        if (teacherId is null) return false;
        var today = await actor.TodayAsync(ct);
        var rows = await db.TeacherAssignments.AsNoTracking().Where(a => a.ClassId == classId && a.TeacherId == teacherId).ToListAsync(ct);
        return rows.Any(a => a.IsActiveOn(today));
    }

    /// <param name="readPermission">Permission that makes the class visible (decides 404 vs 403).</param>
    public async Task<ClassSection> AuthorizeAsync(Guid classId, string permission, bool teacherAllowed, string readPermission, CancellationToken ct,
        bool track = false, bool includeDetails = false)
    {
        IQueryable<ClassSection> q = db.Classes;
        if (!track) q = q.AsNoTracking();
        if (includeDetails) q = q.Include(c => c.Schedule).Include(c => c.Teachers);
        var cls = await q.FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException();
        await EnsureAsync(cls, permission, teacherAllowed, readPermission, ct);
        return cls;
    }

    public async Task EnsureAsync(ClassSection cls, string permission, bool teacherAllowed, string readPermission, CancellationToken ct)
    {
        if ((await access.GetBranchScopeAsync(permission, ct)).Includes(cls.BranchId)) return;
        var teacher = await IsAssignedTeacherAsync(cls.Id, ct);
        if (teacherAllowed && teacher) return;
        if (teacher || (await access.GetBranchScopeAsync(readPermission, ct)).Includes(cls.BranchId) ||
            (await access.GetBranchScopeAsync(Permissions.ClassesRead, ct)).Includes(cls.BranchId))
            throw new ForbiddenException(ErrorCodes.ForbiddenScope);
        throw new NotFoundException();
    }
}

public sealed class AcademicUsage(IAcademicDbContext db) : IAcademicUsage
{
    public Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default) =>
        db.Classes.AnyAsync(c => c.BranchId == branchId, ct);

    public async Task<bool> IsStudentReferencedAsync(Guid studentId, CancellationToken ct = default) =>
        await db.Enrollments.AnyAsync(e => e.StudentId == studentId, ct) || await db.ReportCards.AnyAsync(r => r.StudentId == studentId, ct);

    public Task<bool> IsTeacherReferencedAsync(Guid teacherId, CancellationToken ct = default) =>
        db.TeacherAssignments.AnyAsync(a => a.TeacherId == teacherId, ct);
}

public sealed class EnrollmentDirectory(IAcademicDbContext db) : IEnrollmentDirectory
{
    private IQueryable<EnrollmentInfo> Query() =>
        from e in db.Enrollments.AsNoTracking()
        join c in db.Classes.AsNoTracking() on e.ClassId equals c.Id
        select new EnrollmentInfo(e.Id, e.StudentId, e.ClassId, c.BranchId, c.TermId, c.CourseId, e.Status != EnrollmentStatus.Withdrawn);

    public Task<EnrollmentInfo?> GetAsync(Guid enrollmentId, CancellationToken ct = default) =>
        Query().FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);

    public Task<ClassInfo?> GetClassAsync(Guid classId, CancellationToken ct = default) =>
        db.Classes.AsNoTracking().Where(c => c.Id == classId)
            .Select(c => new ClassInfo(c.Id, c.BranchId, c.TermId, c.CourseId, c.Code, c.Name)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<EnrollmentInfo>> ListForClassAsync(Guid classId, CancellationToken ct = default) =>
        await Query().Where(e => e.ClassId == classId).ToListAsync(ct);

    public async Task<IReadOnlyList<EnrollmentInfo>> ListForTermAsync(Guid termId, CancellationToken ct = default) =>
        await Query().Where(e => e.TermId == termId).ToListAsync(ct);

    public Task<bool> TermExistsAsync(Guid termId, CancellationToken ct = default) => db.Terms.AnyAsync(t => t.Id == termId, ct);
}
