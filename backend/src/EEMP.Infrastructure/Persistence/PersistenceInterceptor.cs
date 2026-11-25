using System.Reflection;
using System.Security.Cryptography;
using EEMP.Infrastructure.Audit;
using EEMP.SharedKernel.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EEMP.Infrastructure.Persistence;

/// <summary>
/// Save pipeline shared by every module DbContext:
///  1. stamps TenantId on new tenant-owned rows from the server tenant context and rejects cross-tenant writes;
///  2. maintains UTC timestamps;
///  3. emulates rowversion on SQLite (tests only);
///  4. records audit events (property names only, never values) after a successful save.
/// </summary>
public sealed class PersistenceInterceptor(
    ITenantContext tenantContext,
    TimeProvider clock,
    IAuditWriter auditWriter,
    ILogger<PersistenceInterceptor> logger) : SaveChangesInterceptor
{
    private readonly Dictionary<DbContext, List<(EntityEntry Entry, string Action, string[] Fields)>> _pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Prepare(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Prepare(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) await FlushAuditAsync(eventData.Context, cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not null) FlushAuditAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null) _pending.Remove(eventData.Context);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) _pending.Remove(eventData.Context);
        return Task.CompletedTask;
    }

    private void Prepare(DbContext context)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var isSqlite = context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
        var audit = new List<(EntityEntry, string, string[])>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;

            if (entry.Entity is ITenantOwned owned)
            {
                var current = tenantContext.TenantId
                    ?? throw new InvalidOperationException("Tenant-owned data cannot be written without a tenant context.");
                if (entry.State == EntityState.Added)
                {
                    if (owned.TenantId == Guid.Empty) owned.TenantId = current;
                    else if (owned.TenantId != current) throw new InvalidOperationException("Cross-tenant write rejected.");
                }
                else
                {
                    var original = (Guid)entry.Property(nameof(ITenantOwned.TenantId)).OriginalValue!;
                    if (original != current || owned.TenantId != current) throw new InvalidOperationException("Cross-tenant write rejected.");
                }
            }

            if (entry.Entity is IOptionalTenantOwned optional && optional.TenantId != tenantContext.TenantId)
                throw new InvalidOperationException("Cross-tenant write rejected.");

            if (entry.Entity is IHasTimestamps ts)
            {
                if (entry.State == EntityState.Added) ts.CreatedAt = now;
                else if (entry.State == EntityState.Modified) ts.UpdatedAt = now;
            }

            if (isSqlite && entry.Entity is IHasRowVersion rv && entry.State is EntityState.Added or EntityState.Modified)
                rv.RowVersion = RandomNumberGenerator.GetBytes(8);

            if (entry.Entity.GetType().GetCustomAttribute<NotAuditedAttribute>() is null)
            {
                var action = entry.State switch
                {
                    EntityState.Added => "create",
                    EntityState.Deleted => "delete",
                    _ => "update",
                };
                var fields = entry.State == EntityState.Modified
                    ? entry.Properties.Where(p => p.IsModified && p.Metadata.Name is not nameof(IHasRowVersion.RowVersion) and not nameof(IHasTimestamps.UpdatedAt))
                        .Select(p => p.Metadata.Name).ToArray()
                    : [];
                if (entry.State != EntityState.Modified || fields.Length > 0) audit.Add((entry, action, fields));
            }
        }

        if (audit.Count > 0) _pending[context] = audit;
    }

    private async Task FlushAuditAsync(DbContext context, CancellationToken ct)
    {
        if (!_pending.Remove(context, out var items)) return;
        try
        {
            var events = items.Select(i =>
            {
                var key = i.Entry.Metadata.FindPrimaryKey();
                var id = key is null ? null : string.Join(",", key.Properties.Select(p => i.Entry.Property(p.Name).CurrentValue?.ToString()));
                var tenantId = i.Entry.Entity switch { ITenantOwned o => o.TenantId, IOptionalTenantOwned oo => oo.TenantId, _ => tenantContext.TenantId };
                return new AuditEntry(i.Action, i.Entry.Metadata.ClrType.Name, id, tenantId, i.Fields);
            }).ToList();
            await auditWriter.WriteAsync(events, ct);
        }
        catch (Exception ex)
        {
            // Audit failure must be visible to operators; the business write already committed.
            logger.LogError(ex, "Failed to persist audit events for {Context}", context.GetType().Name);
            throw;
        }
    }
}
