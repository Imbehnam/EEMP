using EEMP.Infrastructure.Tenancy;
using EEMP.Modules.Finance.Application;
using EEMP.Modules.Finance.Domain;
using EEMP.Modules.Finance.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EEMP.UnitTests;

public class MoneyTests
{
    [Theory]
    [InlineData("IRR", 1234.5, 1235)]
    [InlineData("IRR", -1234.5, -1235)]
    [InlineData("IRR", 1234.49, 1234)]
    [InlineData("USD", 10.005, 10.01)]
    [InlineData("KWD", 1.2345, 1.235)]
    public void Rounds_to_the_currency_minor_unit_half_away_from_zero(string currency, double input, double expected) =>
        Assert.Equal((decimal)expected, Money.Round((decimal)input, currency));

    [Fact]
    public void Validates_scale_per_currency()
    {
        Assert.True(Money.HasValidScale(1_000_000m, "IRR"));
        Assert.False(Money.HasValidScale(10.5m, "IRR"));
        Assert.True(Money.HasValidScale(10.55m, "TRY"));
        Assert.False(Money.HasValidScale(10.555m, "TRY"));
    }

    [Fact]
    public void Formats_gap_free_document_numbers() => Assert.Equal("R-000042", DocumentNumbers.Format(DocumentNumbers.Receipt, 42));

    [Fact]
    public void Results_blocked_code_matches_between_domain_and_contract() =>
        Assert.Equal(FinanceErrors.ResultsBlocked, EEMP.Modules.Finance.Contracts.FinanceStandingErrors.ResultsBlocked);
}

public class InvoiceMathTests
{
    private static Invoice Draft(params (decimal Price, int Qty, bool Taxable)[] lines)
    {
        var inv = new Invoice { Currency = "IRR", Status = InvoiceStatus.Draft };
        foreach (var (p, q, t) in lines) inv.Lines.Add(new InvoiceLine { UnitPrice = p, Quantity = q, Taxable = t });
        return inv;
    }

    private static InvoiceDiscount Discount(DiscountType type, decimal value, ApprovalStatus status, int order) =>
        new() { Type = type, Value = value, Status = status, RequestedAt = new DateTime(2026, 1, 1, 0, 0, order, DateTimeKind.Utc) };

    [Fact]
    public void Only_approved_discounts_count_and_are_capped_by_the_subtotal()
    {
        var inv = Draft((1_000_000m, 2, false), (333_333m, 1, false));
        inv.Discounts.Add(Discount(DiscountType.Percent, 10m, ApprovalStatus.Approved, 1));
        inv.Discounts.Add(Discount(DiscountType.Fixed, 500_000m, ApprovalStatus.Pending, 2));
        inv.Discounts.Add(Discount(DiscountType.Fixed, 5_000_000m, ApprovalStatus.Approved, 3));
        InvoiceCalculator.Recalculate(inv);
        Assert.Equal(2_333_333m, inv.Subtotal);
        Assert.Equal(233_333m, inv.Discounts[0].Amount); // 10% rounded
        Assert.Equal(0m, inv.Discounts[1].Amount);       // pending
        Assert.Equal(2_100_000m, inv.Discounts[2].Amount); // capped
        Assert.Equal(inv.Subtotal, inv.DiscountTotal);
        Assert.Equal(0m, inv.Total);
    }

    [Fact]
    public void Vat_applies_to_taxable_lines_after_their_share_of_discounts()
    {
        var inv = Draft((1_000_000m, 1, true), (1_000_000m, 1, false));
        inv.TaxRatePercent = 10m;
        inv.Discounts.Add(Discount(DiscountType.Fixed, 200_000m, ApprovalStatus.Approved, 1));
        InvoiceCalculator.Recalculate(inv);
        Assert.Equal(90_000m, inv.TaxTotal); // (1,000,000 − 100,000) × 10%
        Assert.Equal(1_890_000m, inv.Total);
    }

