using SkiaSharp;

namespace EEMP.Infrastructure.Pdf;

public enum PdfAlign { Start, Center, End }

public sealed record PdfTextStyle(float Size, PdfWeight Weight = PdfWeight.Regular, SKColor? Color = null);

public sealed record PdfColumn(float Weight, PdfAlign Align = PdfAlign.Start);

public sealed record PdfRow(IReadOnlyList<string> Cells, bool Bold = false, SKColor? Background = null);

/// <summary>
/// A small top-to-bottom flow layout on A4 pages: paragraphs, key/value grids and tables with wrapped cells and repeated
/// headers. Layout is mirrored for right-to-left documents ("start" is the right edge). Drawing is deferred so the footer
/// can print "page n of N" once the page count is known.
/// </summary>
public sealed class PdfFlowDocument
{
    public const float PageWidth = 595.28f;
    public const float PageHeight = 841.89f;
    private const float Margin = 40f;
    private const float FooterHeight = 28f;
    private const float CellPadding = 4f;

    public static readonly SKColor Ink = new(0x1F, 0x29, 0x37);
    public static readonly SKColor Muted = new(0x4B, 0x55, 0x63);
    public static readonly SKColor Rule = new(0xC7, 0xCD, 0xD6);
    public static readonly SKColor HeaderFill = new(0xEE, 0xF1, 0xF5);
    public static readonly SKColor NoticeFill = new(0xFE, 0xF3, 0xC7);

    private readonly PdfTextShaper _shaper;
    private readonly List<List<Action<SKCanvas>>> _pages = [];
    private readonly float _ascent;
    private readonly float _descent;
    private float _y;

    public PdfFlowDocument(PdfFontSet fonts, string language, bool rtl)
    {
        Rtl = rtl;
        _shaper = new PdfTextShaper(fonts, language);
        using var probe = new SKFont(fonts.Typeface(PdfWeight.Regular), 1000f);
        _ascent = -probe.Metrics.Ascent / 1000f;
        _descent = probe.Metrics.Descent / 1000f;
        NewPage();
    }

    public bool Rtl { get; }
    public float Width => PageWidth - 2 * Margin;
    public int PageCount => _pages.Count;
    private float Bottom => PageHeight - Margin - FooterHeight;
    private List<Action<SKCanvas>> Page => _pages[^1];

    public float LineHeight(float size) => (_ascent + _descent) * size * 1.08f;

    public void NewPage()
    {
        _pages.Add([]);
        _y = Margin;
    }

    public void Spacer(float height) => _y = Math.Min(_y + height, Bottom);

    /// <summary>Physical left x of a box that starts <paramref name="logicalX"/> from the start edge.</summary>
    private float X(float logicalX, float width) => Rtl ? Margin + Width - logicalX - width : Margin + logicalX;

    private float AlignedX(float boxX, float boxWidth, float textWidth, PdfAlign align) => align switch
    {
        PdfAlign.Center => boxX + (boxWidth - textWidth) / 2,
        PdfAlign.Start => Rtl ? boxX + boxWidth - textWidth : boxX,
        _ => Rtl ? boxX : boxX + boxWidth - textWidth,
    };

    public void Paragraph(string text, PdfTextStyle style, PdfAlign align = PdfAlign.Start, float spacingAfter = 4f)
    {
        var lh = LineHeight(style.Size);
        foreach (var line in _shaper.Wrap(text, style.Size, style.Weight, Rtl, Width))
        {
            if (_y + lh > Bottom) NewPage();
            DrawLine(line, style, Margin, Width, _y, align);
            _y += lh;
        }
        _y += spacingAfter;
    }

