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
    /// <summary>Magnetic variation at the aircraft, degrees, east positive (magnetic = true − variation); null in old recordings.</summary>
    public double? MagVar { get; init; }
    /// <summary>Ground track, degrees true; null in old recordings.</summary>
    public double? TrackTrueDeg { get; init; }
    /// <summary>Heading, degrees true; null in old recordings.</summary>
    public double? HeadingTrueDeg { get; init; }
    /// <summary>Bank angle in degrees (sign = direction); null in old recordings.</summary>
    public double? BankDeg { get; init; }
    public double? PitchDeg { get; init; }
    /// <summary>Ambient wind at the aircraft: speed in knots and the true direction it blows from.</summary>
    public double? WindSpeedKt { get; init; }
    public double? WindDirectionDeg { get; init; }
    /// <summary>Ambient visibility in metres.</summary>
    public double? VisibilityM { get; init; }
    /// <summary>The sim's UTC time (seconds since midnight) and day of the year: the sim's date, not the real one.</summary>
    public double? ZuluSeconds { get; init; }
    public double? ZuluDayOfYear { get; init; }

    /// <summary>What a landing right now would be flown in (night, visibility, crosswind); null fields where data is missing.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Domain.Career.LandingConditions Conditions => new(
        ZuluSeconds is { } s && ZuluDayOfYear is { } d ? Domain.World.SolarPosition.IsNight(Position, d, s) : null,
        VisibilityM,
        WindSpeedKt is { } w && WindDirectionDeg is { } dir && HeadingTrueDeg is { } h ? Domain.World.Navigation.CrosswindKt(dir, w, h) : null,
        WindSpeedKt, WindDirectionDeg);

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
    /// <summary>
    /// Sets any SimVar on the user aircraft (debug/spike use). Throws when SimConnect rejects it within a second;
    /// a variable that isn't settable may also be ignored silently, so read it back to be sure.
    /// </summary>
    Task WriteVariableAsync(string name, string unit, double value, CancellationToken ct = default);
}
