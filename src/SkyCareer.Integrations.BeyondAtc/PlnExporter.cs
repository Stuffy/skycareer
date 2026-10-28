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
/// </summary>
public static class PlnExporter
{
    public static XDocument Build(Airport origin, Airport destination, PlanRules rules, int? cruiseAltitudeFt = null, string? title = null)
    {
        int altitude = cruiseAltitudeFt ?? CruiseAltitude(origin.Position, destination.Position, rules);
        string fpType = rules == PlanRules.Vfr ? "VFR" : "IFR";
        string routeType = rules == PlanRules.Vfr ? "VFR" : altitude >= 18000 ? "HighAlt" : "LowAlt";

        var plan = new XElement("FlightPlan.FlightPlan",
            new XElement("Title", title ?? $"{origin.Ident} to {destination.Ident}"),
            new XElement("FPType", fpType),
            new XElement("RouteType", routeType),
            new XElement("CruisingAlt", altitude.ToString(CultureInfo.InvariantCulture)),
            new XElement("DepartureID", origin.Ident),
            new XElement("DepartureLLA", Lla(origin.Position, origin.ElevationFt)),
            new XElement("DestinationID", destination.Ident),
            new XElement("DestinationLLA", Lla(destination.Position, destination.ElevationFt)),
            new XElement("Descr", $"{origin.Ident}, {destination.Ident}"),
            new XElement("DepartureName", origin.Name),
            new XElement("DestinationName", destination.Name),
            new XElement("AppVersion",
                new XElement("AppVersionMajor", "11"),
                new XElement("AppVersionBuild", "282174")),
            Waypoint(origin),
            Waypoint(destination));

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
        bool eastbound = InitialCourse(from, to) < 180;
        int thousands = rules == PlanRules.Vfr
            ? distance switch { < 40 => 3, < 120 => 5, _ => 7 }
            : distance switch { < 80 => 9, < 200 => 15, < 400 => 25, _ => 35 };
        if (eastbound != (thousands % 2 == 1)) thousands++;
        return thousands * 1000 + (rules == PlanRules.Vfr ? 500 : 0);
    }

    public static double InitialCourse(GeoPosition from, GeoPosition to)
    {
        double lat1 = Rad(from.Latitude), lat2 = Rad(to.Latitude), dLon = Rad(to.Longitude - from.Longitude);
        double y = Math.Sin(dLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
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

    private static XElement Waypoint(Airport a) =>
        new("ATCWaypoint",
            new XAttribute("id", a.Ident),
            new XElement("ATCWaypointType", "Airport"),
            new XElement("WorldPosition", Lla(a.Position, a.ElevationFt)),
            new XElement("ICAO", new XElement("ICAOIdent", a.Ident)));

    private static double Rad(double d) => d * Math.PI / 180;
}
