using System.Text.RegularExpressions;
using SkyCareer.Localization;

namespace SkyCareer.Domain.Tests;

public partial class TranslationTests
{
    /// <summary>Literal keys of <c>T("…")</c> / <c>F("…")</c> / <c>L.T("…")</c> / <c>L.F("…")</c> calls (also <c>@T("…")</c> in Razor).</summary>
    [GeneratedRegex("""(?<![\w.])(?:L\.)?[TF]\(\s*"((?:[^"\\]|\\.)*)"(?=\s*[,)])""")]
    private static partial Regex KeyCall();

    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex Placeholder();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static HashSet<string> UsedKeys()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var files = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
            foreach (Match m in KeyCall().Matches(File.ReadAllText(file)))
                keys.Add(Regex.Unescape(m.Groups[1].Value));
        return keys;
    }

    [Fact]
    public void Every_ui_text_has_a_german_translation_with_the_same_placeholders()
    {
        var used = UsedKeys();
        var german = L.Dictionary(L.German);
        Assert.True(used.Count > 100, $"only {used.Count} texts found – is the scan broken?");

        var missing = used.Where(k => !german.ContainsKey(k)).Order().ToList();
        Assert.True(missing.Count == 0, "Missing German translations:\n" + string.Join("\n", missing));

        var orphaned = german.Keys.Where(k => !used.Contains(k)).Order().ToList();
        Assert.True(orphaned.Count == 0, "German entries no longer used:\n" + string.Join("\n", orphaned));

        var mismatched = german
            .Where(e => !Placeholders(e.Key).SetEquals(Placeholders(e.Value)))
            .Select(e => $"{e.Key} → {e.Value}").ToList();
        Assert.True(mismatched.Count == 0, "Placeholders differ:\n" + string.Join("\n", mismatched));
    }

    private static HashSet<string> Placeholders(string text) => Placeholder().Matches(text).Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void Texts_are_translated_and_fall_back_to_english()
    {
        // TextIn instead of SetLanguage: the language is app-wide and other tests run in parallel.
        Assert.Equal("Sprache", L.TextIn(L.German, "Language"));
        Assert.Equal("Not translated at all", L.TextIn(L.German, "Not translated at all"));
        Assert.Equal("Language", L.TextIn(L.English, "Language"));
        Assert.Equal("Language", L.T("Language"));   // tests run in English
        Assert.Equal("de-DE", L.CultureFor(L.German).Name);
    }
}
