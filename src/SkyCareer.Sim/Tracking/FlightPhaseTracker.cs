namespace SkyCareer.Sim.Tracking;

public enum FlightPhase
{
    Preflight,
    TaxiOut,
    TakeoffRoll,
    Climb,
    Cruise,
    Descent,
    Approach,
    Rollout,
    TaxiIn,
    Arrived,
    Crashed
}

public sealed record PhaseChange(FlightPhase From, FlightPhase To, Telemetry At);

public sealed record TouchdownReport(DateTimeOffset Time, double VerticalSpeedFpm, double GForce, double GroundSpeedKt,
    double Latitude, double Longitude, int Bounces);

/// <summary>
/// Derives the flight phase from a telemetry stream. Transitions require their condition to hold for a
/// short time (hysteresis) so turbulence or a single bad sample can't flip the phase.
/// </summary>
public sealed class FlightPhaseTracker
{
    public sealed record Options
    {
        public double TaxiSpeedKt { get; init; } = 3;
        public double TakeoffRollSpeedKt { get; init; } = 30;
        public double RolloutEndSpeedKt { get; init; } = 30;
        public double LevelVsFpm { get; init; } = 300;
        public double ClimbDescentVsFpm { get; init; } = 500;
        public double ApproachAglFt { get; init; } = 2500;
        public TimeSpan LiftoffConfirm { get; init; } = TimeSpan.FromSeconds(3);
        public TimeSpan LevelConfirm { get; init; } = TimeSpan.FromSeconds(60);
        public TimeSpan VerticalConfirm { get; init; } = TimeSpan.FromSeconds(30);
        public TimeSpan ParkedConfirm { get; init; } = TimeSpan.FromSeconds(5);
    }

    private readonly Options _opt;
    private readonly Dictionary<string, DateTimeOffset> _since = new();
    private Telemetry? _last;
    private DateTimeOffset? _touchdownAt;
    private TouchdownReport? _pendingTouchdown;
    private bool _airborneDuringRollout;

    public FlightPhaseTracker(Options? options = null) => _opt = options ?? new Options();

    public FlightPhase Phase { get; private set; } = FlightPhase.Preflight;
    public DateTimeOffset? BlockOff { get; private set; }
    public DateTimeOffset? TakeoffTime { get; private set; }
    public DateTimeOffset? LandingTime { get; private set; }
    public DateTimeOffset? BlockOn { get; private set; }
    public IReadOnlyList<TouchdownReport> Touchdowns => _touchdowns;
    private readonly List<TouchdownReport> _touchdowns = [];

    public event EventHandler<PhaseChange>? PhaseChanged;
    public event EventHandler<TouchdownReport>? TouchedDown;

    public void Process(Telemetry t)
    {
        if (Phase is FlightPhase.Crashed or FlightPhase.Arrived)
        {
            _last = t;
            return;
        }

        if (t.Crashed)
        {
            SetPhase(FlightPhase.Crashed, t);
            _last = t;
            return;
        }

        UpdatePendingTouchdown(t);

        switch (Phase)
        {
            case FlightPhase.Preflight:
                if (t.OnGround && t.GroundSpeedKt > _opt.TaxiSpeedKt && !t.ParkingBrake)
                {
                    BlockOff = t.Timestamp;
                    SetPhase(FlightPhase.TaxiOut, t);
                }
                break;

            case FlightPhase.TaxiOut:
                if (t.OnGround && t.GroundSpeedKt > _opt.TakeoffRollSpeedKt)
                    SetPhase(FlightPhase.TakeoffRoll, t);
                break;

            case FlightPhase.TakeoffRoll:
                if (Held("liftoff", !t.OnGround, _opt.LiftoffConfirm, t))
                {
                    TakeoffTime ??= t.Timestamp;
                    SetPhase(FlightPhase.Climb, t);
                }
                else if (t.OnGround && t.GroundSpeedKt < _opt.TaxiSpeedKt * 5)
                    SetPhase(FlightPhase.TaxiOut, t); // rejected takeoff
                break;

            case FlightPhase.Climb:
            case FlightPhase.Cruise:
            case FlightPhase.Descent:
            case FlightPhase.Approach:
                if (t.OnGround)
                {
                    RegisterTouchdown(t);
                    SetPhase(FlightPhase.Rollout, t);
                    break;
                }
                UpdateAirbornePhase(t);
                break;

            case FlightPhase.Rollout:
                if (!t.OnGround)
                {
                    // Sustained lift-off is a touch-and-go; anything shorter is a bounce counted on re-contact.
                    _airborneDuringRollout = true;
                    if (Held("rollout-airborne", true, _opt.LiftoffConfirm, t))
                    {
                        _airborneDuringRollout = false;
                        SetPhase(FlightPhase.Climb, t);
                    }
                    break;
                }
                _since.Remove("rollout-airborne");
                if (_airborneDuringRollout)
                {
                    _airborneDuringRollout = false;
                    RegisterBounce(t);
                }
                if (t.GroundSpeedKt < _opt.RolloutEndSpeedKt)
                    SetPhase(FlightPhase.TaxiIn, t);
                break;

            case FlightPhase.TaxiIn:
                if (!t.OnGround && Held("taxiin-airborne", true, _opt.LiftoffConfirm, t))
                {
                    SetPhase(FlightPhase.Climb, t);
                    break;
                }
                if (Held("parked", t.OnGround && t.GroundSpeedKt < 1 && t.ParkingBrake && !t.AnyEngineRunning, _opt.ParkedConfirm, t))
                {
                    BlockOn = t.Timestamp;
                    SetPhase(FlightPhase.Arrived, t);
                }
                break;
        }

        _last = t;
    }

