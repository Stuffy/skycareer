namespace SkyCareer.Domain;

/// <summary>Mass in kilograms. SimConnect uses pounds, so conversions live here.</summary>
public readonly record struct Mass(double Kg) : IComparable<Mass>
{
    public const double KgPerLb = 0.45359237;

    public double Lb => Kg / KgPerLb;

    public static Mass Zero => new(0);
    public static Mass FromKg(double kg) => new(kg);
    public static Mass FromLb(double lb) => new(lb * KgPerLb);

    public static Mass operator +(Mass a, Mass b) => new(a.Kg + b.Kg);
    public static Mass operator -(Mass a, Mass b) => new(a.Kg - b.Kg);
    public static Mass operator *(Mass a, double f) => new(a.Kg * f);
    public static bool operator <(Mass a, Mass b) => a.Kg < b.Kg;
    public static bool operator >(Mass a, Mass b) => a.Kg > b.Kg;
    public static bool operator <=(Mass a, Mass b) => a.Kg <= b.Kg;
    public static bool operator >=(Mass a, Mass b) => a.Kg >= b.Kg;

    public int CompareTo(Mass other) => Kg.CompareTo(other.Kg);
    public override string ToString() => $"{Kg:N0} kg";
}

/// <summary>Money in the career's base currency (EUR), stored as decimal to avoid rounding drift in the ledger.</summary>
public readonly record struct Money(decimal Amount)
{
    public static Money Zero => new(0m);
    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);
    public static Money operator -(Money a) => new(-a.Amount);
    public static Money operator *(Money a, decimal f) => new(a.Amount * f);
    public override string ToString() => $"{Amount:N2} €";
}

public readonly record struct GeoPosition(double Latitude, double Longitude)
{
    private const double EarthRadiusNm = 3440.065;

    /// <summary>Great-circle distance in nautical miles (haversine).</summary>
    public double DistanceNmTo(GeoPosition other)
    {
        double lat1 = ToRad(Latitude), lat2 = ToRad(other.Latitude);
        double dLat = lat2 - lat1, dLon = ToRad(other.Longitude - Longitude);
        double a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusNm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
