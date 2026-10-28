namespace SkyCareer.Domain.Career;

/// <summary>
/// Simulator-independent outcome of a tracked flight, mapped from the tracker's summary. Integrity violations are
/// split into severe ones (slew, teleport, refuelling in flight, changing aircraft or payload) that void the job and
/// minor ones (time acceleration) that only cost score.
/// </summary>
public sealed record FlightResult
{
    public TimeSpan BlockTime { get; init; }
    public TimeSpan AirTime { get; init; }
    public double DistanceFlownNm { get; init; }
    public double FuelUsedKg { get; init; }
    public double TouchdownFpm { get; init; }
    public double MaxGForce { get; init; } = 1;
    public int Bounces { get; init; }
    public int OverspeedSeconds { get; init; }
    public int StallWarningSeconds { get; init; }
    public GeoPosition FinalPosition { get; init; }
    public bool Crashed { get; init; }
    /// <summary>True when the aircraft reached the parked state (engines off, brake set) after landing.</summary>
    public bool Parked { get; init; }
    public int SevereViolations { get; init; }
    public int MinorViolations { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
    /// <summary>Departure checks that were red at liftoff (load, fuel, weight limits, OFP); each costs part of the payout.</summary>
    public IReadOnlyList<string> DepartureIssues { get; init; } = [];
}
