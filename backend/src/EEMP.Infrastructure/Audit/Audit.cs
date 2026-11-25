using System.Text.Json;
using EEMP.SharedKernel.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EEMP.Infrastructure.Audit;

/// <summary>Append-only audit record. Holds identifiers and changed field names only — never field values or secrets.</summary>
[NotAudited]
public sealed class AuditEvent : IGlobalEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string Result { get; set; } = "success";
    public string? CorrelationId { get; set; }
    public string? Metadata { get; set; }
    public DateTime OccurredAt { get; set; }
}

public sealed record AuditEntry(string Action, string ResourceType, string? ResourceId, Guid? TenantId, string[]? ChangedFields = null, string Result = "success", Guid? ActorOverride = null);

public interface IAuditWriter
{
    Task WriteAsync(IReadOnlyCollection<AuditEntry> entries, CancellationToken ct = default);
}

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string SchemaName = "audit";
    public DbSet<AuditEvent> Events => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.ToTable("AuditEvents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(100).IsRequired();
            b.Property(x => x.ResourceType).HasMaxLength(100).IsRequired();
            b.Property(x => x.ResourceId).HasMaxLength(200);
            b.Property(x => x.Result).HasMaxLength(30).IsRequired();
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.Metadata).HasMaxLength(4000);
            b.HasIndex(x => new { x.TenantId, x.OccurredAt });
        });
    }
}

public sealed class AuditWriter(IServiceProvider services, ITenantContext tenant, IHttpContextAccessor http, TimeProvider clock) : IAuditWriter
{
    // Resolved lazily: the audit context's options depend on the persistence interceptor, which depends on this writer.
    private AuditDbContext Db => services.GetRequiredService<AuditDbContext>();

    public async Task WriteAsync(IReadOnlyCollection<AuditEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0) return;
        var correlationId = http.HttpContext?.Items[CorrelationIdConstants.ItemKey] as string;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var e in entries)
        {
            Db.Events.Add(new AuditEvent
            {
                TenantId = e.TenantId,
                ActorUserId = e.ActorOverride ?? tenant.UserId,
                Action = e.Action,
                ResourceType = e.ResourceType,
                ResourceId = e.ResourceId,
                Result = e.Result,
                CorrelationId = correlationId,
                Metadata = e.ChangedFields is { Length: > 0 } ? JsonSerializer.Serialize(new { changed = e.ChangedFields }) : null,
                OccurredAt = now,
            });
        }
        await Db.SaveChangesAsync(ct);
    }
}

public static class CorrelationIdConstants
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "eemp.correlation_id";
}
