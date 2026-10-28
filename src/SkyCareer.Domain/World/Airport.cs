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
    /// <summary>Biggest city the airport serves (from GeoNames, matched by distance at import); empty if none nearby.</summary>
    public string City { get; init; } = "";
    /// <summary>Population of <see cref="City"/>, scaled down with distance to the airport.</summary>
    public int CityPopulation { get; init; }
    public bool IsCapital { get; init; }
    /// <summary>Military field (detected by name at import); only aircraft tagged "military" fly there.</summary>
    public bool IsMilitary { get; init; }

    public bool HasPavedRunway => Surfaces.Contains(RunwaySurface.Paved);
}
