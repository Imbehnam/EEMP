using EEMP.Infrastructure.Authorization;
using EEMP.Infrastructure.Persistence;
using EEMP.Infrastructure.Web;
using EEMP.Modules.People.Application;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EEMP.Modules.People.Infrastructure;

public static class PeopleModule
{
    public static IServiceCollection AddPeopleModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<PeopleDbContext>(PeopleDbContext.SchemaName);
        services.AddScoped<IPeopleDbContext>(sp => sp.GetRequiredService<PeopleDbContext>());
        services.AddScoped(typeof(PersonService<>));
        services.AddScoped<StudentService>();
        services.AddScoped<GuardianService>();
        services.AddScoped<TeacherService>();
        services.AddScoped<StaffService>();
        services.AddScoped<PortalService>();
        services.AddScoped<IPeopleBranchUsage, PeopleBranchUsage>();
        services.AddScoped<IPeopleDirectory, PeopleDirectory>();
        return services;
    }

    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder api)
    {
        var students = api.MapGroup("/students").WithTags("Students");
        students.MapGet("/", async ([AsParameters] PagingQuery q, Guid? branchId, StudentService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), branchId, ct))).RequirePermission(Permissions.StudentsRead);
        students.MapPost("/", async (StudentRequest req, StudentService s, CancellationToken ct) =>
        {
            var r = await s.CreateAsync(req, ct);
            return Results.Created($"/api/v1/students/{r.Id}", r);
        }).RequirePermission(Permissions.StudentsManage);
        students.MapGet("/{id:guid}", async (Guid id, StudentService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Permissions.StudentsRead);
        students.MapPut("/{id:guid}", async (Guid id, StudentRequest req, StudentService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(id, req, ct)))
            .RequirePermission(Permissions.StudentsManage);
        students.MapDelete("/{id:guid}", async (Guid id, StudentService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.StudentsManage);
        students.MapGet("/{id:guid}/guardians", async (Guid id, StudentService s, CancellationToken ct) => Results.Ok(await s.GetGuardiansAsync(id, ct)))
            .RequirePermission(Permissions.StudentsRead);
        students.MapPut("/{id:guid}/guardians", async (Guid id, List<StudentGuardianRequest> req, StudentService s, CancellationToken ct) =>
            Results.Ok(await s.SetGuardiansAsync(id, req, ct))).RequirePermission(Permissions.StudentsManage);

        var guardians = api.MapGroup("/guardians").WithTags("Guardians");
        guardians.MapGet("/", async ([AsParameters] PagingQuery q, Guid? branchId, GuardianService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), branchId, ct))).RequirePermission(Permissions.GuardiansRead);
        guardians.MapPost("/", async (GuardianRequest req, GuardianService s, CancellationToken ct) =>
        {
            var r = await s.CreateAsync(req, ct);
            return Results.Created($"/api/v1/guardians/{r.Id}", r);
        }).RequirePermission(Permissions.GuardiansManage);
        guardians.MapGet("/{id:guid}", async (Guid id, GuardianService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Permissions.GuardiansRead);
        guardians.MapPut("/{id:guid}", async (Guid id, GuardianRequest req, GuardianService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(id, req, ct)))
            .RequirePermission(Permissions.GuardiansManage);
        guardians.MapDelete("/{id:guid}", async (Guid id, GuardianService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.GuardiansManage);

        var teachers = api.MapGroup("/teachers").WithTags("Teachers");
        teachers.MapGet("/", async ([AsParameters] PagingQuery q, Guid? branchId, TeacherService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), branchId, ct))).RequirePermission(Permissions.TeachersRead);
        teachers.MapPost("/", async (TeacherRequest req, TeacherService s, CancellationToken ct) =>
        {
            var r = await s.CreateAsync(req, ct);
            return Results.Created($"/api/v1/teachers/{r.Id}", r);
        }).RequirePermission(Permissions.TeachersManage);
        teachers.MapGet("/{id:guid}", async (Guid id, TeacherService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Permissions.TeachersRead);
        teachers.MapPut("/{id:guid}", async (Guid id, TeacherRequest req, TeacherService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(id, req, ct)))
            .RequirePermission(Permissions.TeachersManage);
        teachers.MapDelete("/{id:guid}", async (Guid id, TeacherService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.TeachersManage);

        var staff = api.MapGroup("/staff").WithTags("Staff");
        staff.MapGet("/", async ([AsParameters] PagingQuery q, Guid? branchId, StaffService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), branchId, ct))).RequirePermission(Permissions.StaffRead);
        staff.MapPost("/", async (StaffRequest req, StaffService s, CancellationToken ct) =>
        {
            var r = await s.CreateAsync(req, ct);
            return Results.Created($"/api/v1/staff/{r.Id}", r);
        }).RequirePermission(Permissions.StaffManage);
        staff.MapGet("/{id:guid}", async (Guid id, StaffService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Permissions.StaffRead);
        staff.MapPut("/{id:guid}", async (Guid id, StaffRequest req, StaffService s, CancellationToken ct) => Results.Ok(await s.UpdateAsync(id, req, ct)))
            .RequirePermission(Permissions.StaffManage);
        staff.MapDelete("/{id:guid}", async (Guid id, StaffService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.StaffManage);

        // Portal: any authenticated tenant user; the account ↔ profile link is the authorization.
        var portal = api.MapGroup("/portal/me").WithTags("Portal").RequireAuthorization(Policies.TenantUser);
        portal.MapGet("/profile", async (PortalService s, CancellationToken ct) => Results.Ok(await s.GetProfileAsync(ct)));
        portal.MapGet("/students", async (PortalService s, CancellationToken ct) => Results.Ok(await s.GetMyStudentsAsync(ct)));
        return api;
    }
}
