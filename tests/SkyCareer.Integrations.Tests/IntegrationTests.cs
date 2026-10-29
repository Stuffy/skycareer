using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Loading;
using SkyCareer.Domain.World;
using SkyCareer.Integrations.BeyondAtc;
using SkyCareer.Integrations.Gsx;

namespace SkyCareer.Integrations.Tests;

public class GsxTests
{
    private static GsxState State(GsxServiceState boarding = GsxServiceState.Callable, GsxServiceState deboarding = GsxServiceState.NotAvailable,
        GsxServiceState refuel = GsxServiceState.Callable, int boarded = 0, int total = 0, double cargo = 0, int deboarded = 0) => new()
    {
        Boarding = boarding, Deboarding = deboarding, Refueling = refuel, Catering = GsxServiceState.Callable,
        Departure = GsxServiceState.Callable, Deice = GsxServiceState.NotAvailable,
        PassengersBoarded = boarded, PassengersBoardingTotal = total, CargoBoardingPercent = cargo,
        PassengersDeboarded = deboarded, PassengersDeboardingTotal = total
    };

    [Fact]
    public void Values_are_mapped_and_zero_means_not_running()
    {
        var running = GsxClient.FromValues([5, 1, 6, 1, 1, 2, 40, 120, 0, 0, 120, 35, 0, 1]);
        Assert.True(running.Detected);
        Assert.Equal(GsxServiceState.Performing, running.Boarding);
        Assert.Equal(GsxServiceState.Completed, running.Refueling);
        Assert.Equal(40, running.PassengersBoarded);
        Assert.Equal(35, running.CargoBoardingPercent);
        Assert.True(running.FuelHoseConnected);

        Assert.False(GsxClient.FromValues(new double[14]).Detected);
    }

    [Fact]
    public void Boarding_fills_cabin_progressively_and_sets_fuel_after_refuel()
    {
        var sync = new GsxLoadSync();
        Assert.Null(sync.Next(GsxState.NotRunning));

        var empty = sync.Next(State());
        Assert.Equal((0.0, 0.0), (empty!.PassengerFraction, empty.CargoFraction));

        Assert.Null(sync.Next(State())); // nothing changed

        var refuelDone = sync.Next(State(refuel: GsxServiceState.Completed));
        Assert.True(refuelDone!.ApplyFuel);
        Assert.False(refuelDone.WritesPayload);

        var half = sync.Next(State(GsxServiceState.Performing, refuel: GsxServiceState.Completed, boarded: 60, total: 120, cargo: 50));
        Assert.Equal(0.5, half!.PassengerFraction, 3);
        Assert.Equal(0.5, half.CargoFraction, 3);
        Assert.False(half.ApplyFuel);

        // Below the 5 % step: no write.
        Assert.Null(sync.Next(State(GsxServiceState.Performing, refuel: GsxServiceState.Completed, boarded: 63, total: 120, cargo: 52)));

        var done = sync.Next(State(GsxServiceState.Completed, refuel: GsxServiceState.Completed, boarded: 120, total: 120, cargo: 100));
        Assert.Equal((1.0, 1.0), (done!.PassengerFraction, done.CargoFraction));
    }

    [Fact]
    public void Fuel_is_set_at_end_of_boarding_when_gsx_does_not_refuel()
    {
        var sync = new GsxLoadSync();
        sync.Next(State());
        var done = sync.Next(State(GsxServiceState.Completed, boarded: 4, total: 4, cargo: 100));
        Assert.True(done!.ApplyFuel);
    }

    [Fact]
    public void Deboarding_empties_the_aircraft()
    {
        var sync = new GsxLoadSync();
        sync.Next(State(GsxServiceState.Completed, boarded: 100, total: 100, cargo: 100));
        var mid = sync.Next(State(GsxServiceState.Completed, GsxServiceState.Performing, boarded: 100, total: 100, deboarded: 25) with { CargoDeboardingPercent = 50 });
        Assert.Equal(0.75, mid!.PassengerFraction, 3);
        Assert.Equal(0.5, mid.CargoFraction, 3);
        var end = sync.Next(State(GsxServiceState.Completed, GsxServiceState.Completed, boarded: 100, total: 100, deboarded: 100));
        Assert.Equal((0.0, 0.0), (end!.PassengerFraction, end.CargoFraction));
    }

