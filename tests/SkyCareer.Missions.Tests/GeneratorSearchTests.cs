using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;
using SkyCareer.Missions.Generation;

namespace SkyCareer.Missions.Tests;

public class GeneratorSearchTests
{
    private static readonly GameContent Content = ContentLoader.Load(FindContentRoot());
    private static readonly Airport Origin = Field("ORIG", new GeoPosition(50, 8), 0, 0);

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "content");
    }

    /// <summary>Airport at a distance/bearing from (lat, lon); good enough for test geometry.</summary>
    private static Airport Field(string ident, GeoPosition from, double distanceNm, double bearingDeg, double runwayM = 3000,
        bool paved = true, bool lighted = true, bool? ils = null, AirportSize size = AirportSize.Medium)
    {
        double b = bearingDeg * Math.PI / 180;
        double lat = from.Latitude + distanceNm / 60 * Math.Cos(b);
        double lon = from.Longitude + distanceNm / 60 * Math.Sin(b) / Math.Cos(from.Latitude * Math.PI / 180);
        return new Airport(ident, ident, new GeoPosition(lat, lon), size, runwayM,
            paved ? [RunwaySurface.Paved] : [RunwaySurface.Grass, RunwaySurface.Gravel, RunwaySurface.Dirt])
        {
            LongestPavedRunwayM = paved ? runwayM : 0, HasLightedRunway = lighted, HasIls = ils
        };
    }

    /// <summary>Airports every 50 nm from 50 to 3000 nm, on several bearings.</summary>
    private static List<Airport> Grid()
    {
        var list = new List<Airport>();
        for (int d = 50; d <= 3000; d += 50)
            for (int b = 0; b < 360; b += 72)
                list.Add(Field($"A{d}_{b}", Origin.Position, d, b + d % 7));
        return list;
    }

    private static readonly MissionType WideType = new()
    {
        Id = "wide", Name = "Wide", Distance = new DistanceRule(20, 3000), Pay = new PayRule { Base = 100, PerNm = 1 }
    };

    private static AircraftProfile Jet() => new()
    {
        Id = "test_jet", Name = "Test Jet", IcaoType = "C750", SimMatch = ["Test Jet"],
        OperatingEmptyKg = 10038, MaxZeroFuelKg = 12000, MaxTakeoffKg = 16375, MaxLandingKg = 14000, MaxFuelKg = 5865,
        PassengerSeats = 9, MaxCargoKg = 300, CrewCount = 2, CruiseKtas = 500, FuelBurnKgPerHour = 1100, TaxiFuelKg = 70, MinRunwayM = 1600,
        Tags = ["pax", "jet", "multi_engine"]
    };

    private static List<double> Distances(AircraftProfile aircraft, MissionType type, int n, MissionSearch? search = null, List<Airport>? airports = null)
    {
        var gen = new StandardMissionGenerator();
        var rng = new Random(11);
        var result = new List<double>();
        for (int i = 0; i < n; i++)
        {
            var offer = gen.TryGenerate(new GenerationContext(type, aircraft, Origin, airports ?? Grid(), DateTimeOffset.UnixEpoch, rng)
            {
                Search = search ?? MissionSearch.Any
            });
            if (offer is not null) result.Add(offer.DistanceNm);
        }
        return result;
    }

    private static double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);

    [Fact]
    public void Long_range_aircraft_get_longer_flights()
    {
        var a320 = Content.Aircraft.Single(a => a.Id == "a20n");
        var c172 = Content.Aircraft.Single(a => a.Id == "c172");

        var longHaul = Distances(a320, WideType, 300);
        var shortHaul = Distances(c172, WideType, 300);

        Assert.InRange(Median(longHaul), 700, 2000);
        Assert.InRange(Median(shortHaul), 150, 450);
        // Short trips stay possible for the airliner thanks to the minimum weight.
        Assert.Contains(longHaul, d => d < 500);
        Assert.All(shortHaul, d => Assert.True(d <= RequirementEvaluator.EstimatedRangeNm(c172) * 0.9));
    }

    [Fact]
    public void Preferred_distance_is_a_share_of_the_range_inside_the_window()
    {
        Assert.Equal(3000 * StandardMissionGenerator.PreferredRangeShare, StandardMissionGenerator.PreferredDistance(3000, 100, 2500), 3);
        Assert.Equal(800, StandardMissionGenerator.PreferredDistance(3000, 100, 800));
        Assert.Equal(300, StandardMissionGenerator.PreferredDistance(200, 300, 900));
        Assert.True(StandardMissionGenerator.DistanceWeight(1500, 1500) > StandardMissionGenerator.DistanceWeight(300, 1500));
        // Far rings hold more airports; the compensation shrinks their per-airport weight.
        Assert.True(StandardMissionGenerator.DensityCompensation(2000, 1000) < StandardMissionGenerator.DensityCompensation(500, 1000));
    }

    [Fact]
    public void Search_window_and_destination_criteria_are_respected()
    {
        var airports = Grid();
        // Only every other airport has ILS, some have short or unpaved runways.
        for (int i = 0; i < airports.Count; i++)
            airports[i] = airports[i] with { HasIls = i % 2 == 0, LongestRunwayM = i % 3 == 0 ? 1200 : 3000, LongestPavedRunwayM = i % 3 == 0 ? 1200 : 3000 };

        var search = new MissionSearch { DistanceMinNm = 500, DistanceMaxNm = 800, RequireIls = true, MinRunwayM = 2500, RequirePaved = true };
        var gen = new StandardMissionGenerator();
        var rng = new Random(5);
        int offers = 0;
        for (int i = 0; i < 100; i++)
        {
            var o = gen.TryGenerate(new GenerationContext(WideType, Jet(), Origin, airports, DateTimeOffset.UnixEpoch, rng) { Search = search });
            if (o is null) continue;
            offers++;
            Assert.InRange(o.DistanceNm, 500, 800);
            Assert.True(o.Destination.HasIls);
            Assert.True(o.Destination.LongestPavedRunwayM >= 2500);
        }
        Assert.True(offers > 50);
    }

    [Fact]
    public void Passenger_and_payout_criteria_are_respected()
    {
        var charter = Content.MissionTypes.Single(t => t.Id == "charter");
        var search = new MissionSearch { PaxMin = 6, PaxMax = 8, PayoutMin = 5000 };
        var gen = new StandardMissionGenerator();
        var rng = new Random(3);
        int offers = 0;
        for (int i = 0; i < 100; i++)
        {
            var o = gen.TryGenerate(new GenerationContext(charter, Jet(), Origin, Grid(), DateTimeOffset.UnixEpoch, rng) { Search = search });
            if (o is null) continue;
            offers++;
            Assert.InRange(o.Passengers.Seated, 6, 8);
            Assert.True(o.Payout.Amount >= 5000);
        }
        Assert.True(offers > 30);

        // Impossible combination: no offer instead of a wrong one.
        var impossible = new MissionSearch { PaxMin = 20 };
        Assert.Null(gen.TryGenerate(new GenerationContext(charter, Jet(), Origin, Grid(), DateTimeOffset.UnixEpoch, rng) { Search = impossible }));
    }

    [Fact]
    public void Charter_is_available_for_business_jets_and_turboprops_but_not_helicopters()
    {
        var charter = Content.MissionTypes.Single(t => t.Id == "charter");
        Assert.True(RequirementEvaluator.Evaluate(charter, Jet()).Eligible);
        Assert.True(RequirementEvaluator.Evaluate(charter, Content.Aircraft.Single(a => a.Id == "kingair350")).Eligible);
        Assert.False(RequirementEvaluator.Evaluate(charter, Content.Aircraft.Single(a => a.Id == "h145")).Eligible);
        Assert.False(RequirementEvaluator.Evaluate(charter, Content.Aircraft.Single(a => a.Id == "c172")).Eligible);
    }

    [Fact]
    public void List_filter_matches_contracts_against_destination_data()
    {
        var contract = new MissionContract
        {
            MissionTypeId = "charter", Origin = "ORIG", Destination = "DEST", DistanceNm = 600, Adults = 5, Children = 1, Payout = 9000
        };
        var dest = Field("DEST", Origin.Position, 600, 90, ils: null);

        Assert.True(new MissionSearch { MissionTypeIds = new HashSet<string> { "charter" }, DistanceMaxNm = 700, PaxMin = 6 }.Matches(contract, dest));
        Assert.False(new MissionSearch { PaxMin = 7 }.Matches(contract, dest));
        Assert.False(new MissionSearch { RequireIls = true }.Matches(contract, dest)); // unknown ILS doesn't count
        Assert.True(new MissionSearch { RequireIls = true }.Matches(contract, dest with { HasIls = true }));
        Assert.False(new MissionSearch { RequirePaved = true }.Matches(contract, null));
        Assert.False(new MissionSearch { Sizes = new HashSet<AirportSize> { AirportSize.Large } }.Matches(contract, dest));
    }
}
