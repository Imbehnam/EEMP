using System.Globalization;
using System.Reflection;
using System.Text;
using HarfBuzzSharp;
using SkiaSharp;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace EEMP.Infrastructure.Pdf;

public enum PdfWeight { Regular, Bold }

/// <summary>
/// The embedded Vazirmatn font (SIL OFL 1.1, see Pdf/Fonts/OFL.txt): Persian/Arabic plus Latin incl. Turkish.
/// Typefaces and HarfBuzz fonts are immutable after load and shared across requests (thread-safe for shaping/drawing).
/// </summary>
public sealed class PdfFontSet : IDisposable
{
    public static readonly Lazy<PdfFontSet> Shared = new(() => new PdfFontSet(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Face[] _faces;
    private readonly Blob[] _blobs;
    private readonly HbFont[] _hb;
    private readonly SKTypeface[] _typefaces;

    private PdfFontSet()
    {
        string[] files = ["Vazirmatn-Regular.ttf", "Vazirmatn-Bold.ttf"];
        _blobs = new Blob[files.Length];
        _faces = new Face[files.Length];
        _hb = new HbFont[files.Length];
        _typefaces = new SKTypeface[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            var bytes = ReadResource(files[i]);
            _typefaces[i] = SKTypeface.FromData(SKData.CreateCopy(bytes)) ?? throw new InvalidOperationException($"Font {files[i]} could not be loaded.");
            // HarfBuzz keeps a pointer to the font data for the font's lifetime: give it unmanaged memory it frees itself.
            var native = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, native, bytes.Length);
            _blobs[i] = new Blob(native, bytes.Length, MemoryMode.ReadOnly, () => System.Runtime.InteropServices.Marshal.FreeHGlobal(native));
            _faces[i] = new Face(_blobs[i], 0);
            _hb[i] = new HbFont(_faces[i]);
            _hb[i].SetScale(_faces[i].UnitsPerEm, _faces[i].UnitsPerEm);
        }
    }

    internal SKTypeface Typeface(PdfWeight w) => _typefaces[(int)w];
    internal HbFont Shaper(PdfWeight w) => _hb[(int)w];
    internal int UnitsPerEm(PdfWeight w) => _faces[(int)w].UnitsPerEm;

    private static byte[] ReadResource(string file)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream($"EEMP.Infrastructure.Pdf.Fonts.{file}")
            ?? throw new InvalidOperationException($"Embedded font {file} is missing.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public void Dispose()
    {
        foreach (var f in _hb) f.Dispose();
        foreach (var f in _faces) f.Dispose();
        foreach (var b in _blobs) b.Dispose();
        foreach (var t in _typefaces) t.Dispose();
    }
}

/// <summary>A directional run in visual order: logical text, whether it is shaped right-to-left.</summary>
public readonly record struct BidiRun(string Text, bool Rtl);

/// <summary>
/// Minimal Unicode bidi for single-line labels and table cells (UBA rules W1–W7, N1–N2, I1–I2, L2 without explicit
/// embeddings): strong Arabic-script/Hebrew → R, other letters → L, digit sequences with their separators form
/// left-to-right number runs, neutrals take the surrounding direction or the paragraph direction.
/// </summary>
public static class Bidi
{
    private enum Cls { L, R, Num, N }

    public static bool IsRtlChar(char c) =>
        c is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF';

    public static bool IsDigit(char c) => c is >= '0' and <= '9' or >= '\u0660' and <= '\u0669' or >= '\u06F0' and <= '\u06F9';

    private static bool IsNumberSeparator(char c) => c is '.' or ',' or ':' or '/' or '-' or '+' or '\u066B' or '\u066C' or '\u060C';

    private static bool IsNumberSuffix(char c) => c is '%' or '\u066A' or '\u060F';

    private static Cls Classify(char c)
    {
        if (IsDigit(c)) return Cls.Num;
        // Arabic number separators/percent/comma sit in the Arabic block but are weak/neutral (AN separators, CS, ET).
        if (IsNumberSeparator(c) || IsNumberSuffix(c)) return Cls.N;
        if (IsRtlChar(c)) return Cls.R;
        if (char.IsLetter(c)) return Cls.L;
        return Cls.N;
    }

    /// <summary>Splits <paramref name="text"/> into runs and returns them in visual (left-to-right) order.</summary>
    public static IReadOnlyList<BidiRun> Visual(string text, bool rtlParagraph)
    {
        if (text.Length == 0) return [];
        var cls = new Cls[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var cat = char.GetUnicodeCategory(c);
            // Combining marks, ZWNJ/ZWJ follow the previous character (W1 / BN).
            if (i > 0 && (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark || c is '\u200C' or '\u200D'))
                cls[i] = cls[i - 1];
            else
                cls[i] = Classify(c);
        }
        // W4/W5: separators between digits and suffixes after digits join the number.
        for (var i = 1; i < text.Length; i++)
        {
            if (cls[i] == Cls.N && cls[i - 1] == Cls.Num)
            {
                if (IsNumberSuffix(text[i])) cls[i] = Cls.Num;
                else if (IsNumberSeparator(text[i]) && i + 1 < text.Length && cls[i + 1] == Cls.Num) cls[i] = Cls.Num;
            }
        }
        // W7: a number whose preceding strong type is L (or the LTR paragraph start) is L.
        var lastStrong = rtlParagraph ? Cls.R : Cls.L;
        for (var i = 0; i < text.Length; i++)
        {
            if (cls[i] is Cls.L or Cls.R) lastStrong = cls[i];
            else if (cls[i] == Cls.Num && lastStrong == Cls.L) cls[i] = Cls.L;
        }
        // N1/N2: neutrals between same-direction context (numbers count as R) take it, otherwise the paragraph direction.
        var e = rtlParagraph ? Cls.R : Cls.L;
        for (var i = 0; i < text.Length;)
        {
            if (cls[i] != Cls.N) { i++; continue; }
            var j = i;
            while (j < text.Length && cls[j] == Cls.N) j++;
            var before = i == 0 ? e : Strong(cls[i - 1]);
            var after = j == text.Length ? e : Strong(cls[j]);
            var resolved = before == after ? before : e;
            for (var k = i; k < j; k++) cls[k] = resolved;
            i = j;
        }
        // I1/I2: embedding levels.
        var levels = new int[text.Length];
        for (var i = 0; i < text.Length; i++)
            levels[i] = (rtlParagraph, cls[i]) switch
            {
                (true, Cls.R) => 1,
                (true, _) => 2,
                (false, Cls.L) => 0,
                (false, Cls.R) => 1,
                (false, _) => 2,
            };
        // Group into level runs (logical order).
        var runs = new List<(int Start, int End, int Level)>();
        for (var i = 0; i < text.Length;)
        {
            var j = i;
            while (j < text.Length && levels[j] == levels[i]) j++;
            runs.Add((i, j, levels[i]));
            i = j;
        }
        // L2: reverse contiguous sequences at each level from the highest down to the lowest odd level.
        var max = runs.Max(r => r.Level);
        var minOdd = runs.Where(r => r.Level % 2 == 1).Select(r => r.Level).DefaultIfEmpty(max + 1).Min();
        for (var level = max; level >= minOdd; level--)
        {
            for (var i = 0; i < runs.Count;)
            {
                if (runs[i].Level < level) { i++; continue; }
                var j = i;
                while (j < runs.Count && runs[j].Level >= level) j++;
                runs.Reverse(i, j - i);
                i = j;
            }
        }
        return runs.Select(r => new BidiRun(text[r.Start..r.End], r.Level % 2 == 1)).ToList();
    }

    private static Cls Strong(Cls c) => c == Cls.Num ? Cls.R : c;
}

/// <summary>Shaped, bidi-ordered single line: glyphs positioned from x = 0 with the original text attached (for copy/search).</summary>
public sealed class ShapedLine
{
    internal ShapedLine(List<ShapedRun> runs, float width) { Runs = runs; Width = width; }
    internal List<ShapedRun> Runs { get; }
    public float Width { get; }
}

internal sealed record ShapedRun(ushort[] Glyphs, SKPoint[] Positions, uint[] Clusters, byte[] Utf8, float Size, PdfWeight Weight);

public sealed class PdfTextShaper(PdfFontSet fonts, string language)
{

    public ShapedLine Shape(string text, float size, PdfWeight weight, bool rtlParagraph)
    {
        var runs = new List<ShapedRun>();
        float x = 0;
        foreach (var run in Bidi.Visual(text, rtlParagraph))
        {
            using var buffer = new HbBuffer();
            using var hbLanguage = new Language(language);
            buffer.AddUtf16(run.Text);
            buffer.Direction = run.Rtl ? Direction.RightToLeft : Direction.LeftToRight;
            buffer.Language = hbLanguage;
            buffer.ClusterLevel = ClusterLevel.MonotoneCharacters;
            buffer.GuessSegmentProperties();
            fonts.Shaper(weight).Shape(buffer);
            var infos = buffer.GlyphInfos;
            var pos = buffer.GlyphPositions;
            var scale = size / fonts.UnitsPerEm(weight);
            var utf8 = Encoding.UTF8.GetBytes(run.Text);
            var offsets = Utf8Offsets(run.Text);
            var glyphs = new ushort[infos.Length];
            var points = new SKPoint[infos.Length];
            var clusters = new uint[infos.Length];
            for (var i = 0; i < infos.Length; i++)
            {
                glyphs[i] = (ushort)infos[i].Codepoint;
                points[i] = new SKPoint(x + pos[i].XOffset * scale, -pos[i].YOffset * scale);
                clusters[i] = (uint)offsets[Math.Min((int)infos[i].Cluster, run.Text.Length)];
                x += pos[i].XAdvance * scale;
            }
            runs.Add(new ShapedRun(glyphs, points, clusters, utf8, size, weight));
        }
        return new ShapedLine(runs, x);
    }

    public float Measure(string text, float size, PdfWeight weight, bool rtl) => Shape(text, size, weight, rtl).Width;

    /// <summary>Greedy word wrap on spaces (long words are split by character). Each returned line is shaped separately.</summary>
    public IReadOnlyList<string> Wrap(string text, float size, PdfWeight weight, bool rtl, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = "";
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (Measure(candidate, size, weight, rtl) <= maxWidth) { current = candidate; continue; }
                if (current.Length > 0) lines.Add(current);
                current = word;
                while (current.Length > 1 && Measure(current, size, weight, rtl) > maxWidth)
                {
                    var cut = current.Length - 1;
                    while (cut > 1 && Measure(current[..cut], size, weight, rtl) > maxWidth) cut--;
                    lines.Add(current[..cut]);
                    current = current[cut..];
                }
            }
            lines.Add(current);
        }
        return lines;
    }

