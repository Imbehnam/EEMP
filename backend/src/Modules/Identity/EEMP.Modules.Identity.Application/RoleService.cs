using System.Text.RegularExpressions;
using EEMP.Modules.Identity.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Identity.Application;

public sealed partial class RoleService(IIdentityDbContext db, IAccessControl access)
{
    public static IReadOnlyList<PermissionDto> Catalog() =>
        Permissions.All.Select(p => new PermissionDto(p.Code, p.Module, p.IsSensitive, p.BranchScoped)).ToList();

    /// <summary>Roles are few per tenant, so the list is a plain array (contract clarification 2026-11-24).</summary>
    public async Task<IReadOnlyList<RoleDto>> ListAsync(string? search, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.RolesRead, ct);
        var q = db.Roles.AsNoTracking().Include(r => r.Permissions).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); q = q.Where(r => r.Name.Contains(s) || r.Code.Contains(s)); }
        var items = await q.OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<RoleDto> GetAsync(Guid id, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.RolesRead, ct);
        return ToDto(await FindAsync(id, ct));
    }

    public async Task<RoleDto> CreateAsync(CreateRoleRequest req, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.RolesManage, ct);
        var v = new Validator().RequiredText("name", req.Name, 200);
        if (string.IsNullOrWhiteSpace(req.Code)) v.Add("code", Validator.Required);
        else if (!CodeRegex().IsMatch(req.Code)) v.Add("code", Validator.InvalidFormat);
        ValidatePermissions(v, req.Permissions);
        v.ThrowIfInvalid();

        var code = req.Code!.Trim();
        if (await db.Roles.AnyAsync(r => r.Code == code, ct)) throw new ConflictException(ErrorCodes.RoleCodeTaken);
        var role = new Role { Code = code, Name = req.Name!.Trim() };
        role.SetPermissions(req.Permissions ?? []);
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return ToDto(role);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, UpdateRoleRequest req, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.RolesManage, ct);
        var v = new Validator().RequiredText("name", req.Name, 200).RowVersion("rowVersion", req.RowVersion);
        ValidatePermissions(v, req.Permissions);
        v.ThrowIfInvalid();

        var role = await FindAsync(id, ct);
        if (role.IsSystem) throw new ConflictException(ErrorCodes.RoleSystemImmutable);
        db.SetOriginalRowVersion(role, Convert.FromBase64String(req.RowVersion!));
        role.Name = req.Name!.Trim();
        role.SetPermissions(req.Permissions ?? []);
        role.UpdatedAt = DateTime.UtcNow; // forces a rowversion bump even when only the permission set changed
        await db.SaveChangesAsync(ct);
        return ToDto(role);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.RolesManage, ct);
        var role = await FindAsync(id, ct);
        if (role.IsSystem) throw new ConflictException(ErrorCodes.RoleSystemImmutable);
        if (await db.RoleAssignments.AnyAsync(a => a.RoleId == id, ct)) throw new ConflictException(ErrorCodes.RoleInUse);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Role> FindAsync(Guid id, CancellationToken ct) =>
        await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException();

    private static void ValidatePermissions(Validator v, IReadOnlyList<string>? permissions)
    {
        if (permissions is null) return;
        if (permissions.Any(p => !Permissions.Exists(p))) v.Add("permissions", Validator.UnknownPermission);
    }

    public static RoleDto ToDto(Role r) => new(r.Id, r.Code, r.Name, r.IsSystem,
        r.Permissions.Select(p => p.PermissionCode).OrderBy(p => p, StringComparer.Ordinal).ToList(), Convert.ToBase64String(r.RowVersion));

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.\\-]{1,49}$")]
    private static partial Regex CodeRegex();
}
