namespace SkyCareer.Domain.World;

/// <summary>Where the sun stands, to tell day from night without relying on the simulator's own day/night flag.</summary>
public static class SolarPosition
{
    /// <summary>Night begins when the sun is this far below the horizon (end of civil twilight).</summary>
    public const double NightElevationDeg = -6;

    /// <summary>
    /// Sun elevation in degrees above the horizon at <paramref name="position"/>, from the day of the year (1–366) and the
    /// UTC time in seconds since midnight. Accurate to about a degree, which is plenty for day or night.
    /// </summary>
    public static double ElevationDeg(GeoPosition position, double dayOfYear, double utcSeconds)
    {
        double hours = utcSeconds / 3600.0;
        // Fractional year in radians.
        double g = 2 * Math.PI / 365.0 * (dayOfYear - 1 + (hours - 12) / 24.0);
        double declination = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g)
                             + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
        // Equation of time in minutes.
        double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g) - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
        double solarMinutes = hours * 60 + eqTime + 4 * position.Longitude;
        double hourAngle = (solarMinutes / 4.0 - 180) * Math.PI / 180;
        double lat = position.Latitude * Math.PI / 180;
        double cosZenith = Math.Sin(lat) * Math.Sin(declination) + Math.Cos(lat) * Math.Cos(declination) * Math.Cos(hourAngle);
        return 90 - Math.Acos(Math.Clamp(cosZenith, -1, 1)) * 180 / Math.PI;
    }

    public static bool IsNight(GeoPosition position, double dayOfYear, double utcSeconds) =>
        ElevationDeg(position, dayOfYear, utcSeconds) < NightElevationDeg;
}
