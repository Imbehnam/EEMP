using System.Globalization;
using EEMP.Infrastructure.Localization;
using EEMP.Infrastructure.Pdf;
using EEMP.Modules.Finance.Application;
using EEMP.Modules.Finance.Domain;

namespace EEMP.Modules.Finance.Infrastructure;

/// <summary>
/// Invoice and receipt PDFs (A4, mirrored for fa/ar, Jalali dates for fa) on the shared report-card PDF pipeline (DL-057).
/// Labels come from the server catalogs (<c>finance.pdf.*</c>); amounts and names come from the stored records only.
/// </summary>
public sealed class FinancePdfRenderer(IServerTextLocalizer text) : IFinancePdfRenderer
{
    private const string Prefix = "finance.pdf.";

    private sealed class Ctx(IServerTextLocalizer text, PdfLocale locale)
    {
        public PdfLocale Locale { get; } = locale;
        public string L(string key) => text.Get(Prefix + key, Locale.Culture);
        public string Opt(string group, string value) => text.HasKey(Locale.Language, $"{Prefix}{group}.{value}") ? L($"{group}.{value}") : value;
        public string Money(decimal amount, string currency) =>
            $"{Locale.Digits(amount.ToString("#,0." + new string('#', Domain.Money.MinorUnits(currency)), CultureInfo.InvariantCulture).TrimEnd('.'))} {Opt("currency", currency)}";
        public string Date(string? iso) => iso is not null && DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? Locale.Date(d) : "—";
    }

    private static (PdfFlowDocument Doc, Ctx C) Start(IServerTextLocalizer text, string language, FinancePdfHeader header, string titleKey)
    {
        var locale = PdfLocale.TryResolve(language) ?? throw new ArgumentException("Unsupported language.", nameof(language));
        var c = new Ctx(text, locale);
        var doc = new PdfFlowDocument(PdfFontSet.Shared.Value, locale.Language, locale.Rtl);
        if (header.InstituteName.Length > 0) doc.Paragraph(header.InstituteName, new PdfTextStyle(15f, PdfWeight.Bold), PdfAlign.Center, 0);
        if (header.BranchName.Length > 0) doc.Paragraph(header.BranchName, new PdfTextStyle(10.5f, PdfWeight.Regular, PdfFlowDocument.Muted), PdfAlign.Center, 2);
        doc.Paragraph(c.L(titleKey), new PdfTextStyle(13f, PdfWeight.Bold), PdfAlign.Center, 2);
        doc.HorizontalRule(8f);
        return (doc, c);
    }

    private static void Grid(PdfFlowDocument doc, IReadOnlyList<(string Key, string Value)> info)
    {
        var rows = new List<PdfRow>();
        for (var i = 0; i < info.Count; i += 2)
        {
            var right = i + 1 < info.Count ? info[i + 1] : ("", "");
            rows.Add(new PdfRow([info[i].Key, info[i].Value, right.Item1, right.Item2]));
        }
        doc.Table([new PdfColumn(1.1f), new PdfColumn(1.9f), new PdfColumn(1.1f), new PdfColumn(1.9f)], null, rows, 9f);
        doc.Spacer(10f);
    }

    private static byte[] Finish(PdfFlowDocument doc, Ctx c, string titleKey, DateTime renderedAtUtc, string timeZoneId) =>
        doc.Render(c.L(titleKey), (page, pages) => (c.L("generatedAt").Replace("{at}", c.Locale.DateTime(renderedAtUtc, PdfLocale.Zone(timeZoneId)), StringComparison.Ordinal),
            c.L("page").Replace("{page}", c.Locale.Integer(page), StringComparison.Ordinal).Replace("{pages}", c.Locale.Integer(pages), StringComparison.Ordinal)));

