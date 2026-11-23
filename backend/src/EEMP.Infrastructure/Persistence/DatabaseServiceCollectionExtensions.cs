using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EEMP.Infrastructure.Persistence;

public sealed record ModuleDbContextRegistration(Type ContextType, string Schema);

public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema, bool intercept = true)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            ConfigureProvider(options, db, typeof(TContext).Assembly.GetName().Name!, schema);
            if (intercept) options.AddInterceptors(sp.GetRequiredService<PersistenceInterceptor>());
        });
        services.AddSingleton(new ModuleDbContextRegistration(typeof(TContext), schema));
        return services;
    }

    public static void ConfigureProvider(DbContextOptionsBuilder options, DatabaseOptions db, string migrationsAssembly, string schema)
    {
        if (string.IsNullOrWhiteSpace(db.ConnectionString))
            throw new InvalidOperationException("Database:ConnectionString is not configured.");

        switch (db.Provider)
        {
            case DatabaseProvider.SqlServer:
                options.UseSqlServer(db.ConnectionString, sql =>
                {
                    sql.MigrationsAssembly(migrationsAssembly);
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
                    sql.EnableRetryOnFailure(3);
                });
                break;
            case DatabaseProvider.Sqlite:
                options.UseSqlite(db.ConnectionString);
                break;
            default:
                throw new InvalidOperationException($"Unsupported provider {db.Provider}.");
        }
    }

    /// <summary>Applies migrations (SQL Server) or creates tables from the model (SQLite test fallback) for every module.</summary>
    public static async Task InitializeDatabasesAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("EEMP.Database");
        await using var scope = services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.BeginTenantScope(null);

        foreach (var reg in scope.ServiceProvider.GetServices<ModuleDbContextRegistration>())
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(reg.ContextType);
            if (db.Provider == DatabaseProvider.SqlServer)
            {
                if (!db.ApplyMigrationsOnStartup) continue;
                logger.LogInformation("Applying migrations for {Context}", reg.ContextType.Name);
                await context.Database.MigrateAsync(ct);
            }
            else
            {
                var creator = context.GetService<IRelationalDatabaseCreator>();
                if (!await creator.ExistsAsync(ct)) await creator.CreateAsync(ct);
                var firstTable = context.Model.GetEntityTypes().Select(t => t.GetTableName()).First(t => t is not null);
                var conn = context.Database.GetDbConnection();
                await context.Database.OpenConnectionAsync(ct);
                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "$n";
                    p.Value = firstTable;
                    cmd.Parameters.Add(p);
                    var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
                    if (!exists) await creator.CreateTablesAsync(ct);
                }
                finally
                {
                    await context.Database.CloseConnectionAsync();
                }
            }
        }
    }
}
