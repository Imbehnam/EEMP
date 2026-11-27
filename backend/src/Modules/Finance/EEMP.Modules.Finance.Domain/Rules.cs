namespace EEMP.Modules.Finance.Domain;

public static class FinanceErrors
{
    public const string CurrencyMismatch = "finance.currency_mismatch";
    public const string InvalidState = "finance.invalid_state";
    public const string InvoiceEmpty = "finance.invoice_empty";
    public const string InvoiceNotIssued = "finance.invoice_not_issued";
    public const string DiscountPending = "finance.discount_pending";
    public const string MakerChecker = "finance.maker_checker";
    public const string AmountExceedsBalance = "finance.amount_exceeds_balance";
    public const string InstallmentsMismatch = "finance.installments_mismatch";
    public const string InstallmentsLocked = "finance.installments_locked";
    public const string RefundExceedsCredit = "finance.refund_exceeds_credit";
    public const string OnlinePaymentsDisabled = "finance.online_payments_disabled";
    public const string GatewayUnavailable = "finance.gateway_unavailable";
    public const string AmountMismatch = "finance.amount_mismatch";
    public const string PaymentFailed = "finance.payment_failed";
    public const string ReceiptUnavailable = "finance.receipt_unavailable";
    public const string FeeTypeInUse = "finance.fee_type_in_use";
    public const string FeeItemInUse = "finance.fee_item_in_use";
    public const string FeeItemInactive = "finance.fee_item_inactive";
    public const string ResultsBlocked = "finance.results_blocked";
    public const string NoCredit = "finance.no_credit";
    /// <summary>Student or branch delete refused: invoices or payments reference it (DL-085). Same value as FinanceStandingErrors.RecordsExist.</summary>
    public const string RecordsExist = "finance.records_exist";
}

/// <summary>Money rules (DL-059): amounts are rounded to the currency's minor unit, half away from zero.</summary>
public static class Money
{
    private static readonly HashSet<string> ZeroDecimals = new(StringComparer.OrdinalIgnoreCase)
        { "IRR", "IRT", "JPY", "KRW", "VND", "CLP", "ISK", "UGX", "XAF", "XOF", "PYG", "RWF" };
    private static readonly HashSet<string> ThreeDecimals = new(StringComparer.OrdinalIgnoreCase)
        { "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND" };

    public static int MinorUnits(string currency) => ZeroDecimals.Contains(currency) ? 0 : ThreeDecimals.Contains(currency) ? 3 : 2;

    public static decimal Round(decimal amount, string currency) => decimal.Round(amount, MinorUnits(currency), MidpointRounding.AwayFromZero);

    public static bool HasValidScale(decimal amount, string currency) => decimal.Round(amount, MinorUnits(currency)) == amount;

    public static decimal Percent(decimal amount, decimal percent, string currency) => Round(amount * percent / 100m, currency);
}

public static class DocumentNumbers
{
    public const string Invoice = "INV";
    public const string Receipt = "R";
    public const string CreditNote = "CN";

    public static string Format(string series, long value) => $"{series}-{value:D6}";
}

/// <summary>Invoice totals. Discounts (approved only, in request order) are taken from the subtotal and capped by what is left.</summary>
public static class InvoiceCalculator
{
    public static void Recalculate(Invoice invoice)
    {
        var c = invoice.Currency;
        foreach (var line in invoice.Lines) line.Amount = Money.Round(line.UnitPrice * line.Quantity, c);
        invoice.Subtotal = invoice.Lines.Sum(l => l.Amount);
        var remaining = invoice.Subtotal;
        foreach (var d in invoice.Discounts.OrderBy(d => d.RequestedAt).ThenBy(d => d.Id))
        {
            if (d.Status != ApprovalStatus.Approved) { d.Amount = 0; continue; }
            var raw = d.Type == DiscountType.Percent ? Money.Percent(invoice.Subtotal, d.Value, c) : Money.Round(d.Value, c);
            d.Amount = Math.Min(raw, remaining);
            remaining -= d.Amount;
        }
        invoice.DiscountTotal = invoice.Subtotal - remaining;
        var taxableBase = invoice.Lines.Where(l => l.Taxable).Sum(l => l.Amount);
        if (invoice.TaxRatePercent > 0 && taxableBase > 0 && invoice.Subtotal > 0)
        {
            var discountShare = Money.Round(invoice.DiscountTotal * taxableBase / invoice.Subtotal, c);
            invoice.TaxTotal = Money.Percent(taxableBase - discountShare, invoice.TaxRatePercent, c);
        }
        else invoice.TaxTotal = 0;
        invoice.Total = invoice.Subtotal - invoice.DiscountTotal + invoice.TaxTotal;
    }

