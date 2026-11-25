using EEMP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace EEMP.Modules.Tenancy.Infrastructure;

/// <summary>Used by dotnet-ef only (see EEMP.Infrastructure.Persistence.DesignTime).</summary>
internal sealed class TenancyDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args) => new(DesignTime.Options<TenancyDbContext>(TenancyDbContext.SchemaName), DesignTime.NoTenant());
}