    [Fact]
    public void Payment_status_follows_the_balance()
    {
        var inv = Draft((1_000m, 1, false));
        InvoiceCalculator.Recalculate(inv);
        inv.Status = InvoiceStatus.Issued;
        inv.PaidAmount = 400m;
        InvoiceCalculator.UpdatePaymentStatus(inv);
        Assert.Equal(InvoiceStatus.PartiallyPaid, inv.Status);
        inv.CreditTotal = 600m;
        InvoiceCalculator.UpdatePaymentStatus(inv);
        Assert.Equal(InvoiceStatus.Paid, inv.Status);
        inv.LateFeeTotal = 50m;
        InvoiceCalculator.UpdatePaymentStatus(inv);
        Assert.Equal(InvoiceStatus.PartiallyPaid, inv.Status);
        Assert.Equal(50m, inv.Balance);
    }

    [Fact]
    public void Overdue_uses_installments_when_present()
    {
        var today = new DateOnly(2026, 11, 26);
        var inv = Draft((1_000m, 1, false));
        InvoiceCalculator.Recalculate(inv);
        inv.Status = InvoiceStatus.Issued;
        inv.DueDate = today.AddDays(30);
        Assert.False(InvoiceCalculator.IsOverdue(inv, today));
        inv.Installments.Add(new Installment { DueDate = today.AddDays(-1), Amount = 500m });
        inv.Installments.Add(new Installment { DueDate = today.AddDays(30), Amount = 500m });
        Assert.True(InvoiceCalculator.IsOverdue(inv, today));
        inv.Installments[0].PaidAmount = 500m;
        inv.PaidAmount = 500m;
        Assert.False(InvoiceCalculator.IsOverdue(inv, today));
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft, true, true, false, false, false)]
    [InlineData(InvoiceStatus.Issued, false, false, true, true, true)]
    [InlineData(InvoiceStatus.PartiallyPaid, false, false, true, true, true)]
    [InlineData(InvoiceStatus.Paid, false, false, true, false, false)]
    [InlineData(InvoiceStatus.Void, false, false, false, false, false)]
    public void State_machine(InvoiceStatus s, bool edit, bool issue, bool voidable, bool credit, bool pay)
    {
        Assert.Equal(edit, InvoiceStateMachine.CanEdit(s));
        Assert.Equal(issue, InvoiceStateMachine.CanIssue(s));
        Assert.Equal(voidable, InvoiceStateMachine.CanVoid(s));
        Assert.Equal(credit, InvoiceStateMachine.CanCredit(s));
        Assert.Equal(pay, InvoiceStateMachine.CanReceivePayment(s));
    }
}

public class InstallmentAndAllocationTests
{
    [Fact]
    public void Equal_split_puts_the_remainder_on_the_last_installment()
    {
        var plan = InstallmentPlanner.EqualSplit(10_000_001m, 3, new DateOnly(2026, 1, 1), 30, "IRR");
        Assert.Equal([3_333_333m, 3_333_333m, 3_333_335m], plan.Select(p => p.Amount));
        Assert.Equal(new DateOnly(2026, 3, 2), plan[2].DueDate);
        Assert.Equal(10_000_001m, plan.Sum(p => p.Amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallmentPlanner.EqualSplit(1m, 0, default, 1, "IRR"));
    }

    [Fact]
    public void Allocates_by_due_date_across_invoices_and_keeps_the_rest_as_credit()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), i1 = Guid.NewGuid(), i2 = Guid.NewGuid();
        var targets = new[]
        {
            new AllocationTarget(a, 1_000m, [(i2, new DateOnly(2026, 3, 1), 500m), (i1, new DateOnly(2026, 2, 1), 500m)]),
            new AllocationTarget(b, 300m, []),
        };
        var (lines, rest) = Allocator.Allocate(1_500m, targets);
        Assert.Equal(200m, rest);
        Assert.Equal([(a, (Guid?)i1, 500m), (a, i2, 500m), (b, null, 300m)], lines.Select(l => (l.InvoiceId, l.InstallmentId, l.Amount)));
    }

    [Fact]
    public void Preferred_installment_first_and_never_more_than_the_invoice_balance()
    {
        Guid a = Guid.NewGuid(), i1 = Guid.NewGuid(), i2 = Guid.NewGuid();
        // A credit note reduced the balance below the installments' remaining total.
        var target = new AllocationTarget(a, 700m, [(i1, new DateOnly(2026, 2, 1), 500m), (i2, new DateOnly(2026, 3, 1), 500m)]);
        var (lines, rest) = Allocator.Allocate(1_000m, [target], preferredInstallmentId: i2);
        Assert.Equal(300m, rest);
        Assert.Equal([(i2, 500m), (i1, 200m)], lines.Select(l => (l.InstallmentId!.Value, l.Amount)));
    }

    [Fact]
    public void Late_fees_beyond_installments_are_allocated_to_the_invoice()
    {
        var a = Guid.NewGuid();
        var i1 = Guid.NewGuid();
        var (lines, rest) = Allocator.Allocate(600m, [new AllocationTarget(a, 550m, [(i1, new DateOnly(2026, 2, 1), 500m)])]);
        Assert.Equal(50m, rest);
        Assert.Equal([((Guid?)i1, 500m), (null, 50m)], lines.Select(l => (l.InstallmentId, l.Amount)));
    }
}

