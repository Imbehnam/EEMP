using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Infrastructure.Persistence;

/// <summary>Base for module DbContexts: one schema per module, central tenant filters and conventions.</summary>
public abstract class ModuleDbContext : DbContext, ITenantFilteredContext
{
    protected ModuleDbContext(DbContextOptions options, ITenantContext tenantContext) : base(options)
    {
        TenantContext = tenantContext;
        TenantStamping.Attach(this, tenantContext);
    }

    protected ITenantContext TenantContext { get; }

    public Guid? CurrentTenantId => TenantContext.TenantId;

    protected abstract string Schema { get; }

    /// <summary>Sets the client-supplied rowversion as the original value so a stale update raises a concurrency conflict.</summary>
    public void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class =>
        Entry(entity).Property(nameof(EEMP.SharedKernel.Domain.IHasRowVersion.RowVersion)).OriginalValue = rowVersion;

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureModel(modelBuilder);
        ModelConventions.Apply(modelBuilder, this);
    }
}
