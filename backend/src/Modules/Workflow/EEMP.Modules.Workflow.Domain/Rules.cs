using EEMP.SharedKernel.Authorization;

namespace EEMP.Modules.Workflow.Domain;

public static class WorkflowErrors
{
    public const string DefinitionKeyTaken = "workflow.definition_key_taken";
    public const string DefinitionInactive = "workflow.definition_inactive";
    public const string DefinitionNotPublished = "workflow.definition_not_published";
    public const string VersionNotDraft = "workflow.version_not_draft";
    public const string DraftExists = "workflow.draft_exists";
    public const string InvalidTransition = "workflow.invalid_transition";
    public const string SelfApprovalForbidden = "workflow.self_approval_forbidden";
    public const string ApproverAlreadyActed = "workflow.approver_already_acted";
    public const string NotEligible = "workflow.not_eligible";
    public const string ReturnNotAllowed = "workflow.return_not_allowed";
    public const string ActiveInstanceExists = "workflow.active_instance_exists";
    public const string SubjectMismatch = "workflow.subject_mismatch";
    public const string StepPermissionNotAllowed = "workflow.step_permission_not_allowed";

    // Field-level codes (validation errors[field]).
    public const string UnknownAssignee = "workflow.unknown_assignee";
    public const string BranchNotFound = "workflow.branch_not_found";
}

/// <summary>The fixed instance state machine. Steps are configurable; the transitions between statuses are not.</summary>
public static class WorkflowMachine
{
    public sealed record Outcome(InstanceStatus Status, int? StepOrder, HistoryAction Action);

    /// <summary>Returns the next status/step, or null when the command is not valid from the current state.</summary>
    public static Outcome? Next(InstanceStatus status, int? currentStep, int stepCount, WorkflowCommand command, bool stepAllowsReturn = true)
    {
        switch (status, command)
        {
            case (InstanceStatus.Pending, WorkflowCommand.Approve) when currentStep is { } s:
                return s >= stepCount
                    ? new Outcome(InstanceStatus.Approved, null, HistoryAction.Approved)
                    : new Outcome(InstanceStatus.Pending, s + 1, HistoryAction.Approved);
            case (InstanceStatus.Pending, WorkflowCommand.Reject):
                return new Outcome(InstanceStatus.Rejected, null, HistoryAction.Rejected);
            case (InstanceStatus.Pending, WorkflowCommand.Return) when stepAllowsReturn:
                return new Outcome(InstanceStatus.Returned, null, HistoryAction.Returned);
            case (InstanceStatus.Returned, WorkflowCommand.Resubmit):
                return new Outcome(InstanceStatus.Pending, 1, HistoryAction.Resubmitted);
            case (InstanceStatus.Pending or InstanceStatus.Returned, WorkflowCommand.Cancel):
                return new Outcome(InstanceStatus.Cancelled, null, HistoryAction.Cancelled);
            default:
                return null;
        }
    }

    public static bool IsTerminal(InstanceStatus s) => s is InstanceStatus.Approved or InstanceStatus.Rejected or InstanceStatus.Cancelled;
}

public enum Eligibility { Eligible, NoPermission, NotAssignee, SelfApproval, AlreadyActed, NotPending }

/// <summary>
/// Approver resolution for the current step: the caller holds the step permission in the instance branch (tenant-wide for
/// tenant-level instances), is the named assignee when there is one, is not the requester (maker-checker), and has not approved an
/// earlier step of this round when distinct approvers are required.
/// </summary>
public static class ApproverPolicy
{
    public static Eligibility Evaluate(WorkflowInstance instance, Guid userId, BranchScope scopeForStepPermission, bool alreadyApprovedThisRound)
    {
        if (instance.Status != InstanceStatus.Pending || instance.CurrentStepPermission is null) return Eligibility.NotPending;
        var holds = instance.BranchId is { } b ? scopeForStepPermission.Includes(b) : scopeForStepPermission.AllBranches;
        if (!holds) return Eligibility.NoPermission;
        if (instance.CurrentStepAssigneeUserId is { } assignee && assignee != userId) return Eligibility.NotAssignee;
        if (instance.RequestedByUserId == userId) return Eligibility.SelfApproval;
        if (instance.RequireDistinctApprovers && alreadyApprovedThisRound) return Eligibility.AlreadyActed;
        return Eligibility.Eligible;
    }

    /// <summary>True when the scope covers the branch (or, for tenant-level instances, is tenant-wide).</summary>
    public static bool Covers(BranchScope scope, Guid? branchId) => branchId is { } b ? scope.Includes(b) : scope.AllBranches;
}
