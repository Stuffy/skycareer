using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;
using SkyCareer.Localization;

namespace SkyCareer.Missions.Events;

/// <summary>An event planned for a flight: what happens and at which share of the planned air time.</summary>
public sealed record PlannedEvent(InFlightEventKind Kind, double TriggerFraction);

/// <summary>
/// Events during a flight: a medical emergency on board, the customer changing the destination, a technical warning from
/// a worn component. Planned per job from its id (so a restart doesn't roll again), on about every tenth flight, and
/// only where they make sense. Pure functions; the automation raises them and the settlement judges the decision.
/// </summary>
public static class InFlightEvents
{
    /// <summary>Share of flights with an event, in percent.</summary>
    public const int ChancePercent = 10;
    /// <summary>A medical diversion that was flown earns this much reputation, ignoring the emergency costs this much.</summary>
    public const int MedicalDiversionReputation = 2, MedicalIgnoredReputation = -3;
    /// <summary>A new destination asked for by the customer pays this surcharge on top of the distance.</summary>
    public const decimal DestinationChangeSurcharge = 0.10m;
    /// <summary>New destinations of a destination change lie this far from the old one.</summary>
    public const double ChangeMinNm = 20, ChangeMaxNm = 80;
    /// <summary>Flying on with a technical warning costs this much extra condition.</summary>
    public const double IgnoredWarningConditionLoss = 3;
    /// <summary>Time to decide before the crew flies on as planned.</summary>
    public static readonly TimeSpan DecisionTime = TimeSpan.FromMinutes(5);

    public static PlannedEvent? Plan(MissionContract c, MissionType type, FleetAircraft aircraft)
    {
        if (c.IsTour || type.Weight <= 0 || c.EventKind is not null) return null;
        if (StableHash.Of(c.Id, "event") % 100 >= ChancePercent) return null;
        var kinds = new List<InFlightEventKind>();
        if (c.TotalPassengers > 0) kinds.Add(InFlightEventKind.MedicalEmergency);
        if (type.Category == "charter" && c.PackageId is null && c.ChainId is null) kinds.Add(InFlightEventKind.DestinationChange);
        if (aircraft.Components.WorstLevel < WearComponents.WarningLevel) kinds.Add(InFlightEventKind.TechnicalWarning);
        if (kinds.Count == 0) return null;
        var kind = kinds[StableHash.Of(c.Id, "kind") % kinds.Count];
        return new PlannedEvent(kind, 0.3 + StableHash.Of(c.Id, "when") % 31 / 100.0);
    }

    /// <summary>The nearest airfield the aircraft can land at, other than <paramref name="except"/>: for diversions.</summary>
    public static Airport? NearestSuitable(GeoPosition position, IEnumerable<Airport> airports, AircraftProfile profile, string? except = null) =>
        airports.Where(a => Usable(a, profile) && !string.Equals(a.Ident, except, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Position.DistanceNmTo(position))
            .FirstOrDefault();

    /// <summary>Whether the aircraft can land there: runway length, surface, no military field; heliports only for helicopters.</summary>
    public static bool Usable(Airport a, AircraftProfile profile) =>
        !a.IsMilitary && a.LongestRunwayM >= profile.MinRunwayM
        && (a.HasPavedRunway || profile.Tags.Contains("unpaved_ok"))
        && (a.Size is not (AirportSize.Heliport or AirportSize.Seaplane) || a.Size == AirportSize.Heliport && profile.Tags.Contains("helicopter"));

    /// <summary>A new destination for a customer's change of plans: a medium or large airport 20–80 nm from the old one.</summary>
    public static Airport? NewDestination(MissionContract c, Airport destination, IEnumerable<Airport> airports, AircraftProfile profile) =>
        airports.Where(a => a.Ident != destination.Ident && a.Ident != c.Origin && a.Size is AirportSize.Medium or AirportSize.Large
                            && Usable(a, profile)
                            && a.Position.DistanceNmTo(destination.Position) is >= ChangeMinNm and <= ChangeMaxNm)
            .OrderBy(a => StableHash.Of(c.Id, a.Ident))
            .FirstOrDefault();

    /// <summary>Pay for flying to the new destination: by the distance from the origin, plus the surcharge.</summary>
    public static decimal ChangedPayout(MissionContract c, Airport origin, Airport newDestination)
    {
        double ratio = c.DistanceNm <= 0 ? 1 : origin.Position.DistanceNmTo(newDestination.Position) / c.DistanceNm;
        return Math.Round(c.Payout * (decimal)Math.Max(0.5, ratio) * (1 + DestinationChangeSurcharge), 0);
    }

    /// <summary>Pay for a precautionary landing: the share of the trip flown towards the destination.</summary>
    public static decimal PrecautionaryPayout(MissionContract c, Airport origin, Airport alternate)
    {
        double ratio = c.DistanceNm <= 0 ? 0 : origin.Position.DistanceNmTo(alternate.Position) / c.DistanceNm;
        return Math.Round(c.Payout * (decimal)Math.Clamp(ratio, 0, 1), 0);
    }

    public static string Title(InFlightEventKind kind) => kind switch
    {
        InFlightEventKind.MedicalEmergency => L.T("Medical emergency on board"),
        InFlightEventKind.DestinationChange => L.T("The customer asks for another destination"),
        _ => L.T("Technical warning")
    };
}
