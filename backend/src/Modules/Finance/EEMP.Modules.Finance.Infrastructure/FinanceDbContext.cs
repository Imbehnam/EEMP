using EEMP.Infrastructure.Persistence;
using EEMP.Modules.Finance.Application;
using EEMP.Modules.Finance.Domain;
using EEMP.SharedKernel.Domain;
using EEMP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMP.Modules.Finance.Infrastructure;

/// <summary>
/// Finance persistence (schema "finance"). Tenant-owned tables with tenant-leading indexes; intra-module references are
/// tenant-composite (TenantId, Id). Money columns are decimal(19,4) with an explicit currency (DL-059). Audit rows are append-only.
/// </summary>
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IFinanceDbContext
{
    public const string SchemaName = "finance";
    protected override string Schema => SchemaName;

    public DbSet<FinanceSettings> Settings => Set<FinanceSettings>();
    public DbSet<FeeType> FeeTypes => Set<FeeType>();
    public DbSet<FeeItem> FeeItems => Set<FeeItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<InvoiceDiscount> Discounts => Set<InvoiceDiscount>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<InvoiceAdjustment> Adjustments => Set<InvoiceAdjustment>();
    public DbSet<GeneratedCharge> GeneratedCharges => Set<GeneratedCharge>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> Allocations => Set<PaymentAllocation>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    public DbSet<FinanceAuditEntry> Audit => Set<FinanceAuditEntry>();

    public void ResetTracking() => ChangeTracker.Clear();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>DL-063: the finance audit is append-only.</summary>
    private void GuardAudit()
    {
        if (ChangeTracker.Entries<FinanceAuditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Finance audit entries are append-only.");
    }

    private const string MoneyType = "decimal(19,4)";
    private const string RateType = "decimal(5,2)";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FinanceSettings>(b =>
        {
            Root(b, "Settings");
            b.HasIndex(x => x.TenantId).IsUnique().HasDatabaseName("UX_Settings_Tenant");
            b.Property(x => x.VatRatePercent).HasColumnType(RateType);
            b.Property(x => x.SiblingDiscountPercent).HasColumnType(RateType);
            b.Property(x => x.LateFeeValue).HasColumnType(MoneyType);
            b.Property(x => x.LateFeeType).HasConversion<string>().HasMaxLength(20);
        });
        modelBuilder.Entity<FeeType>(b =>
        {
            Root(b, "FeeTypes");
            b.Property(x => x.Code).HasMaxLength(30).IsRequired();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        modelBuilder.Entity<FeeItem>(b =>
        {
            Root(b, "FeeItems");
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            Money(b.Property(x => x.Amount));
            Currency(b.Property(x => x.Currency));
            Ref<FeeItem, FeeType>(b, x => x.FeeTypeId);
            b.HasIndex(x => new { x.TenantId, x.TermId });
        });
        modelBuilder.Entity<Invoice>(b =>
        {
            Root(b, "Invoices");
            b.Property(x => x.Number).HasMaxLength(30);
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[Number] IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.StudentId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.BranchId, x.Status });
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            Currency(b.Property(x => x.Currency));
            foreach (var p in new[] { nameof(Invoice.Subtotal), nameof(Invoice.DiscountTotal), nameof(Invoice.TaxTotal), nameof(Invoice.Total),
                         nameof(Invoice.LateFeeTotal), nameof(Invoice.CreditTotal), nameof(Invoice.PaidAmount) })
                b.Property<decimal>(p).HasColumnType(MoneyType);
            b.Property(x => x.TaxRatePercent).HasColumnType(RateType);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.Property(x => x.VoidReason).HasMaxLength(500);
            b.Ignore(x => x.Balance);
            b.Ignore(x => x.IsOpen);
            Children(b, x => x.Lines);
            Children(b, x => x.Discounts);
            Children(b, x => x.Installments);
            Children(b, x => x.Adjustments);
        });
        modelBuilder.Entity<InvoiceLine>(b =>
        {
            b.ToTable("InvoiceLines");
            b.HasKey(x => x.Id);
            b.Property(x => x.Description).HasMaxLength(200).IsRequired();
            Money(b.Property(x => x.UnitPrice));
            Money(b.Property(x => x.Amount));
        });
        modelBuilder.Entity<InvoiceDiscount>(b =>
        {
            b.ToTable("InvoiceDiscounts");
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            Money(b.Property(x => x.Value));
            Money(b.Property(x => x.Amount));
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.Property(x => x.DecisionNote).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.Status });
        });
        modelBuilder.Entity<Installment>(b =>
        {
            b.ToTable("Installments");
            b.HasKey(x => x.Id);
            Money(b.Property(x => x.Amount));
            Money(b.Property(x => x.PaidAmount));
            b.Ignore(x => x.Remaining);
            b.HasIndex(x => new { x.TenantId, x.InvoiceId, x.SeqNo }).IsUnique();
        });
        modelBuilder.Entity<InvoiceAdjustment>(b =>
        {
            b.ToTable("InvoiceAdjustments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Number).HasMaxLength(30);
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("[Number] IS NOT NULL");
            Money(b.Property(x => x.Amount));
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        });
        modelBuilder.Entity<GeneratedCharge>(b =>
        {
            b.ToTable("GeneratedCharges");
            b.HasKey(x => new { x.TenantId, x.EnrollmentId, x.FeeItemId });
            b.HasIndex(x => new { x.TenantId, x.InvoiceId });
            Ref<GeneratedCharge, Invoice>(b, x => x.InvoiceId);
            Ref<GeneratedCharge, FeeItem>(b, x => x.FeeItemId);
        });
        modelBuilder.Entity<Payment>(b =>
        {
            Root(b, "Payments");
            b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ChequeStatus).HasConversion<string>().HasMaxLength(20);
            Money(b.Property(x => x.Amount));
            Money(b.Property(x => x.Allocated));
            Money(b.Property(x => x.Refunded));
            Currency(b.Property(x => x.Currency));
            b.Property(x => x.Reference).HasMaxLength(100);
            b.Property(x => x.ChequeNumber).HasMaxLength(50);
            b.Property(x => x.ChequeBank).HasMaxLength(100);
            b.Property(x => x.Provider).HasMaxLength(30);
            b.Property(x => x.ProviderAuthority).HasMaxLength(100);
            b.Property(x => x.ProviderRef).HasMaxLength(100);
            b.Property(x => x.IdempotencyKey).HasMaxLength(100);
            b.Property(x => x.ReceiptNumber).HasMaxLength(30);
            b.Property(x => x.FailureCode).HasMaxLength(60);
            b.Ignore(x => x.Unallocated);
            b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.ReceiptNumber }).IsUnique().HasFilter("[ReceiptNumber] IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.Provider, x.ProviderAuthority }).IsUnique().HasFilter("[ProviderAuthority] IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.StudentId });
            b.HasIndex(x => new { x.TenantId, x.BranchId, x.ReceivedOn });
            b.HasMany(x => x.Allocations).WithOne().HasForeignKey(nameof(PaymentAllocation.TenantId), nameof(PaymentAllocation.PaymentId))
                .HasPrincipalKey(nameof(TenantEntity.TenantId), nameof(TenantEntity.Id)).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PaymentAllocation>(b =>
        {
            b.ToTable("PaymentAllocations");
            b.HasKey(x => x.Id);
            Money(b.Property(x => x.Amount));
            b.HasIndex(x => new { x.TenantId, x.InvoiceId });
            Ref<PaymentAllocation, Invoice>(b, x => x.InvoiceId);
        });
        modelBuilder.Entity<Refund>(b =>
        {
            Root(b, "Refunds");
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            Money(b.Property(x => x.Amount));
            Currency(b.Property(x => x.Currency));
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.Property(x => x.DecisionNote).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.Status });
            Ref<Refund, Payment>(b, x => x.PaymentId);
        });
        modelBuilder.Entity<NumberSeries>(b =>
        {
            b.ToTable("NumberSeries");
            b.HasKey(x => new { x.TenantId, x.Series });
            b.Property(x => x.Series).HasMaxLength(10);
        });
        modelBuilder.Entity<FinanceAuditEntry>(b =>
        {
            b.ToTable("FinanceAudit");
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(60).IsRequired();
            b.Property(x => x.EntityType).HasMaxLength(30).IsRequired();
            b.Property(x => x.Amount).HasColumnType(MoneyType);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Details).HasMaxLength(400);
            b.HasIndex(x => new { x.TenantId, x.At });
            b.HasIndex(x => new { x.TenantId, x.InvoiceId });
            b.HasIndex(x => new { x.TenantId, x.PaymentId });
        });

        // Ids are client-generated (Guid.NewGuid): new children added to a tracked invoice/payment must be inserted, not updated.
        foreach (var e in modelBuilder.Model.GetEntityTypes())
            if (e.FindProperty("Id") is { } id && id.ClrType == typeof(Guid)) id.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }

    private static void Money(PropertyBuilder<decimal> p) => p.HasColumnType(MoneyType);
    private static void Currency(PropertyBuilder<string> p) => p.HasMaxLength(3).IsFixedLength().IsRequired();

    private static void Root<T>(EntityTypeBuilder<T> b, string table) where T : TenantEntity
    {
        b.ToTable(table);
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
    }

    /// <summary>Pure children of an invoice: tenant-composite FK, cascade with the invoice.</summary>
    private static void Children<TChild>(EntityTypeBuilder<Invoice> b, System.Linq.Expressions.Expression<Func<Invoice, IEnumerable<TChild>?>> nav)
        where TChild : class =>
        b.HasMany(nav).WithOne().HasForeignKey(nameof(TenantEntity.TenantId), "InvoiceId")
            .HasPrincipalKey(nameof(TenantEntity.TenantId), nameof(TenantEntity.Id)).OnDelete(DeleteBehavior.Cascade);

    /// <summary>Tenant-composite, non-cascading reference to another finance aggregate.</summary>
    private static void Ref<T, TPrincipal>(EntityTypeBuilder<T> b, System.Linq.Expressions.Expression<Func<T, Guid>> fk)
        where T : class where TPrincipal : TenantEntity
    {
        var name = ((System.Linq.Expressions.MemberExpression)fk.Body).Member.Name;
        b.HasOne<TPrincipal>().WithMany().HasForeignKey(nameof(TenantEntity.TenantId), name)
            .HasPrincipalKey(nameof(TenantEntity.TenantId), nameof(TenantEntity.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Used by dotnet-ef only (see EEMP.Infrastructure.Persistence.DesignTime).</summary>
internal sealed class FinanceDbContextDesignTimeFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args) => new(DesignTime.Options<FinanceDbContext>(FinanceDbContext.SchemaName), DesignTime.NoTenant());
}
