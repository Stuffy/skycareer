using System.Text.RegularExpressions;
using SkyCareer.Domain.Aircraft;

namespace SkyCareer.Integrations.SimBrief;

/// <summary>
/// Profile drafts from a SimBrief aircraft type: either complete (no sim needed, stations added later) or merged into a
/// draft read from the sim, where empty weight, stations and title must stay as the sim has them.
/// </summary>
public static partial class SimBriefProfileDraft
{
    private const double MPerFt = 0.3048;

    /// <summary>A complete profile from SimBrief alone; values SimBrief doesn't have are estimated from the most similar known profile.</summary>
    public static AircraftProfile FromSimBrief(SimBriefType t, IEnumerable<AircraftProfile> known)
    {
        bool piston = IsPiston(t);
        var draft = new AircraftProfile
        {
            Id = AircraftProfileEstimator.Slug(t.Name),
            Name = t.Name,
            IcaoType = t.Icao,
            SimMatch = [t.Icao, t.Name],
            SimBriefAirframe = t.Id,
            OperatingEmptyKg = t.OperatingEmptyKg,
            MaxTakeoffKg = t.MaxTakeoffKg,
            MaxZeroFuelKg = t.MaxZeroFuelKg > 0
                ? t.MaxZeroFuelKg
                : AircraftProfileEstimator.EstimateMaxZeroFuelKg(t.OperatingEmptyKg, t.MaxTakeoffKg, t.MaxPassengers),
            MaxLandingKg = t.MaxLandingKg > 0 ? t.MaxLandingKg : AircraftProfileEstimator.EstimateMaxLandingKg(t.MaxTakeoffKg),
            MaxFuelKg = t.MaxFuelKg,
            FuelType = piston ? FuelType.AvGas : FuelType.JetA,
            PassengerSeats = t.MaxPassengers,
            // Same rule as AircraftProfile.MinimumPilots, so the editor doesn't start with a warning.
            CrewCount = t.CruisesAtMach || t.MaxTakeoffKg > 5_700 || t.MaxPassengers > 9 ? 2 : 1,
            CruiseKtas = t.CruiseKtas,
            FuelBurnKgPerHour = t.FuelFlowKgPerHour,
            MinRunwayM = MinRunwayM(t),
            Tags = Tags(t, piston),
            Loading = LoadingMethod.Manual,
            Stations = []
        };
        return AircraftProfileEstimator.MostSimilar(draft, known) is { } reference
            ? AircraftProfileEstimator.FillFrom(draft, reference)
            : draft;
    }

    /// <summary>
    /// SimBrief's limits and performance replace the sim draft's estimates; empty weight, stations, seats and sim
    /// recognition stay as read from the sim. Returns a warning when SimBrief's OEW differs noticeably from the sim's.
    /// </summary>
    public static (AircraftProfile Profile, string? Warning) Merge(AircraftProfile simDraft, SimBriefType t)
    {
        double Or(double simBrief, double current) => simBrief > 0 ? simBrief : current;
        var merged = simDraft with
        {
            IcaoType = t.Icao,
            SimBriefAirframe = t.Id,
            SimMatch = simDraft.SimMatch.Contains(t.Icao, StringComparer.OrdinalIgnoreCase) ? simDraft.SimMatch : [.. simDraft.SimMatch, t.Icao],
            MaxZeroFuelKg = Or(t.MaxZeroFuelKg, simDraft.MaxZeroFuelKg),
            MaxTakeoffKg = Or(t.MaxTakeoffKg, simDraft.MaxTakeoffKg),
            MaxLandingKg = Or(t.MaxLandingKg, simDraft.MaxLandingKg),
            MaxFuelKg = Or(t.MaxFuelKg, simDraft.MaxFuelKg),
            CruiseKtas = Or(t.CruiseKtas, simDraft.CruiseKtas),
            FuelBurnKgPerHour = Or(t.FuelFlowKgPerHour, simDraft.FuelBurnKgPerHour),
            MinRunwayM = Or(MinRunwayM(t), simDraft.MinRunwayM)
        };
        string? warning = t.OperatingEmptyKg > 0 && simDraft.OperatingEmptyKg > 0 &&
                          Math.Abs(t.OperatingEmptyKg - simDraft.OperatingEmptyKg) > simDraft.OperatingEmptyKg * 0.05
            ? $"Leergewicht laut SimBrief {t.OperatingEmptyKg:N0} kg, im Sim {simDraft.OperatingEmptyKg:N0} kg. Das Profil nutzt den Sim-Wert; " +
              "für passende OFPs das Leergewicht im SimBrief-Airframe angleichen."
            : null;
        return (merged, warning);
    }

    /// <summary>Longer of SimBrief's takeoff and landing distance; 0 when unknown.</summary>
    private static double MinRunwayM(SimBriefType t) => Math.Round(Math.Max(t.TakeoffFt, t.LandingFt) * MPerFt);

    /// <summary>Lycoming/Continental designations such as IO-360, TSIO-550 or O-235 mean a piston engine (AvGas).</summary>
    private static bool IsPiston(SimBriefType t) => PistonEngine().IsMatch(t.Engines);

    private static List<string> Tags(SimBriefType t, bool piston)
    {
        var tags = new List<string>();
        if (t.MaxPassengers > 0 && !t.IsCargo) tags.Add("pax");
        if (t.IsCargo) tags.Add("cargo");
        tags.Add(t.CruisesAtMach ? "jet" : piston ? "piston" : "turboprop");
        return tags;
    }

    [GeneratedRegex(@"^(AE|L|T|TS|G|GTS)?I?O-\d", RegexOptions.IgnoreCase)]
    private static partial Regex PistonEngine();
}
