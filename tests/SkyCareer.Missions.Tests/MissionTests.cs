using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.World;
using SkyCareer.Integrations.SimBrief;
using SkyCareer.Missions.Generation;

namespace SkyCareer.Missions.Tests;

public class MissionTests
{
    private static readonly GameContent Content = ContentLoader.Load(FindContentRoot());

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("SkyCareer.sln not found"), "content");
    }

    private static AircraftProfile Aircraft(string id) => Content.Aircraft.Single(a => a.Id == id);
    private static MissionType Mission(string id) => Content.MissionTypes.Single(m => m.Id == id);

    [Fact]
    public void Shipped_content_loads_without_errors()
    {
        Assert.Empty(Content.Errors);
        Assert.True(Content.Aircraft.Count >= 5);
        Assert.True(Content.MissionTypes.Count >= 6);
    }

    [Theory]
    [InlineData("a20n", "scheduled_pax", true)]
    [InlineData("c172", "scheduled_pax", false)]
    [InlineData("h145", "scheduled_pax", false)]
    [InlineData("c208b", "bush_supply", true)]
    [InlineData("a20n", "bush_supply", false)]
    [InlineData("h145", "air_ambulance", true)]
    [InlineData("c172", "air_ambulance", false)]
    [InlineData("c208b", "cargo_feeder", true)]
    [InlineData("kingair350", "vip_charter", true)]
    [InlineData("c172", "vip_charter", false)]
    public void Mission_types_are_restricted_by_aircraft(string aircraft, string mission, bool eligible)
    {
        var result = RequirementEvaluator.Evaluate(Mission(mission), Aircraft(aircraft));
        Assert.Equal(eligible, result.Eligible);
        if (!eligible) Assert.NotEmpty(result.Reasons);
    }

    [Fact]
    public void Generated_offers_always_fit_aircraft_limits()
    {
        var airports = new List<Airport>
        {
            new("EDDF", "Frankfurt", new GeoPosition(50.0333, 8.5706), AirportSize.Large, 4000, [RunwaySurface.Paved]),
            new("EDDM", "München", new GeoPosition(48.3538, 11.7861), AirportSize.Large, 4000, [RunwaySurface.Paved]),
            new("EDDH", "Hamburg", new GeoPosition(53.6304, 9.9882), AirportSize.Large, 3666, [RunwaySurface.Paved]),
            new("LEPA", "Palma", new GeoPosition(39.5517, 2.7388), AirportSize.Large, 3270, [RunwaySurface.Paved]),
            new("EDFE", "Egelsbach", new GeoPosition(49.9608, 8.6436), AirportSize.Small, 1400, [RunwaySurface.Paved]),
            new("EDGZ", "Weinheim", new GeoPosition(49.5675, 8.6108), AirportSize.Small, 770, [RunwaySurface.Grass])
        };
        var generator = new StandardMissionGenerator();
        var rng = new Random(7);
        int offers = 0;

        foreach (var aircraft in Content.Aircraft)
        foreach (var type in RequirementEvaluator.AvailableFor(Content.MissionTypes, aircraft))
        foreach (var origin in airports)
        {
            var offer = generator.TryGenerate(new GenerationContext(type, aircraft, origin, airports, DateTimeOffset.UnixEpoch, rng));
            if (offer is null) continue;
            offers++;
            Assert.True(offer.PlannedLoad.IsWithinLimits, $"{aircraft.Id}/{type.Id}: {string.Join(",", offer.PlannedLoad.Violations)}");
            Assert.True(offer.Passengers.Seated <= aircraft.PassengerSeats);
            Assert.InRange(offer.DistanceNm, type.Distance.MinNm, type.Distance.MaxNm);
            Assert.True(offer.Destination.LongestRunwayM >= aircraft.MinRunwayM);
            Assert.True(offer.Payout.Amount > 0);
        }

        Assert.True(offers > 10);
    }

    [Fact]
    public void SimBrief_dispatch_link_contains_mission_data()
    {
        var uri = SimBriefClient.BuildDispatchUri(new DispatchRequest("EDDF", "EDDM", "A20N", 150, 2500));
        Assert.Contains("orig=EDDF", uri.Query);
        Assert.Contains("dest=EDDM", uri.Query);
        Assert.Contains("pax=150", uri.Query);
        Assert.Contains("cargo=2.5", uri.Query);
    }

    [Fact]
    public void SimBrief_ofp_is_parsed_and_validated()
    {
        const string json = """
            {
              "fetch": { "status": "Success" },
              "params": { "units": "kgs", "time_generated": "1761465600", "static_id": "SC-1" },
              "origin": { "icao_code": "EDDF" },
              "destination": { "icao_code": "EDDM" },
              "alternate": { "icao_code": "EDDN" },
              "aircraft": { "icaocode": "A20N" },
              "general": { "route": "SULUS UZ650 ...", "initial_altitude": "35000" },
              "weights": { "pax_count": "150", "bag_count": "150", "freight_added": "1000", "cargo": "2500", "payload": "15100", "est_zfw": "59800" },
              "fuel": { "plan_ramp": "6200", "enroute_burn": "3100" }
            }
            """;
        var ofp = SimBriefClient.Parse(json);

        Assert.Equal("EDDF", ofp.Origin);
        Assert.Equal(6200, ofp.BlockFuelKg);
        // SimBrief's "cargo" is bags plus freight; the mission's cargo is the freight alone.
        Assert.Equal(1000, ofp.FreightKg);
        Assert.Equal(1500, ofp.BaggageKg);
        var expected = new OfpExpectation("EDDF", "EDDM", "A20N", 150, 1000, 15100, 59800);
        Assert.Empty(OfpValidator.Validate(ofp, expected));
        Assert.Single(OfpValidator.Validate(ofp, expected with { Passengers = 140 }));
    }

    [Fact]
    public void SimBrief_ofp_that_dropped_bags_is_reported()
    {
        // Shape of a real Citation X OFP: SimBrief's airframe MZFW (11 068 kg) cut 3 of 9 bags.
        const string json = """
            {
              "fetch": { "status": "Success" },
              "params": { "units": "kgs", "time_generated": "1793107876" },
              "origin": { "icao_code": "EGGW" },
              "destination": { "icao_code": "EDDB" },
              "aircraft": { "icaocode": "C750" },
              "general": { "route": "MATCH Q295 BRAIN", "initial_altitude": "45000" },
              "weights": { "oew": "10197", "pax_count": "9", "bag_count": "9", "pax_count_actual": "9", "bag_count_actual": "6",
                           "pax_weight": "79.379", "bag_weight": "22.68", "freight_added": "0", "cargo": "157", "payload": "871",
                           "est_zfw": "11068", "max_zfw": "11068" },
              "fuel": { "plan_ramp": "2503", "enroute_burn": "1900" }
            }
            """;
        var ofp = SimBriefClient.Parse(json);
        Assert.Equal(6, ofp.BagCount);
        Assert.Equal(9, ofp.PlannedBagCount);

        var problems = OfpValidator.Validate(ofp, new OfpExpectation("EGGW", "EDDB", "C750", 9, 0, 1000, 11300, 16375));
        Assert.Contains(problems, p => p.Contains("3 von 9 Gepäckstücken"));
        Assert.Contains(problems, p => p.StartsWith("Zuladung"));
        Assert.Contains(problems, p => p.StartsWith("ZFW"));
        Assert.Contains(problems, p => p.StartsWith("MZFW im Flugzeugprofil"));
        Assert.DoesNotContain(problems, p => p.StartsWith("Fracht"));
    }

    [Fact]
    public void Dispatch_link_sends_airframe_and_freight_only()
    {
        var uri = SimBriefClient.BuildDispatchUri(new DispatchRequest("EGGW", "EDDB", "C750", 9, 120, Airframe: "123456_1700000000000")).ToString();
        Assert.Contains("type=123456_1700000000000", uri);
        Assert.Contains("cargo=0.12", uri);
        Assert.DoesNotContain("acdata", uri);   // the dispatch page ignores custom masses

        // No freight: SimBrief adds the bags itself, so cargo is left out instead of sending 0.
        Assert.DoesNotContain("cargo=", SimBriefClient.BuildDispatchUri(new DispatchRequest("EGGW", "EDDB", "C750", 9, 0)).ToString());
    }

    [Fact]
    public void Ofp_with_other_passenger_or_bag_weight_is_reported()
    {
        var ofp = SimBriefClient.Parse("""
            {
              "fetch": { "status": "Success" }, "params": { "units": "kgs", "time_generated": "1793107876" },
              "origin": { "icao_code": "EGGW" }, "destination": { "icao_code": "EDDB" }, "aircraft": { "icaocode": "C750" },
              "general": { "route": "DCT", "initial_altitude": "45000" },
              "weights": { "pax_count": "9", "bag_count": "9", "pax_weight": "79.379", "bag_weight": "24.948", "cargo": "225", "freight_added": "0" },
              "fuel": { "plan_ramp": "2500", "enroute_burn": "1900" }
            }
            """);
        var problems = OfpValidator.Validate(ofp, new OfpExpectation("EGGW", "EDDB", "C750", 9, 0, PassengerWeightKg: 79.38, BagWeightKg: 22.68));
        Assert.DoesNotContain(problems, p => p.Contains("je Passagier"));
        Assert.Contains(problems, p => p.Contains("55 lbs je Gepäckstück") && p.Contains("50 lbs"));
    }

    [Fact]
    public void Draft_weights_estimate_mzfw_and_mlw_below_mtow()
    {
        Assert.InRange(AircraftProfileEstimator.EstimateMaxZeroFuelKg(10038, 16375, 9), 11000, 11200);   // real 11 068
        Assert.Equal(1111, AircraftProfileEstimator.EstimateMaxZeroFuelKg(767, 1111, 3));                // C172: no MZFW
        Assert.Equal(14738, AircraftProfileEstimator.EstimateMaxLandingKg(16375));
    }
}
