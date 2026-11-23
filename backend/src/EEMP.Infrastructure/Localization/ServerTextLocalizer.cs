using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace EEMP.Infrastructure.Localization;

/// <summary>
/// Localizes server-generated text (problem titles) from embedded JSON catalogs: fa (default), en, tr, ar.
/// Clients must translate the stable <c>code</c>; this text is a human-readable fallback.
/// </summary>
public interface IServerTextLocalizer
{
    string Get(string key, CultureInfo? culture = null);
    bool HasKey(string language, string key);
    IReadOnlyCollection<string> Keys(string language);
}

public sealed class ServerTextLocalizer : IServerTextLocalizer
{
    public const string DefaultLanguage = "fa";
    public static readonly string[] Languages = ["fa", "en", "tr", "ar"];
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _catalogs = new();

    public string Get(string key, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        var lang = culture.TwoLetterISOLanguageName;
        if (Catalog(lang).TryGetValue(key, out var value)) return value;
        if (Catalog(DefaultLanguage).TryGetValue(key, out value)) return value;
        if (Catalog("en").TryGetValue(key, out value)) return value;
        return key;
    }

    public bool HasKey(string language, string key) => Catalog(language).ContainsKey(key);

    public IReadOnlyCollection<string> Keys(string language) => Catalog(language).Keys.ToList();

    private IReadOnlyDictionary<string, string> Catalog(string lang) => _catalogs.GetOrAdd(lang, static l =>
    {
        if (!Languages.Contains(l)) return new Dictionary<string, string>();
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($"messages.{l}.json", StringComparison.Ordinal));
        if (name is null) return new Dictionary<string, string>();
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    });
}
