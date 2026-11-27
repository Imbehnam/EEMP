using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.Workflow.Contracts;
using EEMP.Modules.Workflow.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Tenancy;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Workflow.Application.WorkflowFormat;

namespace EEMP.Modules.Workflow.Application;

/// <summary>In-process entry point for modules that own workflow subjects (there is deliberately no HTTP start endpoint).</summary>
public sealed class WorkflowService(IWorkflowDbContext db, ITenantContext tenant, IBranchDirectory branches, TimeProvider clock,
    IWorkflowOutboxDispatcher dispatcher, InstanceService instances, IEnumerable<IWorkflowSubjectPolicy> policies) : IWorkflowService
{
    public const int MaxTitle = 200;
    public const int MaxIdempotencyKey = 100;

    public async Task<WorkflowInstanceInfo> StartAsync(StartWorkflowCommand command, CancellationToken ct = default)
    {
        tenant.RequireTenantId();
        var v = new Validator().RequiredText("title", command.Title, MaxTitle).RequiredText("idempotencyKey", command.IdempotencyKey, MaxIdempotencyKey)
            .When(!IsIdentifier(command.DefinitionKey), "definitionKey", Validator.InvalidFormat)
            .When(!IsIdentifier(command.SubjectModule), "subjectModule", Validator.InvalidFormat)
            .When(!IsIdentifier(command.SubjectType), "subjectType", Validator.InvalidFormat)
            .When(command.SubjectId == Guid.Empty, "subjectId", Validator.Required);
        var requester = tenant.UserId ?? command.RequestedByUserId;
        if (requester is null || requester == Guid.Empty) v.Add("requestedByUserId", Validator.Required);
        v.ThrowIfInvalid();

        var existing = await FindByKeyAsync(command, ct);
        if (existing is not null) return existing;

        var def = await db.Definitions.AsNoTracking().FirstOrDefaultAsync(d => d.Key == command.DefinitionKey, ct)
                  ?? throw new BusinessRuleException(WorkflowErrors.DefinitionNotPublished);
        if (def.SubjectModule != command.SubjectModule || def.SubjectType != command.SubjectType)
            throw new BusinessRuleException(WorkflowErrors.SubjectMismatch);
        if (!def.IsActive) throw new BusinessRuleException(WorkflowErrors.DefinitionInactive);
        if (def.PublishedVersion is not { } versionNo) throw new BusinessRuleException(WorkflowErrors.DefinitionNotPublished);
        if (command.BranchId is { } b && (b == Guid.Empty || !await branches.ExistsAsync(b, ct)))
        {
            v.Add("branchId", WorkflowErrors.BranchNotFound);
            v.ThrowIfInvalid();
        }
        if (await db.Instances.AnyAsync(i => i.DefinitionId == def.Id && i.SubjectId == command.SubjectId && i.SubjectType == command.SubjectType &&
                                             (i.Status == InstanceStatus.Pending || i.Status == InstanceStatus.Returned), ct))
            throw new ConflictException(WorkflowErrors.ActiveInstanceExists);

        var version = await db.Versions.AsNoTracking().Include(x => x.Steps)
            .FirstAsync(x => x.DefinitionId == def.Id && x.VersionNo == versionNo, ct);
        var steps = version.Steps.OrderBy(s => s.Order).ToList();
        var now = clock.GetUtcNow().UtcDateTime;
        var instance = new WorkflowInstance
        {
            DefinitionId = def.Id, VersionId = version.Id, VersionNo = version.VersionNo, DefinitionKey = def.Key,
            SubjectModule = command.SubjectModule, SubjectType = command.SubjectType, SubjectId = command.SubjectId, BranchId = command.BranchId,
            Title = command.Title.Trim(), RequestedByUserId = requester!.Value, Status = InstanceStatus.Pending,
            RequireDistinctApprovers = version.RequireDistinctApprovers, StepCount = steps.Count,
            IdempotencyKey = command.IdempotencyKey, StartedAt = now,
        };
        instance.EnterStep(steps[0], now);
        db.Instances.Add(instance);
        db.History.Add(new WorkflowHistoryEntry
        {
            InstanceId = instance.Id, Sequence = 1, Round = 1, Action = HistoryAction.Started, ToStatus = InstanceStatus.Pending,
            ActorUserId = requester, OccurredAt = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent start with the same idempotency key won the unique index: return its instance.
            db.ResetTracking();
            return await FindByKeyAsync(command, ct) ?? throw new ConflictException(WorkflowErrors.ActiveInstanceExists);
        }
        return Transitions.Info(instance);
    }

    private async Task<WorkflowInstanceInfo?> FindByKeyAsync(StartWorkflowCommand command, CancellationToken ct)
    {
        var existing = await db.Instances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.SubjectModule == command.SubjectModule && i.IdempotencyKey == command.IdempotencyKey, ct);
        if (existing is null) return null;
        if (existing.DefinitionKey != command.DefinitionKey || existing.SubjectType != command.SubjectType || existing.SubjectId != command.SubjectId)
            throw new ConflictException(WorkflowErrors.SubjectMismatch);
        return Transitions.Info(existing);
    }

    public async Task<WorkflowInstanceInfo?> FindAsync(Guid instanceId, CancellationToken ct = default) =>
        await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instanceId, ct) is { } i ? Transitions.Info(i) : null;

    public async Task<WorkflowInstanceInfo?> FindActiveAsync(string subjectModule, string subjectType, Guid subjectId, CancellationToken ct = default) =>
        await db.Instances.AsNoTracking()
            .Where(i => i.SubjectModule == subjectModule && i.SubjectType == subjectType && i.SubjectId == subjectId &&
                        (i.Status == InstanceStatus.Pending || i.Status == InstanceStatus.Returned))
            .OrderByDescending(i => i.StartedAt).FirstOrDefaultAsync(ct) is { } i ? Transitions.Info(i) : null;

    public async Task CancelAsync(Guid instanceId, string subjectModule, string? reason, CancellationToken ct = default)
    {
        var instance = await db.Instances.FirstOrDefaultAsync(i => i.Id == instanceId && i.SubjectModule == subjectModule, ct) ?? throw new NotFoundException();
        var outcome = WorkflowMachine.Next(instance.Status, instance.CurrentStepOrder, instance.StepCount, WorkflowCommand.Cancel);
        if (outcome is null) return;
        var comment = Clean(reason);
        if (comment is { Length: > InstanceService.MaxComment }) comment = comment[..InstanceService.MaxComment];
        await Transitions.ApplyAsync(db, instance, outcome, [], actorUserId: null, comment, requestId: null, clock.GetUtcNow().UtcDateTime, ct);
        await db.SaveChangesAsync(ct);
        await dispatcher.DispatchAsync(instance.Id, ct);
    }

    public async Task<WorkflowInstanceInfo> ActAsync(Guid instanceId, string subjectModule, string action, string? comment, CancellationToken ct = default)
    {
        var command = action switch
        {
            "approve" => WorkflowCommand.Approve,
            "reject" => WorkflowCommand.Reject,
            "return" => WorkflowCommand.Return,
            "resubmit" => WorkflowCommand.Resubmit,
            "cancel" => WorkflowCommand.Cancel,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        var current = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instanceId && i.SubjectModule == subjectModule, ct)
                      ?? throw new NotFoundException();
        // Same path as POST /workflow/instances/{id}/{action}: visibility, eligibility, maker-checker and the rowversion check all apply.
        await instances.ActAsync(instanceId, command, new WorkflowActionRequest(Rv(current.RowVersion), comment, null), ct);
        var after = await db.Instances.AsNoTracking().FirstAsync(i => i.Id == instanceId, ct);
        return Transitions.Info(after);
    }

    public async Task<bool> EnsureDefinitionAsync(WorkflowDefinitionTemplate template, CancellationToken ct = default)
    {
        tenant.RequireTenantId();
        if (await db.Definitions.AnyAsync(d => d.Key == template.Key, ct)) return false;
        var v = new Validator().RequiredText("name", template.Name, 200)
            .When(!IsIdentifier(template.Key), "key", Validator.InvalidFormat)
            .When(!IsIdentifier(template.SubjectModule), "subjectModule", Validator.InvalidFormat)
            .When(!IsIdentifier(template.SubjectType), "subjectType", Validator.InvalidFormat)
            .When(template.Steps.Count == 0 || template.Steps.Count > DefinitionService.MaxSteps, "steps", Validator.InvalidValue);
        for (var i = 0; i < template.Steps.Count; i++)
            v.When(!Permissions.Exists(template.Steps[i].Permission), $"steps[{i}].permission", Validator.UnknownPermission);
        v.ThrowIfInvalid();
        DefinitionService.EnsureStepsAllowed(policies, template.SubjectModule, template.SubjectType, template.Steps.Select(s => s.Permission));

        var now = clock.GetUtcNow().UtcDateTime;
        var def = new WorkflowDefinition
        {
            Key = template.Key, Name = template.Name.Trim(), SubjectModule = template.SubjectModule, SubjectType = template.SubjectType, PublishedVersion = 1,
        };
        var version = new WorkflowDefinitionVersion
        {
            DefinitionId = def.Id, VersionNo = 1, Status = DefinitionVersionStatus.Published, RequireDistinctApprovers = template.RequireDistinctApprovers,
            PublishedAt = now, PublishedByUserId = tenant.UserId,
        };
        var order = 1;
        foreach (var s in template.Steps)
            version.Steps.Add(new WorkflowStepDefinition
            {
                VersionId = version.Id, Order = order++, Name = s.Name, Permission = s.Permission, SlaHours = s.SlaHours, AllowReturn = s.AllowReturn,
            });
        def.Versions.Add(version);
        db.Definitions.Add(def);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // A concurrent first use provisioned the same key (unique index): theirs wins.
            db.ResetTracking();
            if (await db.Definitions.AnyAsync(d => d.Key == template.Key, ct)) return false;
            throw;
        }
    }
}