    public byte[] RenderInvoice(InvoiceDocument document, string language, DateTime renderedAtUtc)
    {
        var inv = document.Invoice;
        var (doc, c) = Start(text, language, document.Header, "invoiceTitle");
        if (inv.Status == "void")
            doc.Notice(c.L("voidNotice"), new PdfTextStyle(9.5f, PdfWeight.Bold), PdfFlowDocument.NoticeFill);
        Grid(doc, [
            (c.L("invoiceNumber"), inv.Number ?? "—"), (c.L("status"), c.Opt("status", inv.Status)),
            (c.L("student"), inv.StudentName), (c.L("studentNumber"), inv.StudentNumber),
            (c.L("issueDate"), c.Date(inv.IssueDate)), (c.L("dueDate"), c.Date(inv.DueDate)),
        ]);

        var money = new PdfTextStyle(11f, PdfWeight.Bold);
        doc.Paragraph(c.L("lines"), money, PdfAlign.Start, 3);
        var cols = new[] { new PdfColumn(3.2f), new PdfColumn(0.7f, PdfAlign.Center), new PdfColumn(1.5f, PdfAlign.End), new PdfColumn(1.6f, PdfAlign.End) };
        doc.Table(cols, [c.L("description"), c.L("quantity"), c.L("unitPrice"), c.L("amount")],
            inv.Lines.Select(l => new PdfRow([l.Description, c.Locale.Integer(l.Quantity), c.Money(l.UnitPrice, inv.Currency), c.Money(l.Amount, inv.Currency)])).ToList(), 9f);
        doc.Spacer(8f);

        var totals = new List<PdfRow> { new([c.L("subtotal"), c.Money(inv.Subtotal, inv.Currency)]) };
        foreach (var d in inv.Discounts)
            totals.Add(new PdfRow([$"{c.L("discount")} ({c.Opt("discountKind", d.Kind)})", "−" + c.Money(d.Amount, inv.Currency)]));
        if (inv.TaxTotal > 0) totals.Add(new PdfRow([$"{c.L("tax")} ({c.Locale.Number(inv.TaxRatePercent)}%)", c.Money(inv.TaxTotal, inv.Currency)]));
        totals.Add(new PdfRow([c.L("total"), c.Money(inv.Total, inv.Currency)], Bold: true));
        if (inv.LateFeeTotal > 0) totals.Add(new PdfRow([c.L("lateFees"), c.Money(inv.LateFeeTotal, inv.Currency)]));
        if (inv.CreditTotal > 0) totals.Add(new PdfRow([c.L("credits"), "−" + c.Money(inv.CreditTotal, inv.Currency)]));
        totals.Add(new PdfRow([c.L("paid"), c.Money(inv.PaidAmount, inv.Currency)]));
        totals.Add(new PdfRow([c.L("balance"), c.Money(inv.Balance, inv.Currency)], Bold: true, Background: PdfFlowDocument.HeaderFill));
        doc.KeepTogether(18f * totals.Count);
        doc.Table([new PdfColumn(4f), new PdfColumn(2f, PdfAlign.End)], null, totals, 9.5f);

        if (inv.Installments.Count > 0)
        {
            doc.Spacer(10f);
            doc.KeepTogether(60f);
            doc.Paragraph(c.L("installments"), money, PdfAlign.Start, 3);
            doc.Table([new PdfColumn(0.6f, PdfAlign.Center), new PdfColumn(2f), new PdfColumn(1.6f, PdfAlign.End), new PdfColumn(1.6f, PdfAlign.End), new PdfColumn(1.2f, PdfAlign.Center)],
                ["#", c.L("dueDate"), c.L("amount"), c.L("paid"), c.L("status")],
                inv.Installments.Select(i => new PdfRow([c.Locale.Integer(i.SeqNo), c.Date(i.DueDate), c.Money(i.Amount, inv.Currency),
                    c.Money(i.PaidAmount, inv.Currency), c.Opt("installmentStatus", i.Status)])).ToList(), 9f);
        }
        if (inv.Adjustments.Count > 0)
        {
            doc.Spacer(10f);
            doc.KeepTogether(50f);
            doc.Paragraph(c.L("adjustments"), money, PdfAlign.Start, 3);
            doc.Table([new PdfColumn(1.6f), new PdfColumn(1.4f), new PdfColumn(1.6f, PdfAlign.End)], [c.L("type"), c.L("number"), c.L("amount")],
                inv.Adjustments.Select(a => new PdfRow([c.Opt("adjustment", a.Kind), a.Number ?? "—", c.Money(a.Amount, inv.Currency)])).ToList(), 9f);
        }
        return Finish(doc, c, "invoiceTitle", renderedAtUtc, document.Header.TimeZoneId);
    }

    public byte[] RenderReceipt(ReceiptDocument document, string language, DateTime renderedAtUtc)
    {
        var p = document.Payment;
        var (doc, c) = Start(text, language, document.Header, "receiptTitle");
        var info = new List<(string, string)>
        {
            (c.L("receiptNumber"), p.ReceiptNumber ?? "—"), (c.L("receivedOn"), c.Date(p.ReceivedOn)),
            (c.L("student"), p.StudentName), (c.L("studentNumber"), document.StudentNumber),
            (c.L("method"), c.Opt("method", p.Method)), (c.L("reference"), p.ProviderRef ?? p.Reference ?? p.Cheque?.Number ?? "—"),
        };
        Grid(doc, info);
        doc.Paragraph($"{c.L("amountReceived")}: {c.Money(p.Amount, p.Currency)}", new PdfTextStyle(12f, PdfWeight.Bold), PdfAlign.Start, 6);
        if (p.Allocations.Count > 0)
        {
            doc.Paragraph(c.L("appliedTo"), new PdfTextStyle(11f, PdfWeight.Bold), PdfAlign.Start, 3);
            doc.Table([new PdfColumn(2f), new PdfColumn(1.6f, PdfAlign.End)], [c.L("invoiceNumber"), c.L("amount")],
                p.Allocations.GroupBy(a => a.InvoiceNumber ?? "—").Select(g => new PdfRow([g.Key, c.Money(g.Sum(a => a.Amount), p.Currency)])).ToList(), 9f);
            doc.Spacer(6f);
        }
        if (p.Unallocated > 0) doc.Paragraph($"{c.L("creditBalance")}: {c.Money(p.Unallocated, p.Currency)}", new PdfTextStyle(10f), PdfAlign.Start, 4);
        if (p.Refunded > 0) doc.Paragraph($"{c.L("refunded")}: {c.Money(p.Refunded, p.Currency)}", new PdfTextStyle(10f), PdfAlign.Start, 4);
        return Finish(doc, c, "receiptTitle", renderedAtUtc, document.Header.TimeZoneId);
    }
}
