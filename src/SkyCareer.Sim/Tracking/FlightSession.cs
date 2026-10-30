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
    bool Resumed = false,
    int ManualPhaseChanges = 0,
    double? FuelAtBlockOffKg = null,
    double FinalFuelKg = 0,
    IReadOnlyList<string>? Notes = null)
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
    private double? _fuelAtBlockOff;
    private readonly List<string> _notes = [];
    private DateTimeOffset? _restartedAt;

    /// <summary>A pause in the samples longer than this after an app restart is treated as a gap without data.</summary>
    public static readonly TimeSpan RestartGap = TimeSpan.FromSeconds(30);

    public FlightSession(FlightPhaseTracker.Options? phaseOptions = null, IntegrityMonitor.Options? integrityOptions = null)
    {
        Phases = new FlightPhaseTracker(phaseOptions);
        Integrity = new IntegrityMonitor(integrityOptions);
        // Loading may change as long as the aircraft is on the ground; it is locked when the takeoff roll starts and
        // released again after a rejected takeoff.
        Phases.PhaseChanged += (_, change) =>
        {
            // From block-off on, adding fuel is a violation: what's in the tanks now is what was bought for this flight.
            if (change.From == FlightPhase.Preflight) _fuelAtBlockOff ??= change.At.FuelKg;
            if (change.To == FlightPhase.TakeoffRoll) Integrity.LockPayload(change.At.PayloadKg);
            else if (change.From == FlightPhase.TakeoffRoll && change.To == FlightPhase.TaxiOut) Integrity.UnlockPayload();
        };
    }

    public FlightPhaseTracker Phases { get; }
    public IntegrityMonitor Integrity { get; }
    public IReadOnlyList<Telemetry> Samples => _samples;
    public bool IsFinished => Phases.Phase is FlightPhase.Arrived or FlightPhase.Crashed;

    /// <summary>
    /// Picks up a flight in progress without its earlier samples (see <see cref="FlightPhaseTracker.Resume"/>).
    /// <paramref name="fuelAtBlockOffKg"/> (stored with the job at liftoff) keeps fuel used and uplift right.
    /// </summary>
    public void Resume(Telemetry current, DateTimeOffset startedAt, double? fuelAtBlockOffKg = null)
    {
        Phases.Resume(current, startedAt);
        Resumed = true;
        _fuelAtBlockOff ??= fuelAtBlockOffKg;
        if (Phases.Phase != FlightPhase.Arrived) Integrity.LockPayload(current.PayloadKg);
        _first ??= current;
        _samples.Add(current);
        _last = current;
    }

    /// <summary>True when the flight was picked up without its full history; the result notes the gap.</summary>
    public bool Resumed { get; private set; }

    /// <summary>
    /// Call after replaying the journal of a flight the app was closed during. If the next live sample comes after a
    /// gap, what happened without data isn't judged: a different payload becomes the new lock (noted, not flagged) and a
    /// landing in the gap continues on the ground without a touchdown rating. Changes after that count as usual.
    /// </summary>
    public void ContinueAfterRestart(DateTimeOffset startedAt)
    {
        if (_last is null) return;
        _restartedAt = startedAt;
    }

    /// <summary>Remarks for the settlement (e.g. what changed while the app was closed).</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>Phases set by hand in debug mode; the result notes them.</summary>
    public int ManualPhaseChanges { get; private set; }

    /// <summary>
    /// Debug: sets the flight phase by hand (and keeps the payload lock consistent with it). With
    /// <paramref name="freeze"/> telemetry can't move the phase on until <see cref="FlightPhaseTracker.Frozen"/> is cleared.
    /// </summary>
    public void ForcePhase(FlightPhase phase, Telemetry at, bool freeze)
    {
        Phases.Frozen = freeze;
        if (phase == Phases.Phase) return;
        ManualPhaseChanges++;
        Phases.ForcePhase(phase, at);
        bool flying = phase is >= FlightPhase.TakeoffRoll and < FlightPhase.Arrived;
        if (flying && !Integrity.PayloadLocked) Integrity.LockPayload(at.PayloadKg);
        else if (phase is FlightPhase.Preflight or FlightPhase.TaxiOut) Integrity.UnlockPayload();
    }

    public void Process(Telemetry t)
    {
        _first ??= t;
        _samples.Add(t);
        if (_restartedAt is { } startedAt && _last is { } before)
        {
            _restartedAt = null;
            if (t.Timestamp - before.Timestamp > RestartGap) BridgeGap(before, t, startedAt);
            else Phases.Process(t);
        }
        else Phases.Process(t);
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

    private void BridgeGap(Telemetry before, Telemetry t, DateTimeOffset startedAt)
    {
        var minutes = (t.Timestamp - before.Timestamp).TotalMinutes;
        _notes.Add($"App {minutes:N0} min geschlossen; Flug danach aus der Aufzeichnung fortgesetzt.");
        bool flying = Phases.Phase is >= FlightPhase.TakeoffRoll and <= FlightPhase.Approach;
        if (flying && t.OnGround)
        {
            // Landed while nobody was watching: continue on the ground, the landing rate is unknown.
            Phases.Resume(t, startedAt);
            _notes.Add("Landung während die App geschlossen war – Landerate nicht erfasst.");
        }
        else Phases.Process(t);

        if (Integrity.PayloadLocked && Math.Abs(t.PayloadKg - before.PayloadKg) > 10)
        {
            Integrity.LockPayload(t.PayloadKg);
            _notes.Add($"Gewicht nach Neustart geändert: {before.PayloadKg:N0} → {t.PayloadKg:N0} kg (nicht gewertet).");
        }
    }

    public FlightSummary Summarize() => new(
        Phases.Phase,
        Phases.BlockOff,
        Phases.TakeoffTime,
        Phases.LandingTime,
        Phases.BlockOn,
        _distanceNm,
        _last is null ? 0 : Math.Max(0, (_fuelAtBlockOff ?? _first?.FuelKg ?? 0) - _last.FuelKg),
        _maxG,
        _minG,
        (int)_overspeedSec,
        (int)_stallSec,
        Phases.Touchdowns.Count > 0 ? Phases.Touchdowns[^1] : null,
        Integrity.Violations,
        _last?.Latitude ?? 0,
        _last?.Longitude ?? 0,
        Resumed,
        ManualPhaseChanges,
        _fuelAtBlockOff,
        _last?.FuelKg ?? 0,
        _notes);
}
