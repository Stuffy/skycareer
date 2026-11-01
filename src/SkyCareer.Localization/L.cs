using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace SkyCareer.Localization;

/// <summary>
/// UI texts. The English source text is the key: <see cref="T"/> returns the translation for the current UI language
/// (embedded <c>&lt;language&gt;/*.json</c> dictionaries, one per app area) and falls back to the English key, so
/// English needs no dictionary. Logs stay English and don't go through here.
/// </summary>
public static class L
{
    public const string English = "en";
    public const string German = "de";

    /// <summary>Selectable languages with their own names.</summary>
    public static IReadOnlyList<(string Code, string Name)> Languages { get; } = [(English, "English"), (German, "Deutsch")];

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Dictionaries = new();
    private static volatile string _language = English;

    /// <summary>
    /// The app's UI language. Kept app-wide on purpose: Blazor work items restore the thread culture they captured, so
    /// reading the thread's UI culture would keep the old language after a switch.
    /// </summary>
    public static string Language => _language;

    /// <summary>Culture of <see cref="Language"/>, used for the numbers and dates inside <see cref="F"/> and <see cref="Eur"/>.</summary>
    public static CultureInfo Culture => CultureFor(_language);

    /// <summary>The text in the current language (the English source if there's no translation).</summary>
    public static string T(string english) => TextIn(_language, english);

    /// <summary>The text in <paramref name="language"/> (the English source if there's no translation).</summary>
    public static string TextIn(string language, string english) =>
        language == English ? english : Dictionary(language).TryGetValue(english, out var text) ? text : english;

    /// <summary>Formatted text: <paramref name="english"/> uses {0}, {1} … placeholders, formatted in the UI language's culture.</summary>
    public static string F(string english, params object?[] args) => string.Format(Culture, T(english), args);

    /// <summary>A euro amount without decimals (en "€12,345", de "12.345 €").</summary>
    public static string Eur(decimal amount) => _language == German
        ? string.Format(Culture, "{0:N0} €", amount)
        : string.Format(Culture, "{0}€{1:N0}", amount < 0 ? "−" : "", Math.Abs(amount));

    /// <summary>The Windows display language if supported, else English.</summary>
    public static string SystemLanguage => Supported(CultureInfo.InstalledUICulture.TwoLetterISOLanguageName);

    /// <summary>
    /// Switches the texts at once and the default number and date formats for new threads; null = Windows language.
    /// Formatting outside <see cref="F"/>/<see cref="Eur"/> on threads that already run follows after a restart.
    /// </summary>
    public static void SetLanguage(string? code)
    {
        _language = code is null ? SystemLanguage : Supported(code);
        var culture = CultureFor(_language);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
    }

    public static CultureInfo CultureFor(string code) => CultureInfo.GetCultureInfo(code == German ? "de-DE" : "en-GB");

    private static string Supported(string code) => Languages.Any(l => l.Code == code) ? code : English;

    /// <summary>All translations of a language (for tests and tools).</summary>
    public static IReadOnlyDictionary<string, string> Dictionary(string language) => Dictionaries.GetOrAdd(language, Load);

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        var assembly = typeof(L).Assembly;
        var prefix = $"SkyCareer.Localization.{language}\\";
        var altPrefix = $"SkyCareer.Localization.{language}/";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix) || n.StartsWith(altPrefix)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            var entries = JsonSerializer.Deserialize(stream, LocalizationJson.Default.DictionaryStringString) ?? [];
            foreach (var (key, value) in entries) result[key] = value;
        }
        return result;
    }
}
