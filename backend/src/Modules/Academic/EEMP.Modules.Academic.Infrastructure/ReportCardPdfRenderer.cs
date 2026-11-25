using EEMP.Infrastructure.Localization;
using EEMP.Infrastructure.Pdf;
using EEMP.Modules.Academic.Application;

namespace EEMP.Modules.Academic.Infrastructure;

/// <summary>
/// Report-card PDF layout (A4, mirrored for fa/ar). Every score, total, band and name comes from the version snapshot;
/// labels come from the server text catalogs (<c>reportCard.pdf.*</c>), so the document has no hard-coded UI text.
/// </summary>
public sealed class ReportCardPdfRenderer(IServerTextLocalizer text) : IReportCardPdfRenderer
{
    private const string Prefix = "reportCard.pdf.";

    public byte[] Render(ReportCardDocument document, string language, DateTime renderedAtUtc)
    {
        var locale = PdfLocale.TryResolve(language) ?? throw new ArgumentException("Unsupported language.", nameof(language));
        string L(string key) => text.Get(Prefix + key, locale.Culture);
        string Lopt(string group, string? value) =>
            string.IsNullOrEmpty(value) ? "" : text.HasKey(locale.Language, $"{Prefix}{group}.{value}") ? L($"{group}.{value}") : value;
        var zone = PdfLocale.Zone(document.TimeZoneId);
        var c = document.Content;
        var none = L("none");

        string Total(Application.GradeTotalDto? total, SchemeInfoDto scheme) =>
            total is null ? none : $"{locale.Number(total.Value, scheme.DecimalPlaces)} / {locale.Number(scheme.MaxScore, scheme.DecimalPlaces)}";
        string Result(Application.GradeTotalDto? total) => total is null ? none : total.Passed ? L("passed") : L("failed");
        string Band(Application.GradeTotalDto? total) => total?.Label is { Length: > 0 } label ? Lopt("band", label) : none;
        string Date(string iso) => DateOnly.TryParseExact(iso, "yyyy-MM-dd", out var d) ? locale.Date(d) : iso;

        var doc = new PdfFlowDocument(PdfFontSet.Shared.Value, locale.Language, locale.Rtl);
        var muted = PdfFlowDocument.Muted;

        // Header.
        if (document.InstituteName.Length > 0) doc.Paragraph(document.InstituteName, new PdfTextStyle(15f, PdfWeight.Bold), PdfAlign.Center, 0);
        if (document.BranchName.Length > 0) doc.Paragraph(document.BranchName, new PdfTextStyle(10.5f, PdfWeight.Regular, muted), PdfAlign.Center, 2);
        doc.Paragraph(L("title"), new PdfTextStyle(13f, PdfWeight.Bold), PdfAlign.Center, 2);
        doc.HorizontalRule(8f);

        if (document.VersionStatus != "published")
            doc.Notice(L("draftNotice").Replace("{status}", Lopt("status", document.VersionStatus), StringComparison.Ordinal),
                new PdfTextStyle(9.5f, PdfWeight.Bold), PdfFlowDocument.NoticeFill);

        // Identity and version grid.
        var info = new List<(string Key, string Value)>
        {
            (L("student"), $"{c.Student.FirstName} {c.Student.LastName}".Trim()),
            (L("studentNumber"), c.Student.StudentNumber),
            (L("academicYear"), c.AcademicYear.Name),
            (L("term"), c.Term.Name),
            (L("termDates"), $"{Date(c.Term.StartDate)} – {Date(c.Term.EndDate)}"),
            (L("gradingScheme"), c.GradingScheme.Name),
            (L("version"), $"{locale.Integer(document.VersionNo)} ({Lopt("status", document.VersionStatus)})"),
            (L("publishedAt"), document.PublishedAt is { } p ? locale.DateTime(p, zone) : L("notPublished")),
            (L("snapshotAt"), locale.DateTime(c.GeneratedAt, zone)),
            (L("classes"), locale.Integer(c.Lines.Count)),
        };
        var grid = new List<PdfRow>();
        for (var i = 0; i < info.Count; i += 2)
        {
            var right = i + 1 < info.Count ? info[i + 1] : ("", "");
            grid.Add(new PdfRow([info[i].Key, info[i].Value, right.Item1, right.Item2]));
        }
        doc.Table([new PdfColumn(1.1f), new PdfColumn(1.9f), new PdfColumn(1.1f), new PdfColumn(1.9f)], null, grid, 9f);
        doc.Spacer(10f);

        // Summary per class line + overall average on the tenant default scheme.
        var heading = new PdfTextStyle(11f, PdfWeight.Bold);
        doc.KeepTogether(60f);
        doc.Paragraph(L("summary"), heading, PdfAlign.Start, 3);
        var summaryCols = new[]
        {
            new PdfColumn(1.6f), new PdfColumn(1.8f), new PdfColumn(1.3f), new PdfColumn(1.2f, PdfAlign.Center), new PdfColumn(1f, PdfAlign.Center),
            new PdfColumn(0.9f, PdfAlign.Center),
        };
        var summary = c.Lines.Select(l => new PdfRow([l.SubjectName, l.CourseName, $"{l.ClassName} ({l.ClassCode})", Total(l.Total, l.GradingScheme),
            Band(l.Total), Result(l.Total)])).ToList();
        if (summary.Count == 0) summary.Add(new PdfRow([L("noLines"), "", "", "", "", ""]));
        summary.Add(new PdfRow([L("average"), "", "", Total(c.Average, c.GradingScheme), Band(c.Average), Result(c.Average)], Bold: true,
            Background: PdfFlowDocument.HeaderFill));
        doc.Table(summaryCols, [L("subject"), L("course"), L("class"), L("weightedTotal"), L("band"), L("result")], summary);
        doc.Spacer(10f);

        // Attendance summary.
        doc.KeepTogether(50f);
        doc.Paragraph(L("attendance"), heading, PdfAlign.Start, 3);
        var a = c.Attendance;
        doc.Table([new PdfColumn(1, PdfAlign.Center), new PdfColumn(1, PdfAlign.Center), new PdfColumn(1, PdfAlign.Center), new PdfColumn(1, PdfAlign.Center)],
            [L("attendance.present"), L("attendance.absent"), L("attendance.late"), L("attendance.excused")],
            [new PdfRow([locale.Integer(a.Present), locale.Integer(a.Absent), locale.Integer(a.Late), locale.Integer(a.Excused)])]);
        doc.Spacer(10f);

        // Assessment details per class.
        if (c.Lines.Any(l => l.Assessments.Count > 0))
        {
            doc.KeepTogether(70f);
            doc.Paragraph(L("details"), heading, PdfAlign.Start, 3);
            foreach (var line in c.Lines.Where(l => l.Assessments.Count > 0))
            {
                doc.KeepTogether(50f);
                doc.Paragraph($"{line.CourseName} — {line.ClassName} ({line.ClassCode})", new PdfTextStyle(9.5f, PdfWeight.Bold), PdfAlign.Start, 2);
                var rows = line.Assessments.Select(x => new PdfRow([
                    x.Name,
                    Lopt("assessmentType", x.Type),
                    locale.Number(x.Weight),
                    x.Mark == "scored" || (x.Mark is null && x.Score is not null)
                        ? $"{locale.Number(x.Score ?? 0, line.GradingScheme.DecimalPlaces)} / {locale.Number(x.MaxScore)}"
                        : x.Mark is { } m ? Lopt("mark", m) : none,
                ])).ToList();
                rows.Add(new PdfRow([L("weightedTotal"), "", "", Total(line.Total, line.GradingScheme)], Bold: true));
                doc.Table([new PdfColumn(2.4f), new PdfColumn(1.3f), new PdfColumn(0.8f, PdfAlign.Center), new PdfColumn(1.3f, PdfAlign.Center)],
                    [L("assessment"), L("type"), L("weight"), L("score")], rows, 8.5f);
                doc.Spacer(6f);
            }
        }

        doc.Paragraph(L("formula").Replace("{formula}", c.Formula, StringComparison.Ordinal), new PdfTextStyle(7.5f, PdfWeight.Regular, muted), PdfAlign.Start, 0);

        var generated = L("generatedAt").Replace("{date}", locale.DateTime(renderedAtUtc, zone), StringComparison.Ordinal);
        return doc.Render(L("title"), (page, pages) => (generated,
            L("page").Replace("{page}", locale.Integer(page), StringComparison.Ordinal).Replace("{pages}", locale.Integer(pages), StringComparison.Ordinal)));
    }
}
