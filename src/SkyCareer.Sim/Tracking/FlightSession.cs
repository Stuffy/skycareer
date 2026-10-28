namespace SkyCareer.Sim.Tracking;

public sealed record FlightSummary(
    FlightPhase FinalPhase,
    DateTimeOffset? BlockOff,
    DateTimeOffset? TakeoffTime,
    DateTimeOffset? LandingTime,
    DateTimeOffset? BlockOn,
    double DistanceFlownNm,
    double FuelUsedKg,
    double MaxGForce,
    double MinGForce,
    int OverspeedSeconds,
    int StallWarningSeconds,
    TouchdownReport? FinalTouchdown,
    IReadOnlyList<IntegrityViolation> Violations,
    double FinalLatitude,
    double FinalLongitude,
    bool Resumed = false)
{
    public TimeSpan? BlockTime => BlockOn - BlockOff;
    public TimeSpan? AirTime => LandingTime - TakeoffTime;
}

/// <summary>
/// One tracked flight: feeds telemetry into the phase tracker and integrity monitor, records every sample
/// for the logbook/replay, and accumulates statistics for scoring and billing.
/// </summary>
public sealed class FlightSession
{
    private readonly List<Telemetry> _samples = [];
    private Telemetry? _first, _last;
    private double _distanceNm, _maxG = 1, _minG = 1, _overspeedSec, _stallSec;

    public FlightSession(FlightPhaseTracker.Options? phaseOptions = null, IntegrityMonitor.Options? integrityOptions = null)
    {
        Phases = new FlightPhaseTracker(phaseOptions);
        Integrity = new IntegrityMonitor(integrityOptions);
        // Loading may change as long as the aircraft is on the ground; it is locked when the takeoff roll starts and
        // released again after a rejected takeoff.
        Phases.PhaseChanged += (_, change) =>
        {
            if (change.To == FlightPhase.TakeoffRoll) Integrity.LockPayload(change.At.PayloadKg);
            else if (change.From == FlightPhase.TakeoffRoll && change.To == FlightPhase.TaxiOut) Integrity.UnlockPayload();
        };
    }

    public FlightPhaseTracker Phases { get; }
    public IntegrityMonitor Integrity { get; }
    public IReadOnlyList<Telemetry> Samples => _samples;
    public bool IsFinished => Phases.Phase is FlightPhase.Arrived or FlightPhase.Crashed;

    /// <summary>Picks up a flight in progress without its earlier samples (see <see cref="FlightPhaseTracker.Resume"/>).</summary>
    public void Resume(Telemetry current, DateTimeOffset startedAt)
    {
        Phases.Resume(current, startedAt);
        Resumed = true;
        if (Phases.Phase != FlightPhase.Arrived) Integrity.LockPayload(current.PayloadKg);
        _first ??= current;
        _samples.Add(current);
        _last = current;
    }

    /// <summary>True when the flight was picked up without its full history; the result notes the gap.</summary>
    public bool Resumed { get; private set; }

    public void Process(Telemetry t)
    {
        _first ??= t;
        _samples.Add(t);
        Phases.Process(t);
        Integrity.Process(t, Phases.Phase);

        if (_last is { } prev && !t.Paused)
        {
            if (!t.Slew) _distanceNm += prev.Position.DistanceNmTo(t.Position);
            double dt = (t.Timestamp - prev.Timestamp).TotalSeconds;
            if (t.OverspeedWarning) _overspeedSec += dt;
            if (t.StallWarning && !t.OnGround) _stallSec += dt;
        }
        if (!t.OnGround || Phases.Phase == FlightPhase.Rollout)
        {
            _maxG = Math.Max(_maxG, t.GForce);
            _minG = Math.Min(_minG, t.GForce);
        }
        _last = t;
    }

    public FlightSummary Summarize() => new(
        Phases.Phase,
        Phases.BlockOff,
        Phases.TakeoffTime,
        Phases.LandingTime,
        Phases.BlockOn,
        _distanceNm,
        _first is null || _last is null ? 0 : Math.Max(0, _first.FuelKg - _last.FuelKg),
        _maxG,
        _minG,
        (int)_overspeedSec,
        (int)_stallSec,
        Phases.Touchdowns.Count > 0 ? Phases.Touchdowns[^1] : null,
        Integrity.Violations,
        _last?.Latitude ?? 0,
        _last?.Longitude ?? 0,
        Resumed);
}
