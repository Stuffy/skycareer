using SkyCareer.Domain.Aircraft;

namespace SkyCareer.Domain.Tests;

public class ProfileToolsTests
{
    private static AircraftProfile Profile(string id = "test_plane", double mtow = 4000) => new()
    {
        Id = id, Name = "Test", IcaoType = "TEST", SimMatch = ["Test"],
        OperatingEmptyKg = 2000, MaxZeroFuelKg = mtow, MaxTakeoffKg = mtow, MaxLandingKg = mtow, MaxFuelKg = 800,
        PassengerSeats = 4, MaxCargoKg = 200, CruiseKtas = 160, FuelBurnKgPerHour = 150,
        Stations = [new(1, "Pilot", StationKind.Crew, 1), new(2, "Cabin", StationKind.Passengers, 4), new(3, "Hold", StationKind.Baggage, MaxKg: 200)],
        Economics = new AircraftEconomics { PurchasePriceNew = 1_000_000, LeasePerMonth = 10_000, RentPerBlockHour = 500, MaintenancePerFlightHour = 100, InsurancePerMonth = 800 }
    };

    [Fact]
    public void Valid_profile_has_no_issues()
    {
        Assert.Empty(AircraftProfileValidator.Validate(Profile()));
    }

    [Fact]
    public void Broken_weights_and_ids_are_errors()
    {
        var p = Profile() with { Id = "Bad Id", MaxZeroFuelKg = 5000, MaxLandingKg = 4500, CruiseKtas = 0 };
        var errors = AircraftProfileValidator.Validate(p).Where(i => i.Level == IssueLevel.Error).Select(i => i.Message).ToList();

        Assert.Contains(errors, e => e.Contains("ID"));
        Assert.Contains(errors, e => e.Contains("MZFW"));
        Assert.Contains(errors, e => e.Contains("MLW"));
        Assert.Contains(errors, e => e.Contains("Reisegeschwindigkeit"));
        Assert.False(AircraftProfileValidator.IsValid(p));
    }

    [Fact]
    public void Seat_mismatch_is_only_a_warning()
    {
        var issues = AircraftProfileValidator.Validate(Profile() with { PassengerSeats = 6 });
        Assert.Equal(IssueLevel.Warning, Assert.Single(issues).Level);
    }

    [Fact]
    public void Larger_aircraft_are_planned_with_two_pilots()
    {
        Assert.Equal(1, Profile().Pilots);
        Assert.Equal(85, Profile().CrewMass.Kg, 3);

        var jet = Profile() with { Tags = ["pax", "jet"] };
        Assert.Equal(2, jet.Pilots);
        Assert.Equal(170, jet.CrewMass.Kg, 3);
        Assert.Equal(245, (jet with { CabinCrewCount = 1 }).CrewMass.Kg, 3);   // 2 × 85 kg pilots + 75 kg cabin crew
        Assert.Contains(AircraftProfileValidator.Validate(jet), i => i.Level == IssueLevel.Warning && i.Message.Contains("zwei Piloten"));

        Assert.Equal(2, (Profile() with { PassengerSeats = 10 }).Pilots);
        Assert.Equal(2, Profile(mtow: 6000).Pilots);
        Assert.Equal(1, (jet with { Tags = ["jet", AircraftProfile.SinglePilotTag] }).Pilots);
        Assert.Equal(3, (jet with { CrewCount = 3 }).Pilots);
    }

    [Fact]
    public void Estimator_fills_missing_values_scaled_by_mtow()
    {
        var reference = Profile("ref", mtow: 4000);
        var target = Profile("new", mtow: 8000) with { FuelBurnKgPerHour = 0, CruiseKtas = 0, Economics = new AircraftEconomics() };

        var filled = AircraftProfileEstimator.FillFrom(target, reference);

        Assert.Equal(300, filled.FuelBurnKgPerHour);
        Assert.Equal(160, filled.CruiseKtas);
        Assert.Equal(2_000_000, filled.Economics.PurchasePriceNew);
        Assert.Equal(200, filled.MaxCargoKg); // existing values are kept
    }

    [Fact]
    public void Most_similar_prefers_same_fuel_and_close_mass()
    {
        var target = Profile("t", 3900);
        var known = new[]
        {
            Profile("close_avgas", 3800) with { FuelType = FuelType.AvGas },
            Profile("close_jet", 4500),
            Profile("far_jet", 70000)
        };
        Assert.Equal("close_jet", AircraftProfileEstimator.MostSimilar(target, known)!.Id);
    }

    [Theory]
    [InlineData("$$:C750", "C750")]
    [InlineData("TT:ATCCOM.AC_MODEL_C172.0.text", "C172")]
    [InlineData("TT:ATCCOM.AC_MODEL A20N.0.text", "A20N")]
    [InlineData("Some long model name", null)]
    public void Icao_is_extracted_from_atc_model(string atcModel, string? expected)
    {
        Assert.Equal(expected, AircraftProfileEstimator.IcaoFromAtcModel(atcModel));
    }

    [Fact]
    public void Slug_is_a_valid_id()
    {
        Assert.Equal("citation_x_winglets", AircraftProfileEstimator.Slug("Citation X (Winglets)"));
        Assert.Equal("aircraft", AircraftProfileEstimator.Slug("äöü"));
    }
}
