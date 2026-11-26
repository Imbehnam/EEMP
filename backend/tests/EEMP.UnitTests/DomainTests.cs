using EEMP.Infrastructure.Tenancy;
using EEMP.Modules.Identity.Application;
using EEMP.Modules.Identity.Domain;
using EEMP.Modules.Tenancy.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;

namespace EEMP.UnitTests;

public class DomainTests
{
    [Fact]
    public void Role_SetPermissions_replaces_set_without_duplicates()
    {
        var role = new Role { TenantId = Guid.NewGuid() };
        role.SetPermissions([Permissions.BranchesRead, Permissions.BranchesRead, Permissions.UsersRead]);
        Assert.Equal(2, role.Permissions.Count);
        role.SetPermissions([Permissions.UsersRead, Permissions.RolesRead]);
        Assert.Equal([Permissions.RolesRead, Permissions.UsersRead], role.Permissions.Select(p => p.PermissionCode).Order().ToArray());
        Assert.All(role.Permissions, p => Assert.Equal(role.Id, p.RoleId));
    }

    [Fact]
    public void Tenant_suspend_and_activate()
    {
        var t = new Tenant();
        t.Suspend();
        Assert.Equal(TenantStatus.Suspended, t.Status);
        t.Activate();
        Assert.Equal(TenantStatus.Active, t.Status);
    }

    [Fact]
    public void TenantAdmin_system_role_has_no_sensitive_people_permissions()
    {
        var admin = TenantAccessProvisioner.SystemRoleDefinitions[SystemRoles.TenantAdmin].Permissions;
        var sensitive = Permissions.All.Where(p => p.IsSensitive).Select(p => p.Code).ToHashSet();
        Assert.DoesNotContain(admin, sensitive.Contains);
    }

    [Fact]
    public void All_system_role_permissions_exist_in_catalog()
    {
        foreach (var (_, def) in TenantAccessProvisioner.SystemRoleDefinitions)
            Assert.All(def.Permissions, p => Assert.True(Permissions.Exists(p), p));
    }

    [Fact]
    public void Permission_codes_follow_module_action_convention()
    {
        // M2 (DL-055): approve/publish verbs separate grade approval and publication from entry (segregation of duties).
        // M3 (DL-064): finance codes may carry one sub-resource segment (finance.invoices.manage, finance.reports.read).
        Assert.All(Permissions.All, p => Assert.Matches("^[a-z]+(\\.[a-z]+)?\\.(read|manage|approve|publish)$", p.Code));
        Assert.All(Permissions.All.Where(p => p.Code.Count(c => c == '.') == 2), p => Assert.Equal("finance", p.Module));
        Assert.Equal(Permissions.All.Count, Permissions.All.Select(p => p.Code).Distinct().Count());
    }

    [Fact]
    public void BranchScope_includes_only_granted_branches()
    {
        var a = Guid.NewGuid();
        var scope = new BranchScope(false, new HashSet<Guid> { a });
        Assert.True(scope.Includes(a));
        Assert.False(scope.Includes(Guid.NewGuid()));
        Assert.True(BranchScope.Tenant.Includes(Guid.NewGuid()));
        Assert.True(BranchScope.None.IsEmpty);
    }

    [Fact]
    public void TenantContext_fails_closed_and_initializes_once()
    {
        var ctx = new TenantContext();
        Assert.Throws<ForbiddenException>(() => ctx.RequireTenantId());
        var tenant = Guid.NewGuid();
        ctx.Initialize(tenant, Guid.NewGuid(), false);
        Assert.Equal(tenant, ctx.RequireTenantId());
        Assert.Throws<InvalidOperationException>(() => ctx.Initialize(tenant, Guid.NewGuid(), false));
        using (ctx.BeginTenantScope(null)) Assert.Null(ctx.TenantId);
        Assert.Equal(tenant, ctx.TenantId);
    }

    [Fact]
    public void Platform_admin_cannot_carry_a_tenant()
    {
        Assert.Throws<InvalidOperationException>(() => new TenantContext().Initialize(Guid.NewGuid(), Guid.NewGuid(), true));
    }

    [Fact]
    public void Refresh_token_hash_is_deterministic_and_not_the_token()
    {
        var token = TokenHasher.NewToken();
        Assert.Equal(TokenHasher.Hash(token), TokenHasher.Hash(token));
        Assert.NotEqual(token, TokenHasher.Hash(token));
        Assert.NotEqual(TokenHasher.NewToken(), token);
    }
}
