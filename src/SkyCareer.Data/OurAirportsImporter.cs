using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.World;

namespace SkyCareer.Data;

/// <summary>
/// Imports airports and runways from the public-domain OurAirports dataset
/// (https://ourairports.com/data/: airports.csv + runways.csv). Closed fields and balloon ports are skipped;
/// small airports without any open runway are skipped too because no mission could use them.
/// Optionally matches each airport to the city it serves (<see cref="GeoNamesCities"/>).
/// </summary>
public static class OurAirportsImporter
{
    public const string AirportsUrl = "https://davidmegginson.github.io/ourairports-data/airports.csv";
    public const string RunwaysUrl = "https://davidmegginson.github.io/ourairports-data/runways.csv";

    public static async Task<int> ImportAsync(SkyCareerDb db, TextReader airportsCsv, TextReader runwaysCsv, GeoNamesCities? cities = null,
        CancellationToken ct = default)
    {
        var runways = ReadRunways(runwaysCsv);
        var rows = new List<AirportRow>();

        var header = Index(ParseLine(airportsCsv.ReadLine() ?? throw new InvalidDataException("airports.csv is empty")));
        while (airportsCsv.ReadLine() is { } line)
        {
            var f = ParseLine(line);
            if (f.Count < header.Count) continue;
            string ident = f[header["ident"]];
            AirportSize? size = f[header["type"]] switch
            {
                "large_airport" => AirportSize.Large,
                "medium_airport" => AirportSize.Medium,
                "small_airport" => AirportSize.Small,
                "heliport" => AirportSize.Heliport,
                "seaplane_base" => AirportSize.Seaplane,
                _ => null
            };
            if (size is null || string.IsNullOrWhiteSpace(ident)) continue;

            runways.TryGetValue(ident, out var rw);
            if (rw is null && size is AirportSize.Small or AirportSize.Medium or AirportSize.Large) continue;

            var surfaces = new HashSet<RunwaySurface>(rw?.Surfaces ?? []);
            if (size == AirportSize.Seaplane) surfaces.Add(RunwaySurface.Water);
            if (size == AirportSize.Heliport && surfaces.Count == 0) surfaces.Add(RunwaySurface.Paved);

            double lat = Num(f[header["latitude_deg"]]), lon = Num(f[header["longitude_deg"]]);
            var city = cities?.Find(lat, lon);
            rows.Add(new AirportRow
            {
                Ident = ident,
                Name = f[header["name"]],
                Latitude = lat,
                Longitude = lon,
                Size = size.Value,
                LongestRunwayM = Math.Round(rw?.LongestM ?? 0),
                LongestPavedRunwayM = Math.Round(rw?.LongestPavedM ?? 0),
                HasLightedRunway = rw?.Lighted ?? false,
                ScheduledService = header.TryGetValue("scheduled_service", out var sched) && f[sched] == "yes",
                Surfaces = string.Join(",", surfaces),
                Country = f[header["iso_country"]],
                Municipality = f[header["municipality"]],
                ElevationFt = header.TryGetValue("elevation_ft", out var e) ? Num(f[e]) : 0,
                City = city?.City ?? "",
                CityPopulation = city?.Population ?? 0,
                IsCapital = city?.IsCapital ?? false,
                IsMilitary = IsMilitaryField(f[header["name"]], header.TryGetValue("keywords", out var kw) ? f[kw] : "",
                    header.TryGetValue("scheduled_service", out var sv) && f[sv] == "yes")
            });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Airports.ExecuteDeleteAsync(ct);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        foreach (var chunk in rows.Chunk(5000))
        {
            db.Airports.AddRange(chunk);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        await tx.CommitAsync(ct);
        return rows.Count;
    }

    // OurAirports has no military flag, but military fields are named consistently ("Ramstein Air Base",
    // "Celle Army Airfield", "RAF Lakenheath", "NAS Whidbey Island", "Fliegerhorst …").
    private static readonly Regex MilitaryName = new(
        @"\b(air base|air force base|afb|army air ?field|aaf|army heliport|naval air (station|facility)|naval station|nas|nolf|" +
        @"marine corps|mcas|air station|joint base|air national guard|ang base|raf|fliegerhorst|heeresflugplatz|" +
        @"base a[ée]rienne|base a[ée]rea|military|militar|militaire)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MilitaryKeyword = new(@"\b(fliegerhorst|militärflugplatz|military air(port|field))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Military by name or keyword. Joint-use fields with scheduled service ("Baghdad International Airport /
    /// New Al Muthana Air Base") count as civil.
    /// </summary>
    public static bool IsMilitaryField(string name, string keywords, bool scheduledService) =>
        !scheduledService && (MilitaryName.IsMatch(name) || MilitaryKeyword.IsMatch(keywords));

    /// <summary>Aggregate of an airport's open runways.</summary>
    private sealed class RunwayInfo
    {
        public double LongestM;
        public double LongestPavedM;
        public bool Lighted;
        public readonly HashSet<RunwaySurface> Surfaces = [];
    }

    private static Dictionary<string, RunwayInfo> ReadRunways(TextReader csv)
    {
        var result = new Dictionary<string, RunwayInfo>(StringComparer.OrdinalIgnoreCase);
        var header = Index(ParseLine(csv.ReadLine() ?? throw new InvalidDataException("runways.csv is empty")));
        header.TryGetValue("lighted", out int lightedCol);
        while (csv.ReadLine() is { } line)
        {
            var f = ParseLine(line);
            if (f.Count < header.Count || f[header["closed"]] == "1") continue;
            string ident = f[header["airport_ident"]];
            double lengthM = Num(f[header["length_ft"]]) * 0.3048;
            var surface = MapSurface(f[header["surface"]]);

            if (!result.TryGetValue(ident, out var info))
                result[ident] = info = new RunwayInfo();
            if (surface is { } s) info.Surfaces.Add(s);
            info.LongestM = Math.Max(info.LongestM, lengthM);
            if (surface == RunwaySurface.Paved) info.LongestPavedM = Math.Max(info.LongestPavedM, lengthM);
            if (header.ContainsKey("lighted") && f[lightedCol] == "1") info.Lighted = true;
        }
        return result;
    }

    /// <summary>OurAirports surfaces are free text (ASP, ASPH-G, CON, TURF, GRVL, …); map them by keyword.</summary>
    public static RunwaySurface? MapSurface(string raw)
    {
        var s = raw.ToUpperInvariant();
        if (s.Length == 0) return null;
        if (s.Contains("WATER")) return RunwaySurface.Water;
        if (s.Contains("SNOW") || s.Contains("ICE")) return RunwaySurface.Snow;
        if (s.Contains("ASP") || s.Contains("CON") || s.Contains("BIT") || s.Contains("PEM") || s.Contains("TAR") || s.Contains("PAV") || s.Contains("MAC"))
            return RunwaySurface.Paved;
        if (s.Contains("GRV") || s.Contains("GRAVEL") || s.Contains("COR") || s.Contains("SHALE")) return RunwaySurface.Gravel;
        if (s.Contains("GRS") || s.Contains("GRASS") || s.Contains("TURF")) return RunwaySurface.Grass;
        if (s.Contains("DIRT") || s.Contains("CLA") || s.Contains("SAN") || s.Contains("SOIL") || s.Contains("LAT") || s.Contains("EARTH"))
            return RunwaySurface.Dirt;
        return null;
    }

    private static Dictionary<string, int> Index(List<string> header) =>
        header.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i, StringComparer.OrdinalIgnoreCase);

    private static double Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    /// <summary>RFC 4180 line parser (quoted fields, doubled quotes). OurAirports has no multi-line fields.</summary>
    public static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
