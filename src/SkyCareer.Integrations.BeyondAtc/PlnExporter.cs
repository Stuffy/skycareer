using System.Globalization;
using System.Xml.Linq;
using SkyCareer.Domain;
using SkyCareer.Domain.World;

namespace SkyCareer.Integrations.BeyondAtc;

public enum PlanRules { Vfr, Ifr }

/// <summary>
/// Writes an MSFS flight plan (.pln, AceXML). BeyondATC has no public API but imports .pln files (and the MSFS
/// world map / EFB can load the same file), so this is how missions without a SimBrief OFP – VFR, bush,
/// short hops – reach ATC. Direct routing between the two airports; IFR routes should come from SimBrief.
/// MSFS only loads plans that start and end at airports it knows, so an airfield the sim doesn't know
/// (<see cref="Airport.KnownToSim"/>, e.g. OurAirports strips like "BR-2197") becomes a user waypoint with its coordinates
/// next to an anchor airport the sim knows: departure = anchor → strip → …, or … → strip → destination = anchor. The GPS
/// then leads to the strip; the leg beyond it is simply not flown. Without an anchor the strip stays the endpoint.
/// </summary>
public static class PlnExporter
{
    /// <param name="originAnchor">Airport the sim knows near an unknown origin (departure of the plan).</param>
    /// <param name="destinationAnchor">Airport the sim knows near an unknown destination (end of the plan).</param>
    /// <param name="via">Waypoints of a round trip, written as user waypoints between departure and destination.</param>
    public static XDocument Build(Airport origin, Airport destination, PlanRules rules, int? cruiseAltitudeFt = null, string? title = null,
        Airport? originAnchor = null, Airport? destinationAnchor = null, IReadOnlyList<TourWaypoint>? via = null)
    {
        int altitude = cruiseAltitudeFt ?? CruiseAltitude(origin.Position, destination.Position, rules);
        string fpType = rules == PlanRules.Vfr ? "VFR" : "IFR";
        string routeType = rules == PlanRules.Vfr ? "VFR" : altitude >= 18000 ? "HighAlt" : "LowAlt";
        var first = !origin.KnownToSim && originAnchor is { KnownToSim: true } ? originAnchor : origin;
        var last = !destination.KnownToSim && destinationAnchor is { KnownToSim: true } ? destinationAnchor : destination;

        var plan = new XElement("FlightPlan.FlightPlan",
            new XElement("Title", title ?? $"{origin.Ident} to {destination.Ident}"),
            new XElement("FPType", fpType),
            new XElement("RouteType", routeType),
            new XElement("CruisingAlt", altitude.ToString(CultureInfo.InvariantCulture)),
            new XElement("DepartureID", WaypointId(first)),
            new XElement("DepartureLLA", Lla(first.Position, first.ElevationFt)),
            new XElement("DestinationID", WaypointId(last)),
            new XElement("DestinationLLA", Lla(last.Position, last.ElevationFt)),
            new XElement("Descr", $"{first.Ident}, {last.Ident}"),
            new XElement("DepartureName", first.Name),
            new XElement("DestinationName", last.Name),
            new XElement("AppVersion",
                new XElement("AppVersionMajor", "11"),
                new XElement("AppVersionBuild", "282174")));
        foreach (var point in new[] { first, origin }.Distinct()) plan.Add(Waypoint(point));
        foreach (var w in via ?? [])
            plan.Add(new XElement("ATCWaypoint",
                new XAttribute("id", w.Ident),
                new XElement("ATCWaypointType", "User"),
                new XElement("WorldPosition", Lla(w.Position, Math.Max(altitude, w.MaxAglFt ?? 0)))));
        // A round trip starts and ends at the same airport: it appears again as the last waypoint.
        foreach (var point in new[] { destination, last }.Distinct()) plan.Add(Waypoint(point));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement("SimBase.Document",
                new XAttribute("Type", "AceXML"),
                new XAttribute("version", "1,0"),
                new XElement("Descr", "AceXML Document"),
                plan));
    }

    public static async Task<string> SaveAsync(XDocument plan, string directory, string fileName, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName.EndsWith(".pln", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".pln");
        await using var stream = File.Create(path);
        await plan.SaveAsync(stream, SaveOptions.None, ct);
        return path;
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SkyCareer", "Flugplaene");

    /// <summary>
    /// Semicircular cruising levels: eastbound (true course 0–179°) odd thousands, westbound even thousands,
    /// plus 500 ft for VFR. Height grows with distance so short hops stay low.
    /// </summary>
    public static int CruiseAltitude(GeoPosition from, GeoPosition to, PlanRules rules)
    {
        double distance = from.DistanceNmTo(to);
        bool eastbound = from.InitialCourseTo(to) < 180;
        int thousands = rules == PlanRules.Vfr
            ? distance switch { < 40 => 3, < 120 => 5, _ => 7 }
            : distance switch { < 80 => 9, < 200 => 15, < 400 => 25, _ => 35 };
        if (eastbound != (thousands % 2 == 1)) thousands++;
        return thousands * 1000 + (rules == PlanRules.Vfr ? 500 : 0);
    }

    /// <summary>MSFS position format, e.g. N50° 2' 0.00",E8° 34' 14.00",+000364.00</summary>
    public static string Lla(GeoPosition p, double elevationFt) =>
        $"{Dms(p.Latitude, 'N', 'S')},{Dms(p.Longitude, 'E', 'W')},{elevationFt.ToString("+000000.00;-000000.00", CultureInfo.InvariantCulture)}";

    private static string Dms(double value, char positive, char negative)
    {
        double abs = Math.Abs(value);
        int deg = (int)abs;
        double minutesFull = (abs - deg) * 60;
        int min = (int)minutesFull;
        double sec = Math.Round((minutesFull - min) * 60, 2);
        if (sec >= 60) { sec = 0; min++; }
        if (min >= 60) { min = 0; deg++; }
        return $"{(value >= 0 ? positive : negative)}{deg}° {min}' {sec.ToString("0.00", CultureInfo.InvariantCulture)}\"";
    }

    private static XElement Waypoint(Airport a) => a.KnownToSim
        ? new("ATCWaypoint",
            new XAttribute("id", a.Ident),
            new XElement("ATCWaypointType", "Airport"),
            new XElement("WorldPosition", Lla(a.Position, a.ElevationFt)),
            new XElement("ICAO", new XElement("ICAOIdent", a.Ident)))
        : new("ATCWaypoint",
            new XAttribute("id", WaypointId(a)),
            new XElement("ATCWaypointType", "User"),
            new XElement("WorldPosition", Lla(a.Position, a.ElevationFt)));

    /// <summary>
    /// The airport's ident, or for a user waypoint letters and digits only, at most 6 (GPS units reject "-"; "BR-2197" → "BR2197").
    /// </summary>
    public static string WaypointId(Airport a)
    {
        if (a.KnownToSim) return a.Ident;
        var id = new string(a.Ident.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return id.Length == 0 ? "USER" : id.Length > 6 ? id[..6] : id;
    }
}
