namespace EEMP.Modules.Workflow.Contracts;

/// <summary>
/// Starts and inspects approval workflows for subjects owned by other modules (in-process; current tenant only).
/// The calling module authorizes the surrounding operation and validates its own subject before starting an instance.
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// Starts an instance of the tenant's published definition <see cref="StartWorkflowCommand.DefinitionKey"/>.
    /// The requester is the authenticated user; <see cref="StartWorkflowCommand.RequestedByUserId"/> is only used without one (system jobs).
    /// Idempotent on (SubjectModule, IdempotencyKey): a retry returns the instance created by the first call.
    /// </summary>
    Task<WorkflowInstanceInfo> StartAsync(StartWorkflowCommand command, CancellationToken ct = default);

    Task<WorkflowInstanceInfo?> FindAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>The pending or returned instance for a subject, if any.</summary>
    Task<WorkflowInstanceInfo?> FindActiveAsync(string subjectModule, string subjectType, Guid subjectId, CancellationToken ct = default);

    /// <summary>System cancel by the module that owns the subject (e.g. the subject was deleted). No-op for finished instances.</summary>
    Task CancelAsync(Guid instanceId, string subjectModule, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Acts on an instance of <paramref name="subjectModule"/> as the authenticated user, with exactly the checks of the inbox endpoints
    /// (visibility, current-step permission in the instance branch, maker-checker, distinct approvers, comment rules) — DL-082.
    /// Lets the owning module keep thin wrapper endpoints. <paramref name="action"/>: approve, reject, return, resubmit or cancel.
    /// </summary>
    Task<WorkflowInstanceInfo> ActAsync(Guid instanceId, string subjectModule, string action, string? comment, CancellationToken ct = default);

    /// <summary>
    /// Creates and publishes version 1 of <paramref name="template"/> when the tenant has no definition with its key (DL-083).
    /// An existing definition is left untouched, so tenant administrators keep their own versions. Returns true when created.
    /// </summary>
    Task<bool> EnsureDefinitionAsync(WorkflowDefinitionTemplate template, CancellationToken ct = default);
}

/// <summary>A module's default approval definition (single or multi-step), provisioned per tenant on first use.</summary>
public sealed record WorkflowDefinitionTemplate(
    string Key,
    string Name,
    string SubjectModule,
    string SubjectType,
    IReadOnlyList<WorkflowStepTemplate> Steps,
    bool RequireDistinctApprovers = true);

public sealed record WorkflowStepTemplate(string Name, string Permission, int? SlaHours, bool AllowReturn = true);

public sealed record StartWorkflowCommand(
    string DefinitionKey,
    string SubjectModule,
    string SubjectType,
    Guid SubjectId,
    Guid? BranchId,
    string Title,
    string IdempotencyKey,
    Guid? RequestedByUserId = null);

/// <summary>Status values: pending, returned, approved, rejected, cancelled.</summary>
public sealed record WorkflowInstanceInfo(
    Guid Id,
    string DefinitionKey,
    int VersionNo,
    string SubjectModule,
    string SubjectType,
    Guid SubjectId,
    Guid? BranchId,
    string Status,
    int? CurrentStepOrder,
    DateTime? CurrentStepDueAt,
    Guid RequestedByUserId,
    DateTime StartedAt,
    DateTime? CompletedAt);

/// <summary>
/// Receives workflow events for subjects of <see cref="SubjectModule"/>. Delivery is at-least-once, after the transition committed,
/// in a fresh scope inside the event's tenant with no user: implementations must be idempotent (key on <see cref="WorkflowEvent.EventId"/>
/// or guard the state change) and ignore instances they did not start. Throwing leaves the event pending for retry.
/// </summary>
public interface IWorkflowEventHandler
{
    string SubjectModule { get; }
    Task HandleAsync(WorkflowEvent workflowEvent, CancellationToken ct);
}

/// <summary>
/// Optional rule a subject-owning module registers so that only suitable approvers can be configured for its subjects (DL-083):
/// a version of a definition for <see cref="SubjectModule"/> can only be published when every step permission is allowed.
/// </summary>
public interface IWorkflowSubjectPolicy
{
    string SubjectModule { get; }
    /// <summary>Permissions a step may require for <paramref name="subjectType"/>; an empty list allows any permission.</summary>
    IReadOnlyCollection<string> AllowedStepPermissions(string subjectType);
}

public static class WorkflowEventTypes
{
    public const string Completed = "completed";
    public const string Returned = "returned";
    /// <summary>A returned instance was resubmitted by its requester and is pending again (DL-084). Handlers may ignore it.</summary>
    public const string Resubmitted = "resubmitted";
}

public static class WorkflowOutcomes
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Returned = "returned";
}

public sealed record WorkflowEvent(
    Guid EventId,
    Guid TenantId,
    Guid InstanceId,
    string DefinitionKey,
    int VersionNo,
    string SubjectModule,
    string SubjectType,
    Guid SubjectId,
    Guid? BranchId,
    string EventType,
    string Outcome,
    Guid? ActorUserId,
    Guid RequestedByUserId,
    DateTime OccurredAt,
    /// <summary>The actor's comment on the transition (DL-084), e.g. the approver's note; null when none was given.</summary>
    string? Comment = null);
