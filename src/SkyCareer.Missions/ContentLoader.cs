using System.Text.Json;
using System.Text.Json.Serialization;
using SkyCareer.Domain.Aircraft;

namespace SkyCareer.Missions;

public sealed record ContentError(string File, string Message);

public sealed record GameContent(
    IReadOnlyList<AircraftProfile> Aircraft,
    IReadOnlyList<MissionType> MissionTypes,
    IReadOnlyList<ContentError> Errors)
{
    /// <summary>File each aircraft profile was loaded from (after overrides).</summary>
    public IReadOnlyDictionary<string, string> AircraftFiles { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Loads data-driven content (aircraft profiles and mission types) from JSON files. A broken file is
/// reported and skipped instead of stopping the app, so community content can't brick a career.
/// Several roots can be layered: an id in a later root replaces the same id from an earlier one, which lets
/// user profiles override the shipped ones.
/// </summary>
public static class ContentLoader
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static GameContent Load(params string[] contentRoots)
    {
        var errors = new List<ContentError>();
        var aircraft = new Dictionary<string, (AircraftProfile Item, string File)>(StringComparer.OrdinalIgnoreCase);
        var missions = new Dictionary<string, (MissionType Item, string File)>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in contentRoots)
        {
            Merge(LoadDir<AircraftProfile>(Path.Combine(root, "aircraft"), errors), a => a.Id, aircraft, "aircraft", errors);
            Merge(LoadDir<MissionType>(Path.Combine(root, "missions"), errors), m => m.Id, missions, "mission type", errors);
        }

        return new GameContent(
            aircraft.Values.Select(v => v.Item).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            missions.Values.Select(v => v.Item).ToList(),
            errors)
        {
            AircraftFiles = aircraft.ToDictionary(kv => kv.Key, kv => kv.Value.File, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static void Merge<T>(List<(T Item, string File)> loaded, Func<T, string> id, Dictionary<string, (T, string)> target,
        string kind, List<ContentError> errors)
    {
        // Duplicates inside one root are content mistakes; across roots they are intended overrides.
        foreach (var dup in loaded.GroupBy(x => id(x.Item), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add(new ContentError(dup.First().File, $"duplicate {kind} id '{dup.Key}'"));
        foreach (var x in loaded) target[id(x.Item)] = x;
    }

    private static List<(T Item, string File)> LoadDir<T>(string dir, List<ContentError> errors)
    {
        var result = new List<(T, string)>();
        if (!Directory.Exists(dir)) return result;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order())
        {
            try
            {
                var item = JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json);
                if (item is null) errors.Add(new ContentError(file, "empty file"));
                else result.Add((item, file));
            }
            catch (JsonException ex)
            {
                errors.Add(new ContentError(file, ex.Message));
            }
        }
        return result;
    }
}
