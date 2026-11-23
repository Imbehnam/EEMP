using EEMP.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Tenancy.Application;

public interface ITenancyDbContext
{
    DbSet<Tenant> Tenants { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class;
}
