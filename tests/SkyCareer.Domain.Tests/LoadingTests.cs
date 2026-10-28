using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Loading;

namespace SkyCareer.Domain.Tests;

public class LoadingTests
{
    private static AircraftProfile Caravan() => new()
    {
        Id = "c208b", Name = "Caravan", IcaoType = "C208",
        OperatingEmptyKg = 2145, MaxZeroFuelKg = 3995, MaxTakeoffKg = 3995, MaxLandingKg = 3856, MaxFuelKg = 1009,
        PassengerSeats = 9, MaxCargoKg = 1100, CrewCount = 1, CruiseKtas = 175, FuelBurnKgPerHour = 175, TaxiFuelKg = 20,
        Stations =
        [
            new(1, "Pilot", StationKind.Crew, Seats: 1),
            new(2, "Front", StationKind.Passengers, Seats: 4),
            new(3, "Rear", StationKind.Passengers, Seats: 5),
            new(4, "Cargo A", StationKind.Cargo, MaxKg: 600),
            new(5, "Cargo B", StationKind.Cargo, MaxKg: 400)
        ]
    };

    [Fact]
    public void Standard_masses_follow_simbrief_with_one_bag_per_seated_passenger()
    {
        var table = PassengerMassTable.Default;
        var pax = new PassengerCount(Adults: 10, Children: 2, Infants: 1);

        Assert.Equal(10 * 79.38 + 2 * 35 + 10, table.PassengerMass(pax, PassengerMassMode.Standard, new Random(1)).Kg, 6);
        Assert.Equal(12 * 22.68, table.BaggageMass(pax, BaggageProfile.European, PassengerMassMode.Standard, new Random(1)).Kg, 6);
        Assert.Equal(0, table.BaggageMass(pax, BaggageProfile.HandOnly, PassengerMassMode.Standard, new Random(1)).Kg);
    }

    [Fact]
    public void Randomized_masses_average_close_to_standard()
    {
        var table = PassengerMassTable.Default;
        var pax = new PassengerCount(Adults: 5000);
        var mass = table.PassengerMass(pax, PassengerMassMode.Randomized, new Random(42));

        Assert.InRange(mass.Kg / 5000, 77.5, 81.5);
    }

    [Fact]
    public void Load_sheet_sums_weights_and_distributes_to_stations()
    {
        var ac = Caravan();
        var fuel = FuelPlanner.Plan(ac, 150, FlightRules.Vfr);
        var sheet = LoadSheetBuilder.Build(ac, new PassengerCount(6), BaggageProfile.Domestic, Mass.FromKg(200), fuel);

        Assert.Equal(6 * 79.38, sheet.PassengerMass.Kg, 6);
        Assert.Equal(6 * 22.68, sheet.BaggageMass.Kg, 6);
        Assert.Equal(2145 + 85 + 6 * 79.38 + 6 * 22.68 + 200, sheet.ZeroFuelWeight.Kg, 3);
        Assert.Equal(sheet.SimPayload.Kg, sheet.Stations.Sum(s => s.Mass.Kg), 3);

        // Front station fills first (4 seats), the rest goes aft.
        Assert.Equal(4 * 79.38, sheet.Stations.Single(s => s.Index == 2).Mass.Kg, 3);
        Assert.Equal(2 * 79.38, sheet.Stations.Single(s => s.Index == 3).Mass.Kg, 3);
        // Hold load is split by capacity 600:400.
        Assert.Equal((6 * 22.68 + 200) * 0.6, sheet.Stations.Single(s => s.Index == 4).Mass.Kg, 3);
        Assert.True(sheet.IsWithinLimits);
    }

    [Fact]
    public void Overloaded_sheet_reports_violations()
    {
        var ac = Caravan();
        var fuel = FuelPlanner.Plan(ac, 600, FlightRules.Ifr, alternateDistanceNm: 80);
        var sheet = LoadSheetBuilder.Build(ac, new PassengerCount(9), BaggageProfile.Intercontinental, Mass.FromKg(1000), fuel);

        Assert.Contains(LimitViolation.CargoExceeded, sheet.Violations);
        Assert.Contains(LimitViolation.TakeoffWeight, sheet.Violations);
        Assert.False(sheet.IsWithinLimits);
    }

    [Fact]
    public void Ifr_fuel_includes_contingency_alternate_and_final_reserve()
    {
        var ac = Caravan();
        var ifr = FuelPlanner.Plan(ac, 175, FlightRules.Ifr, alternateDistanceNm: 35);
        var vfr = FuelPlanner.Plan(ac, 175, FlightRules.Vfr);

        // 175 nm * 1.05 / 175 kt = 63 min + 10 min = 73 min at 175 kg/h.
        Assert.Equal(73 * 175 / 60.0, ifr.Trip.Kg, 3);
        Assert.Equal(30 * 175 / 60.0, ifr.FinalReserve.Kg, 3);
        Assert.True(ifr.Contingency.Kg > 0 && ifr.Alternate.Kg > 0);
        Assert.Equal(0, vfr.Contingency.Kg);
        Assert.True(ifr.Block > vfr.Block);
    }

    [Fact]
    public void Verifier_accepts_small_deviation_and_flags_large_one()
    {
        var ac = Caravan();
        var sheet = LoadSheetBuilder.Build(ac, new PassengerCount(4), BaggageProfile.Domestic, Mass.Zero, FuelPlanner.Plan(ac, 100, FlightRules.Vfr));

        Assert.Empty(LoadVerifier.Verify(sheet, sheet.SimPayload + Mass.FromKg(3), sheet.Fuel.Block - Mass.FromKg(4)));

        var deviations = LoadVerifier.Verify(sheet, sheet.SimPayload, sheet.Fuel.Block + Mass.FromKg(150));
        Assert.Equal("Fuel", Assert.Single(deviations).Item);
    }

    [Fact]
    public void Mass_converts_pounds()
    {
        Assert.Equal(100, Mass.FromLb(220.462262).Kg, 3);
        Assert.Equal(220.462, Mass.FromKg(100).Lb, 2);
    }

    [Fact]
    public void Great_circle_distance_is_plausible()
    {
        var eddf = new GeoPosition(50.0333, 8.5706);
        var eddm = new GeoPosition(48.3538, 11.7861);
        Assert.InRange(eddf.DistanceNmTo(eddm), 160, 165);
    }
}
