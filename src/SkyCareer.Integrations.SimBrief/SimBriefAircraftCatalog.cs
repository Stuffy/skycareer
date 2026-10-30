using System.Globalization;
using System.Text.Json;

namespace SkyCareer.Integrations.SimBrief;

/// <summary>
/// One aircraft type from SimBrief's public type list, with the limits of its default airframe in kg.
/// <see cref="CruiseKtas"/> is derived from SimBrief's cruise speed text and only approximate.
/// </summary>
public sealed record SimBriefType(
    string Id,
    string Icao,
    string Name,
    string Engines,
    int MaxPassengers,
    double OperatingEmptyKg,
    double MaxZeroFuelKg,
    double MaxTakeoffKg,
    double MaxLandingKg,
    double MaxFuelKg,
    double PassengerWeightKg,
    double BagWeightKg,
    double FuelFlowKgPerHour,
    string SpeedText,
    double CruiseKtas,
    double CeilingFt,
    double TakeoffFt,
    double LandingFt,
    bool IsCargo,
    string SearchText)
{
    /// <summary>SimBrief gives jets' cruise speed as a Mach number and propeller aircraft's in KIAS.</summary>
    public bool CruisesAtMach => SpeedText.StartsWith("Mach", StringComparison.OrdinalIgnoreCase);

    public string Label => $"{Icao} – {Name}";
}

/// <summary>
/// SimBrief's public aircraft type list (<c>inputs.airframes.json</c>, no API key). Only each type's default airframe is
/// used: the list also carries other users' airframes, which are ignored and never stored.
/// </summary>
public sealed class SimBriefAircraftCatalog
{
    public const string Url = "https://www.simbrief.com/api/inputs.airframes.json";
    public static readonly TimeSpan CacheAge = TimeSpan.FromDays(7);
    private const double KgPerLb = 0.45359237;

    public SimBriefAircraftCatalog(IReadOnlyList<SimBriefType> types) => Types = types;

    public IReadOnlyList<SimBriefType> Types { get; }

    public SimBriefType? Find(string id) => Types.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Types whose ICAO code, name or SimBrief search words contain every word of <paramref name="query"/>.</summary>
    public IEnumerable<SimBriefType> Search(string? query)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Types
            .Where(t => words.All(w => t.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => words.Length > 0 && t.Icao.Equals(words[0], StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t.Icao, StringComparer.Ordinal);
    }

    /// <summary>
    /// Loads the list from <paramref name="cacheFile"/>, downloading a fresh copy when the cache is missing or older than
    /// <see cref="CacheAge"/>. Without a connection an outdated cache is still used.
    /// </summary>
    public static async Task<SimBriefAircraftCatalog> LoadAsync(HttpClient http, string cacheFile, CancellationToken ct = default)
    {
        bool fresh = File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < CacheAge;
        if (!fresh)
        {
            try
            {
                string json = await http.GetStringAsync(Url, ct).ConfigureAwait(false);
                var catalog = Parse(json);
                if (catalog.Types.Count > 0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                    // Store only the parsed default airframes, never the other users' ones in the raw list.
                    await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(catalog.Types), ct).ConfigureAwait(false);
                    return catalog;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
            {
                if (!File.Exists(cacheFile)) throw new InvalidOperationException($"SimBrief-Typenliste nicht erreichbar: {ex.Message}", ex);
            }
        }
        var cached = JsonSerializer.Deserialize<List<SimBriefType>>(await File.ReadAllTextAsync(cacheFile, ct).ConfigureAwait(false)) ?? [];
        return new SimBriefAircraftCatalog(cached);
    }

    /// <summary>Parses SimBrief's raw type list (a JSON object keyed by type id).</summary>
    public static SimBriefAircraftCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var types = new List<SimBriefType>();
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            var t = entry.Value;
            if (t.ValueKind != JsonValueKind.Object) continue;
            // A few types only come with user airframes: then just the type-level MTOW and seats are used.
            var o = DefaultAirframe(t) ?? default;
            bool hasDefault = o.ValueKind == JsonValueKind.Object;
            double unit = hasDefault && Str(o, "wgtunits")?.Equals("KGS", StringComparison.OrdinalIgnoreCase) == true ? 1 : KgPerLb;
            double Kg(string name) => hasDefault ? Math.Round(Num(o, name) * unit) : 0;
            string speed = Str(t, "aircraft_speed") ?? "";
            double ceiling = Num(t, "aircraft_ceiling");
            types.Add(new SimBriefType(
                Id: Str(t, "aircraft_id") ?? entry.Name,
                Icao: Str(t, "aircraft_icao") ?? entry.Name,
                Name: Str(t, "aircraft_name") ?? entry.Name,
                Engines: Str(t, "aircraft_engines") ?? "",
                MaxPassengers: (int)(hasDefault ? Num(o, "maxpax") : Num(t, "aircraft_passengers")),
                OperatingEmptyKg: Kg("oew"),
                MaxZeroFuelKg: Kg("mzfw"),
                MaxTakeoffKg: hasDefault ? Kg("mtow") : Num(t, "aircraft_mtow_kgs"),
                MaxLandingKg: Kg("mlw"),
                MaxFuelKg: Kg("maxfuel"),
                // Passenger and bag weights are always given in lb (paxwgt/bagwgt), whatever the weight unit.
                PassengerWeightKg: hasDefault ? Math.Round(Num(o, "paxwgt") * KgPerLb, 2) : 0,
                BagWeightKg: hasDefault ? Math.Round(Num(o, "bagwgt") * KgPerLb, 2) : 0,
                FuelFlowKgPerHour: Num(t, "aircraft_fuelflow_kgs"),
                SpeedText: speed,
                CruiseKtas: CruiseKtas(speed, ceiling),
                CeilingFt: ceiling,
                TakeoffFt: Num(t, "aircraft_takeoff"),
                LandingFt: Num(t, "aircraft_landing"),
                IsCargo: t.TryGetProperty("aircraft_is_cargo", out var cargo) && cargo.ValueKind == JsonValueKind.True,
                SearchText: string.Join(' ', entry.Name, Str(t, "aircraft_icao"), Str(t, "aircraft_name"), Str(t, "aircraft_search"))));
        }
        return new SimBriefAircraftCatalog(types.OrderBy(t => t.Icao, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Approximate cruise TAS: "Mach 0.78" → 0.78 × 573.6 kt (speed of sound above FL360, ISA); "150 KIAS" → IAS raised by
    /// 2 % per 1000 ft at 70 % of the ceiling, capped at FL250.
    /// </summary>
    public static double CruiseKtas(string speed, double ceilingFt)
    {
        var parts = speed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0].Equals("Mach", StringComparison.OrdinalIgnoreCase) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mach))
            return Math.Round(mach * 573.6);
        if (parts.Length == 2 && parts[1].Equals("KIAS", StringComparison.OrdinalIgnoreCase) && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var kias))
            return Math.Round(kias * (1 + 0.02 * Math.Min(ceilingFt * 0.7, 25000) / 1000));
        return 0;
    }

    /// <summary>The airframe SimBrief ships for the type (<c>airframe_id: false</c>); user airframes are skipped.</summary>
    private static JsonElement? DefaultAirframe(JsonElement type)
    {
        if (!type.TryGetProperty("airframes", out var frames) || frames.ValueKind != JsonValueKind.Array) return null;
        foreach (var f in frames.EnumerateArray())
            if (f.TryGetProperty("airframe_id", out var id) && id.ValueKind == JsonValueKind.False &&
                f.TryGetProperty("airframe_options", out var options) && options.ValueKind == JsonValueKind.Object)
                return options;
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => 0
        };
    }
}
