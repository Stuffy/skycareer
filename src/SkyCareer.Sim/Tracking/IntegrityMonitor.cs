namespace SkyCareer.Sim.Tracking;

public enum ViolationKind { Slew, SimRate, InflightRefuel, PayloadChanged, Teleport, AircraftChanged }

public sealed record IntegrityViolation(ViolationKind Kind, DateTimeOffset Time, string Detail);

/// <summary>
/// Detects actions that would make a career flight unfair: slewing, time acceleration, refuelling in the air,
/// changing payload after the takeoff roll started, position jumps and swapping the aircraft. Each kind is reported once per
/// continuous occurrence so a long slew doesn't flood the log.
/// </summary>
public sealed class IntegrityMonitor
{
    public sealed record Options
    {
        public double MaxSimRate { get; init; } = 1;
        public double FuelIncreaseToleranceKg { get; init; } = 5;
        public double PayloadChangeToleranceKg { get; init; } = 10;
        /// <summary>Extra distance on top of what ground speed allows before a jump counts as teleport.</summary>
        public double TeleportSlackNm { get; init; } = 2;
    }

    private readonly Options _opt;
    private readonly List<IntegrityViolation> _violations = [];
    private readonly HashSet<ViolationKind> _active = [];
    private Telemetry? _last;
    private double? _payloadAtDeparture;

    public IntegrityMonitor(Options? options = null) => _opt = options ?? new Options();

    public IReadOnlyList<IntegrityViolation> Violations => _violations;
    public event EventHandler<IntegrityViolation>? ViolationDetected;

    /// <summary>Call when the takeoff roll starts; payload is locked from then on.</summary>
    public void LockPayload(double payloadKg) => _payloadAtDeparture = payloadKg;

    public bool PayloadLocked => _payloadAtDeparture is not null;

    /// <summary>Releases the lock after a rejected takeoff, so the load can be corrected on the ground.</summary>
    public void UnlockPayload()
    {
        _payloadAtDeparture = null;
        _active.Remove(ViolationKind.PayloadChanged);
    }

    public void Process(Telemetry t, FlightPhase phase)
    {
        Flag(ViolationKind.Slew, t.Slew, t, "Slew mode active");
        Flag(ViolationKind.SimRate, t.SimulationRate > _opt.MaxSimRate + 0.01, t, $"Simulation rate {t.SimulationRate:0.##}x");

        // Only the flight itself counts: unloading after landing (GSX deboarding, a resume that catches up after the
        // aircraft was already parked) is expected.
        if (_payloadAtDeparture is { } locked && phase is >= FlightPhase.TakeoffRoll and <= FlightPhase.Approach)
            Flag(ViolationKind.PayloadChanged, Math.Abs(t.PayloadKg - locked) > _opt.PayloadChangeToleranceKg, t,
                $"Payload {t.PayloadKg:N0} kg, locked at {locked:N0} kg");

        if (_last is { } prev && !t.Paused && !prev.Paused)
        {
            bool airborne = !t.OnGround && !prev.OnGround;
            Flag(ViolationKind.InflightRefuel, airborne && t.FuelKg - prev.FuelKg > _opt.FuelIncreaseToleranceKg, t,
                $"Fuel increased from {prev.FuelKg:N0} to {t.FuelKg:N0} kg in flight");

            double seconds = Math.Max((t.Timestamp - prev.Timestamp).TotalSeconds, 0.001);
            double maxNm = Math.Max(t.GroundSpeedKt, prev.GroundSpeedKt) * 1.5 * seconds / 3600.0 * Math.Max(1, t.SimulationRate);
            double moved = prev.Position.DistanceNmTo(t.Position);
            Flag(ViolationKind.Teleport, !t.Slew && moved > maxNm + _opt.TeleportSlackNm, t, $"Position jumped {moved:N1} nm in {seconds:N0} s");

            Flag(ViolationKind.AircraftChanged,
                phase != FlightPhase.Preflight && !string.IsNullOrEmpty(prev.AircraftTitle) && t.AircraftTitle != prev.AircraftTitle, t,
                $"Aircraft changed from '{prev.AircraftTitle}' to '{t.AircraftTitle}'");
        }

        _last = t;
    }

    private void Flag(ViolationKind kind, bool condition, Telemetry t, string detail)
    {
        if (!condition)
        {
            _active.Remove(kind);
            return;
        }
        if (!_active.Add(kind)) return;
        var v = new IntegrityViolation(kind, t.Timestamp, detail);
        _violations.Add(v);
        ViolationDetected?.Invoke(this, v);
    }
}
