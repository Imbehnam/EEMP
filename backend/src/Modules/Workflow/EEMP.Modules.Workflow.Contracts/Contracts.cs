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
}

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

public static class WorkflowEventTypes
{
    public const string Completed = "completed";
    public const string Returned = "returned";
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
    DateTime OccurredAt);
