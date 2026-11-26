using EEMP.SharedKernel.Domain;

namespace EEMP.Modules.Workflow.Domain;

public enum DefinitionVersionStatus { Draft, Published, Retired }

public enum InstanceStatus { Pending, Returned, Approved, Rejected, Cancelled }

public enum WorkflowCommand { Approve, Reject, Return, Resubmit, Cancel }

public enum HistoryAction { Started, Approved, Rejected, Returned, Resubmitted, Cancelled }

public enum OutboxStatus { Pending, Delivered, Failed }

/// <summary>A tenant's process definition (e.g. "discount-approval") bound to one kind of subject owned by another module.</summary>
public sealed class WorkflowDefinition : TenantEntity
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SubjectModule { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>Version number new instances start on; null until a version is published.</summary>
    public int? PublishedVersion { get; set; }
    public List<WorkflowDefinitionVersion> Versions { get; set; } = [];
}

/// <summary>Immutable once published. Instances pin the version they started on.</summary>
public sealed class WorkflowDefinitionVersion : TenantEntity
{
    public Guid DefinitionId { get; set; }
    public int VersionNo { get; set; }
    public DefinitionVersionStatus Status { get; set; } = DefinitionVersionStatus.Draft;
    public bool RequireDistinctApprovers { get; set; } = true;
    public DateTime? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public List<WorkflowStepDefinition> Steps { get; set; } = [];
}

/// <summary>Sequential approval step. Approvers hold <see cref="Permission"/> in the instance branch (and are the assignee when one is named).</summary>
public sealed class WorkflowStepDefinition : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid VersionId { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public Guid? AssigneeUserId { get; set; }
    public int? SlaHours { get; set; }
    public bool AllowReturn { get; set; } = true;
}

/// <summary>One run of a published definition version for a subject (module + type + id).</summary>
public sealed class WorkflowInstance : TenantEntity
{
    public Guid DefinitionId { get; set; }
    public Guid VersionId { get; set; }
    public int VersionNo { get; set; }
    public string DefinitionKey { get; set; } = string.Empty;
    public string SubjectModule { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public Guid? BranchId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid RequestedByUserId { get; set; }
    public InstanceStatus Status { get; set; } = InstanceStatus.Pending;
    public bool RequireDistinctApprovers { get; set; } = true;
    /// <summary>Incremented on each resubmission; distinct-approver checks apply within a round.</summary>
    public int Round { get; set; } = 1;
    public int StepCount { get; set; }

    // Current step, denormalised from the pinned version so the inbox is one indexed query.
    public int? CurrentStepOrder { get; set; }
    public string? CurrentStepName { get; set; }
    public string? CurrentStepPermission { get; set; }
    public Guid? CurrentStepAssigneeUserId { get; set; }
    public DateTime? CurrentStepEnteredAt { get; set; }
    public DateTime? CurrentStepDueAt { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public bool IsActive => Status is InstanceStatus.Pending or InstanceStatus.Returned;

    public void EnterStep(WorkflowStepDefinition? step, DateTime now)
    {
        CurrentStepOrder = step?.Order;
        CurrentStepName = step?.Name;
        CurrentStepPermission = step?.Permission;
        CurrentStepAssigneeUserId = step?.AssigneeUserId;
        CurrentStepEnteredAt = step is null ? null : now;
        CurrentStepDueAt = step?.SlaHours is { } h ? now.AddHours(h) : null;
    }
}

/// <summary>Append-only transition record.</summary>
public sealed class WorkflowHistoryEntry : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public int Sequence { get; set; }
    public int Round { get; set; }
    public HistoryAction Action { get; set; }
    public InstanceStatus? FromStatus { get; set; }
    public InstanceStatus ToStatus { get; set; }
    public int? StepOrder { get; set; }
    public string? StepName { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Comment { get; set; }
    /// <summary>Client-supplied id making a retried command idempotent (unique per instance).</summary>
    public string? RequestId { get; set; }
    public DateTime OccurredAt { get; set; }
}

/// <summary>Integration event written in the same transaction as the transition; delivered at-least-once to the subject module.</summary>
[NotAudited]
public sealed class WorkflowOutboxMessage : TenantEntity
{
    public Guid InstanceId { get; set; }
    public string SubjectModule { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? DeliveredAt { get; set; }
    /// <summary>Exception type name of the last failure (never a message: messages may carry personal data).</summary>
    public string? LastError { get; set; }
}
