using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EEMP.Infrastructure.Localization;
using EEMP.Infrastructure.Pdf;
using EEMP.Modules.Academic.Application;
using EEMP.Modules.Academic.Infrastructure;

namespace EEMP.UnitTests;

/// <summary>
/// Report-card PDF: bidi ordering, locale digits/calendars, and the rendered document. Text assertions extract the PDF text
/// with poppler's <c>pdftotext</c> (installed in CI); set EEMP_PDF_SAMPLES_DIR to keep the rendered samples.
/// </summary>
public class ReportCardPdfTests
{
    private static readonly DateTime Rendered = new(2026, 11, 25, 20, 30, 0, DateTimeKind.Utc); // 2026-11-26 00:00 Tehran

    [Fact]
    public void Bidi_keeps_numbers_left_to_right_inside_rtl_text()
    {
        var runs = Bidi.Visual("نمره ۱۸٫۵ از ۲۰", rtlParagraph: true);
        // Visual order (left → right); numbers are LTR runs, words RTL runs.
        Assert.Equal(["۲۰", " از ", "۱۸٫۵", "نمره "], runs.Select(r => r.Text).ToArray());
        Assert.False(runs[0].Rtl);
        Assert.True(runs[1].Rtl);
        Assert.False(runs[2].Rtl);
    }

    [Fact]
    public void Bidi_embeds_latin_in_rtl_and_rtl_in_ltr()
    {
        var rtl = Bidi.Visual("دانش‌آموز Sara Ahmadi S0001", rtlParagraph: true);
        Assert.Equal("Sara Ahmadi S0001", rtl[0].Text);
        Assert.False(rtl[0].Rtl);
        Assert.Equal("دانش‌آموز ", rtl[1].Text);

        var ltr = Bidi.Visual("Student سارا احمدی 18.5", rtlParagraph: false);
        // UBA: a number after Arabic-script text in an LTR paragraph is displayed to the left of that text.
        Assert.Equal(["Student ", "18.5", "سارا احمدی "], ltr.Select(r => r.Text).ToArray());
        Assert.True(ltr[2].Rtl);
        Assert.False(ltr[1].Rtl);
    }

    [Fact]
    public void Bidi_keeps_a_year_range_together_in_rtl_text()
    {
        var runs = Bidi.Visual("سال 1405-1406", rtlParagraph: true);
        Assert.Equal(["1405-1406", "سال "], runs.Select(r => r.Text).ToArray());
    }

    [Fact]
    public void Paragraph_direction_follows_the_first_strong_character()
    {
        Assert.False(Bidi.ParagraphRtl("Mathematics 7 - A (MATH-7A)", fallback: true));
        Assert.True(Bidi.ParagraphRtl("۱ (منتشرشده)", fallback: false));
        Assert.True(Bidi.ParagraphRtl("۲۰ / ۱۸٫۷۵", fallback: true));
        Assert.False(Bidi.ParagraphRtl("18.75 / 20", fallback: false));
    }

    [Fact]
    public void Dates_do_not_depend_on_the_request_culture()
    {
        // The API runs requests under the Accept-Language culture; fa-IR uses the Persian calendar for parsing too.
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("fa-IR");
            var text = ExtractText(Render(PersianSample(), "fa"));
            if (text is not null)
            {
                Assert.Contains("مهر", text);   // term start 2026-09-23 = 1 Mehr 1405
                Assert.DoesNotContain("۲۰۲۶", text);
            }
            var en = ExtractText(Render(EnglishSample(), "en"));
            if (en is not null) Assert.Contains("23 September 2026", en);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }

    [Theory]
    [InlineData("fa-IR", "۵ آذر ۱۴۰۵", "۱۸٫۵")]
    [InlineData("fa", "۵ آذر ۱۴۰۵", "۱۸٫۵")]
    [InlineData("en", "26 November 2026", "18.5")]
    [InlineData("tr", "26 Kasım 2026", "18.5")]
    [InlineData("ar", "٢٦ نوفمبر ٢٠٢٦", "١٨٫٥")]
    public void Locale_digits_and_calendar(string tag, string date, string number)
    {
        var locale = PdfLocale.TryResolve(tag)!;
        Assert.Equal(date, locale.Date(new DateOnly(2026, 11, 26)));
        Assert.Equal(number, locale.Number(18.50m));
        Assert.Equal(tag.StartsWith("fa", StringComparison.Ordinal) || tag == "ar", locale.Rtl);
    }

