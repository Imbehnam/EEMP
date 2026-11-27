namespace EEMP.Modules.Workflow.Application;

public sealed record StepRequest(string? Name, string? Permission, Guid? AssigneeUserId, int? SlaHours, bool? AllowReturn);

public sealed record CreateDefinitionRequest(string? Key, string? Name, string? SubjectModule, string? SubjectType,
    bool? RequireDistinctApprovers, List<StepRequest>? Steps);

public sealed record UpdateDefinitionRequest(string? Name, bool? IsActive, string? RowVersion);

public sealed record UpdateVersionRequest(bool? RequireDistinctApprovers, List<StepRequest>? Steps, string? RowVersion);

public sealed record PublishVersionRequest(string? RowVersion);

public sealed record StepDto(int Order, string Name, string Permission, Guid? AssigneeUserId, int? SlaHours, bool AllowReturn);

public sealed record VersionDto(int VersionNo, string Status, bool RequireDistinctApprovers, DateTime? PublishedAt, string RowVersion,
    IReadOnlyList<StepDto> Steps);

public sealed record DefinitionSummaryDto(Guid Id, string Key, string Name, string SubjectModule, string SubjectType, bool IsActive,
    int? PublishedVersion, int LatestVersion, string RowVersion);

public sealed record DefinitionDto(Guid Id, string Key, string Name, string SubjectModule, string SubjectType, bool IsActive,
    int? PublishedVersion, int LatestVersion, string RowVersion, IReadOnlyList<VersionDto> Versions);

public sealed record CurrentStepDto(int Order, string Name, string Permission, Guid? AssigneeUserId, DateTime? DueAt, bool IsOverdue);

public record InstanceSummaryDto(Guid Id, Guid DefinitionId, string DefinitionKey, string DefinitionName, int VersionNo, string SubjectModule,
    string SubjectType, Guid SubjectId, Guid? BranchId, string Title, Guid RequestedByUserId, string Status, CurrentStepDto? CurrentStep,
    DateTime StartedAt, DateTime? CompletedAt, string RowVersion);

public sealed record HistoryDto(int Sequence, int Round, string Action, string? FromStatus, string ToStatus, int? StepOrder, string? StepName,
    Guid? ActorUserId, string? Comment, DateTime OccurredAt);

public sealed record InstanceDetailDto(Guid Id, Guid DefinitionId, string DefinitionKey, string DefinitionName, int VersionNo, string SubjectModule,
    string SubjectType, Guid SubjectId, Guid? BranchId, string Title, Guid RequestedByUserId, string Status, CurrentStepDto? CurrentStep,
    DateTime StartedAt, DateTime? CompletedAt, string RowVersion, int Round, IReadOnlyList<HistoryDto> History, IReadOnlyList<string> AllowedActions)
    : InstanceSummaryDto(Id, DefinitionId, DefinitionKey, DefinitionName, VersionNo, SubjectModule, SubjectType, SubjectId, BranchId, Title,
        RequestedByUserId, Status, CurrentStep, StartedAt, CompletedAt, RowVersion);

/// <summary>Body of approve/reject/return/resubmit/cancel. <c>requestId</c> makes a retried command idempotent.</summary>
public sealed record WorkflowActionRequest(string? RowVersion, string? Comment, string? RequestId);

public sealed record InstanceListQuery(string? Scope, string? Status, string? DefinitionKey, string? SubjectModule, string? SubjectType, Guid? SubjectId);

public sealed record InboxQuery(string? DefinitionKey, bool? Overdue);
