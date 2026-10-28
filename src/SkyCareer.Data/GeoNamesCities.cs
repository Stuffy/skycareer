using System.Globalization;
using SkyCareer.Domain;

namespace SkyCareer.Data;

/// <summary>
/// Cities from the GeoNames dump cities15000.txt (CC BY 4.0, https://www.geonames.org/), used to tell which
/// airports serve big cities and capitals. Each airport gets the most important city around it; a city's
/// population counts fully up to <see cref="FullRadiusKm"/> and fades out at <see cref="MaxRadiusKm"/>,
/// because big airports often sit 20–40 km outside town (MUC, ARN, CDG).
/// </summary>
public sealed class GeoNamesCities
{
    public const string Url = "https://download.geonames.org/export/dump/cities15000.zip";
    public const string EntryName = "cities15000.txt";
    public const double FullRadiusKm = 35;
    public const double MaxRadiusKm = 80;
    /// <summary>Airports this close to a capital count as capital airports (Arlanda is 37 km from Stockholm).</summary>
    public const double CapitalRadiusKm = 50;

    public sealed record City(string Name, double Latitude, double Longitude, int Population, bool IsCapital);
    public readonly record struct Match(string City, int Population, bool IsCapital);

    private readonly Dictionary<(int, int), List<City>> _grid = [];

    public int Count { get; private set; }

    public GeoNamesCities(IEnumerable<City> cities)
    {
        foreach (var c in cities)
        {
            var key = Cell(c.Latitude, c.Longitude);
            if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = [];
            list.Add(c);
            Count++;
        }
    }

    /// <summary>Reads the tab-separated GeoNames format (population in column 14, feature code PPLC = capital).</summary>
    public static GeoNamesCities Read(TextReader tsv)
    {
        var cities = new List<City>();
        while (tsv.ReadLine() is { } line)
        {
            var f = line.Split('\t');
            if (f.Length < 15 || f[6] != "P") continue;
            if (!int.TryParse(f[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pop) || pop <= 0) continue;
            if (!double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
                || !double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)) continue;
            cities.Add(new City(f[1], lat, lon, pop, f[7] == "PPLC"));
        }
        return new GeoNamesCities(cities);
    }

    /// <summary>The city that makes the airport most important (distance-scaled population); capital if one is close.</summary>
    public Match? Find(double latitude, double longitude)
    {
        var here = new GeoPosition(latitude, longitude);
        City? best = null;
        double bestPop = 0;
        bool capital = false;
        var (cy, cx) = Cell(latitude, longitude);
        // Longitude cells shrink towards the poles, so look further sideways there.
        int span = (int)Math.Ceiling(MaxRadiusKm / (111 * Math.Max(0.05, Math.Cos(latitude * Math.PI / 180))));
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -Math.Min(span, 180); dx <= Math.Min(span, 179); dx++)
        {
            if (!_grid.TryGetValue((cy + dy, Wrap(cx + dx)), out var list)) continue;
            foreach (var c in list)
            {
                double km = here.DistanceNmTo(new GeoPosition(c.Latitude, c.Longitude)) * 1.852;
                if (km >= MaxRadiusKm) continue;
                double pop = c.Population * (km <= FullRadiusKm ? 1 : (MaxRadiusKm - km) / (MaxRadiusKm - FullRadiusKm));
                if (c.IsCapital && km <= CapitalRadiusKm) capital = true;
                if (pop > bestPop)
                {
                    best = c;
                    bestPop = pop;
                }
            }
        }
        return best is null ? null : new Match(best.Name, (int)Math.Round(bestPop), capital);
    }

    // 1° cells; one cell is 111 km north-south, more than MaxRadiusKm.
    private static (int, int) Cell(double lat, double lon) => ((int)Math.Floor(lat), Wrap((int)Math.Floor(lon)));
    private static int Wrap(int lonCell) => ((lonCell + 180) % 360 + 360) % 360 - 180;
}
