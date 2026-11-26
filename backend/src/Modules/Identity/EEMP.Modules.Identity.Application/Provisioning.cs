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

public sealed class UserDirectory(IIdentityDbContext db) : IUserDirectory
{
    public Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default) => db.Users.AnyAsync(u => u.Id == userId, ct);
}

public sealed class AssignmentUsage(IIdentityDbContext db) : IAssignmentUsage
{
    public Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default) =>
        db.RoleAssignments.AnyAsync(a => a.BranchId == branchId, ct);
}
