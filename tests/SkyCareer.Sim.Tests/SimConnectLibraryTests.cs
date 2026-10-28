namespace SkyCareer.Sim.Tests;

public class SimConnectLibraryTests
{
    [Fact]
    public void SimConnect_library_loads_when_compiled_in()
    {
        if (!FlightDataSourceFactory.SimConnectAvailable) return; // build without the MSFS SDK

        Assert.Null(FlightDataSourceFactory.ProbeLibrary());
    }
}