    public void Draw(SKCanvas canvas, ShapedLine line, float x, float baseline, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        foreach (var run in line.Runs)
        {
            if (run.Glyphs.Length == 0) continue;
            using var font = new SKFont(fonts.Typeface(run.Weight), run.Size) { Subpixel = true };
            using var builder = new SKTextBlobBuilder();
            var buffer = builder.AllocatePositionedTextRun(font, run.Glyphs.Length, run.Utf8.Length, null);
            run.Glyphs.CopyTo(buffer.Glyphs);
            run.Positions.CopyTo(buffer.Positions);
            run.Clusters.CopyTo(buffer.Clusters);
            run.Utf8.CopyTo(buffer.Text);
            using var blob = builder.Build();
            if (blob is not null) canvas.DrawText(blob, x, baseline, paint);
        }
    }

    private static int[] Utf8Offsets(string s)
    {
        var map = new int[s.Length + 1];
        var b = 0;
        for (var i = 0; i < s.Length; i++)
        {
            map[i] = b;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length)
            {
                map[i + 1] = b;
                b += Encoding.UTF8.GetByteCount(s.AsSpan(i, 2));
                i++;
            }
            else b += Encoding.UTF8.GetByteCount(s.AsSpan(i, 1));
        }
        map[s.Length] = b;
        return map;
    }
}
