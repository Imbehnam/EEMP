using EEMP.Infrastructure.Persistence;
using EEMP.Modules.Tenancy.Application;
using EEMP.Modules.Tenancy.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EEMP.Modules.Tenancy.Infrastructure;

public sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), ITenancyDbContext
{
    public const string SchemaName = "tenancy";
    protected override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("Tenants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Slug).HasMaxLength(63).IsRequired();
            b.HasIndex(x => x.Slug).IsUnique();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.DefaultLocale).HasMaxLength(10).IsRequired();
            b.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.SupportedLocales)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    new ValueComparer<List<string>>((a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode(StringComparison.Ordinal))), v => v.ToList()))
                .HasMaxLength(100);
        });
    }
}
