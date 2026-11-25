using EEMP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace EEMP.Modules.People.Infrastructure;

/// <summary>Used by dotnet-ef only (see EEMP.Infrastructure.Persistence.DesignTime).</summary>
internal sealed class PeopleDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PeopleDbContext>
{
    public PeopleDbContext CreateDbContext(string[] args) => new(DesignTime.Options<PeopleDbContext>(PeopleDbContext.SchemaName), DesignTime.NoTenant());
}
