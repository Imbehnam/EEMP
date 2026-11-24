using EEMP.SharedKernel.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Infrastructure.Persistence;

/// <summary>
/// Stamps the server tenant id on new tenant-owned entities before EF starts tracking them, so tenant-composite keys
/// (TenantId, Id) are correct from the start. The save interceptor still rejects any cross-tenant write.
/// </summary>
public static class TenantStamping
{
    public static void Attach(DbContext context, ITenantContext tenant)
    {
        context.ChangeTracker.Tracking += (_, e) =>
        {
            if (e.FromQuery || e.State != EntityState.Added) return;
            if (e.Entry.Entity is ITenantOwned owned && owned.TenantId == Guid.Empty && tenant.TenantId is { } current)
                owned.TenantId = current;
        };
    }
}
