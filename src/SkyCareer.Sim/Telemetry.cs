using SkyCareer.Domain;

namespace SkyCareer.Sim;

/// <summary>
/// One sample of aircraft state, already converted to app units (kg, kt, ft, ft/min).
/// Produced by any <see cref="IFlightDataSource"/> so trackers never depend on SimConnect directly.
/// </summary>
public sealed record Telemetry
{
    public required DateTimeOffset Timestamp { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double AltitudeFt { get; init; }
    public double AltitudeAglFt { get; init; }
    public double GroundSpeedKt { get; init; }
    public double IndicatedAirspeedKt { get; init; }
    public double VerticalSpeedFpm { get; init; }
    public double GForce { get; init; } = 1;
    public bool OnGround { get; init; }
    public bool ParkingBrake { get; init; }
    public bool AnyEngineRunning { get; init; }
    public double FuelKg { get; init; }
    public double PayloadKg { get; init; }
    public double TotalWeightKg { get; init; }
    /// <summary>Touchdown vertical speed reported by the sim (ft/min, negative = descending). Only meaningful right after landing.</summary>
    public double TouchdownVsFpm { get; init; }
    public double SimulationRate { get; init; } = 1;
    public bool Slew { get; init; }
    public bool Paused { get; init; }
    public bool Crashed { get; init; }
    public bool OverspeedWarning { get; init; }
    public bool StallWarning { get; init; }
    public string AircraftTitle { get; init; } = "";

    public GeoPosition Position => new(Latitude, Longitude);
}

public enum ConnectionState { Disconnected, Connecting, Connected }

public interface IFlightDataSource : IAsyncDisposable
{
    ConnectionState State { get; }
    event EventHandler<ConnectionState>? StateChanged;
    event EventHandler<Telemetry>? TelemetryReceived;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
}

/// <summary>Write access to the aircraft's payload stations and fuel. Not every source supports it (replays don't).</summary>
public interface IAircraftLoader
{
    Task ApplyPayloadAsync(IReadOnlyList<Domain.Loading.StationLoad> stations, CancellationToken ct = default);
    /// <summary>Sets total fuel, distributed proportionally over the aircraft's tanks by capacity.</summary>
    Task ApplyFuelAsync(Mass totalFuel, CancellationToken ct = default);
}
