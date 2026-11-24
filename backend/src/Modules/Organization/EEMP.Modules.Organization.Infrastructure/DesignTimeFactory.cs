using EEMP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace EEMP.Modules.Organization.Infrastructure;

/// <summary>Used by dotnet-ef only (see EEMP.Infrastructure.Persistence.DesignTime).</summary>
internal sealed class OrganizationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrganizationDbContext>
{
    public OrganizationDbContext CreateDbContext(string[] args) => new(DesignTime.Options<OrganizationDbContext>(OrganizationDbContext.SchemaName), DesignTime.NoTenant());
}
