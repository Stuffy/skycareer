using System.Globalization;

namespace SkyCareer.Domain.World;

/// <summary>
/// A point a sightseeing or survey flight has to pass: within <see cref="RadiusNm"/> and, where set, inside the height
/// band above ground. Stored on the contract as text ("name|lat|lon|minAgl|maxAgl;…").
/// </summary>
public sealed record TourWaypoint(string Name, GeoPosition Position, double? MinAglFt = null, double? MaxAglFt = null)
{
    /// <summary>A waypoint counts as passed within this distance.</summary>
    public const double RadiusNm = 1.5;

    public bool PassedBy(GeoPosition position, double aglFt) =>
        position.DistanceNmTo(Position) <= RadiusNm && aglFt >= (MinAglFt ?? double.MinValue) && aglFt <= (MaxAglFt ?? double.MaxValue);

    /// <summary>Waypoint id for flight plans and GPS units: letters and digits of the name, at most 6 ("Heidelberg" → "HEIDEL").</summary>
    public string Ident
    {
        get
        {
            var id = new string(Name.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
            return id.Length == 0 ? "WPT" : id.Length > 6 ? id[..6] : id;
        }
    }

    public static string Serialize(IEnumerable<TourWaypoint> waypoints) => string.Join(";", waypoints.Select(w => string.Join("|",
        w.Name.Replace("|", "/").Replace(";", ","),
        w.Position.Latitude.ToString("0.######", CultureInfo.InvariantCulture),
        w.Position.Longitude.ToString("0.######", CultureInfo.InvariantCulture),
        w.MinAglFt?.ToString("0", CultureInfo.InvariantCulture) ?? "",
        w.MaxAglFt?.ToString("0", CultureInfo.InvariantCulture) ?? "")));

    public static IReadOnlyList<TourWaypoint> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<TourWaypoint>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split('|');
            if (f.Length < 3 || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
            double? Height(int i) => f.Length > i && double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            result.Add(new TourWaypoint(f[0], new GeoPosition(lat, lon), Height(3), Height(4)));
        }
        return result;
    }

    /// <summary>Length of the round trip from <paramref name="home"/> over the waypoints and back, in nm.</summary>
    public static double TourLengthNm(GeoPosition home, IReadOnlyList<TourWaypoint> waypoints)
    {
        double total = 0;
        var at = home;
        foreach (var w in waypoints)
        {
            total += at.DistanceNmTo(w.Position);
            at = w.Position;
        }
        return total + at.DistanceNmTo(home);
    }
}
