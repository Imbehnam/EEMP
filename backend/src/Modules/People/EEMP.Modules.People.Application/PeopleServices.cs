using EEMP.Modules.Academic.Contracts;
using EEMP.Modules.Finance.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.Modules.People.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Tenancy;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.People.Application.PersonService<EEMP.Modules.People.Domain.Student>;

namespace EEMP.Modules.People.Application;

public sealed class StudentService(PersonService<Student> core, PersonService<Guardian> guardianCore, IAccessControl access, IAcademicUsage academic,
    IFinanceUsage finance)
{
    private static readonly PersonKind<Student> Kind = PeopleKinds.Students;
    private static IQueryable<Student> WithGuardians(IQueryable<Student> q) => q.Include(s => s.Guardians);

    public async Task<PagedResult<StudentDto>> ListAsync(PageRequest page, Guid? branchId, CancellationToken ct)
    {
        var (items, total) = await core.ListAsync(Kind, page, branchId, s => x => x.StudentNumber.Contains(s), WithGuardians, ct);
        return new PagedResult<StudentDto>(items.Select(ToDto).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<StudentDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await core.FindAsync(Kind, id, Kind.ReadPermission, ct, WithGuardians));

    public async Task<StudentDto> CreateAsync(StudentRequest req, CancellationToken ct)
    {
        var student = new Student();
        var v = new Validator().OptionalText("studentNumber", req.StudentNumber, 30);
        await core.ApplyAsync(Kind, student, req, true, v, ct);
        student.StudentNumber = Clean(req.StudentNumber) ?? $"S{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        if (await core.Db.Students.AnyAsync(s => s.StudentNumber == student.StudentNumber, ct)) throw new ConflictException(ErrorCodes.DuplicateValue, new Dictionary<string, object?> { ["field"] = "studentNumber" });
        core.Db.Students.Add(student);
        if (req.Guardians is not null) await SetGuardiansCoreAsync(student, req.Guardians, ct);
        await core.Db.SaveChangesAsync(ct);
        return ToDto(student);
    }

    public async Task<StudentDto> UpdateAsync(Guid id, StudentRequest req, CancellationToken ct)
    {
        var student = await core.FindAsync(Kind, id, Kind.ManagePermission, ct, WithGuardians);
        var v = new Validator().RequiredText("studentNumber", req.StudentNumber, 30);
        await core.ApplyAsync(Kind, student, req, false, v, ct);
        var number = req.StudentNumber!.Trim();
        if (number != student.StudentNumber && await core.Db.Students.AnyAsync(s => s.StudentNumber == number && s.Id != id, ct))
            throw new ConflictException(ErrorCodes.DuplicateValue, new Dictionary<string, object?> { ["field"] = "studentNumber" });
        student.StudentNumber = number;
        if (req.Guardians is not null) await SetGuardiansCoreAsync(student, req.Guardians, ct);
        await core.Db.SaveChangesAsync(ct);
        return ToDto(student);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await core.FindAsync(Kind, id, Kind.ManagePermission, ct);
        if (await academic.IsStudentReferencedAsync(id, ct)) throw new ConflictException("people.in_use");
        // DL-085: invoices, payments or refunds keep the student (financial records must stay attributable).
        if (await finance.IsStudentReferencedAsync(id, ct)) throw new ConflictException(FinanceStandingErrors.RecordsExist);
        await core.DeleteAsync(Kind, id, ct);
    }

    public async Task<IReadOnlyList<StudentGuardianDetailDto>> GetGuardiansAsync(Guid id, CancellationToken ct)
    {
        var student = await core.FindAsync(Kind, id, Kind.ReadPermission, ct, WithGuardians);
        var guardianScope = await access.GetBranchScopeAsync(Permissions.GuardiansRead, ct);
        var ids = student.Guardians.Select(g => g.GuardianId).ToList();
        var guardians = await core.Db.Guardians.AsNoTracking().Where(g => ids.Contains(g.Id)).ToListAsync(ct);
        // Guardian details are only shown where the caller may read guardians; links remain visible by id.
        return student.Guardians.Select(link =>
        {
            var g = guardians.FirstOrDefault(x => x.Id == link.GuardianId);
            var visible = g is not null && guardianScope.Includes(g.BranchId);
            return new StudentGuardianDetailDto(link.GuardianId, Camel(link.Relationship), link.IsPrimary,
                visible ? g!.FirstName : string.Empty, visible ? g!.LastName : string.Empty, visible ? g!.Phone : null, g?.BranchId ?? Guid.Empty);
        }).ToList();
    }

    public async Task<IReadOnlyList<StudentGuardianDetailDto>> SetGuardiansAsync(Guid id, IReadOnlyList<StudentGuardianRequest>? req, CancellationToken ct)
    {
        var student = await core.FindAsync(Kind, id, Kind.ManagePermission, ct, WithGuardians);
        await SetGuardiansCoreAsync(student, req ?? [], ct);
        student.UpdatedAt = DateTime.UtcNow;
        await core.Db.SaveChangesAsync(ct);
        return await GetGuardiansAsync(id, ct);
    }

    private async Task SetGuardiansCoreAsync(Student student, IReadOnlyList<StudentGuardianRequest> links, CancellationToken ct)
    {
        var v = new Validator();
        if (links.Count(l => l.IsPrimary) > 1) v.Add("guardians", Validator.InvalidValue);
        if (links.Select(l => l.GuardianId).Distinct().Count() != links.Count) v.Add("guardians", Validator.InvalidValue);
        var parsed = links.Select(l => (l, rel: ParseEnum<GuardianRelationship>(l.Relationship, "guardians.relationship", v))).ToList();
        if (parsed.Any(p => p.rel is null) && v.IsValid) v.Add("guardians.relationship", Validator.Required);
        v.ThrowIfInvalid();

        foreach (var (l, _) in parsed)
        {
            // Guardian must exist in this tenant and be visible to the caller (guardians.read on its branch).
            var g = await guardianCore.Db.Guardians.AsNoTracking().FirstOrDefaultAsync(x => x.Id == l.GuardianId, ct);
            if (g is null || !(await access.GetBranchScopeAsync(Permissions.GuardiansRead, ct)).Includes(g.BranchId))
                v.Add("guardians", "people.guardian_not_found");
        }
        v.ThrowIfInvalid();

        student.Guardians.RemoveAll(existing => parsed.All(p => p.l.GuardianId != existing.GuardianId));
        foreach (var (l, rel) in parsed)
        {
            var existing = student.Guardians.FirstOrDefault(x => x.GuardianId == l.GuardianId);
            if (existing is null)
                student.Guardians.Add(new StudentGuardian { StudentId = student.Id, GuardianId = l.GuardianId, Relationship = rel!.Value, IsPrimary = l.IsPrimary });
            else
            {
                existing.Relationship = rel!.Value;
                existing.IsPrimary = l.IsPrimary;
            }
        }
    }

    public static StudentDto ToDto(Student s) => new(s.Id, s.BranchId, s.FirstName, s.LastName, s.NationalId, Date(s.BirthDate),
        s.Gender is { } g ? Camel(g) : null, s.Phone, s.Email, Camel(s.Status), s.UserId, s.StudentNumber,
        s.Guardians.Select(x => new StudentGuardianDto(x.GuardianId, Camel(x.Relationship), x.IsPrimary)).ToList(), Rv(s.RowVersion));
}

public sealed class GuardianService(PersonService<Guardian> core)
{
    private static readonly PersonKind<Guardian> Kind = PeopleKinds.Guardians;

    public async Task<PagedResult<GuardianDto>> ListAsync(PageRequest page, Guid? branchId, CancellationToken ct)
    {
        var (items, total) = await core.ListAsync(Kind, page, branchId, null, null, ct);
        return new PagedResult<GuardianDto>(items.Select(ToDto).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<GuardianDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await core.FindAsync(Kind, id, Kind.ReadPermission, ct));

    public async Task<GuardianDto> CreateAsync(GuardianRequest req, CancellationToken ct)
    {
        var g = new Guardian();
        await core.ApplyAsync(Kind, g, req, true, new Validator(), ct);
        core.Db.Guardians.Add(g);
        await core.Db.SaveChangesAsync(ct);
        return ToDto(g);
    }

    public async Task<GuardianDto> UpdateAsync(Guid id, GuardianRequest req, CancellationToken ct)
    {
        var g = await core.FindAsync(Kind, id, Kind.ManagePermission, ct);
        await core.ApplyAsync(Kind, g, req, false, new Validator(), ct);
        await core.Db.SaveChangesAsync(ct);
        return ToDto(g);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct) => core.DeleteAsync(Kind, id, ct);

    public static GuardianDto ToDto(Guardian s) => new(s.Id, s.BranchId, s.FirstName, s.LastName, s.NationalId, PersonService<Guardian>.Date(s.BirthDate),
        s.Gender is { } g ? PersonService<Guardian>.Camel(g) : null, s.Phone, s.Email, PersonService<Guardian>.Camel(s.Status), s.UserId, PersonService<Guardian>.Rv(s.RowVersion));
}

public sealed class TeacherService(PersonService<Teacher> core, IAcademicUsage academic)
{
    private static readonly PersonKind<Teacher> Kind = PeopleKinds.Teachers;

    public async Task<PagedResult<TeacherDto>> ListAsync(PageRequest page, Guid? branchId, CancellationToken ct)
    {
        var (items, total) = await core.ListAsync(Kind, page, branchId, s => x => x.EmployeeNumber.Contains(s), null, ct);
        return new PagedResult<TeacherDto>(items.Select(ToDto).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<TeacherDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await core.FindAsync(Kind, id, Kind.ReadPermission, ct));

    public async Task<TeacherDto> CreateAsync(TeacherRequest req, CancellationToken ct)
    {
        var t = new Teacher();
        await core.ApplyAsync(Kind, t, req, true, new Validator().RequiredText("employeeNumber", req.EmployeeNumber, 30).OptionalText("subjects", req.Subjects, 500), ct);
        await EnsureUniqueAsync(req.EmployeeNumber!.Trim(), null, ct);
        t.EmployeeNumber = req.EmployeeNumber!.Trim();
        t.Subjects = PersonService<Teacher>.Clean(req.Subjects);
        core.Db.Teachers.Add(t);
        await core.Db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    public async Task<TeacherDto> UpdateAsync(Guid id, TeacherRequest req, CancellationToken ct)
    {
        var t = await core.FindAsync(Kind, id, Kind.ManagePermission, ct);
        await core.ApplyAsync(Kind, t, req, false, new Validator().RequiredText("employeeNumber", req.EmployeeNumber, 30).OptionalText("subjects", req.Subjects, 500), ct);
        await EnsureUniqueAsync(req.EmployeeNumber!.Trim(), id, ct);
        t.EmployeeNumber = req.EmployeeNumber!.Trim();
        t.Subjects = PersonService<Teacher>.Clean(req.Subjects);
        await core.Db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await core.FindAsync(Kind, id, Kind.ManagePermission, ct);
        if (await academic.IsTeacherReferencedAsync(id, ct)) throw new ConflictException("people.in_use");
        await core.DeleteAsync(Kind, id, ct);
    }

    private async Task EnsureUniqueAsync(string number, Guid? id, CancellationToken ct)
    {
        if (await core.Db.Teachers.AnyAsync(x => x.EmployeeNumber == number && x.Id != id, ct))
            throw new ConflictException(ErrorCodes.DuplicateValue, new Dictionary<string, object?> { ["field"] = "employeeNumber" });
    }

    public static TeacherDto ToDto(Teacher s) => new(s.Id, s.BranchId, s.FirstName, s.LastName, s.NationalId, PersonService<Teacher>.Date(s.BirthDate),
        s.Gender is { } g ? PersonService<Teacher>.Camel(g) : null, s.Phone, s.Email, PersonService<Teacher>.Camel(s.Status), s.UserId,
        s.EmployeeNumber, s.Subjects, PersonService<Teacher>.Rv(s.RowVersion));
}

public sealed class StaffService(PersonService<StaffMember> core)
{
    private static readonly PersonKind<StaffMember> Kind = PeopleKinds.Staff;

    public async Task<PagedResult<StaffDto>> ListAsync(PageRequest page, Guid? branchId, CancellationToken ct)
    {
        var (items, total) = await core.ListAsync(Kind, page, branchId, s => x => x.EmployeeNumber.Contains(s), null, ct);
        return new PagedResult<StaffDto>(items.Select(ToDto).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<StaffDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await core.FindAsync(Kind, id, Kind.ReadPermission, ct));

    public async Task<StaffDto> CreateAsync(StaffRequest req, CancellationToken ct)
    {
        var t = new StaffMember();
        await core.ApplyAsync(Kind, t, req, true, Extra(req), ct);
        await EnsureUniqueAsync(req.EmployeeNumber!.Trim(), null, ct);
        t.EmployeeNumber = req.EmployeeNumber!.Trim();
        t.Position = req.Position!.Trim();
        core.Db.Staff.Add(t);
        await core.Db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    public async Task<StaffDto> UpdateAsync(Guid id, StaffRequest req, CancellationToken ct)
    {
        var t = await core.FindAsync(Kind, id, Kind.ManagePermission, ct);
        await core.ApplyAsync(Kind, t, req, false, Extra(req), ct);
        await EnsureUniqueAsync(req.EmployeeNumber!.Trim(), id, ct);
        t.EmployeeNumber = req.EmployeeNumber!.Trim();
        t.Position = req.Position!.Trim();
        await core.Db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct) => core.DeleteAsync(Kind, id, ct);

    private static Validator Extra(StaffRequest req) =>
        new Validator().RequiredText("employeeNumber", req.EmployeeNumber, 30).RequiredText("position", req.Position, 100);

    private async Task EnsureUniqueAsync(string number, Guid? id, CancellationToken ct)
    {
        if (await core.Db.Staff.AnyAsync(x => x.EmployeeNumber == number && x.Id != id, ct))
            throw new ConflictException(ErrorCodes.DuplicateValue, new Dictionary<string, object?> { ["field"] = "employeeNumber" });
    }

    public static StaffDto ToDto(StaffMember s) => new(s.Id, s.BranchId, s.FirstName, s.LastName, s.NationalId, PersonService<StaffMember>.Date(s.BirthDate),
        s.Gender is { } g ? PersonService<StaffMember>.Camel(g) : null, s.Phone, s.Email, PersonService<StaffMember>.Camel(s.Status), s.UserId,
        s.EmployeeNumber, s.Position, PersonService<StaffMember>.Rv(s.RowVersion));
}

/// <summary>
/// Self-service portal: a signed-in student/guardian/teacher/staff member sees only the record linked to their own account,
/// and a guardian sees only students linked to them. No branch permission is involved; the link itself is the authorization.
/// </summary>
public sealed class PortalService(IPeopleDbContext db, ITenantContext tenant)
{
    public async Task<PortalProfileDto> GetProfileAsync(CancellationToken ct)
    {
        var uid = tenant.UserId ?? throw new UnauthorizedException();
        var student = await db.Students.AsNoTracking().Include(s => s.Guardians).FirstOrDefaultAsync(s => s.UserId == uid, ct);
        if (student is not null) return new PortalProfileDto("student", StudentService.ToDto(student));
        var guardian = await db.Guardians.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == uid, ct);
        if (guardian is not null) return new PortalProfileDto("guardian", GuardianService.ToDto(guardian));
        var teacher = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == uid, ct);
        if (teacher is not null) return new PortalProfileDto("teacher", TeacherService.ToDto(teacher));
        var staff = await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == uid, ct);
        if (staff is not null) return new PortalProfileDto("staff", StaffService.ToDto(staff));
        throw new NotFoundException(ErrorCodes.PortalProfileNotFound);
    }

    public async Task<IReadOnlyList<PortalStudentDto>> GetMyStudentsAsync(CancellationToken ct)
    {
        var uid = tenant.UserId ?? throw new UnauthorizedException();
        var guardian = await db.Guardians.AsNoTracking().FirstOrDefaultAsync(g => g.UserId == uid, ct);
        if (guardian is null) return [];
        var rows = await (from link in db.StudentGuardians
                          join s in db.Students on link.StudentId equals s.Id
                          where link.GuardianId == guardian.Id
                          orderby s.LastName, s.FirstName
                          select new { s.Id, s.BranchId, s.FirstName, s.LastName, s.StudentNumber, link.Relationship, link.IsPrimary }).ToListAsync(ct);
        return rows.Select(r => new PortalStudentDto(r.Id, r.BranchId, r.FirstName, r.LastName, r.StudentNumber, Camel(r.Relationship), r.IsPrimary)).ToList();
    }
}

/// <summary>Person lookups for other modules (no permission checks; see <see cref="IPeopleDirectory"/>).</summary>
public sealed class PeopleDirectory(IPeopleDbContext db) : IPeopleDirectory
{
    public async Task<IReadOnlyDictionary<Guid, StudentSummary>> GetStudentsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new Dictionary<Guid, StudentSummary>();
        var list = ids.Distinct().ToList();
        return await db.Students.AsNoTracking().Where(s => list.Contains(s.Id))
            .Select(s => new StudentSummary(s.Id, s.BranchId, s.FirstName, s.LastName, s.StudentNumber, s.Status == PersonStatus.Active))
            .ToDictionaryAsync(s => s.Id, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, TeacherSummary>> GetTeachersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new Dictionary<Guid, TeacherSummary>();
        var list = ids.Distinct().ToList();
        return await db.Teachers.AsNoTracking().Where(s => list.Contains(s.Id))
            .Select(s => new TeacherSummary(s.Id, s.BranchId, s.FirstName, s.LastName, s.Status == PersonStatus.Active))
            .ToDictionaryAsync(s => s.Id, ct);
    }

    public async Task<Guid?> FindTeacherIdForUserAsync(Guid userId, CancellationToken ct = default) =>
        await db.Teachers.AsNoTracking().Where(t => t.UserId == userId && t.Status == PersonStatus.Active).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);

    public async Task<Guid?> FindStudentIdForUserAsync(Guid userId, CancellationToken ct = default) =>
        await db.Students.AsNoTracking().Where(t => t.UserId == userId).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetGuardianStudentIdsForUserAsync(Guid userId, CancellationToken ct = default) =>
        await (from g in db.Guardians
               join link in db.StudentGuardians on g.Id equals link.GuardianId
               where g.UserId == userId && g.Status == PersonStatus.Active
               select link.StudentId).Distinct().ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetSiblingIdsAsync(Guid studentId, CancellationToken ct = default)
    {
        var guardians = db.StudentGuardians.Where(l => l.StudentId == studentId).Select(l => l.GuardianId);
        return await db.StudentGuardians.AsNoTracking().Where(l => guardians.Contains(l.GuardianId) && l.StudentId != studentId)
            .Select(l => l.StudentId).Distinct().ToListAsync(ct);
    }
}
