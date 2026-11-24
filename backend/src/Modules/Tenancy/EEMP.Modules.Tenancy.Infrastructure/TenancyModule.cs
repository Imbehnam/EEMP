using EEMP.Infrastructure.Authorization;
using EEMP.Infrastructure.Persistence;
using EEMP.Infrastructure.Web;
using EEMP.Modules.Tenancy.Application;
using EEMP.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EEMP.Modules.Tenancy.Infrastructure;

public static class TenancyModule
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<TenancyDbContext>(TenancyDbContext.SchemaName);
        services.AddScoped<ITenancyDbContext>(sp => sp.GetRequiredService<TenancyDbContext>());
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<TenantService>();
        return services;
    }

    public static IEndpointRouteBuilder MapTenancyEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/platform/tenants").WithTags("Platform").RequireAuthorization(Policies.PlatformAdmin);

        g.MapGet("/", async ([AsParameters] PagingQuery q, TenantService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), ct)));
        g.MapPost("/", async (CreateTenantRequest req, TenantService s, CancellationToken ct) =>
        {
            var t = await s.CreateAsync(req, ct);
            return Results.Created($"/api/v1/platform/tenants/{t.Id}", t);
        });
        g.MapGet("/{id:guid}", async (Guid id, TenantService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)));
        g.MapPut("/{id:guid}", async (Guid id, UpdateTenantRequest req, TenantService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(id, req, ct)));
        g.MapPost("/{id:guid}/suspend", async (Guid id, TenantService s, CancellationToken ct) => Results.Ok(await s.SetStatusAsync(id, false, ct)));
        g.MapPost("/{id:guid}/activate", async (Guid id, TenantService s, CancellationToken ct) => Results.Ok(await s.SetStatusAsync(id, true, ct)));
        return api;
    }
}
