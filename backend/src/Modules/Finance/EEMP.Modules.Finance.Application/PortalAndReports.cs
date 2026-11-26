using System.Globalization;
using System.Text;
using EEMP.Modules.Finance.Contracts;
using EEMP.Modules.Finance.Domain;
using EEMP.Modules.Organization.Contracts;
using EEMP.Modules.People.Contracts;
using EEMP.SharedKernel.Authorization;
using EEMP.SharedKernel.Errors;
using EEMP.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;
using static EEMP.Modules.Finance.Application.Fmt;

namespace EEMP.Modules.Finance.Application;

/// <summary>Student / linked guardian views. Drafts are never visible; anything not own/linked is 404.</summary>
public sealed class PortalFinanceService(IFinanceDbContext db, FinanceActor actor, InvoiceMapper invoices, PaymentMapper payments,
    PaymentService accounts, IPeopleDirectory people)
{
    private async Task EnsureStudentAsync(Guid studentId, CancellationToken ct)
    {
        if (!(await actor.PortalStudentIdsAsync(ct)).Contains(studentId)) throw new NotFoundException();
    }

    public async Task<StudentAccountDto> AccountAsync(Guid studentId, CancellationToken ct)
    {
        await EnsureStudentAsync(studentId, ct);
        var student = (await people.GetStudentsAsync([studentId], ct)).GetValueOrDefault(studentId) ?? throw new NotFoundException();
        var inv = await db.Invoices.AsNoTracking().Include(i => i.Installments).Where(i => i.StudentId == studentId && i.Status != InvoiceStatus.Draft).ToListAsync(ct);
        var pay = await db.Payments.AsNoTracking().Include(p => p.Allocations).Where(p => p.StudentId == studentId).ToListAsync(ct);
        return await accounts.BuildAccountAsync(student, inv, pay, includeDrafts: false, ct);
    }

    public async Task<Invoice> LoadInvoiceAsync(Guid id, CancellationToken ct)
    {
        var inv = await InvoiceMapper.WithDetails(db.Invoices.AsNoTracking()).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException();
        if (inv.Status == InvoiceStatus.Draft) throw new NotFoundException();
        await EnsureStudentAsync(inv.StudentId, ct);
        return inv;
    }

    public async Task<InvoiceDto> InvoiceAsync(Guid id, CancellationToken ct)
    {
        var dto = await invoices.ToDtoAsync(await LoadInvoiceAsync(id, ct), await actor.TodayAsync(ct), ct);
        // Maker/checker identities are staff data: not shown in the portal.
        return dto with { Discounts = dto.Discounts.Where(d => d.Status == "approved").Select(d => d with { RequestedBy = null, DecidedBy = null, DecisionNote = null, Reason = "" }).ToList() };
    }

    public async Task<Payment> LoadPaymentAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Payments.AsNoTracking().Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        await EnsureStudentAsync(p.StudentId, ct);
        return p;
    }

    public async Task<PaymentDto> PaymentAsync(Guid id, CancellationToken ct) => await payments.ToDtoAsync(await LoadPaymentAsync(id, ct), ct);
}

public sealed class FinanceReportService(IFinanceDbContext db, FinanceActor actor, FinanceAccess access, StudentNames names)
{
    private async Task<BranchScope> ScopeAsync(Guid? branchId, CancellationToken ct)
    {
        var scope = await access.ScopeAsync(Permissions.FinanceReports, ct);
        if (scope.IsEmpty) throw new ForbiddenException();
        if (branchId is { } b && !scope.Includes(b)) throw new ForbiddenException(ErrorCodes.ForbiddenScope);
        return scope;
    }

    private static string Name(IReadOnlyDictionary<Guid, StudentSummary> s, Guid id) => s.GetValueOrDefault(id) is { } x ? $"{x.FirstName} {x.LastName}".Trim() : "";

