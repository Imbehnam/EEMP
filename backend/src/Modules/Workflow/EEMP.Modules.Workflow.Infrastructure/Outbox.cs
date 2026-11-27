using System.Text.Json;
using EEMP.Modules.Workflow.Application;
using EEMP.Modules.Workflow.Contracts;
using EEMP.Modules.Workflow.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EEMP.Modules.Workflow.Infrastructure;

public sealed class WorkflowOutboxOptions
{
    public const string SectionName = "Workflow:Outbox";
    /// <summary>Background retry worker (inline delivery after each transition always runs).</summary>
    public bool WorkerEnabled { get; set; } = true;
    public int PollSeconds { get; set; } = 15;
    public int MaxAttempts { get; set; } = 10;
    public int BatchSize { get; set; } = 50;
}

/// <summary>
/// At-least-once delivery of workflow outbox messages. Each message is claimed with an optimistic lock (rowversion + lease),
/// delivered in a fresh DI scope inside its tenant with no user, and marked delivered only after every handler succeeded.
/// Failures back off exponentially and become <c>failed</c> after <see cref="WorkflowOutboxOptions.MaxAttempts"/>. Only the
/// exception type is recorded or logged (exception messages may contain personal data).
/// </summary>
public sealed class WorkflowOutboxDispatcher(IServiceScopeFactory scopes, TimeProvider clock, IOptions<WorkflowOutboxOptions> options,
    ILogger<WorkflowOutboxDispatcher> logger) : IWorkflowOutboxDispatcher
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<int> DispatchAsync(Guid? instanceId = null, CancellationToken ct = default)
    {
        try { return await DispatchCoreAsync(instanceId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Inline delivery runs after the transition committed; a failure here must not fail the request. The worker retries.
            logger.LogWarning("Workflow outbox dispatch failed ({Error})", ex.GetType().Name);
            return 0;
        }
    }

    private async Task<int> DispatchCoreAsync(Guid? instanceId, CancellationToken ct)
    {
        List<(Guid Id, Guid TenantId)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var now = clock.GetUtcNow().UtcDateTime;
            var q = db.Outbox.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.Status == OutboxStatus.Pending && m.NextAttemptAt <= now && (m.LockedUntil == null || m.LockedUntil < now));
            if (instanceId is { } iid) q = q.Where(m => m.InstanceId == iid);
            due = (await q.OrderBy(m => m.NextAttemptAt).Take(options.Value.BatchSize).Select(m => new { m.Id, m.TenantId }).ToListAsync(ct))
                .Select(m => (m.Id, m.TenantId)).ToList();
        }

        var delivered = 0;
        foreach (var (id, tenantId) in due)
            if (await DeliverAsync(id, tenantId, ct)) delivered++;
        return delivered;
    }

    private async Task<bool> DeliverAsync(Guid messageId, Guid tenantId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.BeginTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;

        var message = await db.Outbox.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message is null || message.Status != OutboxStatus.Pending || message.NextAttemptAt > now || (message.LockedUntil is { } l && l >= now))
            return false;

        // Claim: a concurrent dispatcher that read the same row fails the rowversion check and skips it.
        message.LockedUntil = now.Add(Lease);
        message.Attempts++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return false; }

        try
        {
            var evt = JsonSerializer.Deserialize<WorkflowEvent>(message.Payload, WorkflowFormat.Json)!;
            var handlers = scope.ServiceProvider.GetServices<IWorkflowEventHandler>().Where(h => h.SubjectModule == message.SubjectModule).ToList();
            if (handlers.Count == 0) throw new InvalidOperationException("no_handler");
            foreach (var handler in handlers) await handler.HandleAsync(evt, ct);

            message.Status = OutboxStatus.Delivered;
            message.DeliveredAt = clock.GetUtcNow().UtcDateTime;
            message.LockedUntil = null;
            message.LastError = null;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = ex is InvalidOperationException { Message: "no_handler" } ? "no_handler" : ex.GetType().Name;
            message.LastError = error.Length > 200 ? error[..200] : error;
            message.LockedUntil = null;
            message.NextAttemptAt = clock.GetUtcNow().UtcDateTime.Add(Backoff(message.Attempts));
            if (message.Attempts >= options.Value.MaxAttempts) message.Status = OutboxStatus.Failed;
            logger.LogWarning("Workflow event {EventId} delivery attempt {Attempt} failed ({Error})", message.Id, message.Attempts, message.LastError);
            try { await db.SaveChangesAsync(CancellationToken.None); }
            catch (DbUpdateException) { /* lease expiry makes it due again */ }
            return false;
        }
    }

    /// <summary>5s, 20s, 45s, … capped at one hour.</summary>
    public static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(3600, 5 * attempts * attempts));
}

/// <summary>Retries outbox messages that inline delivery could not complete (handler failure, crash between commit and delivery).</summary>
public sealed class WorkflowOutboxWorker(IWorkflowOutboxDispatcher dispatcher, IOptions<WorkflowOutboxOptions> options, ILogger<WorkflowOutboxWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled) return;
        var delay = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
            try { await dispatcher.DispatchAsync(null, stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Workflow outbox poll failed ({Error})", ex.GetType().Name);
            }
        }
    }
}
