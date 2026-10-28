using SkyCareer.Domain.Aircraft;

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

        // Reasons are shown in the (German) UI as-is.
        foreach (var tag in r.RequiredTags)
            if (!aircraft.HasTag(tag)) reasons.Add($"benötigt Fähigkeit „{tag}“");
        if (r.AnyOfTags.Count > 0 && !r.AnyOfTags.Any(aircraft.HasTag))
            reasons.Add($"benötigt eine von: {string.Join(", ", r.AnyOfTags)}");
        foreach (var tag in r.ForbiddenTags)
            if (aircraft.HasTag(tag)) reasons.Add($"nicht für „{tag}“-Flugzeuge");

        int neededSeats = Math.Max(r.MinSeats, mission.Passengers?.Min ?? 0);
        if (aircraft.PassengerSeats < neededSeats) reasons.Add($"benötigt {neededSeats} Sitze");

        double neededCargo = Math.Max(r.MinCargoKg, mission.Cargo?.MinKg ?? 0);
        if (aircraft.MaxCargoKg < neededCargo) reasons.Add($"benötigt {neededCargo:N0} kg Frachtkapazität");

        if (r.MaxMtowKg > 0 && aircraft.MaxTakeoffKg > r.MaxMtowKg) reasons.Add($"MTOW über {r.MaxMtowKg:N0} kg");
        if (r.Surfaces.Count > 0 && !r.Surfaces.Any(aircraft.Surfaces.Contains))
            reasons.Add($"kann nicht auf {string.Join("/", r.Surfaces)} landen");

        if (r.MinRangeNm > 0 && EstimatedRangeNm(aircraft) < r.MinRangeNm)
            reasons.Add($"Reichweite unter {r.MinRangeNm:N0} nm");

        return reasons.Count == 0 ? EligibilityResult.Ok : new EligibilityResult(false, reasons);
    }

    public static IEnumerable<MissionType> AvailableFor(IEnumerable<MissionType> missions, AircraftProfile aircraft) =>
        missions.Where(m => Evaluate(m, aircraft).Eligible);

    /// <summary>Still-air range with full tanks keeping a 45 minute reserve.</summary>
    public static double EstimatedRangeNm(AircraftProfile a) =>
        a.FuelBurnKgPerHour <= 0 ? 0 : Math.Max(0, a.MaxFuelKg / a.FuelBurnKgPerHour - 0.75) * a.CruiseKtas;
}