    private async Task<List<Invoice>> OpenInvoicesAsync(Guid? branchId, Guid? termId, CancellationToken ct)
    {
        var scope = await ScopeAsync(branchId, ct);
        var q = db.Invoices.AsNoTracking().Include(i => i.Installments)
            .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid);
        if (branchId is { } b) q = q.Where(i => i.BranchId == b);
        if (termId is { } t) q = q.Where(i => i.TermId == t);
        return (await q.ToListAsync(ct)).Where(i => scope.Includes(i.BranchId) && i.Balance > 0).ToList();
    }

    public async Task<IReadOnlyList<OutstandingRow>> OutstandingAsync(Guid? branchId, Guid? termId, CancellationToken ct)
    {
        var rows = await OpenInvoicesAsync(branchId, termId, ct);
        var today = await actor.TodayAsync(ct);
        var s = await names.GetAsync(rows.Select(r => r.StudentId), ct);
        return rows.OrderBy(r => r.DueDate).ThenBy(r => r.Number, StringComparer.Ordinal)
            .Select(r => new OutstandingRow(r.Id, r.Number, r.StudentId, Name(s, r.StudentId), s.GetValueOrDefault(r.StudentId)?.StudentNumber ?? "", r.BranchId,
                Date(r.DueDate), r.Total + r.LateFeeTotal - r.CreditTotal, r.PaidAmount, r.Balance, InvoiceCalculator.IsOverdue(r, today))).ToList();
    }

    public async Task<AgingReport> AgingAsync(string? asOfText, Guid? branchId, CancellationToken ct)
    {
        var v = new Validator();
        var asOf = OptionalDate(asOfText, "asOf", v) ?? await actor.TodayAsync(ct);
        v.ThrowIfInvalid();
        var rows = await OpenInvoicesAsync(branchId, null, ct);
        var s = await names.GetAsync(rows.Select(r => r.StudentId), ct);
        var perStudent = new Dictionary<Guid, decimal[]>();
        var branchOf = new Dictionary<Guid, Guid>();
        foreach (var inv in rows)
        {
            if (!perStudent.TryGetValue(inv.StudentId, out var b)) perStudent[inv.StudentId] = b = new decimal[5];
            branchOf[inv.StudentId] = inv.BranchId;
            // Age each unpaid installment (or the whole invoice) by its due date; late fees / credits follow the invoice due date.
            var parts = inv.Installments.Count > 0
                ? inv.Installments.Where(i => i.Remaining > 0).Select(i => (i.DueDate, i.Remaining)).ToList()
                : [(inv.DueDate ?? asOf, inv.Total - inv.PaidAmount)];
            var partsTotal = parts.Sum(p => p.Item2);
            var adjust = inv.Balance - partsTotal; // late fees − credit notes not tied to an installment
            if (adjust != 0) parts.Add((inv.DueDate ?? asOf, adjust));
            foreach (var (due, amount) in parts)
            {
                var days = asOf.DayNumber - due.DayNumber;
                var idx = days <= 0 ? 0 : days <= 30 ? 1 : days <= 60 ? 2 : days <= 90 ? 3 : 4;
                b[idx] += amount;
            }
        }
        static AgingBuckets Of(decimal[] b) => new(b[0], b[1], b[2], b[3], b[4], b.Sum());
        var total = new decimal[5];
        foreach (var b in perStudent.Values) for (var i = 0; i < 5; i++) total[i] += b[i];
        return new AgingReport(Date(asOf), await actor.CurrencyAsync(ct), Of(total),
            perStudent.OrderByDescending(p => p.Value.Sum()).Select(p => new AgingRow(p.Key, Name(s, p.Key), s.GetValueOrDefault(p.Key)?.StudentNumber ?? "",
                branchOf[p.Key], Of(p.Value))).ToList());
    }

    public async Task<IReadOnlyList<CollectionRow>> CollectionsAsync(string? fromText, string? toText, Guid? branchId, CancellationToken ct)
    {
        var v = new Validator();
        var today = await actor.TodayAsync(ct);
        var from = OptionalDate(fromText, "from", v) ?? today.AddDays(-30);
        var to = OptionalDate(toText, "to", v) ?? today;
        v.When(to < from || to.DayNumber - from.DayNumber > 366, "to", Validator.InvalidValue);
        v.ThrowIfInvalid();
        var scope = await ScopeAsync(branchId, ct);
        var q = db.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Succeeded && p.ReceivedOn >= from && p.ReceivedOn <= to);
        if (branchId is { } b) q = q.Where(p => p.BranchId == b);
        var rows = (await q.Select(p => new { p.ReceivedOn, p.BranchId, p.Method, p.Amount, p.Refunded }).ToListAsync(ct)).Where(p => scope.Includes(p.BranchId));
        return rows.GroupBy(p => (p.ReceivedOn, p.BranchId, p.Method))
            .OrderBy(g => g.Key.ReceivedOn).ThenBy(g => g.Key.BranchId).ThenBy(g => g.Key.Method)
            .Select(g => new CollectionRow(Date(g.Key.ReceivedOn), g.Key.BranchId, Camel(g.Key.Method), g.Count(), g.Sum(x => x.Amount))).ToList();
    }
}