/// <summary>DL-060 numbering on a real (SQLite) finance context with the rowversion token.</summary>
#pragma warning disable CA1001 // disposed in DisposeAsync
public sealed class ReceiptNumberingTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");

    private FinanceDbContext Context(Guid tenantId)
    {
        var tenant = new TenantContext();
        tenant.Initialize(tenantId, Guid.NewGuid(), false);
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite(_conn).AddInterceptors(new RowVersionStamp())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.SqliteEventId.SchemaConfiguredWarning)).Options;
        return new FinanceDbContext(options, tenant);
    }

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        await using var db = Context(Guid.NewGuid());
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task Numbers_are_sequential_per_tenant_and_series()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        await using (var db = Context(t1))
        {
            var n = new DocumentNumberer(db);
            Assert.Equal("R-000001", await n.NextAsync(DocumentNumbers.Receipt, default));
            Assert.Equal("R-000002", await n.NextAsync(DocumentNumbers.Receipt, default));
            Assert.Equal("INV-000001", await n.NextAsync(DocumentNumbers.Invoice, default));
            await db.SaveChangesAsync();
        }
        await using (var db = Context(t1))
            Assert.Equal("R-000003", await new DocumentNumberer(db).NextAsync(DocumentNumbers.Receipt, default));
        await using (var db = Context(t2))
            Assert.Equal("R-000001", await new DocumentNumberer(db).NextAsync(DocumentNumbers.Receipt, default));
    }

    [Fact]
    public async Task A_lost_race_conflicts_instead_of_duplicating_a_number_and_an_unsaved_number_is_not_consumed()
    {
        var t = Guid.NewGuid();
        await using (var seed = Context(t))
        {
            await new DocumentNumberer(seed).NextAsync(DocumentNumbers.Receipt, default);
            await seed.SaveChangesAsync();
        }
        await using var a = Context(t);
        await using var b = Context(t);
        Assert.Equal("R-000002", await new DocumentNumberer(a).NextAsync(DocumentNumbers.Receipt, default));
        Assert.Equal("R-000002", await new DocumentNumberer(b).NextAsync(DocumentNumbers.Receipt, default));
        await a.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
        // b's transaction rolled back; the next number continues from a's commit with no gap.
        await using var c = Context(t);
        Assert.Equal("R-000003", await new DocumentNumberer(c).NextAsync(DocumentNumbers.Receipt, default));
    }

    private sealed class RowVersionStamp : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            foreach (var e in eventData.Context!.ChangeTracker.Entries<EEMP.SharedKernel.Domain.IHasRowVersion>())
            {
                if (e.State == EntityState.Added) e.Entity.RowVersion = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
                if (e.State == EntityState.Modified) e.Property(x => x.RowVersion).CurrentValue = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
            }
            foreach (var e in eventData.Context.ChangeTracker.Entries<EEMP.SharedKernel.Domain.ITenantOwned>().Where(e => e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty))
                e.Entity.TenantId = ((FinanceDbContext)eventData.Context).CurrentTenantId!.Value;
            return ValueTask.FromResult(result);
        }
    }
}
