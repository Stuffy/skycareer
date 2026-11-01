using SkyCareer.Domain.Aircraft;
using SkyCareer.Localization;

namespace SkyCareer.Missions;

public sealed record EligibilityResult(bool Eligible, IReadOnlyList<string> Reasons)
{
    public static EligibilityResult Ok { get; } = new(true, []);
}

/// <summary>Decides which mission types an aircraft type may fly, with reasons for the UI when it can't.</summary>
public static class RequirementEvaluator
{
    public static EligibilityResult Evaluate(MissionType mission, AircraftProfile aircraft)
    {
        var r = mission.Requirements;
        var reasons = new List<string>();

        // Reasons are localized display texts.
        foreach (var tag in r.RequiredTags)
            if (!aircraft.HasTag(tag)) reasons.Add(L.F("requires capability “{0}”", tag));
        if (r.AnyOfTags.Count > 0 && !r.AnyOfTags.Any(aircraft.HasTag))
            reasons.Add(L.F("requires one of: {0}", string.Join(", ", r.AnyOfTags)));
        foreach (var tag in r.ForbiddenTags)
            if (aircraft.HasTag(tag)) reasons.Add(L.F("not for “{0}” aircraft", tag));

        int neededSeats = Math.Max(r.MinSeats, mission.Passengers?.Min ?? 0);
        if (aircraft.PassengerSeats < neededSeats) reasons.Add(L.F("requires {0} seats", neededSeats));

        double neededCargo = Math.Max(r.MinCargoKg, mission.Cargo?.MinKg ?? 0);
        if (aircraft.MaxCargoKg < neededCargo) reasons.Add(L.F("requires {0:N0} kg cargo capacity", neededCargo));

        if (r.MaxMtowKg > 0 && aircraft.MaxTakeoffKg > r.MaxMtowKg) reasons.Add(L.F("MTOW above {0:N0} kg", r.MaxMtowKg));
        if (r.Surfaces.Count > 0 && !r.Surfaces.Any(aircraft.Surfaces.Contains))
            reasons.Add(L.F("cannot land on {0}", string.Join("/", r.Surfaces)));

        if (r.MinRangeNm > 0 && EstimatedRangeNm(aircraft) < r.MinRangeNm)
            reasons.Add(L.F("range below {0:N0} nm", r.MinRangeNm));

        return reasons.Count == 0 ? EligibilityResult.Ok : new EligibilityResult(false, reasons);
    }

    public static IEnumerable<MissionType> AvailableFor(IEnumerable<MissionType> missions, AircraftProfile aircraft) =>
        missions.Where(m => Evaluate(m, aircraft).Eligible);

    /// <summary>Still-air range with full tanks keeping a 45 minute reserve.</summary>
    public static double EstimatedRangeNm(AircraftProfile a) =>
        a.FuelBurnKgPerHour <= 0 ? 0 : Math.Max(0, a.MaxFuelKg / a.FuelBurnKgPerHour - 0.75) * a.CruiseKtas;
}
