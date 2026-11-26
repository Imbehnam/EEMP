using EEMP.Infrastructure.Authorization;
using EEMP.Infrastructure.Persistence;
using EEMP.Infrastructure.Web;
using EEMP.Modules.Workflow.Application;
using EEMP.Modules.Workflow.Contracts;
using EEMP.Modules.Workflow.Domain;
using EEMP.SharedKernel.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EEMP.Modules.Workflow.Infrastructure;

public static class WorkflowModule
{
    public static IServiceCollection AddWorkflowModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<WorkflowDbContext>(WorkflowDbContext.SchemaName);
        services.AddScoped<IWorkflowDbContext>(sp => sp.GetRequiredService<WorkflowDbContext>());
        services.AddOptions<WorkflowOutboxOptions>().Bind(configuration.GetSection(WorkflowOutboxOptions.SectionName));
        services.AddSingleton<IWorkflowOutboxDispatcher, WorkflowOutboxDispatcher>();
        services.AddHostedService<WorkflowOutboxWorker>();
        services.AddScoped<DefinitionService>();
        services.AddScoped<InstanceService>();
        services.AddScoped<IWorkflowService, WorkflowService>();
        return services;
    }

    /// <summary>
    /// Definition endpoints are gated by tenant-level permissions. Instance endpoints only require a tenant user: visibility and
    /// approver eligibility depend on the instance (branch, current step, requester) and are enforced in <see cref="InstanceService"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder api)
    {
        var defs = api.MapGroup("/workflow/definitions").WithTags("Workflow definitions");
        defs.MapGet("/", async (DefinitionService s, CancellationToken ct) => Results.Ok(await s.ListAsync(ct)))
            .RequirePermission(Permissions.WorkflowDefinitionsRead);
        defs.MapPost("/", async (CreateDefinitionRequest req, DefinitionService s, CancellationToken ct) =>
        {
            var r = await s.CreateAsync(req, ct);
            return Results.Created($"/api/v1/workflow/definitions/{r.Id}", r);
        }).RequirePermission(Permissions.WorkflowDefinitionsManage);
        defs.MapGet("/{id:guid}", async (Guid id, DefinitionService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(Permissions.WorkflowDefinitionsRead);
        defs.MapPut("/{id:guid}", async (Guid id, UpdateDefinitionRequest req, DefinitionService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(id, req, ct))).RequirePermission(Permissions.WorkflowDefinitionsManage);
        defs.MapPost("/{id:guid}/versions", async (Guid id, DefinitionService s, CancellationToken ct) =>
        {
            var r = await s.NewVersionAsync(id, ct);
            return Results.Created($"/api/v1/workflow/definitions/{r.Id}", r);
        }).RequirePermission(Permissions.WorkflowDefinitionsManage);
        defs.MapPut("/{id:guid}/versions/{versionNo:int}", async (Guid id, int versionNo, UpdateVersionRequest req, DefinitionService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateVersionAsync(id, versionNo, req, ct))).RequirePermission(Permissions.WorkflowDefinitionsManage);
        defs.MapPost("/{id:guid}/versions/{versionNo:int}/publish", async (Guid id, int versionNo, PublishVersionRequest req, DefinitionService s, CancellationToken ct) =>
            Results.Ok(await s.PublishAsync(id, versionNo, req, ct))).RequirePermission(Permissions.WorkflowDefinitionsManage);

        var wf = api.MapGroup("/workflow").WithTags("Workflow tasks");
        wf.MapGet("/inbox", async ([AsParameters] PagingQuery q, string? definitionKey, bool? overdue, InstanceService s, CancellationToken ct) =>
            Results.Ok(await s.InboxAsync(q.ToPageRequest(), new InboxQuery(definitionKey, overdue), ct))).RequireAuthorization(Policies.TenantUser);
        wf.MapGet("/instances", async ([AsParameters] PagingQuery q, string? scope, string? status, string? definitionKey, string? subjectModule,
                string? subjectType, Guid? subjectId, InstanceService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q.ToPageRequest(), new InstanceListQuery(scope, status, definitionKey, subjectModule, subjectType, subjectId), ct)))
            .RequireAuthorization(Policies.TenantUser);
        wf.MapGet("/instances/{id:guid}", async (Guid id, InstanceService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequireAuthorization(Policies.TenantUser);
        foreach (var (route, command) in new[]
                 {
                     ("approve", WorkflowCommand.Approve), ("reject", WorkflowCommand.Reject), ("return", WorkflowCommand.Return),
                     ("resubmit", WorkflowCommand.Resubmit), ("cancel", WorkflowCommand.Cancel),
                 })
        {
            wf.MapPost($"/instances/{{id:guid}}/{route}", async (Guid id, WorkflowActionRequest req, InstanceService s, CancellationToken ct) =>
                Results.Ok(await s.ActAsync(id, command, req, ct))).RequireAuthorization(Policies.TenantUser);
        }
        return api;
    }
}
