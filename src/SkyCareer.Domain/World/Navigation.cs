using System.Globalization;

namespace SkyCareer.Domain.World;

/// <summary>Course, distance and time to a destination from a position, for dead reckoning or a direct-to.</summary>
/// <param name="TrueCourse">Great-circle initial course, degrees true.</param>
/// <param name="MagneticCourse">Course in degrees magnetic, null without magnetic variation.</param>
/// <param name="TrackOffset">How far the current track lies right (+) or left (−) of the course, degrees; null without a track.</param>
public sealed record DirectTo(double TrueCourse, double? MagneticCourse, double DistanceNm, TimeSpan? Ete, double? TrackOffset)
{
    /// <summary>Course for display: magnetic when known, else true.</summary>
    public string CourseText => MagneticCourse is { } m ? Navigation.Degrees(m) + "°M" : Navigation.Degrees(TrueCourse) + "°T";
}

public static class Navigation
{
    /// <summary>Within this distance of the destination (nm) the flight counts as arrived (see the settlement).</summary>
    public const double ArrivalRadiusNm = 3;

    /// <summary>Below this ground speed (kt) no time en route is given.</summary>
    public const double MinGroundSpeedKt = 30;

    /// <summary>
    /// Course, distance and ETE from <paramref name="from"/> to <paramref name="to"/>. <paramref name="magVar"/> is the
    /// magnetic variation in degrees, east positive (magnetic = true − variation); <paramref name="trackTrue"/> the
    /// aircraft's current ground track in degrees true.
    /// </summary>
    public static DirectTo DirectTo(GeoPosition from, GeoPosition to, double speedKt, double? magVar = null, double? trackTrue = null)
    {
        double course = from.InitialCourseTo(to);
        double distance = from.DistanceNmTo(to);
        return new DirectTo(course, magVar is { } v ? Normalize(course - v) : null, distance, Ete(distance, speedKt),
            trackTrue is { } track ? Offset(course, track) : null);
    }

    /// <summary>Time en route at <paramref name="speedKt"/>; null when too slow to say.</summary>
    public static TimeSpan? Ete(double distanceNm, double speedKt) =>
        speedKt < MinGroundSpeedKt ? null : TimeSpan.FromHours(distanceNm / speedKt);

    /// <summary>Signed difference track − course in −180…180 (positive = track right of course).</summary>
    public static double Offset(double course, double track)
    {
        double d = (track - course) % 360;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }

    /// <summary>Wind component across <paramref name="headingTrue"/> in knots (always positive); the wind direction is where it blows from.</summary>
    public static double CrosswindKt(double windDirectionDeg, double windSpeedKt, double headingTrue) =>
        Math.Abs(windSpeedKt * Math.Sin((windDirectionDeg - headingTrue) * Math.PI / 180));

    public static double Normalize(double degrees) => (degrees % 360 + 360) % 360;

    /// <summary>Three-digit heading as pilots read it, 360 instead of 000.</summary>
    public static string Degrees(double degrees)
    {
        int d = (int)Math.Round(Normalize(degrees)) % 360;
        return (d == 0 ? 360 : d).ToString("000", CultureInfo.InvariantCulture);
    }

    /// <summary>"1:25" (hours:minutes).</summary>
    public static string EteText(TimeSpan ete)
    {
        int minutes = (int)Math.Round(ete.TotalMinutes);
        return $"{minutes / 60}:{minutes % 60:00}";
    }

    /// <summary>Position as degrees and decimal minutes, e.g. "S12°34.56' W045°12.34'" (user waypoints in GNS/G1000).</summary>
    public static string DegreesMinutes(GeoPosition p) =>
        $"{DegreesMinutes(p.Latitude, 'N', 'S', 2)} {DegreesMinutes(p.Longitude, 'E', 'W', 3)}";

    private static string DegreesMinutes(double value, char positive, char negative, int degreeDigits)
    {
        double abs = Math.Abs(value);
        int deg = (int)abs;
        double minutes = Math.Round((abs - deg) * 60, 2);
        if (minutes >= 60) { minutes = 0; deg++; }
        return string.Create(CultureInfo.InvariantCulture,
            $"{(value >= 0 ? positive : negative)}{deg.ToString(new string('0', degreeDigits), CultureInfo.InvariantCulture)}°{minutes:00.00}'");
    }

    /// <summary>
    /// Decimal degrees as the MSFS world map / EFB search field and map services take them: "-18.49150, -51.29880".
    /// </summary>
    public static string Decimal(GeoPosition p) =>
        string.Create(CultureInfo.InvariantCulture, $"{p.Latitude:0.00000}, {p.Longitude:0.00000}");

    /// <summary>Pattern altitude for light aircraft: field elevation plus 1,000 ft.</summary>
    public static double PatternAltitudeFt(double elevationFt) => elevationFt + 1000;
}
