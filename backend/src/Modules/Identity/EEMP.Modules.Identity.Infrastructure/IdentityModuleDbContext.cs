using EEMP.Infrastructure.Persistence;
using EEMP.Modules.Identity.Application;
using EEMP.Modules.Identity.Domain;
using EEMP.SharedKernel.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EEMP.Modules.Identity.Infrastructure;

public sealed class IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options, ITenantContext tenantContext)
    : IdentityUserContext<AppUser, Guid>(options), IIdentityDbContext, ITenantFilteredContext
{
    public const string SchemaName = "identity";

    public Guid? CurrentTenantId => tenantContext.TenantId;

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class =>
        Entry(entity).Property(nameof(IHasRowVersion.RowVersion)).OriginalValue = rowVersion;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(b =>
        {
            b.ToTable("Users");
            // Identities are isolated per tenant: replace the global unique user-name index with a tenant-scoped one.
            var globalIndex = b.Metadata.GetIndexes().FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(AppUser.NormalizedUserName));
            if (globalIndex is not null) b.Metadata.RemoveIndex(globalIndex);
            b.HasIndex(u => new { u.TenantId, u.NormalizedUserName }).IsUnique();
            b.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(u => u.Locale).HasMaxLength(10).IsRequired();
            b.Property(u => u.TimeZone).HasMaxLength(64).IsRequired();
            b.Property(u => u.PhoneNumber).HasMaxLength(30);
            b.HasQueryFilter((System.Linq.Expressions.Expression<Func<AppUser, bool>>)
                ModelConventions.BuildNullableTenantFilter(typeof(AppUser), nameof(AppUser.TenantId), this));
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

        builder.Entity<Role>(b =>
        {
            b.ToTable("Roles");
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.Property(x => x.Code).HasMaxLength(50).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasMany(x => x.Permissions).WithOne()
                .HasForeignKey(p => new { p.TenantId, p.RoleId }).HasPrincipalKey(r => new { r.TenantId, r.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<RolePermission>(b =>
        {
            b.ToTable("RolePermissions");
            b.HasKey(x => new { x.TenantId, x.RoleId, x.PermissionCode });
            b.Property(x => x.PermissionCode).HasMaxLength(100).IsRequired();
        });
        builder.Entity<RoleAssignment>(b =>
        {
            b.ToTable("RoleAssignments");
            b.HasKey(x => x.Id);
            b.HasOne(x => x.Role).WithMany()
                .HasForeignKey(x => new { x.TenantId, x.RoleId }).HasPrincipalKey(r => new { r.TenantId, r.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.UserId });
            b.HasIndex(x => new { x.TenantId, x.BranchId });
        });
        builder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.FamilyId);
            b.HasIndex(x => x.UserId);
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        ModelConventions.Apply(builder, this);
    }
}