    /// <summary>A shaded full-width box with one paragraph (e.g. a draft notice).</summary>
    public void Notice(string text, PdfTextStyle style, SKColor fill)
    {
        var lines = _shaper.Wrap(text, style.Size, style.Weight, Rtl, Width - 4 * CellPadding);
        var lh = LineHeight(style.Size);
        var height = lines.Count * lh + 2 * CellPadding;
        if (_y + height > Bottom) NewPage();
        var top = _y;
        Page.Add(c =>
        {
            using var paint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill };
            c.DrawRect(Margin, top, Width, height, paint);
        });
        var y = top + CellPadding;
        foreach (var line in lines)
        {
            DrawLine(line, style, Margin + 2 * CellPadding, Width - 4 * CellPadding, y, PdfAlign.Start);
            y += lh;
        }
        _y = top + height + 6f;
    }

    public void HorizontalRule(float spacing = 6f)
    {
        if (_y + spacing > Bottom) NewPage();
        var y = _y + spacing / 2;
        Page.Add(c =>
        {
            using var paint = new SKPaint { Color = Rule, StrokeWidth = 0.8f, Style = SKPaintStyle.Stroke };
            c.DrawLine(Margin, y, Margin + Width, y, paint);
        });
        _y += spacing;
    }

    /// <summary>Starts a new page when fewer than <paramref name="height"/> points remain (keeps a heading with its table).</summary>
    public void KeepTogether(float height)
    {
        if (_y + height > Bottom) NewPage();
    }

    public void Table(IReadOnlyList<PdfColumn> columns, IReadOnlyList<string>? header, IReadOnlyList<PdfRow> rows, float fontSize = 9f)
    {
        var total = columns.Sum(c => c.Weight);
        var widths = columns.Select(c => c.Weight / total * Width).ToArray();
        var headerRow = header is null ? null : new PdfRow(header, Bold: true, Background: HeaderFill);
        if (headerRow is not null) KeepTogether(RowHeight(headerRow, widths, fontSize) + (rows.Count > 0 ? RowHeight(rows[0], widths, fontSize) : 0));
        if (headerRow is not null) DrawRow(columns, widths, headerRow, fontSize);
        foreach (var row in rows)
        {
            if (_y + RowHeight(row, widths, fontSize) > Bottom)
            {
                NewPage();
                if (headerRow is not null) DrawRow(columns, widths, headerRow, fontSize);
            }
            DrawRow(columns, widths, row, fontSize);
        }
    }

    private float RowHeight(PdfRow row, float[] widths, float size)
    {
        var weight = row.Bold ? PdfWeight.Bold : PdfWeight.Regular;
        var lines = row.Cells.Select((cell, i) => _shaper.Wrap(cell, size, weight, Rtl, widths[i] - 2 * CellPadding).Count).DefaultIfEmpty(1).Max();
        return lines * LineHeight(size) + 2 * CellPadding;
    }

    private void DrawRow(IReadOnlyList<PdfColumn> columns, float[] widths, PdfRow row, float size)
    {
        var weight = row.Bold ? PdfWeight.Bold : PdfWeight.Regular;
        var style = new PdfTextStyle(size, weight, Ink);
        var height = RowHeight(row, widths, size);
        var top = _y;
        float logical = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            var x = X(logical, widths[i]);
            var w = widths[i];
            var fill = row.Background;
            Page.Add(c =>
            {
                if (fill is { } f)
                {
                    using var bg = new SKPaint { Color = f, Style = SKPaintStyle.Fill };
                    c.DrawRect(x, top, w, height, bg);
                }
                using var border = new SKPaint { Color = Rule, StrokeWidth = 0.6f, Style = SKPaintStyle.Stroke };
                c.DrawRect(x, top, w, height, border);
            });
            var text = i < row.Cells.Count ? row.Cells[i] : "";
            var y = top + CellPadding;
            foreach (var line in _shaper.Wrap(text, size, weight, Rtl, w - 2 * CellPadding))
            {
                DrawLine(line, style, x + CellPadding, w - 2 * CellPadding, y, columns[i].Align);
                y += LineHeight(size);
            }
            logical += w;
        }
        _y = top + height;
    }

    private void DrawLine(string text, PdfTextStyle style, float boxX, float boxWidth, float top, PdfAlign align)
    {
        if (text.Length == 0) return;
        var shaped = _shaper.Shape(text, style.Size, style.Weight, Rtl);
        var x = AlignedX(boxX, boxWidth, shaped.Width, align);
        var baseline = top + _ascent * style.Size;
        var color = style.Color ?? Ink;
        Page.Add(c => _shaper.Draw(c, shaped, x, baseline, color));
    }

    /// <summary>Renders all pages. <paramref name="footer"/> returns (start-side, end-side) footer text for page n of N.</summary>
    public byte[] Render(string title, Func<int, int, (string Start, string End)> footer)
    {
        using var stream = new MemoryStream();
        var metadata = new SKDocumentPdfMetadata
        {
            Title = title,
            Creator = "EEMP",
            Producer = "EEMP (SkiaSharp)",
            RasterDpi = 150,
            PdfA = false,
        };
        using (var document = SKDocument.CreatePdf(stream, metadata))
        {
            var footerStyle = new PdfTextStyle(7.5f, PdfWeight.Regular, Muted);
            var footerTop = PageHeight - Margin - LineHeight(footerStyle.Size) + 4;
            for (var i = 0; i < _pages.Count; i++)
            {
                var canvas = document.BeginPage(PageWidth, PageHeight);
                foreach (var op in _pages[i]) op(canvas);
                var (start, end) = footer(i + 1, _pages.Count);
                using (var rule = new SKPaint { Color = Rule, StrokeWidth = 0.6f, Style = SKPaintStyle.Stroke })
                    canvas.DrawLine(Margin, footerTop - 4, Margin + Width, footerTop - 4, rule);
                DrawNow(canvas, start, footerStyle, footerTop, PdfAlign.Start);
                DrawNow(canvas, end, footerStyle, footerTop, PdfAlign.End);
                document.EndPage();
            }
            document.Close();
        }
        return stream.ToArray();
    }

    private void DrawNow(SKCanvas canvas, string text, PdfTextStyle style, float top, PdfAlign align)
    {
        if (text.Length == 0) return;
        var shaped = _shaper.Shape(text, style.Size, style.Weight, Rtl);
        _shaper.Draw(canvas, shaped, AlignedX(Margin, Width, shaped.Width, align), top + _ascent * style.Size, style.Color ?? Ink);
    }
}
