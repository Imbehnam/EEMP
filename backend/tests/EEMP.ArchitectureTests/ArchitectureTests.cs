using System.Reflection;
using EEMP.Infrastructure.Audit;
using EEMP.Infrastructure.Persistence;
using EEMP.Modules.Academic.Infrastructure;
using EEMP.Modules.Identity.Infrastructure;
using EEMP.Modules.Organization.Infrastructure;
using EEMP.Modules.People.Infrastructure;
using EEMP.Modules.Tenancy.Infrastructure;
using EEMP.Modules.Workflow.Infrastructure;
using EEMP.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace EEMP.ArchitectureTests;

public class ModuleBoundaryTests
{
    private static readonly string[] Modules = ["Tenancy", "Identity", "Organization", "People", "Academic", "Finance", "Workflow"];
    private static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Contracts"];

    private static Assembly Load(string module, string layer) => Assembly.Load($"EEMP.Modules.{module}.{layer}");

    private static HashSet<string> Refs(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name!).ToHashSet();

    public static TheoryData<string, string> ModuleLayers()
    {
        var data = new TheoryData<string, string>();
        foreach (var m in Modules) foreach (var l in Layers) data.Add(m, l);
        return data;
    }

    [Theory]
    [MemberData(nameof(ModuleLayers))]
    public void Modules_only_reference_other_modules_through_contracts(string module, string layer)
    {
        var refs = Refs(Load(module, layer));
        foreach (var other in Modules.Where(m => m != module))
            foreach (var forbidden in new[] { "Domain", "Application", "Infrastructure" })
                Assert.DoesNotContain($"EEMP.Modules.{other}.{forbidden}", refs);
    }