    private void UpdateAirbornePhase(Telemetry t)
    {
        bool climbing = Held("climbing", t.VerticalSpeedFpm > _opt.ClimbDescentVsFpm, _opt.VerticalConfirm, t);
        bool descending = Held("descending", t.VerticalSpeedFpm < -_opt.ClimbDescentVsFpm, _opt.VerticalConfirm, t);
        bool level = Held("level", Math.Abs(t.VerticalSpeedFpm) < _opt.LevelVsFpm, _opt.LevelConfirm, t);

        switch (Phase)
        {
            case FlightPhase.Climb:
                if (level && t.AltitudeAglFt > 1000) SetPhase(FlightPhase.Cruise, t);
                else if (descending) SetPhase(FlightPhase.Descent, t);
                break;
            case FlightPhase.Cruise:
                if (descending) SetPhase(FlightPhase.Descent, t);
                else if (climbing) SetPhase(FlightPhase.Climb, t);
                break;
            case FlightPhase.Descent:
                if (t.AltitudeAglFt < _opt.ApproachAglFt) SetPhase(FlightPhase.Approach, t);
                else if (climbing) SetPhase(FlightPhase.Climb, t);
                else if (level) SetPhase(FlightPhase.Cruise, t);
                break;
            case FlightPhase.Approach:
                // Go-around: sustained climb away from the ground.
                if (climbing && t.AltitudeAglFt > _opt.ApproachAglFt * 0.4) SetPhase(FlightPhase.Climb, t);
                break;
        }
    }

    private void RegisterTouchdown(Telemetry t)
    {
        double vs = t.TouchdownVsFpm != 0 ? t.TouchdownVsFpm : _last?.VerticalSpeedFpm ?? t.VerticalSpeedFpm;
        double g = Math.Max(t.GForce, _last?.GForce ?? 1);

        var report = new TouchdownReport(t.Timestamp, vs, g, t.GroundSpeedKt, t.Latitude, t.Longitude, 0);
        _touchdowns.Add(report);
        _pendingTouchdown = report;
        _touchdownAt = t.Timestamp;
        LandingTime = t.Timestamp;
        TouchedDown?.Invoke(this, report);
    }

    /// <summary>A short hop back into the air during rollout that ended on the ground again.</summary>
    private void RegisterBounce(Telemetry t)
    {
        if (_touchdowns.Count == 0) return;
        var report = _touchdowns[^1];
        _touchdowns[^1] = report with { Bounces = report.Bounces + 1, GForce = Math.Max(report.GForce, t.GForce) };
        _pendingTouchdown = _touchdowns[^1];
        _touchdownAt = t.Timestamp;
    }

    /// <summary>G often peaks one or two samples after the gear touches; keep updating the last report briefly.</summary>
    private void UpdatePendingTouchdown(Telemetry t)
    {
        if (_pendingTouchdown is null || _touchdownAt is null) return;
        if (t.Timestamp - _touchdownAt.Value > TimeSpan.FromSeconds(2))
        {
            _pendingTouchdown = null;
            return;
        }
        if (t.GForce > _pendingTouchdown.GForce)
        {
            _pendingTouchdown = _pendingTouchdown with { GForce = t.GForce };
            _touchdowns[^1] = _pendingTouchdown;
        }
    }

    private bool Held(string key, bool condition, TimeSpan duration, Telemetry t)
    {
        if (!condition)
        {
            _since.Remove(key);
            return false;
        }
        if (!_since.TryGetValue(key, out var start))
            _since[key] = start = t.Timestamp;
        return t.Timestamp - start >= duration;
    }

    private void SetPhase(FlightPhase phase, Telemetry t)
    {
        if (phase == Phase) return;
        var from = Phase;
        Phase = phase;
        _since.Clear();
        PhaseChanged?.Invoke(this, new PhaseChange(from, phase, t));
    }
}
