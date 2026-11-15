using SkyCareer.Domain.Aircraft;

namespace SkyCareer.Domain.World;

public enum AirportSize { Heliport, Seaplane, Small, Medium, Large }

/// <summary>Airport master data (imported from OurAirports). LongestRunwayM/Surfaces describe what can operate there.</summary>
public sealed record Airport(
    string Ident,
    string Name,
    GeoPosition Position,
    AirportSize Size,
    double LongestRunwayM,
    IReadOnlyList<RunwaySurface> Surfaces,
    string Country = "",
    double ElevationFt = 0)
{
    public double LongestPavedRunwayM { get; init; }
    public bool HasLightedRunway { get; init; }
    public bool ScheduledService { get; init; }
    /// <summary>From the simulator's navigation data; null until the airport has been checked with MSFS running.</summary>
    public bool? HasIls { get; init; }
    /// <summary>Whether MSFS knows the airport (asked via its facility data); null until checked with MSFS running.</summary>
    public bool? InSim { get; init; }
    /// <summary>Biggest city the airport serves (from GeoNames, matched by distance at import); empty if none nearby.</summary>
    public string City { get; init; } = "";
    /// <summary>Population of <see cref="City"/>, scaled down with distance to the airport.</summary>
    public int CityPopulation { get; init; }
    public bool IsCapital { get; init; }
    /// <summary>Military field (detected by name at import); only aircraft tagged "military" fly there.</summary>
    public bool IsMilitary { get; init; }

    public bool HasPavedRunway => Surfaces.Contains(RunwaySurface.Paved);

    /// <summary>
    /// Whether the sim's navigation database (GPS, flight plans, SimBrief) can be expected to know the airport: what MSFS
    /// answered, else whether the ident looks like an ICAO or FAA code. OurAirports' own idents of small strips
    /// ("BR-2197", "US-1234") never are.
    /// </summary>
    public bool KnownToSim => InSim ?? IsCodeLike(Ident);

    public static bool IsCodeLike(string ident) =>
        ident.Length is >= 3 and <= 4 && ident.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9');
}
