using System.Linq.Expressions;
using System.Text.Json;
using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.Modules.People.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.People.Application;

public interface IPeopleDbContext
{
    DbSet<Student> Students { get; }
    DbSet<Guardian> Guardians { get; }
    DbSet<Teacher> Teachers { get; }
    DbSet<StaffMember> Staff { get; }
    DbSet<StudentGuardian> StudentGuardians { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class;
}

/// <summary>Describes one person kind: its table and the permissions protecting it.</summary>
public sealed record PersonKind<T>(string ReadPermission, string ManagePermission, Func<IPeopleDbContext, DbSet<T>> Set) where T : PersonProfile;

/// <summary>
/// Shared branch-scoped CRUD for person profiles. Every read is limited to the branches where the caller holds the
/// read permission; records outside that scope (or in another tenant) are reported as not found.
/// </summary>
public sealed class PersonService<T>(IPeopleDbContext db, IAccessControl access, IBranchDirectory branches, IUserDirectory users)
    where T : PersonProfile
{
    public IPeopleDbContext Db => db;

    public async Task<(List<T> Items, int Total)> ListAsync(PersonKind<T> kind, PageRequest page, Guid? branchId,
        Func<string, Expression<Func<T, bool>>>? extraSearch, Func<IQueryable<T>, IQueryable<T>>? include, CancellationToken ct)
    {
        var scope = await access.GetBranchScopeAsync(kind.ReadPermission, ct);
        IQueryable<T> q = kind.Set(db).AsNoTracking();
        if (!scope.AllBranches)
        {
            var ids = scope.BranchIds.ToList();
            q = q.Where(p => ids.Contains(p.BranchId));
        }
        if (branchId is { } b) q = q.Where(p => p.BranchId == b);
        if (page.Search is { } s)
        {
            Expression<Func<T, bool>> basePredicate = p => p.FirstName.Contains(s) || p.LastName.Contains(s) || (p.NationalId != null && p.NationalId.Contains(s));
            q = q.Where(extraSearch is null ? basePredicate : Or(basePredicate, extraSearch(s)));
        }
        var total = await q.CountAsync(ct);
        if (include is not null) q = include(q);
        var items = await q.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<T> FindAsync(PersonKind<T> kind, Guid id, string permission, CancellationToken ct, Func<IQueryable<T>, IQueryable<T>>? include = null)
    {
        IQueryable<T> q = kind.Set(db);
        if (include is not null) q = include(q);
        var entity = await q.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException();
        var scope = await access.GetBranchScopeAsync(permission, ct);
        if (!scope.Includes(entity.BranchId))
        {
            // Same answer as a missing record (no existence leak to sibling branches), unless the caller may read it.
            if (permission == kind.ManagePermission && (await access.GetBranchScopeAsync(kind.ReadPermission, ct)).Includes(entity.BranchId))
                throw new ForbiddenException(ErrorCodes.ForbiddenScope);
            throw new NotFoundException();
        }
        return entity;
    }

    /// <summary>Validates common fields, branch scope (manage permission on the target branch) and the optional user link.</summary>
    public async Task ApplyAsync(PersonKind<T> kind, T entity, IPersonRequest req, bool isNew, Validator v, CancellationToken ct)
    {
        v.RequiredText("firstName", req.FirstName, 100)
         .RequiredText("lastName", req.LastName, 100)
         .OptionalText("nationalId", req.NationalId, 20)
         .Phone("phone", req.Phone)
         .Email("email", req.Email)
         .When(req.BranchId is null || req.BranchId == Guid.Empty, "branchId", Validator.Required);
        if (!isNew) v.RowVersion("rowVersion", req.RowVersion);
        var birth = Validator.ParseDate(req.BirthDate, "birthDate", v);
        var gender = ParseEnum<Gender>(req.Gender, "gender", v);
        var status = ParseEnum<PersonStatus>(req.Status, "status", v) ?? (isNew ? PersonStatus.Active : entity.Status);
        v.ThrowIfInvalid();

        var branchId = req.BranchId!.Value;
        await access.EnsureBranchAsync(kind.ManagePermission, branchId, ct);
        if (!await branches.ExistsAsync(branchId, ct)) v.Add("branchId", "people.branch_not_found");
        if (req.UserId is { } uid && uid != entity.UserId)
        {
            if (!await users.ExistsAsync(uid, ct)) v.Add("userId", "people.user_not_found");
            else if (await kind.Set(db).AnyAsync(p => p.UserId == uid && p.Id != entity.Id, ct)) throw new ConflictException("people.user_already_linked");
        }
        v.ThrowIfInvalid();

        if (!isNew) db.SetOriginalRowVersion(entity, Convert.FromBase64String(req.RowVersion!));
        entity.BranchId = branchId;
        entity.FirstName = req.FirstName!.Trim();
        entity.LastName = req.LastName!.Trim();
        entity.NationalId = Clean(req.NationalId);
        entity.BirthDate = birth;
        entity.Gender = gender;
        entity.Phone = Clean(req.Phone);
        entity.Email = Clean(req.Email);
        entity.Status = status;
        entity.UserId = req.UserId;
    }

    public async Task DeleteAsync(PersonKind<T> kind, Guid id, CancellationToken ct)
    {
        var entity = await FindAsync(kind, id, kind.ManagePermission, ct);
        kind.Set(db).Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public static TEnum? ParseEnum<TEnum>(string? value, string field, Validator v) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var e) && Enum.IsDefined(e)) return e;
        v.Add(field, Validator.InvalidValue);
        return null;
    }

    private static Expression<Func<T, bool>> Or(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
    {
        var body = new ReplaceParameter(b.Parameters[0], a.Parameters[0]).Visit(b.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(a.Body, body), a.Parameters[0]);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string Camel<TEnum>(TEnum value) where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    public static string? Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static string Rv(byte[] rv) => Convert.ToBase64String(rv);
}

public sealed class PeopleBranchUsage(IPeopleDbContext db) : IPeopleBranchUsage
{
    public async Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default) =>
        await db.Students.AnyAsync(p => p.BranchId == branchId, ct) || await db.Guardians.AnyAsync(p => p.BranchId == branchId, ct) ||
        await db.Teachers.AnyAsync(p => p.BranchId == branchId, ct) || await db.Staff.AnyAsync(p => p.BranchId == branchId, ct);
}

public static class PeopleKinds
{
    public static readonly PersonKind<Student> Students = new(Permissions.StudentsRead, Permissions.StudentsManage, d => d.Students);
    public static readonly PersonKind<Guardian> Guardians = new(Permissions.GuardiansRead, Permissions.GuardiansManage, d => d.Guardians);
    public static readonly PersonKind<Teacher> Teachers = new(Permissions.TeachersRead, Permissions.TeachersManage, d => d.Teachers);
    public static readonly PersonKind<StaffMember> Staff = new(Permissions.StaffRead, Permissions.StaffManage, d => d.Staff);
}
