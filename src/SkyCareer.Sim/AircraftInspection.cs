namespace SkyCareer.Sim;

public sealed record SimPayloadStation(int Index, string Name, double WeightKg);

public sealed record SimFuelTank(string Name, double CapacityGallons, double QuantityGallons);

/// <summary>A runway as the sim's facility database knows it; ILS idents are null when the end has no ILS.</summary>
public sealed record SimRunway(string Designator, double LengthM, int Surface, string? PrimaryIls, string? SecondaryIls)
{
    public bool HasIls => PrimaryIls is not null || SecondaryIls is not null;
}

/// <param name="IlsApproachRunways">Runway ends with an ILS approach procedure, e.g. "28", "34".</param>
public sealed record AirportFacilityInfo(string Icao, IReadOnlyList<SimRunway> Runways, IReadOnlyList<string> IlsApproachRunways)
{
    /// <summary>
    /// ILS approaches are the reliable signal: in MSFS 2024 the runway ILS fields are empty at some airports
    /// that do have ILS approaches (seen at LSZH), so the runway idents only count as a fallback.
    /// </summary>
    public bool HasIls => IlsApproachRunways.Count > 0 || Runways.Any(r => r.HasIls);
}

/// <summary>Reads airport data (runways, ILS) from the simulator's navigation database.</summary>
public interface IAirportFacilityReader
{
    /// <summary>Null if the sim doesn't know the airport.</summary>
    Task<AirportFacilityInfo?> ReadAirportAsync(string icao, CancellationToken ct = default);
}

/// <summary>SimConnect ENGINE TYPE values.</summary>
public enum SimEngineType { Piston = 0, Jet = 1, None = 2, HeloTurbine = 3, Unsupported = 4, Turboprop = 5 }

/// <summary>Design data from aircraft.cfg; optional because not every aircraft exposes it.</summary>
public sealed record AircraftDesignInfo(
    SimEngineType EngineType,
    int EngineCount,
    string Category,
    double DesignCruiseSpeedKt,
    double DesignCruiseAltitudeFt)
{
    public bool IsHelicopter => Category.Contains("heli", StringComparison.OrdinalIgnoreCase) || EngineType == SimEngineType.HeloTurbine;

    /// <summary>
    /// DESIGN SPEED VC reflects the aircraft.cfg cruise speed, which is already true airspeed
    /// (checked live: 525 kt for the Citation X, its real cruise TAS), so it is used as-is.
    /// </summary>
    public double EstimatedCruiseKtas => Math.Max(0, DesignCruiseSpeedKt);
}

/// <summary>What the loaded aircraft reports about itself; used to build and check aircraft profiles.</summary>
public sealed record AircraftSnapshot(
    string Title,
    string AtcModel,
    string AtcType,
    double EmptyWeightKg,
    double MaxGrossWeightKg,
    double TotalWeightKg,
    double FuelKg,
    double FuelKgPerGallon,
    IReadOnlyList<SimPayloadStation> Stations,
    IReadOnlyList<SimFuelTank> Tanks,
    AircraftDesignInfo? Design = null)
{
    public double PayloadKg => Stations.Sum(s => s.WeightKg);
    public double TankCapacityGallons => Tanks.Sum(t => t.CapacityGallons);
}

/// <summary>Read-only diagnostics on the live aircraft. Only the SimConnect source implements it.</summary>
public interface IAircraftInspector
{
    Task<AircraftSnapshot> ReadAircraftAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads any SimVar or L:Var (e.g. "L:FSDT_GSX_BOARDING_STATE") once. Throws if the sim rejects it.
    /// Unknown L:Vars are not rejected: MSFS 2024 returns 0 for them.
    /// </summary>
    Task<double> ReadVariableAsync(string name, string unit = "number", CancellationToken ct = default);

    /// <summary>Reads up to <see cref="MaxBatchVariables"/> variables (unit "number") in a single SimConnect request.</summary>
    Task<double[]> ReadVariablesAsync(IReadOnlyList<string> names, CancellationToken ct = default);

    public const int MaxBatchVariables = 16;
}
