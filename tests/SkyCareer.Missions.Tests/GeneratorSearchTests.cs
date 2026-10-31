using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Loading;
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

    private static List<MissionOffer> Offers(MissionType type, AircraftProfile aircraft, int n, int seed = 5)
    {
        var gen = new StandardMissionGenerator();
        var rng = new Random(seed);
        return Enumerable.Range(0, n)
            .Select(_ => gen.TryGenerate(new GenerationContext(type, aircraft, Origin, Grid(), DateTimeOffset.UnixEpoch, rng)))
            .OfType<MissionOffer>().ToList();
    }

    [Fact]
    public void Scheduled_flights_lean_towards_a_full_cabin_with_some_spread()
    {
        var a320 = Content.Aircraft.Single(a => a.Id == "a20n");
        var offers = Offers(Content.MissionTypes.Single(t => t.Id == "scheduled_pax"), a320, 200);
        var occupancy = offers.Select(o => o.Passengers.Seated / (double)a320.PassengerSeats).ToList();

        Assert.True(offers.Count > 100);
        Assert.InRange(occupancy.Average(), 0.75, 0.92);
        Assert.Contains(occupancy, x => x >= 0.99);
        Assert.Contains(occupancy, x => x < 0.8);
        Assert.All(offers, o => Assert.True(o.PlannedLoad.IsWithinLimits));
    }

    [Fact]
    public void Without_a_load_factor_passengers_stay_evenly_spread()
    {
        var type = WideType with { Passengers = new PaxRule(1, 9) };
        var seated = Offers(type, Jet(), 300).Select(o => o.Passengers.Seated).ToList();
        Assert.InRange(seated.Average(), 4, 6);
        Assert.Contains(1, seated);
    }

    [Fact]
    public void Pay_follows_the_passengers_and_cabin_occupancy()
    {
        var scheduled = Content.MissionTypes.Single(t => t.Id == "scheduled_pax");
        var a320 = Content.Aircraft.Single(a => a.Id == "a20n");
        var half = StandardMissionGenerator.Payout(scheduled, a320, 600, new PassengerCount(87), Mass.Zero);
        var full = StandardMissionGenerator.Payout(scheduled, a320, 600, new PassengerCount(174), Mass.Zero);
        Assert.True(full.Amount > half.Amount * 1.25m, $"full {full.Amount} vs half {half.Amount}");

        // Without passengers in the rule the occupancy share has no effect.
        var noPax = WideType with { Pay = new PayRule { Base = 100, HourlyRateFactor = 1, OccupancyShare = 0.5m } };
        Assert.Equal(StandardMissionGenerator.Payout(noPax with { Pay = noPax.Pay with { OccupancyShare = 0 } }, a320, 600, new PassengerCount(0), Mass.Zero),
            StandardMissionGenerator.Payout(noPax, a320, 600, new PassengerCount(0), Mass.Zero));
    }

    [Fact]
    public void Return_legs_fly_back_after_the_outbound()
    {
        var scheduled = Content.MissionTypes.Single(t => t.Id == "scheduled_pax");
        var a320 = Content.Aircraft.Single(a => a.Id == "a20n");
        var offers = Offers(scheduled, a320, 100);
        var packages = offers.Where(o => o.Return is not null).ToList();

        Assert.InRange(packages.Count / (double)offers.Count, 0.4, 0.8);
        Assert.All(packages, o =>
        {
            var back = o.Return!;
            Assert.Equal(o.Destination.Ident, back.Origin.Ident);
            Assert.Equal(o.Origin.Ident, back.Destination.Ident);
            Assert.True(back.Deadline > o.Deadline);
            Assert.Equal(o.ExpiresAt, back.ExpiresAt);
            Assert.True(back.PlannedLoad.IsWithinLimits);
            Assert.Null(back.Return);
        });
        Assert.DoesNotContain(Offers(Content.MissionTypes.Single(t => t.Id == "cargo_feeder"), Content.Aircraft.Single(a => a.Id == "c208b"), 50),
            o => o.Return is not null);
    }
}