/// <summary>RFC 4180 CSV with a UTF-8 BOM (Excel opens Persian text correctly). Cells starting with = + - @ are quoted with a leading ' (formula injection).</summary>
public static class Csv
{
    public static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(',', header.Select(Cell)).Append("\r\n");
        foreach (var r in rows) sb.AppendJoin(',', r.Select(c => Cell(c switch
        {
            null => "",
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => c.ToString() ?? "",
        }))).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Cell(string s)
    {
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) s = "'" + s;
        return s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : s;
    }
}

// PDF documents ----------------------------------------------------------------------------------------------------------

public sealed record FinancePdfHeader(string InstituteName, string BranchName, string TimeZoneId);
public sealed record InvoiceDocument(FinancePdfHeader Header, InvoiceDto Invoice);
public sealed record ReceiptDocument(FinancePdfHeader Header, PaymentDto Payment, string StudentNumber);

public interface IFinancePdfRenderer
{
    byte[] RenderInvoice(InvoiceDocument document, string language, DateTime renderedAtUtc);
    byte[] RenderReceipt(ReceiptDocument document, string language, DateTime renderedAtUtc);
}

/// <summary>Loads invoice/receipt documents with the same authorization as the JSON reads (admin branch scope or portal relationship).</summary>
public sealed class FinanceDocuments(IFinanceDbContext db, FinanceActor actor, FinanceAccess access, InvoiceMapper invoices, PaymentMapper payments,
    PortalFinanceService portal, IOrganizationSettings org, IBranchDirectory branches, StudentNames names)
{
    private async Task<FinancePdfHeader> HeaderAsync(Guid branchId, CancellationToken ct) =>
        new(await org.GetInstituteNameAsync(ct), await branches.GetNameAsync(branchId, ct) ?? "", await org.GetTimeZoneAsync(ct));

    public async Task<InvoiceDocument> InvoiceAsync(Guid id, bool portalView, CancellationToken ct)
    {
        Invoice inv;
        if (portalView) inv = await portal.LoadInvoiceAsync(id, ct);
        else
        {
            inv = await InvoiceMapper.WithDetails(db.Invoices.AsNoTracking()).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException();
            await access.EnsureAsync(inv.BranchId, Permissions.FinanceRead, ct);
        }
        if (inv.Status == InvoiceStatus.Draft) throw new BusinessRuleException(FinanceErrors.InvoiceNotIssued);
        var dto = await invoices.ToDtoAsync(inv, await actor.TodayAsync(ct), ct);
        dto = dto with { Discounts = dto.Discounts.Where(d => d.Status == "approved").ToList() };
        return new InvoiceDocument(await HeaderAsync(inv.BranchId, ct), dto);
    }

    public async Task<ReceiptDocument> ReceiptAsync(Guid paymentId, bool portalView, CancellationToken ct)
    {
        Payment p;
        if (portalView) p = await portal.LoadPaymentAsync(paymentId, ct);
        else
        {
            p = await db.Payments.AsNoTracking().Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == paymentId, ct) ?? throw new NotFoundException();
            await access.EnsureAsync(p.BranchId, Permissions.FinanceRead, ct);
        }
        if (p.Status != PaymentStatus.Succeeded || p.ReceiptNumber is null) throw new BusinessRuleException(FinanceErrors.ReceiptUnavailable);
        var student = (await names.GetAsync([p.StudentId], ct)).GetValueOrDefault(p.StudentId);
        return new ReceiptDocument(await HeaderAsync(p.BranchId, ct), await payments.ToDtoAsync(p, ct), student?.StudentNumber ?? "");
    }
}

/// <summary>IFinanceStanding / IFinanceUsage for other modules (DL-065).</summary>
public sealed class FinanceStanding(IFinanceDbContext db, FinanceActor actor) : IFinanceStanding, IFinanceUsage
{
    public async Task<bool> AreResultsBlockedAsync(Guid studentId, CancellationToken ct = default)
    {
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || !settings.BlockResultsOnOverdue) return false;
        var today = await actor.TodayAsync(ct);
        var open = await db.Invoices.AsNoTracking().Include(i => i.Installments)
            .Where(i => i.StudentId == studentId && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid)).ToListAsync(ct);
        return open.Any(i => InvoiceCalculator.IsOverdue(i, today));
    }

    public Task<bool> IsStudentReferencedAsync(Guid studentId, CancellationToken ct = default) =>
        db.Invoices.AnyAsync(i => i.StudentId == studentId, ct);

    public async Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default) =>
        await db.Invoices.AnyAsync(i => i.BranchId == branchId, ct) || await db.Payments.AnyAsync(p => p.BranchId == branchId, ct)
        || await db.FeeItems.AnyAsync(f => f.BranchId == branchId, ct);
}