    [Theory]
    [MemberData(nameof(ModuleLayers))]
    public void Inner_layers_do_not_depend_on_infrastructure_or_web(string module, string layer)
    {
        if (layer == "Infrastructure") return;
        var refs = Refs(Load(module, layer));
        Assert.DoesNotContain("EEMP.Infrastructure", refs);
        Assert.DoesNotContain("EEMP.Api", refs);
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.AspNetCore.Http", StringComparison.Ordinal));
        if (layer is "Domain" or "Contracts")
            Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedKernel_has_no_framework_or_module_dependencies()
    {
        var refs = Refs(typeof(ITenantOwned).Assembly);
        Assert.DoesNotContain(refs, r => r.StartsWith("EEMP.", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}

public class AcademicArchitectureTests
{
    [Fact]
    public void Contracts_projects_do_not_reference_other_modules()
    {
        foreach (var m in new[] { "Tenancy", "Identity", "Organization", "People", "Academic", "Finance", "Workflow" })
        {
            var refs = Assembly.Load($"EEMP.Modules.{m}.Contracts").GetReferencedAssemblies().Select(r => r.Name!).ToList();
            Assert.DoesNotContain(refs, r => r.StartsWith("EEMP.Modules.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Academic_permissions_on_student_data_are_sensitive_and_branch_scoped()
    {
        var sensitivePrefixes = new[] { "enrollments.", "attendance.", "grades.", "reportcards." };
        var academic = EEMP.SharedKernel.Authorization.Permissions.All.Where(p => p.Module == "academic").ToList();
        Assert.Equal(15, academic.Count);
        Assert.All(academic.Where(p => sensitivePrefixes.Any(x => p.Code.StartsWith(x, StringComparison.Ordinal))),
            p => Assert.True(p.IsSensitive && p.BranchScoped, p.Code));
        Assert.All(academic.Where(p => p.Code.StartsWith("catalog.", StringComparison.Ordinal)), p => Assert.False(p.BranchScoped, p.Code));
    }

    [Fact]
    public void Academic_tables_use_tenant_composite_foreign_keys()
    {
        using var ctx = new AcademicDbContext(DesignTime.Options<AcademicDbContext>(AcademicDbContext.SchemaName), DesignTime.NoTenant());
        foreach (var fk in ctx.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            Assert.True(fk.Properties[0].Name == "TenantId", $"{fk.DeclaringEntityType.GetTableName()} FK {string.Join(",", fk.Properties.Select(p => p.Name))} is not tenant-composite");
    }
}

public class WorkflowArchitectureTests
{
    [Fact]
    public void Workflow_permissions_on_instances_are_sensitive_and_branch_scoped()
    {
        var workflow = EEMP.SharedKernel.Authorization.Permissions.All.Where(p => p.Module == "workflow").ToList();
        Assert.Equal(5, workflow.Count);
        Assert.All(workflow.Where(p => p.Code.StartsWith("workflow.instances.", StringComparison.Ordinal) || p.Code.StartsWith("workflow.tasks.", StringComparison.Ordinal)),
            p => Assert.True(p.IsSensitive && p.BranchScoped, p.Code));
        Assert.All(workflow.Where(p => p.Code.StartsWith("workflow.definitions.", StringComparison.Ordinal)), p => Assert.False(p.IsSensitive || p.BranchScoped, p.Code));
    }

    [Fact]
    public void Tenant_admin_gets_no_workflow_instance_access()
    {
        var admin = EEMP.Modules.Identity.Application.TenantAccessProvisioner.SystemRoleDefinitions[EEMP.Modules.Identity.Domain.SystemRoles.TenantAdmin].Permissions;
        Assert.DoesNotContain(admin, p => p.StartsWith("workflow.instances.", StringComparison.Ordinal) || p.StartsWith("workflow.tasks.", StringComparison.Ordinal));
    }

    [Fact]
    public void Workflow_tables_use_tenant_composite_foreign_keys()
    {
        using var ctx = new WorkflowDbContext(DesignTime.Options<WorkflowDbContext>(WorkflowDbContext.SchemaName), DesignTime.NoTenant());
        foreach (var fk in ctx.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            Assert.True(fk.Properties[0].Name == "TenantId", $"{fk.DeclaringEntityType.GetTableName()} FK is not tenant-composite");
    }

    [Fact]
    public void Workflow_application_references_other_modules_only_through_contracts()
    {
        var refs = System.Reflection.Assembly.Load("EEMP.Modules.Workflow.Application").GetReferencedAssemblies().Select(r => r.Name!).ToList();
        Assert.DoesNotContain(refs, r => r.StartsWith("EEMP.Modules.", StringComparison.Ordinal) && !r.EndsWith(".Contracts", StringComparison.Ordinal)
                                         && !r.StartsWith("EEMP.Modules.Workflow.", StringComparison.Ordinal));
    }
}

public class DataClassificationTests
{
    /// <summary>
    /// ASP.NET Core Identity's per-user tables. They hold no tenant column: every row belongs to exactly one user,
    /// is only reached through that (tenant-filtered) user, and is unused in M1 (no external logins/claims/tokens).
    /// </summary>
    private static readonly HashSet<string> UserOwnedIdentityTables = ["UserClaims", "UserLogins", "UserTokens"];

    public static IEnumerable<object[]> Contexts()
    {
        var t = DesignTime.NoTenant();
        yield return [new AuditDbContext(DesignTime.Options<AuditDbContext>(AuditDbContext.SchemaName))];
        yield return [new TenancyDbContext(DesignTime.Options<TenancyDbContext>(TenancyDbContext.SchemaName), t)];
        yield return [new IdentityModuleDbContext(DesignTime.Options<IdentityModuleDbContext>(IdentityModuleDbContext.SchemaName), t)];
        yield return [new OrganizationDbContext(DesignTime.Options<OrganizationDbContext>(OrganizationDbContext.SchemaName), t)];
        yield return [new PeopleDbContext(DesignTime.Options<PeopleDbContext>(PeopleDbContext.SchemaName), t)];
        yield return [new AcademicDbContext(DesignTime.Options<AcademicDbContext>(AcademicDbContext.SchemaName), t)];
        yield return [new EEMP.Modules.Finance.Infrastructure.FinanceDbContext(DesignTime.Options<EEMP.Modules.Finance.Infrastructure.FinanceDbContext>(EEMP.Modules.Finance.Infrastructure.FinanceDbContext.SchemaName), t)];
        yield return [new WorkflowDbContext(DesignTime.Options<WorkflowDbContext>(WorkflowDbContext.SchemaName), t)];
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_table_is_classified_tenant_owned_or_global(DbContext context)
    {
        using var _ = context;
        foreach (var entity in context.Model.GetEntityTypes())
        {
            var clr = entity.ClrType;
            var table = entity.GetTableName()!;
            var tenantOwned = typeof(ITenantOwned).IsAssignableFrom(clr) || typeof(IOptionalTenantOwned).IsAssignableFrom(clr);
            var global = typeof(IGlobalEntity).IsAssignableFrom(clr);
            Assert.True(tenantOwned || global || UserOwnedIdentityTables.Contains(table), $"{table} ({clr.Name}) is not classified");
        }
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Tenant_owned_tables_have_a_query_filter_and_tenant_leading_index(DbContext context)
    {
        using var _ = context;
        foreach (var entity in context.Model.GetEntityTypes().Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType) || typeof(IOptionalTenantOwned).IsAssignableFrom(e.ClrType)))
        {
            if (entity.ClrType.GetCustomAttribute<NotAuditedAttribute>() is not null && typeof(IGlobalEntity).IsAssignableFrom(entity.ClrType))
                continue; // refresh tokens: global security records looked up by hash, never listed
            Assert.True(entity.GetDeclaredQueryFilters().Count > 0, $"{entity.GetTableName()} has no tenant query filter");
            Assert.Contains(entity.GetIndexes(), i => i.Properties[0].Name == "TenantId");
        }
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Each_module_owns_one_schema(DbContext context)
    {
        using var _ = context;
        var schemas = context.Model.GetEntityTypes().Select(e => e.GetSchema()).Distinct().ToList();
        Assert.Single(schemas);
    }
}

public class FinanceArchitectureTests
{
    private static EEMP.Modules.Finance.Infrastructure.FinanceDbContext Context() =>
        new(DesignTime.Options<EEMP.Modules.Finance.Infrastructure.FinanceDbContext>(EEMP.Modules.Finance.Infrastructure.FinanceDbContext.SchemaName), DesignTime.NoTenant());

    [Fact]
    public void Finance_permissions_are_sensitive_and_branch_scoped_except_setup()
    {
        var finance = EEMP.SharedKernel.Authorization.Permissions.All.Where(p => p.Module == "finance").ToList();
        Assert.Equal(6, finance.Count);
        Assert.All(finance.Where(p => p.Code != EEMP.SharedKernel.Authorization.Permissions.FinanceSetup), p => Assert.True(p.IsSensitive && p.BranchScoped, p.Code));
        Assert.All(finance, p => Assert.StartsWith("finance.", p.Code, StringComparison.Ordinal));
    }

    [Fact]
    public void TenantAdmin_and_BranchManager_hold_no_finance_permission()
    {
        var defs = EEMP.Modules.Identity.Application.TenantAccessProvisioner.SystemRoleDefinitions;
        foreach (var role in new[] { EEMP.Modules.Identity.Domain.SystemRoles.TenantAdmin, EEMP.Modules.Identity.Domain.SystemRoles.BranchManager })
            Assert.DoesNotContain(defs[role].Permissions, c => c.StartsWith("finance.", StringComparison.Ordinal));
        Assert.Contains(EEMP.SharedKernel.Authorization.Permissions.FinanceApprove, defs[EEMP.Modules.Identity.Domain.SystemRoles.FinanceOfficer].Permissions);
    }

    [Fact]
    public void Finance_tables_use_tenant_composite_foreign_keys()
    {
        using var ctx = Context();
        foreach (var fk in ctx.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            Assert.True(fk.Properties[0].Name == "TenantId", $"{fk.DeclaringEntityType.GetTableName()} FK is not tenant-composite");
        foreach (var ix in ctx.Model.GetEntityTypes().SelectMany(e => e.GetIndexes()))
            Assert.True(ix.Properties[0].Name == "TenantId", $"{ix.DeclaringEntityType.GetTableName()} index {string.Join(",", ix.Properties.Select(p => p.Name))} is not tenant-leading");
    }

    [Fact]
    public void Money_is_decimal_never_floating_point_and_carries_a_currency()
    {
        using var ctx = Context();
        foreach (var e in ctx.Model.GetEntityTypes())
        {
            Assert.DoesNotContain(e.GetProperties(), p => p.ClrType == typeof(double) || p.ClrType == typeof(float) || p.ClrType == typeof(double?) || p.ClrType == typeof(float?));
            foreach (var p in e.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
                Assert.True(p.GetColumnType() is "decimal(19,4)" or "decimal(5,2)", $"{e.GetTableName()}.{p.Name} is {p.GetColumnType()}");
            if (e.GetProperties().Any(p => p.Name == "Amount") && e.FindProperty("Currency") is null)
                Assert.Contains(e.ClrType.Name, new[] { "InvoiceLine", "InvoiceDiscount", "Installment", "InvoiceAdjustment", "PaymentAllocation", "FinanceAuditEntry" });
        }
    }

    [Fact]
    public void Finance_audit_is_append_only()
    {
        using var ctx = Context();
        var entry = new EEMP.Modules.Finance.Domain.FinanceAuditEntry { TenantId = Guid.NewGuid(), Action = "x", EntityType = "y" };
        ctx.Attach(entry);
        ctx.Entry(entry).State = Microsoft.EntityFrameworkCore.EntityState.Deleted;
        Assert.Throws<InvalidOperationException>(() => ctx.SaveChanges());
    }
}
