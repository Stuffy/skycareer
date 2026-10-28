namespace SkyCareer.Sim.Tests;

/// <summary>Builds a 1 Hz telemetry stream segment by segment, moving the aircraft north along its ground speed.</summary>
internal sealed class SyntheticFlight
{
    private readonly List<Telemetry> _samples = [];
    private Telemetry _state;

    public SyntheticFlight()
    {
        _state = new Telemetry
        {
            Timestamp = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero),
            Latitude = 50, Longitude = 8,
            OnGround = true, ParkingBrake = true,
            FuelKg = 800, PayloadKg = 700, AircraftTitle = "Cessna 208B Grand Caravan EX"
        };
    }

    public IReadOnlyList<Telemetry> Samples => _samples;

    /// <summary>Holds the given state for <paramref name="seconds"/> samples; altitude follows vertical speed.</summary>
    public SyntheticFlight Hold(int seconds, Func<Telemetry, Telemetry> change)
    {
        _state = change(_state);
        for (int i = 0; i < seconds; i++)
        {
            double dtH = 1 / 3600.0;
            double alt = Math.Max(0, _state.AltitudeAglFt + _state.VerticalSpeedFpm / 60.0);
            _state = _state with
            {
                Timestamp = _state.Timestamp.AddSeconds(1),
                Latitude = _state.Latitude + _state.GroundSpeedKt * dtH / 60.0,
                AltitudeAglFt = _state.OnGround ? 0 : alt,
                AltitudeFt = _state.OnGround ? 0 : alt,
                FuelKg = _state.AnyEngineRunning ? _state.FuelKg - 0.05 : _state.FuelKg
            };
            _samples.Add(_state);
        }
        return this;
    }

    /// <summary>A complete, clean VFR flight.</summary>
    public static SyntheticFlight Standard(double touchdownFpm = -180, double touchdownG = 1.2) => new SyntheticFlight()
        .Hold(30, t => t with { AnyEngineRunning = true })
        .Hold(120, t => t with { ParkingBrake = false, GroundSpeedKt = 12 })
        .Hold(20, t => t with { GroundSpeedKt = 60 })
        .Hold(300, t => t with { OnGround = false, GroundSpeedKt = 110, VerticalSpeedFpm = 800 })
        .Hold(600, t => t with { VerticalSpeedFpm = 0, GroundSpeedKt = 160 })
        .Hold(240, t => t with { VerticalSpeedFpm = -800, GroundSpeedKt = 140 })
        .Hold(1, t => t with { OnGround = true, VerticalSpeedFpm = 0, TouchdownVsFpm = touchdownFpm, GForce = touchdownG, GroundSpeedKt = 70 })
        .Hold(20, t => t with { GForce = 1, GroundSpeedKt = 40 })
        .Hold(120, t => t with { GroundSpeedKt = 12 })
        .Hold(20, t => t with { GroundSpeedKt = 0, ParkingBrake = true, AnyEngineRunning = false });
}
