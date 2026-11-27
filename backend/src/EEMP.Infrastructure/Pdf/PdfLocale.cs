using System.Globalization;
using System.Text;

namespace EEMP.Infrastructure.Pdf;

/// <summary>
/// Language, direction, digits and calendar for generated documents, matching the web apps (OD-020):
/// fa = Jalali calendar with Persian digits, ar = Gregorian with Arabic-Indic digits, en/tr = Gregorian with Latin digits.
/// </summary>
public sealed class PdfLocale
{
    public static readonly IReadOnlyList<string> Languages = ["fa", "en", "tr", "ar"];

    private PdfLocale(string language)
    {
        Language = language;
        Rtl = language is "fa" or "ar";
        Culture = language switch
        {
            "fa" => new CultureInfo("fa-IR"),
            "ar" => ArabicGregorian(),
            "tr" => new CultureInfo("tr-TR"),
            _ => new CultureInfo("en-US"),
        };
    }

    public string Language { get; }
    public bool Rtl { get; }
    public CultureInfo Culture { get; }

    /// <summary>Resolves "fa-IR", "fa", "en-US", "tr", "ar-SA", … to a supported language; null when unsupported.</summary>
    public static PdfLocale? TryResolve(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var lang = tag.Trim().Split('-', '_')[0].ToLowerInvariant();
        return Languages.Contains(lang) ? new PdfLocale(lang) : null;
    }

    private static CultureInfo ArabicGregorian()
    {
        var c = (CultureInfo)new CultureInfo("ar").Clone();
        c.DateTimeFormat.Calendar = new GregorianCalendar();
        return c;
    }

    /// <summary>Replaces ASCII digits (and a decimal point between digits) with the locale's native digits.</summary>
    public string Digits(string value)
    {
        char zero = Language switch { "fa" => '\u06F0', "ar" => '\u0660', _ => '0' };
        if (zero == '0') return value;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is >= '0' and <= '9') sb.Append((char)(zero + (c - '0')));
            else if (c == '.' && i > 0 && char.IsAsciiDigit(value[i - 1]) && i + 1 < value.Length && char.IsAsciiDigit(value[i + 1])) sb.Append('\u066B');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public string Number(decimal value, int maxDecimals = 2)
    {
        var format = maxDecimals <= 0 ? "0" : "0." + new string('#', maxDecimals);
        return Digits(value.ToString(format, CultureInfo.InvariantCulture));
    }

    public string Integer(int value) => Digits(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A calendar date (Jalali for fa, e.g. "۵ آذر ۱۴۰۵"; Gregorian otherwise, e.g. "26 November 2026").</summary>
    public string Date(DateOnly date) => Digits(date.ToDateTime(TimeOnly.MinValue).ToString("d MMMM yyyy", Culture));

    /// <summary>An instant shown in the tenant time zone with hours and minutes.</summary>
    public string DateTime(DateTime utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(System.DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        return Digits(local.ToString("d MMMM yyyy HH:mm", Culture));
    }

    public static TimeZoneInfo Zone(string id) => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) ? tz : TimeZoneInfo.Utc;
}