    [Fact]
    public void Unsupported_language_is_rejected() => Assert.Null(PdfLocale.TryResolve("de-DE"));

    [Fact]
    public void Persian_pdf_has_shaped_rtl_text_persian_digits_and_page_numbers()
    {
        var pdf = Render(PersianSample(), "fa");
        AssertPdf(pdf);
        Save(pdf, "report-card-fa.pdf");
        var text = ExtractText(pdf);
        if (text is null) return; // pdftotext not installed locally; CI installs poppler-utils.
        Assert.Contains("کارنامه تحصیلی", text);
        Assert.Contains("آموزشگاه علمی نمونه", text);
        Assert.Contains("سارا احمدی", text);
        Assert.Contains("ریاضی هفتم", text);
        Assert.Contains("۱۸٫۵", text);        // assessment score in Persian digits with the Persian decimal separator
        Assert.Contains("آذر", text);          // Jalali month of the generated date (pdftotext reorders mixed-direction runs,
        Assert.Contains("۱۴۰۵", text);         // so the day/month/year tokens are asserted separately)
        Assert.Matches(@"صفحه\s*۱\s*از\s*۱", text);
        Assert.Contains("عالی", text);         // band label of the tenant scheme, localized
        Assert.DoesNotContain("18.5", text);
    }

    [Fact]
    public void English_pdf_uses_latin_text_gregorian_dates_and_draft_notice()
    {
        var doc = EnglishSample() with { VersionStatus = "draft", PublishedAt = null };
        var pdf = Render(doc, "en");
        AssertPdf(pdf);
        Save(pdf, "report-card-en.pdf");
        var text = ExtractText(pdf);
        if (text is null) return;
        Assert.Contains("Report card", text);
        Assert.Contains("Sara Ahmadi", text);
        Assert.Contains("S0001", text);
        Assert.Contains("18.5 / 20", text);
        Assert.Contains("26 November 2026", text);
        Assert.Contains("Draft — this version is not the published report card.", text);
        Assert.Contains("Not published", text);
        Assert.Matches(@"Page 1 of \d", text);
    }

    [Fact]
    public void Arabic_and_turkish_render()
    {
        foreach (var lang in new[] { "ar", "tr" })
        {
            var pdf = Render(EnglishSample(), lang);
            AssertPdf(pdf);
            var text = ExtractText(pdf);
            if (text is null) continue;
            Assert.Contains(lang == "ar" ? "بطاقة التقرير المدرسي" : "Karne", text);
            Assert.Contains(lang == "ar" ? "١٨٫٥" : "18.5", text);
        }
    }

    [Fact]
    public void Long_report_cards_flow_onto_more_pages()
    {
        var sample = PersianSample();
        var lines = Enumerable.Range(1, 14).Select(i => sample.Content.Lines[0] with { ClassCode = $"K{i:00}", ClassName = $"کلاس {i}" }).ToList();
        var pdf = Render(sample with { Content = sample.Content with { Lines = lines } }, "en");
        AssertPdf(pdf);
        Save(pdf, "report-card-long.pdf");
        var pages = Regex.Count(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]");
        Assert.True(pages >= 2, $"expected at least 2 pages, got {pages}");
        var text = ExtractText(pdf);
        if (text is null) return;
        Assert.Contains($"Page {pages} of {pages}", text);
    }

    private static byte[] Render(ReportCardDocument doc, string lang) => new ReportCardPdfRenderer(new ServerTextLocalizer()).Render(doc, lang, Rendered);

    private static void AssertPdf(byte[] pdf)
    {
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        // Fonts are embedded (subset) so the document renders without system fonts.
        Assert.Contains("/FontFile", Encoding.Latin1.GetString(pdf));
    }

