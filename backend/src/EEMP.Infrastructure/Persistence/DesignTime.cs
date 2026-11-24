using EEMP.Infrastructure.Audit;
using EEMP.Infrastructure.Tenancy;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EEMP.Infrastructure.Persistence;

/// <summary>
/// Design-time (dotnet-ef) options: SQL Server provider, migrations in the context's own assembly and a per-module
/// history table (&lt;schema&gt;.__EFMigrationsHistory). The connection string is only used when applying from the CLI;
/// override with EEMP_MIGRATIONS_CONNECTION.
/// </summary>
public static class DesignTime
{
    public static DbContextOptions<TContext> Options<TContext>(string schema) where TContext : DbContext
    {
        var connection = Environment.GetEnvironmentVariable("EEMP_MIGRATIONS_CONNECTION")
            ?? "Server=localhost,1433;Database=eemp;User Id=sa;Password=design-time-only;TrustServerCertificate=True";
        var builder = new DbContextOptionsBuilder<TContext>();
        DatabaseServiceCollectionExtensions.ConfigureProvider(builder,
            new DatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = connection },
            typeof(TContext).Assembly.GetName().Name!, schema);
        return builder.Options;
    }

    public static ITenantContext NoTenant() => new TenantContext();
}

internal sealed class AuditDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) => new(DesignTime.Options<AuditDbContext>(AuditDbContext.SchemaName));
}
