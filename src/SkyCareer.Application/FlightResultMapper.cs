using SkyCareer.Domain;
using SkyCareer.Domain.Career;
using SkyCareer.Sim.Tracking;

namespace SkyCareer.Application;

/// <summary>Translates the tracker's summary into the simulator-independent <see cref="FlightResult"/> used for billing.</summary>
public static class FlightResultMapper
{
    private static readonly HashSet<ViolationKind> Severe =
        [ViolationKind.Slew, ViolationKind.Teleport, ViolationKind.InflightRefuel, ViolationKind.AircraftChanged, ViolationKind.PayloadChanged];

    public static FlightResult Map(FlightSummary s, IReadOnlyList<string>? departureIssues = null) => new()
    {
        BlockTime = s.BlockTime ?? (s.BlockOff is { } off && s.LandingTime is { } land ? land - off : TimeSpan.Zero),
        AirTime = s.AirTime ?? TimeSpan.Zero,
        DistanceFlownNm = s.DistanceFlownNm,
        FuelUsedKg = s.FuelUsedKg,
        TouchdownFpm = s.FinalTouchdown?.VerticalSpeedFpm ?? 0,
        MaxGForce = s.MaxGForce,
        Bounces = s.FinalTouchdown?.Bounces ?? 0,
        OverspeedSeconds = s.OverspeedSeconds,
        StallWarningSeconds = s.StallWarningSeconds,
        FinalPosition = new GeoPosition(s.FinalLatitude, s.FinalLongitude),
        Crashed = s.FinalPhase == FlightPhase.Crashed,
        Parked = s.FinalPhase == FlightPhase.Arrived,
        SevereViolations = s.Violations.Count(v => Severe.Contains(v.Kind)),
        MinorViolations = s.Violations.Count(v => !Severe.Contains(v.Kind)),
        Notes = s.Violations.Select(v => $"{v.Kind}: {v.Detail}")
            .Concat(s.Resumed ? ["Tracking war unterbrochen (App-Neustart ohne Flugaufzeichnung); Zeiten teils geschätzt."] : [])
            .ToList(),
        DepartureIssues = departureIssues ?? []
    };
}