    [Fact]
    public void Scaling_keeps_crew_and_scales_cabin_and_holds()
    {
        var ac = new AircraftProfile
        {
            Id = "t", Name = "T", IcaoType = "T", OperatingEmptyKg = 2000, MaxZeroFuelKg = 4000, MaxTakeoffKg = 4000, MaxLandingKg = 4000,
            MaxFuelKg = 1000, PassengerSeats = 4, MaxCargoKg = 500, CruiseKtas = 150, FuelBurnKgPerHour = 150,
            Stations = [new(1, "Pilot", StationKind.Crew, 1), new(2, "Cabin", StationKind.Passengers, 4), new(3, "Hold", StationKind.Cargo, MaxKg: 500)]
        };
        var sheet = LoadSheetBuilder.Build(ac, new PassengerCount(4), BaggageProfile.Domestic, Mass.FromKg(100), FuelPlanner.Plan(ac, 100, FlightRules.Vfr));

        var scaled = GsxLoadSync.ScaleStations(sheet, 0.5, 0.25);
        Assert.Equal(85, scaled.Single(s => s.Index == 1).Mass.Kg, 3);
        Assert.Equal(4 * 79.38 * 0.5, scaled.Single(s => s.Index == 2).Mass.Kg, 3);
        Assert.Equal((4 * 22.68 + 100) * 0.25, scaled.Single(s => s.Index == 3).Mass.Kg, 3);
    }

    [Fact]
    public void Passenger_mismatch_is_reported()
    {
        Assert.Null(GsxLoadSync.PassengerMismatch(State(total: 120), new PassengerCount(118, 2)));
        Assert.NotNull(GsxLoadSync.PassengerMismatch(State(total: 150), new PassengerCount(118, 2)));
        Assert.Null(GsxLoadSync.PassengerMismatch(State(), new PassengerCount(118, 2)));
    }
}

public class GsxCrewTests
{
    [Fact]
    public void Gsx_may_count_the_crew_from_the_ofp()
    {
        var mission = new PassengerCount(8);
        var state = new GsxState { Boarding = GsxServiceState.Callable, PassengersBoardingTotal = 11 };
        Assert.Null(GsxLoadSync.PassengerMismatch(state, mission, crewAsPassengers: 3));
        Assert.NotNull(GsxLoadSync.PassengerMismatch(state, mission));
        Assert.NotNull(GsxLoadSync.PassengerMismatch(state with { PassengersBoardingTotal = 12 }, mission, 3));
    }
}

public class PlnTests
{
    private static readonly Airport Eddf = new("EDDF", "Frankfurt", new GeoPosition(50.033333, 8.570556), AirportSize.Large, 4000, [RunwaySurface.Paved], "DE", 364);
    private static readonly Airport Eddm = new("EDDM", "München", new GeoPosition(48.353802, 11.7861), AirportSize.Large, 4000, [RunwaySurface.Paved], "DE", 1487);

    [Fact]
    public void Position_uses_msfs_dms_format()
    {
        Assert.Equal("N50° 2' 0.00\",E8° 34' 14.00\",+000364.00", PlnExporter.Lla(Eddf.Position, 364));
        Assert.Equal("S33° 56' 46.00\",W118° 24' 29.00\",-000010.00", PlnExporter.Lla(new GeoPosition(-33.946111, -118.408056), -10));
    }

    [Fact]
    public void Cruise_altitude_follows_semicircular_rule()
    {
        // EDDF → EDDM (162 nm) is south-east, i.e. eastbound: odd thousands (+500 ft VFR).
        Assert.Equal(7500, PlnExporter.CruiseAltitude(Eddf.Position, Eddm.Position, PlanRules.Vfr));
        Assert.Equal(15000, PlnExporter.CruiseAltitude(Eddf.Position, Eddm.Position, PlanRules.Ifr));
        // The way back is westbound: even thousands.
        Assert.Equal(8500, PlnExporter.CruiseAltitude(Eddm.Position, Eddf.Position, PlanRules.Vfr));
        Assert.Equal(16000, PlnExporter.CruiseAltitude(Eddm.Position, Eddf.Position, PlanRules.Ifr));
    }

    [Fact]
    public async Task Plan_contains_departure_destination_and_waypoints()
    {
        var doc = PlnExporter.Build(Eddf, Eddm, PlanRules.Vfr);
        var plan = doc.Root!.Element("FlightPlan.FlightPlan")!;
        Assert.Equal("VFR", plan.Element("FPType")!.Value);
        Assert.Equal("EDDF", plan.Element("DepartureID")!.Value);
        Assert.Equal("EDDM", plan.Element("DestinationID")!.Value);
        Assert.Equal(["EDDF", "EDDM"], plan.Elements("ATCWaypoint").Select(w => w.Attribute("id")!.Value));

        var dir = Path.Combine(Path.GetTempPath(), $"skyc-pln-{Guid.NewGuid():N}");
        try
        {
            var path = await PlnExporter.SaveAsync(doc, dir, "EDDF-EDDM");
            Assert.EndsWith(".pln", path);
            Assert.Contains("<DepartureID>EDDF</DepartureID>", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
