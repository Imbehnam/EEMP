using EEMP.Infrastructure.Persistence;
using EEMP.Modules.Consultation.Application;
using EEMP.Modules.Consultation.Domain;
using EEMP.SharedKernel.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMP.Modules.Consultation.Infrastructure;

/// <summary>
/// Consultation module persistence (schema "consultation"): settings, consultants, slots, bookings, confidential notes, support tickets.
/// Every table is tenant-owned with tenant-leading indexes; intra-module foreign keys are tenant-composite (TenantId, Id).
/// Branches, users and students live in other modules and are referenced by id only.
/// </summary>
public sealed class ConsultationDbContext(DbContextOptions<ConsultationDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IConsultationDbContext
{
    public const string SchemaName = "consultation";
    protected override string Schema => SchemaName;

    public DbSet<ConsultationSettings> Settings => Set<ConsultationSettings>();
    public DbSet<Consultant> Consultants => Set<Consultant>();
    public DbSet<ConsultantBranch> ConsultantBranches => Set<ConsultantBranch>();
    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingNote> BookingNotes => Set<BookingNote>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();

    public void ResetTracking() => ChangeTracker.Clear();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConsultationSettings>(b =>
        {
            Root(b, "Settings");
            // One row per tenant.
            b.HasIndex(x => x.TenantId).IsUnique().HasDatabaseName("UX_Settings_TenantId");
        });
        modelBuilder.Entity<Consultant>(b =>
        {
            Root(b, "Consultants");
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Bio).HasMaxLength(1000);
            b.Property(x => x.Specialties).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            b.HasMany(x => x.Branches).WithOne().HasForeignKey(x => new { x.TenantId, x.ConsultantId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ConsultantBranch>(b =>
        {
            b.ToTable("ConsultantBranches");
            b.HasKey(x => new { x.TenantId, x.ConsultantId, x.BranchId });
            b.HasIndex(x => new { x.TenantId, x.BranchId });
        });
        modelBuilder.Entity<Slot>(b =>
        {
            Root(b, "Slots");
            b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Location).HasMaxLength(200);
            b.HasIndex(x => new { x.TenantId, x.ConsultantId, x.StartsAt });
            b.HasIndex(x => new { x.TenantId, x.BranchId, x.StartsAt });
            Ref<Slot, Consultant>(b, x => x.ConsultantId);
        });
        modelBuilder.Entity<Booking>(b =>
        {
            Root(b, "Bookings");
            b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.CancelledBy).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Location).HasMaxLength(200);
            b.Property(x => x.Topic).HasMaxLength(200).IsRequired();
            b.Property(x => x.RequesterNote).HasMaxLength(1000);
            b.Property(x => x.CancelReason).HasMaxLength(500);
            b.Ignore(x => x.IsActive);
            // CNS-003: at most one non-cancelled booking per slot, enforced by the database (atomic reservation).
            b.HasIndex(x => new { x.TenantId, x.SlotId }).IsUnique().HasFilter("[Status] <> 'Cancelled'").HasDatabaseName("UX_Bookings_ActiveSlot");
            b.HasIndex(x => new { x.TenantId, x.BranchId, x.StartsAt });
            b.HasIndex(x => new { x.TenantId, x.ConsultantId, x.StartsAt });
            b.HasIndex(x => new { x.TenantId, x.StudentId, x.Status });
            Ref<Booking, Slot>(b, x => x.SlotId);
            Ref<Booking, Consultant>(b, x => x.ConsultantId);
        });
        modelBuilder.Entity<BookingNote>(b =>
        {
            b.ToTable("BookingNotes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.BookingId, x.CreatedAt });
            b.HasOne<Booking>().WithMany().HasForeignKey(x => new { x.TenantId, x.BookingId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Ticket>(b =>
        {
            Root(b, "Tickets");
            b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            b.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.BranchId, x.Status, x.LastMessageAt });
            b.HasIndex(x => new { x.TenantId, x.CreatedByUserId });
        });
        modelBuilder.Entity<TicketMessage>(b =>
        {
            b.ToTable("TicketMessages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.TicketId, x.CreatedAt });
            b.HasOne<Ticket>().WithMany().HasForeignKey(x => new { x.TenantId, x.TicketId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            entity.FindProperty("Id")?.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }

    private static void Root<T>(EntityTypeBuilder<T> b, string table) where T : TenantEntity
    {
        b.ToTable(table);
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
    }

    private static void Ref<T, TPrincipal>(EntityTypeBuilder<T> b, System.Linq.Expressions.Expression<Func<T, Guid>> fk)
        where T : TenantEntity where TPrincipal : TenantEntity
    {
        var name = ((System.Linq.Expressions.MemberExpression)fk.Body).Member.Name;
        b.HasOne<TPrincipal>().WithMany().HasForeignKey(nameof(TenantEntity.TenantId), name)
            .HasPrincipalKey(nameof(TenantEntity.TenantId), nameof(TenantEntity.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Used by dotnet-ef only (see EEMP.Infrastructure.Persistence.DesignTime).</summary>
internal sealed class ConsultationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ConsultationDbContext>
{
    public ConsultationDbContext CreateDbContext(string[] args) =>
        new(DesignTime.Options<ConsultationDbContext>(ConsultationDbContext.SchemaName), DesignTime.NoTenant());
}
