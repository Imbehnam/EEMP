using System.Globalization;
using System.Text.RegularExpressions;
using EEMP.SharedKernel.Errors;

namespace EEMP.SharedKernel.Validation;

/// <summary>Collects field errors as stable codes (validation.*). Field names are camelCase to match the JSON contract.</summary>
public sealed partial class Validator
{
    public const string Required = "validation.required";
    public const string MaxLength = "validation.max_length";
    public const string InvalidFormat = "validation.invalid_format";
    public const string InvalidValue = "validation.invalid_value";
    public const string UnknownPermission = "validation.unknown_permission";
    public const string UnsupportedLocale = "validation.unsupported_locale";
    public const string InvalidTimeZone = "validation.invalid_timezone";
    public const string PasswordTooWeak = "validation.password_too_weak";

    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public Validator Add(string field, string code)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
        if (!list.Contains(code)) list.Add(code);
        return this;
    }

    public Validator RequiredText(string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, Required);
        else if (value.Length > maxLength) Add(field, MaxLength);
        return this;
    }

    public Validator OptionalText(string field, string? value, int maxLength)
    {
        if (value is not null && value.Length > maxLength) Add(field, MaxLength);
        return this;
    }

    public Validator Email(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && (value.Length > 256 || !EmailRegex().IsMatch(value))) Add(field, InvalidFormat);
        return this;
    }

    public Validator Phone(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !PhoneRegex().IsMatch(value)) Add(field, InvalidFormat);
        return this;
    }

    public Validator Slug(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, Required);
        else if (!SlugRegex().IsMatch(value)) Add(field, InvalidFormat);
        return this;
    }

    public Validator Locale(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, Required);
        else if (!SupportedLocales.IsSupported(value)) Add(field, UnsupportedLocale);
        return this;
    }

    public Validator TimeZone(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, Required);
        else if (!TimeZoneInfo.TryFindSystemTimeZoneById(value, out _)) Add(field, InvalidTimeZone);
        return this;
    }

    public Validator RowVersion(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { Add(field, Required); return this; }
        Span<byte> buffer = stackalloc byte[64];
        if (!Convert.TryFromBase64String(value, buffer, out _)) Add(field, InvalidFormat);
        return this;
    }

    public Validator When(bool condition, string field, string code)
    {
        if (condition) Add(field, code);
        return this;
    }

    public bool IsValid => _errors.Count == 0;

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
            throw new ValidationException(_errors.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.Ordinal));
    }

    public static DateOnly? ParseDate(string? value, string field, Validator v)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        v.Add(field, InvalidFormat);
        return null;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^\+?[0-9 \-]{5,20}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9\-]{0,61}[a-z0-9])?$")]
    private static partial Regex SlugRegex();
}

public static class SupportedLocales
{
    public const string Default = "fa-IR";
    public static readonly IReadOnlyList<string> All = ["fa-IR", "en", "tr", "ar"];
    public static bool IsSupported(string locale) => All.Contains(locale, StringComparer.Ordinal);
}