    /// <summary>Issued → PartiallyPaid → Paid as money arrives; Paid → PartiallyPaid/Issued when allocations are released.</summary>
    public static void UpdatePaymentStatus(Invoice invoice)
    {
        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Void) return;
        invoice.Status = invoice.Balance <= 0 ? InvoiceStatus.Paid
            : invoice.PaidAmount > 0 ? InvoiceStatus.PartiallyPaid
            : InvoiceStatus.Issued;
    }

    public static bool IsOverdue(Invoice invoice, DateOnly today)
    {
        if (!invoice.IsOpen || invoice.Balance <= 0) return false;
        if (invoice.Installments.Count > 0) return invoice.Installments.Any(i => i.Remaining > 0 && i.DueDate < today);
        return invoice.DueDate is { } due && due < today;
    }
}

public static class InvoiceStateMachine
{
    public static bool CanEdit(InvoiceStatus s) => s == InvoiceStatus.Draft;
    public static bool CanIssue(InvoiceStatus s) => s == InvoiceStatus.Draft;
    public static bool CanVoid(InvoiceStatus s) => s is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid;
    public static bool CanCredit(InvoiceStatus s) => s is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid;
    public static bool CanReceivePayment(InvoiceStatus s) => s is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid;
}

public static class InstallmentPlanner
{
    /// <summary>Equal split rounded to the currency; the remainder goes to the last installment.</summary>
    public static IReadOnlyList<(DateOnly DueDate, decimal Amount)> EqualSplit(decimal total, int count, DateOnly first, int intervalDays, string currency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var share = decimal.Round(total / count, Money.MinorUnits(currency), MidpointRounding.ToZero);
        var result = new List<(DateOnly, decimal)>();
        for (var i = 0; i < count; i++)
            result.Add((first.AddDays(i * intervalDays), i == count - 1 ? total - share * (count - 1) : share));
        return result;
    }
}

public sealed record AllocationTarget(Guid InvoiceId, decimal Balance, IReadOnlyList<(Guid Id, DateOnly DueDate, decimal Remaining)> Installments);

public sealed record AllocationLine(Guid InvoiceId, Guid? InstallmentId, decimal Amount);

/// <summary>
/// DL-061: allocate a payment to invoices in the given order; inside an invoice to a preferred installment first, then open
/// installments by due date, then the rest of the invoice balance (late fees). Never more than an invoice's balance.
/// </summary>
public static class Allocator
{
    public static (IReadOnlyList<AllocationLine> Lines, decimal Unallocated) Allocate(decimal amount, IEnumerable<AllocationTarget> targets, Guid? preferredInstallmentId = null)
    {
        var lines = new List<AllocationLine>();
        var left = amount;
        foreach (var t in targets)
        {
            if (left <= 0) break;
            var capacity = Math.Min(t.Balance, left);
            if (capacity <= 0) continue;
            var used = 0m;
            var ordered = t.Installments.Where(i => i.Remaining > 0)
                .OrderBy(i => i.Id == preferredInstallmentId ? 0 : 1).ThenBy(i => i.DueDate).ToList();
            foreach (var inst in ordered)
            {
                var take = Math.Min(inst.Remaining, capacity - used);
                if (take <= 0) break;
                lines.Add(new AllocationLine(t.InvoiceId, inst.Id, take));
                used += take;
            }
            if (capacity - used > 0)
            {
                lines.Add(new AllocationLine(t.InvoiceId, null, capacity - used));
                used = capacity;
            }
            left -= used;
        }
        return (lines, left);
    }
}
