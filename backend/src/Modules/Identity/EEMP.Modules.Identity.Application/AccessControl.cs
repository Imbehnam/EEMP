using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Identity.Application;

/// <summary>
/// Central permission evaluation. Grants are loaded once per request from the database (request-scoped only),
/// so role/assignment changes and revocations apply on the next request. Platform administrators hold no tenant permissions.
/// </summary>
public sealed class AccessControl(IIdentityDbContext db, ITenantContext tenant) : IAccessControl
{
    private List<(string Permission, Guid? BranchId)>? _grants;

    private async Task<List<(string Permission, Guid? BranchId)>> GrantsAsync(CancellationToken ct)
    {
        if (_grants is not null) return _grants;
        if (tenant.TenantId is null || tenant.UserId is not { } userId || tenant.IsPlatformAdmin) return _grants = [];

        var rows = await (from a in db.RoleAssignments
                          join p in db.RolePermissions on a.RoleId equals p.RoleId
                          where a.UserId == userId
                          select new { p.PermissionCode, a.BranchId }).ToListAsync(ct);
        return _grants = rows.Select(r => (r.PermissionCode, r.BranchId)).ToList();
    }

    public async Task<bool> HasAnyAsync(string permission, CancellationToken ct = default) =>
        (await GrantsAsync(ct)).Any(g => g.Permission == permission);

    public async Task<bool> HasTenantWideAsync(string permission, CancellationToken ct = default) =>
        (await GrantsAsync(ct)).Any(g => g.Permission == permission && g.BranchId is null);

    public async Task<BranchScope> GetBranchScopeAsync(string permission, CancellationToken ct = default)
    {
        var grants = (await GrantsAsync(ct)).Where(g => g.Permission == permission).ToList();
        if (grants.Any(g => g.BranchId is null)) return BranchScope.Tenant;
        return new BranchScope(false, grants.Select(g => g.BranchId!.Value).ToHashSet());
    }

    public async Task EnsureBranchAsync(string permission, Guid branchId, CancellationToken ct = default)
    {
        if (!(await GetBranchScopeAsync(permission, ct)).Includes(branchId)) throw new ForbiddenException(ErrorCodes.ForbiddenScope);
    }

    public async Task EnsureTenantWideAsync(string permission, CancellationToken ct = default)
    {
        if (!await HasTenantWideAsync(permission, ct)) throw new ForbiddenException(ErrorCodes.ForbiddenScope);
    }
}
