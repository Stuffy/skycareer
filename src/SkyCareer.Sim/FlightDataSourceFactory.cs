namespace SkyCareer.Sim;

public static class FlightDataSourceFactory
{
    /// <summary>True when this build was compiled against the MSFS SDK's SimConnect wrapper.</summary>
    // A property rather than a const so callers' "if not available" branches don't become unreachable-code warnings.
#if HAS_SIMCONNECT
    public static bool SimConnectAvailable => true;
#else
    public static bool SimConnectAvailable => false;
#endif

    /// <summary>Creates the live simulator connection, or null if the build has no SimConnect support.</summary>
    public static IFlightDataSource? CreateLive() =>
#if HAS_SIMCONNECT
        new SimConnectIntegration.SimConnectSource();
#else
        null;
#endif

    /// <summary>Loads the SimConnect libraries without connecting. Null means they load fine.</summary>
    public static string? ProbeLibrary() =>
#if HAS_SIMCONNECT
        SimConnectIntegration.SimConnectSource.ProbeLibrary();
#else
        "Built without SimConnect support.";
#endif
}
