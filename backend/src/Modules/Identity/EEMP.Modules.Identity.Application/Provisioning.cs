using EEMP.Modules.Identity.Contracts;
using EEMP.Modules.Identity.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Identity.Application;

public sealed class TenantAccessProvisioner(IIdentityDbContext db, UserManager<AppUser> users) : ITenantAccessProvisioner
{
    /// <summary>
    /// Default system roles. TenantAdmin deliberately holds no people (SensitiveData) permissions:
    /// tenant administrators get no implicit access to student/guardian/teacher records (security baseline #1).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Name, string[] Permissions)> SystemRoleDefinitions =
        new Dictionary<string, (string, string[])>
        {
            [SystemRoles.TenantAdmin] = ("Tenant administrator", [
                Permissions.OrganizationRead, Permissions.OrganizationManage, Permissions.BranchesRead, Permissions.BranchesManage,
                Permissions.RolesRead, Permissions.RolesManage, Permissions.UsersRead, Permissions.UsersManage, Permissions.AuditRead,
                Permissions.CatalogRead, Permissions.CatalogManage, Permissions.ClassesRead, Permissions.ClassesManage,
                Permissions.WorkflowDefinitionsRead, Permissions.WorkflowDefinitionsManage]),
            [SystemRoles.BranchManager] = ("Branch manager", [
                Permissions.OrganizationRead, Permissions.BranchesRead,
                Permissions.StudentsRead, Permissions.StudentsManage, Permissions.GuardiansRead, Permissions.GuardiansManage,
                Permissions.TeachersRead, Permissions.TeachersManage, Permissions.StaffRead, Permissions.StaffManage,
                Permissions.CatalogRead, Permissions.ClassesRead, Permissions.ClassesManage,
                Permissions.EnrollmentsRead, Permissions.EnrollmentsManage, Permissions.AttendanceRead, Permissions.AttendanceManage,
                Permissions.GradesRead, Permissions.GradesManage, Permissions.GradesApprove, Permissions.GradesPublish,
                Permissions.ReportCardsRead, Permissions.ReportCardsManage, Permissions.ReportCardsPublish,
                Permissions.WorkflowDefinitionsRead, Permissions.WorkflowInstancesRead, Permissions.WorkflowInstancesManage,
                Permissions.WorkflowTasksApprove]),
            [SystemRoles.Teacher] = ("Teacher", [Permissions.BranchesRead]),
            [SystemRoles.Guardian] = ("Guardian", []),
            [SystemRoles.Student] = ("Student", []),
            // DL-064: finance is a separate duty; TenantAdmin and BranchManager hold no finance.* code.
            [SystemRoles.FinanceOfficer] = ("Finance officer", [
                Permissions.OrganizationRead, Permissions.BranchesRead, Permissions.CatalogRead, Permissions.ClassesRead,
                Permissions.StudentsRead, Permissions.GuardiansRead, Permissions.EnrollmentsRead,
                Permissions.FinanceSetup, Permissions.FinanceRead, Permissions.FinanceInvoicesManage, Permissions.FinancePaymentsManage,
                Permissions.FinanceApprove, Permissions.FinanceReports]),
        };

    public async Task<Guid> ProvisionTenantAsync(Guid tenantId, NewUserSpec admin, CancellationToken ct = default)
    {
        Role? adminRole = null;
        foreach (var (code, def) in SystemRoleDefinitions)
        {
            if (await db.Roles.AnyAsync(r => r.Code == code, ct)) continue;
            var role = new Role { Code = code, Name = def.Name, IsSystem = true };
            role.SetPermissions(def.Permissions);
            db.Roles.Add(role);
            if (code == SystemRoles.TenantAdmin) adminRole = role;
        }
        await db.SaveChangesAsync(ct);
        adminRole ??= await db.Roles.FirstAsync(r => r.Code == SystemRoles.TenantAdmin, ct);

        var user = await UserService.CreateUserAsync(tenantId, admin, users, ct);
        db.RoleAssignments.Add(new RoleAssignment { UserId = user.Id, RoleId = adminRole.Id, BranchId = null });
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    public async Task<IReadOnlyList<string>> ValidatePasswordAsync(string password, CancellationToken ct = default)
    {
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, new AppUser(), password);
            if (!result.Succeeded) return [Validator.PasswordTooWeak];
        }
        return [];
    }
}

/// <summary>
/// DL-064: idempotent per-tenant sync of the built-in roles. Creates missing system roles and adds catalog codes that a system
/// role's definition gained since the tenant was provisioned. Never removes codes and never touches custom roles.
/// Runs inside the tenant's scope.
/// </summary>
public sealed class SystemRoleSync(IIdentityDbContext db)
{
    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        var changes = 0;
        var existing = await db.Roles.Include(r => r.Permissions).Where(r => r.IsSystem).ToListAsync(ct);
        foreach (var (code, def) in TenantAccessProvisioner.SystemRoleDefinitions)
        {
            var role = existing.FirstOrDefault(r => r.Code == code);
            if (role is null)
            {
                if (await db.Roles.AnyAsync(r => r.Code == code, ct)) continue; // a custom role already uses the code
                role = new Role { Code = code, Name = def.Name, IsSystem = true };
                role.SetPermissions(def.Permissions);
                db.Roles.Add(role);
                changes++;
                continue;
            }
            var have = role.Permissions.Select(p => p.PermissionCode).ToHashSet(StringComparer.Ordinal);
            var missing = def.Permissions.Where(p => !have.Contains(p)).ToList();
            if (missing.Count == 0) continue;
            role.SetPermissions(have.Concat(missing));
            changes++;
        }
        if (changes > 0) await db.SaveChangesAsync(ct);
        return changes;
    }
}

public sealed class UserDirectory(IIdentityDbContext db) : IUserDirectory
{
    public Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default) => db.Users.AnyAsync(u => u.Id == userId, ct);
}

public sealed class AssignmentUsage(IIdentityDbContext db) : IAssignmentUsage
{
    public Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default) =>
        db.RoleAssignments.AnyAsync(a => a.BranchId == branchId, ct);
}
