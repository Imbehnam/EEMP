using EEMP.Infrastructure.Authorization;
using EEMP.Infrastructure.Persistence;
using EEMP.Infrastructure.Web;
using EEMP.Modules.Organization.Application;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.Organization.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EEMP.Modules.Organization.Infrastructure;

public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IOrganizationDbContext
{
    public const string SchemaName = "organization";
    protected override string Schema => SchemaName;

    public DbSet<CentralOffice> CentralOffices => Set<CentralOffice>();
    public DbSet<Branch> Branches => Set<Branch>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CentralOffice>(b =>
        {
            b.ToTable("CentralOffices");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId).IsUnique(); // one central office per tenant (OD-003)
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.LegalName).HasMaxLength(300);
            b.Property(x => x.Phone).HasMaxLength(30);
            b.Property(x => x.Email).HasMaxLength(256);
            b.Property(x => x.Address).HasMaxLength(500);
            b.Property(x => x.DefaultLocale).HasMaxLength(10).IsRequired();
            b.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        });
        modelBuilder.Entity<Branch>(b =>
        {
            b.ToTable("Branches");
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.Property(x => x.Code).HasMaxLength(20).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Address).HasMaxLength(500);
            b.Property(x => x.Phone).HasMaxLength(30);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
    }
}

public static class OrganizationModule
{
    public static IServiceCollection AddOrganizationModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<OrganizationDbContext>(OrganizationDbContext.SchemaName);
        services.AddScoped<IOrganizationDbContext>(sp => sp.GetRequiredService<OrganizationDbContext>());
        services.AddScoped<OrganizationService>();
        services.AddScoped<BranchService>();
        services.AddScoped<IOrganizationProvisioner, OrganizationProvisioner>();
        services.AddScoped<IBranchDirectory, BranchDirectory>();
        return services;
    }

    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder api)
    {
        var org = api.MapGroup("/organization").WithTags("Organization");
        org.MapGet("/", async (OrganizationService s, CancellationToken ct) => Results.Ok(await s.GetAsync(ct)))
            .RequirePermission(Permissions.OrganizationRead);
        org.MapPut("/", async (UpdateOrganizationRequest req, OrganizationService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(req, ct)))
            .RequirePermission(Permissions.OrganizationManage);

        var branches = api.MapGroup("/branches").WithTags("Branches");
        branches.MapGet("/", async ([AsParameters] PagingQuery q, BranchService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q.ToPageRequest(), ct)))
            .RequirePermission(Permissions.BranchesRead);
        branches.MapPost("/", async (CreateBranchRequest req, BranchService s, CancellationToken ct) =>
            {
                var b = await s.CreateAsync(req, ct);
                return Results.Created($"/api/v1/branches/{b.Id}", b);
            }).RequirePermission(Permissions.BranchesManage);
        branches.MapGet("/{id:guid}", async (Guid id, BranchService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Permissions.BranchesRead);
        branches.MapPut("/{id:guid}", async (Guid id, UpdateBranchRequest req, BranchService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(id, req, ct)))
            .RequirePermission(Permissions.BranchesManage);
        branches.MapDelete("/{id:guid}", async (Guid id, BranchService s, CancellationToken ct) =>
            {
                await s.DeleteAsync(id, ct);
                return Results.NoContent();
            }).RequirePermission(Permissions.BranchesManage);
        return api;
    }
}
