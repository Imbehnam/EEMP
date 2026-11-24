namespace EEMP.Infrastructure.Persistence;

/// <summary>Implemented by every module DbContext so the central tenant query filter can read the current tenant per query.</summary>
public interface ITenantFilteredContext
{
    Guid? CurrentTenantId { get; }
}
