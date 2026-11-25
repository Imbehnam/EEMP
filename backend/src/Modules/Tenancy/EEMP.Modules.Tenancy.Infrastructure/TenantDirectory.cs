using EEMP.Modules.Tenancy.Contracts;
using EEMP.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Tenancy.Infrastructure;

internal sealed class TenantDirectory(TenancyDbContext db) : ITenantDirectory
{
    public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Tenants.AsNoTracking().Where(t => t.Slug == slug).Select(Map).FirstOrDefaultAsync(ct);

    public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Tenants.AsNoTracking().Where(t => t.Id == id).Select(Map).FirstOrDefaultAsync(ct);

    private static readonly System.Linq.Expressions.Expression<Func<Tenant, TenantInfo?>> Map =
        t => new TenantInfo(t.Id, t.Slug, t.Name, t.Status == TenantStatus.Active, t.DefaultLocale, t.TimeZone);
}
