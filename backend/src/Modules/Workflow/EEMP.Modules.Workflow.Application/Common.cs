using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using EEMP.Modules.Workflow.Contracts;
using EEMP.Modules.Workflow.Domain;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Workflow.Application;

public interface IWorkflowDbContext
{
    DbSet<WorkflowDefinition> Definitions { get; }
    DbSet<WorkflowDefinitionVersion> Versions { get; }
    DbSet<WorkflowStepDefinition> Steps { get; }
    DbSet<WorkflowInstance> Instances { get; }
    DbSet<WorkflowHistoryEntry> History { get; }
    DbSet<WorkflowOutboxMessage> Outbox { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class;
    void ResetTracking();
}

/// <summary>Delivers pending outbox messages to the subject modules' <see cref="IWorkflowEventHandler"/>s.</summary>
public interface IWorkflowOutboxDispatcher
{
    /// <summary>Delivers due messages (all tenants, or only one instance's). Returns the number delivered. Never throws for handler failures.</summary>
    Task<int> DispatchAsync(Guid? instanceId = null, CancellationToken ct = default);
}

public static partial class WorkflowFormat
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Camel<TEnum>(TEnum value) where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    public static string? Camel<TEnum>(TEnum? value) where TEnum : struct, Enum => value is { } v ? Camel(v) : null;
    public static string Rv(byte[] rv) => Convert.ToBase64String(rv);
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Identifier format for definition keys, subject modules and subject types.</summary>
    public static bool IsIdentifier(string? s) => s is not null && IdentifierRegex().IsMatch(s);

    [GeneratedRegex("^[a-z][a-z0-9_.-]{1,49}$")]
    private static partial Regex IdentifierRegex();

    public static Dictionary<string, object?> P(params (string Key, object? Value)[] items) => items.ToDictionary(i => i.Key, i => i.Value);
}

internal static class PredicateExtensions
{
    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var p = left.Parameters[0];
        var body = new Replace(right.Parameters[0], p).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, body), p);
    }

    private sealed class Replace(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

/// <summary>Applies a state-machine outcome to an instance: history row, current step, and the outbox event in the same unit of work.</summary>
internal static class Transitions
{
    public static async Task<WorkflowHistoryEntry> ApplyAsync(IWorkflowDbContext db, WorkflowInstance instance, WorkflowMachine.Outcome outcome,
        IReadOnlyList<WorkflowStepDefinition> steps, Guid? actorUserId, string? comment, string? requestId, DateTime now, CancellationToken ct)
    {
        var from = instance.Status;
        var actedStep = instance.CurrentStepOrder;
        var actedStepName = instance.CurrentStepName;
        var last = await db.History.Where(h => h.InstanceId == instance.Id).MaxAsync(h => (int?)h.Sequence, ct) ?? 0;

        if (outcome.Action == HistoryAction.Resubmitted) instance.Round++;
        instance.Status = outcome.Status;
        instance.EnterStep(outcome.StepOrder is { } o ? steps.First(s => s.Order == o) : null, now);
        if (WorkflowMachine.IsTerminal(outcome.Status)) instance.CompletedAt = now;

        var entry = new WorkflowHistoryEntry
        {
            InstanceId = instance.Id,
            Sequence = last + 1,
            Round = instance.Round,
            Action = outcome.Action,
            FromStatus = from,
            ToStatus = outcome.Status,
            StepOrder = actedStep,
            StepName = actedStepName,
            ActorUserId = actorUserId,
            Comment = comment,
            RequestId = requestId,
            OccurredAt = now,
        };
        db.History.Add(entry);

        if (WorkflowMachine.IsTerminal(outcome.Status) || outcome.Status == InstanceStatus.Returned)
            AddEvent(db, instance, outcome.Status == InstanceStatus.Returned ? WorkflowEventTypes.Returned : WorkflowEventTypes.Completed,
                WorkflowFormat.Camel(outcome.Status), actorUserId, now);
        return entry;
    }

    private static void AddEvent(IWorkflowDbContext db, WorkflowInstance i, string eventType, string outcome, Guid? actor, DateTime now)
    {
        var message = new WorkflowOutboxMessage { InstanceId = i.Id, SubjectModule = i.SubjectModule, EventType = eventType, NextAttemptAt = now };
        var evt = new WorkflowEvent(message.Id, i.TenantId, i.Id, i.DefinitionKey, i.VersionNo, i.SubjectModule, i.SubjectType, i.SubjectId,
            i.BranchId, eventType, outcome, actor, i.RequestedByUserId, now);
        message.Payload = JsonSerializer.Serialize(evt, WorkflowFormat.Json);
        db.Outbox.Add(message);
    }

    public static WorkflowInstanceInfo Info(WorkflowInstance i) => new(i.Id, i.DefinitionKey, i.VersionNo, i.SubjectModule, i.SubjectType,
        i.SubjectId, i.BranchId, WorkflowFormat.Camel(i.Status), i.CurrentStepOrder, i.CurrentStepDueAt, i.RequestedByUserId, i.StartedAt, i.CompletedAt);
}
