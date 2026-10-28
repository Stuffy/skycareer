namespace SkyCareer.Domain.Aircraft;

public enum FuelType { JetA, AvGas }

public enum RunwaySurface { Paved, Gravel, Grass, Dirt, Water, Snow }

/// <summary>How the app gets payload and fuel into the simulated aircraft.</summary>
public enum LoadingMethod
{
    /// <summary>Write PAYLOAD STATION WEIGHT:n and FUEL TANK * QUANTITY via SimConnect.</summary>
    SimConnectStations,
    /// <summary>Aircraft manages payload itself through L:Vars (e.g. FBW, PMDG).</summary>
    LVar,
    /// <summary>Pilot loads in the aircraft's EFB; the app only verifies.</summary>
    Manual
}

public enum StationKind { Crew, Passengers, Baggage, Cargo }

/// <summary>Maps a sim payload station (1-based SimConnect index) to its use.</summary>
public sealed record PayloadStation(int Index, string Name, StationKind Kind, int Seats = 0, double MaxKg = 0);

/// <summary>
/// Static, data-driven description of an aircraft type. Loaded from content/aircraft/*.json
/// so new types can be added without code changes.
/// </summary>
public sealed record AircraftProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string IcaoType { get; init; }

    /// <summary>Case-insensitive substrings matched against ATC MODEL / TITLE to recognise the loaded aircraft.</summary>
    public IReadOnlyList<string> SimMatch { get; init; } = [];

    /// <summary>True if the sim's aircraft title contains one of <see cref="SimMatch"/>.</summary>
    public bool MatchesSimTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title) && SimMatch.Any(m => !string.IsNullOrWhiteSpace(m) && title.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>Empty weight as the sim reports EMPTY WEIGHT. Flight crew is added on top unless <see cref="CrewIncludedInEmptyWeight"/>.</summary>
    public required double OperatingEmptyKg { get; init; }
    public bool CrewIncludedInEmptyWeight { get; init; }
    /// <summary>Standard mass per pilot (EASA: 85 kg).</summary>
    public double CrewMassKg { get; init; } = 85;
    /// <summary>Standard mass per cabin crew member (EASA: 75 kg).</summary>
    public double CabinCrewMassKg { get; init; } = 75;
    /// <summary>Optional SimBrief airframe ID (e.g. "123456_1700000000000") used instead of the ICAO type for dispatch.</summary>
    public string? SimBriefAirframe { get; init; }
    public required double MaxZeroFuelKg { get; init; }
    public required double MaxTakeoffKg { get; init; }
    public required double MaxLandingKg { get; init; }
    public required double MaxFuelKg { get; init; }
    public FuelType FuelType { get; init; } = FuelType.JetA;

    public int PassengerSeats { get; init; }
    public double MaxCargoKg { get; init; }
    public int CrewCount { get; init; } = 1;
    public int CabinCrewCount { get; init; }

    public double CruiseKtas { get; init; }
    public double FuelBurnKgPerHour { get; init; }
    public double TaxiFuelKg { get; init; }
    public double MinRunwayM { get; init; }
    public IReadOnlyList<RunwaySurface> Surfaces { get; init; } = [RunwaySurface.Paved];

    /// <summary>Capability tags matched by mission requirements, e.g. "pax", "cargo", "stol", "medevac".</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public LoadingMethod Loading { get; init; } = LoadingMethod.SimConnectStations;
    public IReadOnlyList<PayloadStation> Stations { get; init; } = [];

    public AircraftEconomics Economics { get; init; } = new();

    public Mass OperatingEmpty => Mass.FromKg(OperatingEmptyKg);
    public Mass MaxZeroFuel => Mass.FromKg(MaxZeroFuelKg);
    public Mass MaxTakeoff => Mass.FromKg(MaxTakeoffKg);
    public Mass MaxLanding => Mass.FromKg(MaxLandingKg);
    public Mass MaxFuel => Mass.FromKg(MaxFuelKg);

    /// <summary>
    /// Pilots the type needs in commercial operation (simplified EASA rule): two for jets, aircraft above 5.7 t MTOW
    /// and more than 9 passenger seats, unless tagged <see cref="SinglePilotTag"/>.
    /// </summary>
    public int MinimumPilots =>
        !HasTag(SinglePilotTag) && (HasTag("jet") || MaxTakeoffKg > 5_700 || PassengerSeats > 9) ? 2 : 1;

    /// <summary>Pilots actually planned for weights and crew costs: <see cref="CrewCount"/>, at least <see cref="MinimumPilots"/>.</summary>
    public int Pilots => Math.Max(CrewCount, MinimumPilots);

    /// <summary>Crew mass that has to be loaded into the sim on top of the empty weight.</summary>
    public Mass CrewMass => CrewIncludedInEmptyWeight ? Mass.Zero : Mass.FromKg(Pilots * CrewMassKg + CabinCrewCount * CabinCrewMassKg);

    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    public const string MilitaryTag = "military";
    /// <summary>Military aircraft may use military airfields; civil aircraft never get them as destinations.</summary>
    public bool IsMilitary => HasTag(MilitaryTag);

    /// <summary>Certified for single-pilot operation (e.g. Citation CJ, Phenom 300): exempts the type from <see cref="MinimumPilots"/>.</summary>
    public const string SinglePilotTag = "single_pilot";
}

public sealed record AircraftEconomics
{
    public decimal PurchasePriceNew { get; init; }
    public decimal LeasePerMonth { get; init; }
    public decimal RentPerBlockHour { get; init; }
    public decimal MaintenancePerFlightHour { get; init; }
    public decimal InsurancePerMonth { get; init; }
}