    private static void Save(byte[] pdf, string name)
    {
        var dir = Environment.GetEnvironmentVariable("EEMP_PDF_SAMPLES_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), pdf);
    }

    /// <summary>Text via pdftotext with bidi control marks removed; null when poppler is not installed.</summary>
    private static string? ExtractText(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"eemp-rc-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            var psi = new ProcessStartInfo("pdftotext", ["-enc", "UTF-8", path, "-"]) { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) return null;
            return Regex.Replace(output, "[\u200E\u200F\u202A-\u202E\u2066-\u2069]", "");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static readonly SchemeInfoDto Ir20 = new("IR20", "Numeric 0–20", "numeric", 0, 20, 10, 2);

    internal static ReportCardDocument PersianSample() => new(Guid.Parse("8f2c1b7e-0000-4000-8000-000000000001"), 2, "published",
        new DateTime(2026, 11, 24, 7, 0, 0, DateTimeKind.Utc), "آموزشگاه علمی نمونه", "شعبه شمال", "Asia/Tehran",
        new ReportCardContent("weightedAverage.v1", new DateTime(2026, 11, 24, 6, 30, 0, DateTimeKind.Utc),
            new RcStudent(Guid.NewGuid(), "سارا", "احمدی", "S0001"), new RcRef(Guid.NewGuid(), "1405-1406"),
            new RcTerm(Guid.NewGuid(), "نیمسال اول", "2026-09-23", "2027-01-20"), Ir20,
            [
                new RcLine(Guid.NewGuid(), "MATH-7A", "ریاضی ۷ الف", "ریاضی هفتم", "ریاضی", Ir20, new GradeTotalDto(17.67m, "excellent", true),
                [
                    new RcAssessment("آزمونک ۱", "quiz", 1, 20, 18.5m, "scored"),
                    new RcAssessment("میان‌ترم", "midterm", 2, 20, 17.25m, "scored"),
                    new RcAssessment("پروژه پژوهشی", "project", 1, 20, null, "exempt"),
                ]),
                new RcLine(Guid.NewGuid(), "PHYS-7S", "فیزیک ۷", "علوم تجربی هفتم", "فیزیک", Ir20, new GradeTotalDto(9.5m, "fail", false),
                [
                    new RcAssessment("آزمون پایانی", "final", 3, 20, 9.5m, "scored"),
                    new RcAssessment("مشارکت", "participation", 1, 20, null, "absent"),
                ]),
            ],
            new GradeTotalDto(13.58m, "fair", true), new RcAttendance(24, 2, 3, 1)));

    internal static ReportCardDocument EnglishSample() => new(Guid.Parse("8f2c1b7e-0000-4000-8000-000000000002"), 1, "published",
        new DateTime(2026, 11, 24, 7, 0, 0, DateTimeKind.Utc), "Sample Learning Institute", "North branch", "Asia/Tehran",
        new ReportCardContent("weightedAverage.v1", new DateTime(2026, 11, 24, 6, 30, 0, DateTimeKind.Utc),
            new RcStudent(Guid.NewGuid(), "Sara", "Ahmadi", "S0001"), new RcRef(Guid.NewGuid(), "1405-1406"),
            new RcTerm(Guid.NewGuid(), "Term 1", "2026-09-23", "2027-01-20"), Ir20,
            [
                new RcLine(Guid.NewGuid(), "MATH-7A", "Mathematics 7A", "Mathematics 7", "Mathematics", Ir20, new GradeTotalDto(17.67m, "excellent", true),
                [
                    new RcAssessment("Quiz 1", "quiz", 1, 20, 18.5m, "scored"),
                    new RcAssessment("Midterm", "midterm", 2, 20, 17.25m, "scored"),
                    new RcAssessment("Research project", "project", 1, 20, null, "exempt"),
                ]),
                new RcLine(Guid.NewGuid(), "PHYS-7S", "Physics 7", "Science 7", "Physics", Ir20, new GradeTotalDto(9.5m, "fail", false),
                [
                    new RcAssessment("Final exam", "final", 3, 20, 9.5m, "scored"),
                    new RcAssessment("Participation", "participation", 1, 20, null, "absent"),
                ]),
            ],
            new GradeTotalDto(13.58m, "fair", true), new RcAttendance(24, 2, 3, 1)));
}
