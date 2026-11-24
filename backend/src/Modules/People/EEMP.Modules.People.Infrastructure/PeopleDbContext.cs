using EEMP.Infrastructure.Persistence;
using EEMP.Modules.People.Application;
using EEMP.Modules.People.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMP.Modules.People.Infrastructure;

public sealed class PeopleDbContext(DbContextOptions<PeopleDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IPeopleDbContext
{
    public const string SchemaName = "people";
    protected override string Schema => SchemaName;

    public DbSet<Student> Students => Set<Student>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<StaffMember> Staff => Set<StaffMember>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Student>(b =>
        {
            ConfigurePerson(b, "Students");
            b.Property(x => x.StudentNumber).HasMaxLength(30).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.StudentNumber }).IsUnique();
            b.HasMany(x => x.Guardians).WithOne()
                .HasForeignKey(x => new { x.TenantId, x.StudentId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Guardian>(b => ConfigurePerson(b, "Guardians"));
        modelBuilder.Entity<Teacher>(b =>
        {
            ConfigurePerson(b, "Teachers");
            b.Property(x => x.EmployeeNumber).HasMaxLength(30).IsRequired();
            b.Property(x => x.Subjects).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.EmployeeNumber }).IsUnique();
        });
        modelBuilder.Entity<StaffMember>(b =>
        {
            ConfigurePerson(b, "Staff");
            b.Property(x => x.EmployeeNumber).HasMaxLength(30).IsRequired();
            b.Property(x => x.Position).HasMaxLength(100).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.EmployeeNumber }).IsUnique();
        });
        modelBuilder.Entity<StudentGuardian>(b =>
        {
            b.ToTable("StudentGuardians");
            b.HasKey(x => new { x.TenantId, x.StudentId, x.GuardianId });
            b.Property(x => x.Relationship).HasConversion<string>().HasMaxLength(30);
            // Tenant-composite FK: a link can never join a student and a guardian from different tenants.
            b.HasOne<Guardian>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.GuardianId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.GuardianId });
        });
    }

    private static void ConfigurePerson<T>(EntityTypeBuilder<T> b, string table) where T : PersonProfile
    {
        b.ToTable(table);
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        b.Property(x => x.NationalId).HasMaxLength(20);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Gender).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(x => new { x.TenantId, x.BranchId });
        // One profile of each kind per user account within a tenant.
        b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
        b.HasIndex(x => new { x.TenantId, x.LastName, x.FirstName });
    }
}
