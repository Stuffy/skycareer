using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.World;

namespace SkyCareer.Data;

/// <summary>Persistence shape of <see cref="Airport"/>; surfaces are stored as a comma-separated list.</summary>
public sealed class AirportRow
{
    public required string Ident { get; set; }
    public required string Name { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public AirportSize Size { get; set; }
    public double LongestRunwayM { get; set; }
    public double LongestPavedRunwayM { get; set; }
    public bool HasLightedRunway { get; set; }
    public bool ScheduledService { get; set; }
    public string Surfaces { get; set; } = "";
    public string Country { get; set; } = "";
    public string Municipality { get; set; } = "";
    public double ElevationFt { get; set; }
    public string City { get; set; } = "";
    public int CityPopulation { get; set; }
    public bool IsCapital { get; set; }
    public bool IsMilitary { get; set; }

    public Airport ToDomain(bool? hasIls = null) => new(Ident, Name, new GeoPosition(Latitude, Longitude), Size, LongestRunwayM,
        Surfaces.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<RunwaySurface>).ToList(), Country, ElevationFt)
    {
        LongestPavedRunwayM = LongestPavedRunwayM,
        HasLightedRunway = HasLightedRunway,
        ScheduledService = ScheduledService,
        HasIls = hasIls,
        City = City,
        CityPopulation = CityPopulation,
        IsCapital = IsCapital,
        IsMilitary = IsMilitary
    };
}

/// <summary>
/// Facts about an airport learned from the simulator (currently ILS). Kept in its own table so re-importing
/// OurAirports data doesn't throw the slow-to-collect sim data away.
/// </summary>
public sealed class AirportFacilityCacheRow
{
    public required string Ident { get; set; }
    /// <summary>Null when the sim didn't know the airport.</summary>
    public bool? HasIls { get; set; }
    /// <summary>Runway ends with ILS, e.g. "25C:IFCW 07C:IFCE".</summary>
    public string IlsRunways { get; set; } = "";
    public DateTime CheckedAtUtc { get; set; }
}
