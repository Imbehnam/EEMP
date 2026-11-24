using EEMP.Modules.Identity.Domain;
using EEMP.Modules.Organization.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Tenancy;
using EEMP.SharedKernel.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Identity.Application;

/// <summary>Tenant user accounts and their scoped role assignments. All operations require tenant-wide users.* permissions.</summary>
public sealed class UserService(
    IIdentityDbContext db,
    UserManager<AppUser> users,
    IAccessControl access,
    ITenantContext tenant,
    IBranchDirectory branches,
    TimeProvider clock)
{
    public async Task<PagedResult<UserDto>> ListAsync(PageRequest page, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.UsersRead, ct);
        var q = db.Users.AsNoTracking();
        if (page.Search is { } s) q = q.Where(u => u.UserName!.Contains(s) || u.DisplayName.Contains(s) || (u.Email != null && u.Email.Contains(s)));
        var total = await q.CountAsync(ct);
        var list = await q.OrderBy(u => u.UserName).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        var assignments = await LoadAssignmentsAsync(list.Select(u => u.Id).ToList(), ct);
        return new PagedResult<UserDto>(list.Select(u => ToDto(u, assignments)).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.UsersRead, ct);
        var user = await FindAsync(id, ct);
        return ToDto(user, await LoadAssignmentsAsync([id], ct));
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest req, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.UsersManage, ct);
        var v = new Validator()
            .RequiredText("userName", req.UserName, 100)
            .RequiredText("displayName", req.DisplayName, 200)
            .Email("email", req.Email)
            .Phone("phone", req.Phone)
            .When(string.IsNullOrEmpty(req.Password), "password", Validator.Required);
        if (req.Locale is not null) v.Locale("locale", req.Locale);
        if (req.TimeZone is not null) v.TimeZone("timeZone", req.TimeZone);
        v.ThrowIfInvalid();

        var user = await CreateUserAsync(tenant.RequireTenantId(), new Contracts.NewUserSpec(
            req.UserName!.Trim(), req.DisplayName!.Trim(), req.Email, req.Phone, req.Password!,
            req.Locale ?? SupportedLocales.Default, req.TimeZone ?? "Asia/Tehran"), users, ct);
        return ToDto(user, []);
    }

    /// <summary>Shared creation path (API and provisioning). Maps Identity errors to stable codes.</summary>
    public static async Task<AppUser> CreateUserAsync(Guid? tenantId, Contracts.NewUserSpec spec, UserManager<AppUser> users, CancellationToken ct, bool isPlatformAdmin = false)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserName = spec.UserName,
            DisplayName = spec.DisplayName,
            Email = string.IsNullOrWhiteSpace(spec.Email) ? null : spec.Email.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(spec.Phone) ? null : spec.Phone.Trim(),
            Locale = spec.Locale,
            TimeZone = spec.TimeZone,
            IsPlatformAdmin = isPlatformAdmin,
            LockoutEnabled = true,
        };
        var result = await users.CreateAsync(user, spec.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code is "DuplicateUserName")) throw new ConflictException(ErrorCodes.UserNameTaken);
            var v = new Validator();
            foreach (var e in result.Errors)
            {
                if (e.Code.StartsWith("Password", StringComparison.Ordinal)) v.Add("password", Validator.PasswordTooWeak);
                else if (e.Code.Contains("UserName", StringComparison.Ordinal)) v.Add("userName", Validator.InvalidFormat);
                else if (e.Code.Contains("Email", StringComparison.Ordinal)) v.Add("email", Validator.InvalidFormat);
                else v.Add("body", Validator.InvalidValue);
            }
            v.ThrowIfInvalid();
        }
        return user;
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest req, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.UsersManage, ct);
        new Validator()
            .RequiredText("displayName", req.DisplayName, 200)
            .Email("email", req.Email)
            .Phone("phone", req.Phone)
            .Locale("locale", req.Locale)
            .TimeZone("timeZone", req.TimeZone)
            .RowVersion("rowVersion", req.RowVersion)
            .ThrowIfInvalid();

        var user = await FindAsync(id, ct);
        db.SetOriginalRowVersion(user, Convert.FromBase64String(req.RowVersion!));
        user.DisplayName = req.DisplayName!.Trim();
        user.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        user.NormalizedEmail = user.Email is null ? null : users.NormalizeEmail(user.Email);
        user.PhoneNumber = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();
        user.Locale = req.Locale!;
        user.TimeZone = req.TimeZone!;
        await db.SaveChangesAsync(ct);
        return ToDto(user, await LoadAssignmentsAsync([id], ct));
    }

    public async Task<UserDto> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.UsersManage, ct);
        if (!active && id == tenant.UserId) throw new BusinessRuleException(ErrorCodes.CannotDeactivateSelf);
        var user = await FindAsync(id, ct);
        user.IsActive = active;
        if (!active)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var t in await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct)) t.RevokedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return ToDto(user, await LoadAssignmentsAsync([id], ct));
    }

    public async Task<UserDto> SetAssignmentsAsync(Guid id, IReadOnlyList<AssignmentRequest>? req, CancellationToken ct)
    {
        await access.EnsureTenantWideAsync(Permissions.UsersManage, ct);
        // No self-grant: a user can never change their own access (privilege-escalation control).
        if (id == tenant.UserId) throw new ForbiddenException(ErrorCodes.SelfGrantForbidden);
        var user = await FindAsync(id, ct);
        var wanted = (req ?? []).Distinct().ToList();

        var v = new Validator();
        var roleIds = wanted.Select(a => a.RoleId).Distinct().ToList();
        var existingRoles = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        if (existingRoles.Count != roleIds.Count) v.Add("assignments", "identity.role_not_found");
        foreach (var branchId in wanted.Where(a => a.BranchId is not null).Select(a => a.BranchId!.Value).Distinct())
            if (!await branches.ExistsAsync(branchId, ct)) v.Add("assignments", "identity.branch_not_found");
        v.ThrowIfInvalid();

        var current = await db.RoleAssignments.Where(a => a.UserId == id).ToListAsync(ct);
        foreach (var a in current.Where(c => !wanted.Any(w => w.RoleId == c.RoleId && w.BranchId == c.BranchId)))
            db.RoleAssignments.Remove(a);
        foreach (var w in wanted.Where(w => !current.Any(c => c.RoleId == w.RoleId && c.BranchId == w.BranchId)))
            db.RoleAssignments.Add(new RoleAssignment { UserId = id, RoleId = w.RoleId, BranchId = w.BranchId, GrantedBy = tenant.UserId });
        await db.SaveChangesAsync(ct);
        return ToDto(user, await LoadAssignmentsAsync([id], ct));
    }

    private async Task<AppUser> FindAsync(Guid id, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException();

    private async Task<Dictionary<Guid, List<AssignmentDto>>> LoadAssignmentsAsync(List<Guid> userIds, CancellationToken ct)
    {
        var rows = await db.RoleAssignments.AsNoTracking().Where(a => userIds.Contains(a.UserId))
            .Select(a => new { a.UserId, a.RoleId, a.Role!.Code, a.BranchId }).ToListAsync(ct);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(r => new AssignmentDto(r.RoleId, r.Code, r.BranchId)).ToList());
    }

    private static UserDto ToDto(AppUser u, Dictionary<Guid, List<AssignmentDto>> assignments) => new(
        u.Id, u.UserName!, u.DisplayName, u.Email, u.PhoneNumber, u.Locale, u.TimeZone, u.IsActive,
        assignments.TryGetValue(u.Id, out var a) ? a : [], u.CreatedAt, Convert.ToBase64String(u.RowVersion));
}
