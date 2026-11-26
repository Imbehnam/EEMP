using EEMP.Modules.Workflow.Domain;
using EEMP.Modules.Workflow.Infrastructure;
using EEMP.SharedKernel.Authorization;

namespace EEMP.UnitTests;

public class WorkflowMachineTests
{
    [Fact]
    public void Approve_moves_to_the_next_step_and_finishes_after_the_last()
    {
        var first = WorkflowMachine.Next(InstanceStatus.Pending, 1, 2, WorkflowCommand.Approve)!;
        Assert.Equal((InstanceStatus.Pending, 2, HistoryAction.Approved), (first.Status, first.StepOrder, first.Action));
        var last = WorkflowMachine.Next(InstanceStatus.Pending, 2, 2, WorkflowCommand.Approve)!;
        Assert.Equal((InstanceStatus.Approved, (int?)null), (last.Status, last.StepOrder));
    }

    [Fact]
    public void Reject_return_resubmit_and_cancel_follow_the_fixed_machine()
    {
        Assert.Equal(InstanceStatus.Rejected, WorkflowMachine.Next(InstanceStatus.Pending, 1, 3, WorkflowCommand.Reject)!.Status);
        Assert.Equal(InstanceStatus.Returned, WorkflowMachine.Next(InstanceStatus.Pending, 2, 3, WorkflowCommand.Return)!.Status);
        Assert.Null(WorkflowMachine.Next(InstanceStatus.Pending, 2, 3, WorkflowCommand.Return, stepAllowsReturn: false));
        var resubmit = WorkflowMachine.Next(InstanceStatus.Returned, null, 3, WorkflowCommand.Resubmit)!;
        Assert.Equal((InstanceStatus.Pending, (int?)1, HistoryAction.Resubmitted), (resubmit.Status, resubmit.StepOrder, resubmit.Action));
        Assert.Equal(InstanceStatus.Cancelled, WorkflowMachine.Next(InstanceStatus.Pending, 1, 1, WorkflowCommand.Cancel)!.Status);
        Assert.Equal(InstanceStatus.Cancelled, WorkflowMachine.Next(InstanceStatus.Returned, null, 1, WorkflowCommand.Cancel)!.Status);
        Assert.Null(WorkflowMachine.Next(InstanceStatus.Returned, null, 1, WorkflowCommand.Approve));
        Assert.Null(WorkflowMachine.Next(InstanceStatus.Pending, 1, 1, WorkflowCommand.Resubmit));
    }

    [Theory]
    [InlineData(InstanceStatus.Approved)]
    [InlineData(InstanceStatus.Rejected)]
    [InlineData(InstanceStatus.Cancelled)]
    public void Terminal_states_accept_no_command(InstanceStatus status)
    {
        Assert.True(WorkflowMachine.IsTerminal(status));
        foreach (var command in Enum.GetValues<WorkflowCommand>())
            Assert.Null(WorkflowMachine.Next(status, null, 2, command));
    }

    [Fact]
    public void Entering_a_step_sets_due_time_from_sla()
    {
        var now = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);
        var i = new WorkflowInstance();
        i.EnterStep(new WorkflowStepDefinition { Order = 1, Name = "Review", Permission = "workflow.tasks.approve", SlaHours = 48 }, now);
        Assert.Equal(now.AddHours(48), i.CurrentStepDueAt);
        i.EnterStep(new WorkflowStepDefinition { Order = 2, Name = "Final", Permission = "x" }, now);
        Assert.Null(i.CurrentStepDueAt);
        i.EnterStep(null, now);
        Assert.Null(i.CurrentStepOrder);
        Assert.Null(i.CurrentStepPermission);
    }

    [Fact]
    public void Outbox_backoff_grows_and_is_capped()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), WorkflowOutboxDispatcher.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(20), WorkflowOutboxDispatcher.Backoff(2));
        Assert.Equal(TimeSpan.FromHours(1), WorkflowOutboxDispatcher.Backoff(100));
    }
}

public class ApproverPolicyTests
{
    private static readonly Guid North = Guid.NewGuid();
    private static readonly Guid South = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    private static WorkflowInstance Pending(Guid? branch = null, Guid? assignee = null, bool distinct = true) => new()
    {
        Status = InstanceStatus.Pending, BranchId = branch, RequestedByUserId = Requester, RequireDistinctApprovers = distinct,
        CurrentStepOrder = 1, CurrentStepPermission = Permissions.WorkflowTasksApprove, CurrentStepAssigneeUserId = assignee,
    };

    private static BranchScope Branches(params Guid[] ids) => new(false, ids.ToHashSet());

    [Fact]
    public void Permission_must_cover_the_instance_branch()
    {
        Assert.Equal(Eligibility.Eligible, ApproverPolicy.Evaluate(Pending(North), Approver, Branches(North), false));
        Assert.Equal(Eligibility.NoPermission, ApproverPolicy.Evaluate(Pending(North), Approver, Branches(South), false));
        Assert.Equal(Eligibility.NoPermission, ApproverPolicy.Evaluate(Pending(North), Approver, BranchScope.None, false));
        Assert.Equal(Eligibility.Eligible, ApproverPolicy.Evaluate(Pending(North), Approver, BranchScope.Tenant, false));
    }

    [Fact]
    public void Tenant_level_instances_need_a_tenant_wide_grant()
    {
        Assert.Equal(Eligibility.NoPermission, ApproverPolicy.Evaluate(Pending(), Approver, Branches(North, South), false));
        Assert.Equal(Eligibility.Eligible, ApproverPolicy.Evaluate(Pending(), Approver, BranchScope.Tenant, false));
    }

    [Fact]
    public void Named_assignee_must_also_hold_the_permission()
    {
        Assert.Equal(Eligibility.Eligible, ApproverPolicy.Evaluate(Pending(North, Approver), Approver, Branches(North), false));
        Assert.Equal(Eligibility.NotAssignee, ApproverPolicy.Evaluate(Pending(North, Guid.NewGuid()), Approver, Branches(North), false));
        Assert.Equal(Eligibility.NoPermission, ApproverPolicy.Evaluate(Pending(North, Approver), Approver, BranchScope.None, false));
    }

    [Fact]
    public void Maker_checker_and_distinct_approvers()
    {
        Assert.Equal(Eligibility.SelfApproval, ApproverPolicy.Evaluate(Pending(North), Requester, BranchScope.Tenant, false));
        Assert.Equal(Eligibility.AlreadyActed, ApproverPolicy.Evaluate(Pending(North), Approver, Branches(North), true));
        Assert.Equal(Eligibility.Eligible, ApproverPolicy.Evaluate(Pending(North, distinct: false), Approver, Branches(North), true));
    }

    [Fact]
    public void Only_pending_instances_have_approvers()
    {
        var i = Pending(North);
        i.Status = InstanceStatus.Returned;
        Assert.Equal(Eligibility.NotPending, ApproverPolicy.Evaluate(i, Approver, BranchScope.Tenant, false));
    }
}
