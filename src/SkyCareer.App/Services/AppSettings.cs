using System.Text.Json;
using SkyCareer.Data;

namespace SkyCareer.App.Services;

/// <summary>
/// App-wide settings that don't belong to a career, stored as JSON next to the database (so SKYCAREER_DB test setups
/// get their own copy). A missing or broken file means defaults.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _lock = new();
    private Values _values;

    public AppSettings() : this(Path.Combine(Path.GetDirectoryName(SkyCareerDb.DefaultPath)!, "appsettings.json"))
    {
    }

    public AppSettings(string path)
    {
        _path = path;
        try
        {
            _values = File.Exists(path) ? JsonSerializer.Deserialize<Values>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _values = new();
        }
    }

    /// <summary>Shows the debug menu, debug tools and developer functions (replay, free relocation).</summary>
    public bool DebugMode => _values.DebugMode;

    /// <summary>UI language ("en", "de"); null follows the Windows display language.</summary>
    public string? Language => _values.Language;

    public event Action? Changed;

    public void SetDebugMode(bool enabled) => Update(v => v with { DebugMode = enabled });

    /// <summary>Stores the UI language and applies it to the whole app (texts, number and date formats).</summary>
    public void SetLanguage(string? code)
    {
        Update(v => v with { Language = code });
        Localization.L.SetLanguage(code);
    }

    private void Update(Func<Values, Values> change)
    {
        lock (_lock)
        {
            var changed = change(_values);
            if (changed == _values) return;
            _values = changed;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_values, Json));
        }
        Changed?.Invoke();
    }

    private sealed record Values
    {
        public bool DebugMode { get; init; }
        public string? Language { get; init; }
    }
}
