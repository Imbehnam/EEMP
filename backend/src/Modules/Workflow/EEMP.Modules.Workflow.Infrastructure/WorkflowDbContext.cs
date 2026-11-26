using EEMP.Infrastructure.Persistence;
using EEMP.Modules.Workflow.Application;
using EEMP.Modules.Workflow.Domain;
using EEMP.SharedKernel.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMP.Modules.Workflow.Infrastructure;

/// <summary>
/// Workflow module persistence (schema "workflow"): definitions, versions, steps, instances, history and the outbox.
/// Every table is tenant-owned with a tenant-leading index; intra-module foreign keys are tenant-composite (TenantId, Id).
/// Subjects, branches and users live in other modules and are referenced by id only.
/// </summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IWorkflowDbContext
{
    public const string SchemaName = "workflow";
    protected override string Schema => SchemaName;

    public DbSet<WorkflowDefinition> Definitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowDefinitionVersion> Versions => Set<WorkflowDefinitionVersion>();
    public DbSet<WorkflowStepDefinition> Steps => Set<WorkflowStepDefinition>();
    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();
    public DbSet<WorkflowHistoryEntry> History => Set<WorkflowHistoryEntry>();
    public DbSet<WorkflowOutboxMessage> Outbox => Set<WorkflowOutboxMessage>();

    public void ResetTracking() => ChangeTracker.Clear();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowDefinition>(b =>
        {
            Root(b, "Definitions");
            b.Property(x => x.Key).HasMaxLength(50).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.SubjectModule).HasMaxLength(50).IsRequired();
            b.Property(x => x.SubjectType).HasMaxLength(50).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            b.HasMany(x => x.Versions).WithOne().HasForeignKey(x => new { x.TenantId, x.DefinitionId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkflowDefinitionVersion>(b =>
        {
            Root(b, "DefinitionVersions");
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.TenantId, x.DefinitionId, x.VersionNo }).IsUnique();
            b.HasMany(x => x.Steps).WithOne().HasForeignKey(x => new { x.TenantId, x.VersionId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<WorkflowStepDefinition>(b =>
        {
            b.ToTable("DefinitionSteps");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Permission).HasMaxLength(100).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.VersionId, x.Order });
        });
        modelBuilder.Entity<WorkflowInstance>(b =>
        {
            Root(b, "Instances");
            b.Property(x => x.DefinitionKey).HasMaxLength(50).IsRequired();
            b.Property(x => x.SubjectModule).HasMaxLength(50).IsRequired();
            b.Property(x => x.SubjectType).HasMaxLength(50).IsRequired();
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.CurrentStepName).HasMaxLength(200);
            b.Property(x => x.CurrentStepPermission).HasMaxLength(100);
            b.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
            b.Ignore(x => x.IsActive);
            b.HasIndex(x => new { x.TenantId, x.SubjectModule, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Status, x.CurrentStepPermission, x.BranchId });
            b.HasIndex(x => new { x.TenantId, x.SubjectModule, x.SubjectType, x.SubjectId });
            b.HasIndex(x => new { x.TenantId, x.RequestedByUserId });
            Ref<WorkflowInstance, WorkflowDefinition>(b, x => x.DefinitionId);
            Ref<WorkflowInstance, WorkflowDefinitionVersion>(b, x => x.VersionId);
        });
        modelBuilder.Entity<WorkflowHistoryEntry>(b =>
        {
            b.ToTable("InstanceHistory");
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.StepName).HasMaxLength(200);
            b.Property(x => x.Comment).HasMaxLength(2000);
            b.Property(x => x.RequestId).HasMaxLength(64);
            b.HasIndex(x => new { x.TenantId, x.InstanceId, x.Sequence }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.InstanceId, x.RequestId }).IsUnique().HasFilter("[RequestId] IS NOT NULL");
            b.HasOne<WorkflowInstance>().WithMany().HasForeignKey(x => new { x.TenantId, x.InstanceId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkflowOutboxMessage>(b =>
        {
            Root(b, "OutboxMessages");
            b.Property(x => x.SubjectModule).HasMaxLength(50).IsRequired();
            b.Property(x => x.EventType).HasMaxLength(30).IsRequired();
            b.Property(x => x.Payload).HasMaxLength(4000).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.LastError).HasMaxLength(200);
            b.HasIndex(x => new { x.TenantId, x.InstanceId });
            // Cross-tenant scan by the dispatcher (system operation): due pending messages.
            b.HasIndex(x => new { x.Status, x.NextAttemptAt });
            Ref<WorkflowOutboxMessage, WorkflowInstance>(b, x => x.InstanceId);
        });

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            entity.FindProperty("Id")?.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }

    private static void Root<T>(EntityTypeBuilder<T> b, string table) where T : TenantEntity
    {
        b.ToTable(table);
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
    }

    private static void Ref<T, TPrincipal>(EntityTypeBuilder<T> b, System.Linq.Expressions.Expression<Func<T, Guid>> fk)
        where T : TenantEntity where TPrincipal : TenantEntity
    {
        var name = ((System.Linq.Expressions.MemberExpression)fk.Body).Member.Name;
        b.HasOne<TPrincipal>().WithMany().HasForeignKey(nameof(TenantEntity.TenantId), name)
            .HasPrincipalKey(nameof(TenantEntity.TenantId), nameof(TenantEntity.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Used by dotnet-ef only (see EEMP.Infrastructure.Persistence.DesignTime).</summary>
internal sealed class WorkflowDbContextDesignTimeFactory : IDesignTimeDbContextFactory<WorkflowDbContext>
{
    public WorkflowDbContext CreateDbContext(string[] args) => new(DesignTime.Options<WorkflowDbContext>(WorkflowDbContext.SchemaName), DesignTime.NoTenant());
}
