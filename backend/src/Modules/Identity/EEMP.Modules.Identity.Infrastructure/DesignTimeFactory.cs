using EEMP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace EEMP.Modules.Identity.Infrastructure;

/// <summary>Used by dotnet-ef only (see EEMP.Infrastructure.Persistence.DesignTime).</summary>
internal sealed class IdentityModuleDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IdentityModuleDbContext>
{
    public IdentityModuleDbContext CreateDbContext(string[] args) => new(DesignTime.Options<IdentityModuleDbContext>(IdentityModuleDbContext.SchemaName), DesignTime.NoTenant());
}
