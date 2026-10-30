using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Sim.Tracking;

namespace SkyCareer.Application;

/// <summary>Translates the tracker's summary into the simulator-independent <see cref="FlightResult"/> used for billing.</summary>
public static class FlightResultMapper
{
    public static FlightResult Map(FlightSummary s, IReadOnlyList<string>? departureIssues = null, AircraftWear? simWearDelta = null) => new()
    {
        BlockTime = s.BlockTime ?? (s.BlockOff is { } off && s.LandingTime is { } land ? land - off : TimeSpan.Zero),
        AirTime = s.AirTime ?? TimeSpan.Zero,
        DistanceFlownNm = s.DistanceFlownNm,
        FuelUsedKg = s.FuelUsedKg,
        FuelAtBlockOffKg = s.FuelAtBlockOffKg,
        FinalFuelKg = s.FinalFuelKg,
        SimWearDelta = simWearDelta is { IsEmpty: false } ? simWearDelta : null,
        TouchdownFpm = s.FinalTouchdown?.VerticalSpeedFpm ?? 0,
        MaxGForce = s.MaxGForce,
        Bounces = s.FinalTouchdown?.Bounces ?? 0,
        OverspeedSeconds = s.OverspeedSeconds,
        StallWarningSeconds = s.StallWarningSeconds,
        FinalPosition = new GeoPosition(s.FinalLatitude, s.FinalLongitude),
        Crashed = s.FinalPhase == FlightPhase.Crashed,
        Parked = s.FinalPhase == FlightPhase.Arrived,
        Violations = s.Violations.Select(v => new RuleViolation(Type(v.Kind), v.Detail)).ToList(),
        Notes = s.Violations.Select(v => $"{v.Kind}: {v.Detail}").Concat(s.Notes ?? [])
            .Concat(s.Resumed ? ["Tracking war unterbrochen (App-Neustart ohne Flugaufzeichnung); Zeiten teils geschätzt."] : [])
            .Concat(s.ManualPhaseChanges > 0 ? [$"Flugphase {s.ManualPhaseChanges}× manuell gesetzt (Debug-Modus)."] : [])
            .ToList(),
        DepartureIssues = departureIssues ?? []
    };

    public static ViolationType Type(ViolationKind kind) => kind switch
    {
        ViolationKind.Slew => ViolationType.Slew,
        ViolationKind.SimRate => ViolationType.SimRate,
        ViolationKind.InflightRefuel => ViolationType.InflightRefuel,
        ViolationKind.PayloadChanged => ViolationType.PayloadChanged,
        ViolationKind.Teleport => ViolationType.Teleport,
        _ => ViolationType.AircraftChanged
    };
}
