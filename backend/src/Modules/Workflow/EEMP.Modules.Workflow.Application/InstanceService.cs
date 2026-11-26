using System.Linq.Expressions;
using EEMP.Modules.Workflow.Domain;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Paging;
using EEMP.SharedKernel.Tenancy;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Workflow.Application.WorkflowFormat;

namespace EEMP.Modules.Workflow.Application;

/// <summary>
/// Inbox, instance views and user actions. Approvers are resolved at action time from the caller's current grants
/// (see <see cref="ApproverPolicy"/>); every transition is checked against the instance rowversion and written to history.
/// Instances the caller may not see are reported as not found.
/// </summary>
public sealed class InstanceService(IWorkflowDbContext db, IAccessControl access, ITenantContext tenant, TimeProvider clock,
    IWorkflowOutboxDispatcher dispatcher)
{
    public const int MaxComment = 2000;
    public const int MaxRequestId = 64;

    private Guid Me => tenant.UserId ?? throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<InstanceSummaryDto>> InboxAsync(PageRequest page, InboxQuery query, CancellationToken ct)
    {
        var q = await EligibleQueryAsync(Me, ct);
        if (!string.IsNullOrWhiteSpace(query.DefinitionKey)) q = q.Where(i => i.DefinitionKey == query.DefinitionKey);
        if (query.Overdue == true) { var now = Now; q = q.Where(i => i.CurrentStepDueAt != null && i.CurrentStepDueAt < now); }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(i => i.CurrentStepDueAt == null).ThenBy(i => i.CurrentStepDueAt).ThenBy(i => i.StartedAt)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<InstanceSummaryDto>(await SummariesAsync(rows, ct), page.Page, page.PageSize, total);
    }

    public async Task<PagedResult<InstanceSummaryDto>> ListAsync(PageRequest page, InstanceListQuery query, CancellationToken ct)
    {
        var v = new Validator();
        var me = Me;
        IQueryable<WorkflowInstance> q = db.Instances.AsNoTracking();
        var scope = string.IsNullOrWhiteSpace(query.Scope) ? "mine" : query.Scope;
        if (scope == "mine") q = q.Where(i => i.RequestedByUserId == me);
        else if (scope == "all")
        {
            var read = await access.GetBranchScopeAsync(Permissions.WorkflowInstancesRead, ct);
            if (read.IsEmpty) throw new ForbiddenException();
            if (!read.AllBranches)
            {
                var ids = read.BranchIds.ToList();
                q = q.Where(i => i.BranchId != null && ids.Contains(i.BranchId.Value));
            }
        }
        else v.Add("scope", Validator.InvalidValue);
        InstanceStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (Enum.TryParse<InstanceStatus>(query.Status, true, out var s) && !int.TryParse(query.Status, out _)) status = s;
            else v.Add("status", Validator.InvalidValue);
        }
        v.ThrowIfInvalid();
        if (status is { } st) q = q.Where(i => i.Status == st);
        if (!string.IsNullOrWhiteSpace(query.DefinitionKey)) q = q.Where(i => i.DefinitionKey == query.DefinitionKey);
        if (!string.IsNullOrWhiteSpace(query.SubjectModule)) q = q.Where(i => i.SubjectModule == query.SubjectModule);
        if (!string.IsNullOrWhiteSpace(query.SubjectType)) q = q.Where(i => i.SubjectType == query.SubjectType);
        if (query.SubjectId is { } sid) q = q.Where(i => i.SubjectId == sid);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(i => i.StartedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<InstanceSummaryDto>(await SummariesAsync(rows, ct), page.Page, page.PageSize, total);
    }

    public async Task<InstanceDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var instance = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException();
        var ctx = await EvaluateAsync(instance, ct);
        if (!ctx.Visible) throw new NotFoundException();
        return await DetailAsync(instance, ctx, ct);
    }

    public async Task<InstanceDetailDto> ActAsync(Guid id, WorkflowCommand command, WorkflowActionRequest req, CancellationToken ct)
    {
        var comment = Clean(req.Comment);
        var requestId = Clean(req.RequestId);
        var v = new Validator().RowVersion("rowVersion", req.RowVersion).OptionalText("comment", comment, MaxComment)
            .OptionalText("requestId", requestId, MaxRequestId);
        if (command is WorkflowCommand.Reject or WorkflowCommand.Return && comment is null) v.Add("comment", Validator.Required);
        v.ThrowIfInvalid();
        var me = Me;

        var instance = await db.Instances.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException();
        var ctx = await EvaluateAsync(instance, ct);
        if (!ctx.Visible) throw new NotFoundException();

        // Idempotent retry: the same caller repeating a command with the same requestId gets the current state.
        if (requestId is not null && await db.History.AnyAsync(h => h.InstanceId == id && h.ActorUserId == me && h.RequestId == requestId, ct))
            return await DetailAsync(instance, ctx, ct);

        var steps = await db.Steps.AsNoTracking().Where(s => s.VersionId == instance.VersionId).OrderBy(s => s.Order).ToListAsync(ct);
        var current = steps.FirstOrDefault(s => s.Order == instance.CurrentStepOrder);
        var outcome = WorkflowMachine.Next(instance.Status, instance.CurrentStepOrder, instance.StepCount, command, current?.AllowReturn ?? true);
        if (outcome is null)
        {
            if (command == WorkflowCommand.Return && instance.Status == InstanceStatus.Pending) throw new BusinessRuleException(WorkflowErrors.ReturnNotAllowed);
            throw new ConflictException(WorkflowErrors.InvalidTransition, P(("from", Camel(instance.Status)), ("action", Camel(command))));
        }

        switch (command)
        {
            case WorkflowCommand.Approve or WorkflowCommand.Reject or WorkflowCommand.Return:
                switch (ctx.Eligibility)
                {
                    case Eligibility.Eligible: break;
                    case Eligibility.SelfApproval: throw new ForbiddenException(WorkflowErrors.SelfApprovalForbidden);
                    case Eligibility.AlreadyActed: throw new ForbiddenException(WorkflowErrors.ApproverAlreadyActed);
                    default: throw new ForbiddenException(WorkflowErrors.NotEligible);
                }
                break;
            case WorkflowCommand.Resubmit:
                if (instance.RequestedByUserId != me) throw new ForbiddenException(WorkflowErrors.NotEligible);
                break;
            case WorkflowCommand.Cancel:
                if (instance.RequestedByUserId != me && !ctx.CanManage) throw new ForbiddenException(WorkflowErrors.NotEligible);
                break;
        }

        db.SetOriginalRowVersion(instance, Convert.FromBase64String(req.RowVersion!));
        await Transitions.ApplyAsync(db, instance, outcome, steps, me, comment, requestId, Now, ct);
        await db.SaveChangesAsync(ct);
        await dispatcher.DispatchAsync(instance.Id, ct);

        return await DetailAsync(instance, await EvaluateAsync(instance, ct), ct);
    }

    private sealed record AccessContext(bool Visible, Eligibility Eligibility, bool CanManage, bool IsRequester);

    private async Task<AccessContext> EvaluateAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var me = Me;
        var stepScope = instance.CurrentStepPermission is { } p ? await access.GetBranchScopeAsync(p, ct) : BranchScope.None;
        var approvedThisRound = await db.History.AnyAsync(h => h.InstanceId == instance.Id && h.Round == instance.Round &&
                                                               h.Action == HistoryAction.Approved && h.ActorUserId == me, ct);
        var eligibility = ApproverPolicy.Evaluate(instance, me, stepScope, approvedThisRound);
        var canManage = ApproverPolicy.Covers(await access.GetBranchScopeAsync(Permissions.WorkflowInstancesManage, ct), instance.BranchId);
        var isRequester = instance.RequestedByUserId == me;
        var visible = isRequester || canManage || eligibility is Eligibility.Eligible or Eligibility.AlreadyActed
                      || ApproverPolicy.Covers(await access.GetBranchScopeAsync(Permissions.WorkflowInstancesRead, ct), instance.BranchId)
                      || await db.History.AnyAsync(h => h.InstanceId == instance.Id && h.ActorUserId == me, ct);
        return new AccessContext(visible, eligibility, canManage, isRequester);
    }

    /// <summary>Pending instances whose current step the caller may act on (maker-checker and distinct-approver rules applied).</summary>
    private async Task<IQueryable<WorkflowInstance>> EligibleQueryAsync(Guid me, CancellationToken ct)
    {
        var permissions = await db.Instances.AsNoTracking().Where(i => i.Status == InstanceStatus.Pending && i.CurrentStepPermission != null)
            .Select(i => i.CurrentStepPermission!).Distinct().ToListAsync(ct);
        Expression<Func<WorkflowInstance, bool>>? held = null;
        foreach (var permission in permissions)
        {
            var scope = await access.GetBranchScopeAsync(permission, ct);
            if (scope.IsEmpty) continue;
            Expression<Func<WorkflowInstance, bool>> e;
            if (scope.AllBranches) e = i => i.CurrentStepPermission == permission;
            else
            {
                var ids = scope.BranchIds.ToList();
                e = i => i.CurrentStepPermission == permission && i.BranchId != null && ids.Contains(i.BranchId.Value);
            }
            held = held is null ? e : held.Or(e);
        }
        var instances = db.Instances.AsNoTracking();
        if (held is null) return instances.Where(i => false);
        var history = db.History;
        return instances.Where(i => i.Status == InstanceStatus.Pending).Where(held)
            .Where(i => i.RequestedByUserId != me && (i.CurrentStepAssigneeUserId == null || i.CurrentStepAssigneeUserId == me))
            .Where(i => !i.RequireDistinctApprovers || !history.Any(h => h.InstanceId == i.Id && h.Round == i.Round &&
                                                                         h.Action == HistoryAction.Approved && h.ActorUserId == me));
    }

    private async Task<List<InstanceSummaryDto>> SummariesAsync(List<WorkflowInstance> rows, CancellationToken ct)
    {
        var defIds = rows.Select(r => r.DefinitionId).Distinct().ToList();
        var names = await db.Definitions.AsNoTracking().Where(d => defIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var now = Now;
        return rows.Select(i => Summary(i, names.GetValueOrDefault(i.DefinitionId, i.DefinitionKey), now)).ToList();
    }

    private static CurrentStepDto? Step(WorkflowInstance i, DateTime now) => i.CurrentStepOrder is { } o
        ? new CurrentStepDto(o, i.CurrentStepName!, i.CurrentStepPermission!, i.CurrentStepAssigneeUserId, i.CurrentStepDueAt,
            i.CurrentStepDueAt is { } due && due < now)
        : null;

    private static InstanceSummaryDto Summary(WorkflowInstance i, string defName, DateTime now) => new(i.Id, i.DefinitionId, i.DefinitionKey, defName,
        i.VersionNo, i.SubjectModule, i.SubjectType, i.SubjectId, i.BranchId, i.Title, i.RequestedByUserId, Camel(i.Status), Step(i, now),
        i.StartedAt, i.CompletedAt, Rv(i.RowVersion));

    private async Task<InstanceDetailDto> DetailAsync(WorkflowInstance i, AccessContext ctx, CancellationToken ct)
    {
        var name = await db.Definitions.AsNoTracking().Where(d => d.Id == i.DefinitionId).Select(d => d.Name).FirstOrDefaultAsync(ct) ?? i.DefinitionKey;
        var history = await db.History.AsNoTracking().Where(h => h.InstanceId == i.Id).OrderBy(h => h.Sequence).ToListAsync(ct);
        var allowReturn = i.CurrentStepOrder is { } o && await db.Steps.AsNoTracking().AnyAsync(s => s.VersionId == i.VersionId && s.Order == o && s.AllowReturn, ct);
        var actions = new List<string>();
        if (ctx.Eligibility == Eligibility.Eligible)
        {
            actions.Add("approve");
            actions.Add("reject");
            if (allowReturn) actions.Add("return");
        }
        if (i.Status == InstanceStatus.Returned && ctx.IsRequester) actions.Add("resubmit");
        if (i.IsActive && (ctx.IsRequester || ctx.CanManage)) actions.Add("cancel");
        var now = Now;
        return new InstanceDetailDto(i.Id, i.DefinitionId, i.DefinitionKey, name, i.VersionNo, i.SubjectModule, i.SubjectType, i.SubjectId, i.BranchId,
            i.Title, i.RequestedByUserId, Camel(i.Status), Step(i, now), i.StartedAt, i.CompletedAt, Rv(i.RowVersion), i.Round,
            history.Select(h => new HistoryDto(h.Sequence, h.Round, Camel(h.Action), Camel(h.FromStatus), Camel(h.ToStatus), h.StepOrder, h.StepName,
                h.ActorUserId, h.Comment, h.OccurredAt)).ToList(), actions);
    }
}
