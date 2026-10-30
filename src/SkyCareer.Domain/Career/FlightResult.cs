namespace SkyCareer.Domain.Career;

public enum ViolationType { Slew, SimRate, InflightRefuel, PayloadChanged, Teleport, AircraftChanged }

/// <summary>A rule broken during the flight. Severe ones cost part of the payout, time acceleration is only noted.</summary>
public sealed record RuleViolation(ViolationType Type, string Detail)
{
    public bool Severe => Type != ViolationType.SimRate;
}

/// <summary>
/// Simulator-independent outcome of a tracked flight, mapped from the tracker's summary. Integrity violations never
/// void the job: severe ones (slew, teleport, refuelling in flight, changing aircraft or payload) cost a share of the
/// payout, time acceleration is only noted.
/// </summary>
public sealed record FlightResult
{
    public TimeSpan BlockTime { get; init; }
    public TimeSpan AirTime { get; init; }
    public double DistanceFlownNm { get; init; }
    public double FuelUsedKg { get; init; }
    /// <summary>Fuel on board at block-off (what was loaded for the flight); null if not tracked from the ground.</summary>
    public double? FuelAtBlockOffKg { get; init; }
    /// <summary>Fuel left at the end; stays in the aircraft for the next flight. 0 = unknown.</summary>
    public double FinalFuelKg { get; init; }
    /// <summary>Wear the sim reported between arming and parking (per component loss); null without sim data.</summary>
    public Aircraft.AircraftWear? SimWearDelta { get; init; }
    public double TouchdownFpm { get; init; }
    public double MaxGForce { get; init; } = 1;
    public int Bounces { get; init; }
    public int OverspeedSeconds { get; init; }
    public int StallWarningSeconds { get; init; }
    public GeoPosition FinalPosition { get; init; }
    public bool Crashed { get; init; }
    /// <summary>True when the aircraft reached the parked state (engines off, brake set) after landing.</summary>
    public bool Parked { get; init; }
    public IReadOnlyList<RuleViolation> Violations { get; init; } = [];
    public int SevereViolations => Violations.Count(v => v.Severe);
    public int MinorViolations => Violations.Count(v => !v.Severe);
    public IReadOnlyList<string> Notes { get; init; } = [];
    /// <summary>Departure checks that were red at liftoff (load, fuel, weight limits, OFP); each costs part of the payout.</summary>
    public IReadOnlyList<string> DepartureIssues { get; init; } = [];
}
