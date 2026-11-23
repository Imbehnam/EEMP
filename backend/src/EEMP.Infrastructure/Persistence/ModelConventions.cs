using System.Linq.Expressions;
using EEMP.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Infrastructure.Persistence;

/// <summary>
/// Central model conventions shared by all module DbContexts:
/// tenant query filters (fail closed), tenant-leading indexes, rowversion tokens.
/// </summary>
public static class ModelConventions
{
    public static void Apply(ModelBuilder modelBuilder, DbContext context)
    {
        var isSqlite = context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null && !t.IsOwned()))
        {
            var clr = entityType.ClrType;

            if (typeof(ITenantOwned).IsAssignableFrom(clr))
            {
                modelBuilder.Entity(clr).HasQueryFilter(BuildTenantFilter(clr, context));
                modelBuilder.Entity(clr).HasIndex(nameof(ITenantOwned.TenantId));
            }

            if (typeof(IHasRowVersion).IsAssignableFrom(clr))
            {
                var prop = modelBuilder.Entity(clr).Property(nameof(IHasRowVersion.RowVersion));
                if (isSqlite)
                    prop.IsConcurrencyToken().ValueGeneratedNever(); // emulated by PersistenceInterceptor
                else
                    prop.IsRowVersion();
            }
        }
    }

    /// <summary>e => ctx.CurrentTenantId != null &amp;&amp; e.TenantId == ctx.CurrentTenantId. No tenant context ⇒ zero rows.</summary>
    public static LambdaExpression BuildTenantFilter(Type entityType, DbContext context)
    {
        var e = Expression.Parameter(entityType, "e");
        var ctx = Expression.Constant(context, context.GetType());
        var current = Expression.Property(ctx, nameof(ITenantFilteredContext.CurrentTenantId));
        var tenantId = Expression.Convert(Expression.Property(e, nameof(ITenantOwned.TenantId)), typeof(Guid?));
        var body = Expression.AndAlso(
            Expression.NotEqual(current, Expression.Constant(null, typeof(Guid?))),
            Expression.Equal(tenantId, current));
        return Expression.Lambda(body, e);
    }

    /// <summary>Same filter for entities whose TenantId is nullable (e.g. user accounts where platform admins have none).</summary>
    public static LambdaExpression BuildNullableTenantFilter(Type entityType, string propertyName, DbContext context)
    {
        var e = Expression.Parameter(entityType, "e");
        var ctx = Expression.Constant(context, context.GetType());
        var current = Expression.Property(ctx, nameof(ITenantFilteredContext.CurrentTenantId));
        var tenantId = Expression.Property(e, propertyName);
        var body = Expression.AndAlso(
            Expression.NotEqual(current, Expression.Constant(null, typeof(Guid?))),
            Expression.Equal(tenantId, current));
        return Expression.Lambda(body, e);
    }
}
