using SkyCareer.Sim;

namespace SkyCareer.Integrations.Gsx;

/// <summary>GSX service state as published in the FSDT_GSX_*_STATE L:Vars.</summary>
public enum GsxServiceState
{
    /// <summary>0: GSX is not running (unknown L:Vars read as 0 in MSFS 2024).</summary>
    Unknown = 0,
    Callable = 1,
    NotAvailable = 2,
    Bypassed = 3,
    Requested = 4,
    Performing = 5,
    Completed = 6
}

public sealed record GsxState
{
    public GsxServiceState Boarding { get; init; }
    public GsxServiceState Deboarding { get; init; }
    public GsxServiceState Refueling { get; init; }
    public GsxServiceState Catering { get; init; }
    public GsxServiceState Departure { get; init; }
    public GsxServiceState Deice { get; init; }
    public int PassengersBoarded { get; init; }
    public int PassengersBoardingTotal { get; init; }
    public int PassengersDeboarded { get; init; }
    public int PassengersDeboardingTotal { get; init; }
    /// <summary>Passenger count GSX works with (from SimBrief or the aircraft).</summary>
    public int Passengers { get; init; }
    public double CargoBoardingPercent { get; init; }
    public double CargoDeboardingPercent { get; init; }
    public bool FuelHoseConnected { get; init; }

    /// <summary>GSX sets every service state to at least 1 once it runs; all zeros means it isn't there.</summary>
    public bool Detected => new[] { Boarding, Deboarding, Refueling, Catering, Departure, Deice }.Any(s => s != GsxServiceState.Unknown);

    public static GsxState NotRunning { get; } = new();
}

/// <summary>
/// Reads GSX Pro's published L:Vars in one SimConnect batch. Names follow the GSX developer documentation
/// (service states 1–6) and third-party integrations; the progress variables should be confirmed with the
/// "Variable lesen" tool on the loading spike page when GSX updates.
/// </summary>
public sealed class GsxClient(IAircraftInspector inspector)
{
    public static readonly string[] Variables =
    [
        "L:FSDT_GSX_BOARDING_STATE",
        "L:FSDT_GSX_DEBOARDING_STATE",
        "L:FSDT_GSX_REFUELING_STATE",
        "L:FSDT_GSX_CATERING_STATE",
        "L:FSDT_GSX_DEPARTURE_STATE",
        "L:FSDT_GSX_DEICE_STATE",
        "L:FSDT_GSX_NUMPASSENGERS_BOARDING",
        "L:FSDT_GSX_NUMPASSENGERS_BOARDING_TOTAL",
        "L:FSDT_GSX_NUMPASSENGERS_DEBOARDING",
        "L:FSDT_GSX_NUMPASSENGERS_DEBOARDING_TOTAL",
        "L:FSDT_GSX_NUMPASSENGERS",
        "L:FSDT_GSX_BOARDING_CARGO_PERCENT",
        "L:FSDT_GSX_DEBOARDING_CARGO_PERCENT",
        "L:FSDT_GSX_FUELHOSE_CONNECTED"
    ];

    public async Task<GsxState> ReadAsync(CancellationToken ct = default) => FromValues(await inspector.ReadVariablesAsync(Variables, ct));

    public static GsxState FromValues(IReadOnlyList<double> v) => new()
    {
        Boarding = State(v[0]),
        Deboarding = State(v[1]),
        Refueling = State(v[2]),
        Catering = State(v[3]),
        Departure = State(v[4]),
        Deice = State(v[5]),
        PassengersBoarded = (int)Math.Round(v[6]),
        PassengersBoardingTotal = (int)Math.Round(v[7]),
        PassengersDeboarded = (int)Math.Round(v[8]),
        PassengersDeboardingTotal = (int)Math.Round(v[9]),
        Passengers = (int)Math.Round(v[10]),
        CargoBoardingPercent = Math.Clamp(v[11], 0, 100),
        CargoDeboardingPercent = Math.Clamp(v[12], 0, 100),
        FuelHoseConnected = v[13] > 0.5
    };

    private static GsxServiceState State(double v)
    {
        int i = (int)Math.Round(v);
        return i is >= 1 and <= 6 ? (GsxServiceState)i : GsxServiceState.Unknown;
    }
}
