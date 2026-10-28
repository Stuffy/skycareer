using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.World;
using SkyCareer.Missions.Generation;

namespace SkyCareer.Missions.Tests;

public class DestinationRuleTests
{
    private static readonly GameContent Content = ContentLoader.Load(FindContentRoot());
    private static readonly GeoPosition Center = new(50, 8);
    private static readonly Airport Origin = new("ORIG", "Origin", Center, AirportSize.Large, 3000, [RunwaySurface.Paved]);

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "content");
    }

    /// <summary>Like real data: 2 % large, 8 % medium, 90 % small airports, spread from 100 to 1000 nm.</summary>
    private static List<Airport> RealisticMix()
    {
        var list = new List<Airport>();
        for (int i = 0; i < 500; i++)
        {
            double d = 100 + i * 1.8, b = i * 37 % 360 * Math.PI / 180;
            var size = i % 50 == 0 ? AirportSize.Large : i % 10 == 0 ? AirportSize.Medium : AirportSize.Small;
            var pos = new GeoPosition(Center.Latitude + d / 60 * Math.Cos(b), Center.Longitude + d / 60 * Math.Sin(b) / Math.Cos(Center.Latitude * Math.PI / 180));
            list.Add(new Airport($"X{i:000}", $"Field {i}", pos, size, 3000, [RunwaySurface.Paved, RunwaySurface.Grass])
            {
                LongestPavedRunwayM = 3000
            });
        }
        return list;
    }

    private static AircraftProfile Jet() => new()
    {
        Id = "jet", Name = "Jet", IcaoType = "C750", OperatingEmptyKg = 10000, MaxZeroFuelKg = 12000, MaxTakeoffKg = 16000,
        MaxLandingKg = 14000, MaxFuelKg = 6000, PassengerSeats = 9, MaxCargoKg = 300, CrewCount = 2, CruiseKtas = 480, FuelBurnKgPerHour = 1000,
        MinRunwayM = 1500, Surfaces = [RunwaySurface.Paved], Tags = ["pax", "jet"]
    };

    private static List<MissionOffer> Generate(MissionType type, AircraftProfile aircraft, int n, Func<int, GenerationContext, GenerationContext>? tweak = null)
    {
        var gen = new StandardMissionGenerator();
        var rng = new Random(17);
        var airports = RealisticMix();
        var offers = new List<MissionOffer>();
        for (int i = 0; i < n; i++)
        {
            var ctx = new GenerationContext(type, aircraft, Origin, airports, DateTimeOffset.UnixEpoch, rng);
            if (tweak is not null) ctx = tweak(i, ctx);
            if (gen.TryGenerate(ctx) is { } o) offers.Add(o);
        }
        return offers;
    }

    private static double LargeShare(List<MissionOffer> offers) => offers.Count(o => o.Destination.Size == AirportSize.Large) / (double)offers.Count;

    [Fact]
    public void Size_weights_raise_the_share_of_big_airports()
    {
        var charter = Content.MissionTypes.Single(t => t.Id == "charter");
        var neutral = charter with { Destinations = new DestinationRule() };

        double weighted = LargeShare(Generate(charter, Jet(), 400));
        double plain = LargeShare(Generate(neutral, Jet(), 400));

        Assert.InRange(plain, 0, 0.06);       // roughly the 2 % in the data
        Assert.True(weighted > plain * 2, $"weighted {weighted:P0} vs plain {plain:P0}");
    }

    [Fact]
    public void Allowed_sizes_are_enforced()
    {
        var scheduled = Content.MissionTypes.Single(t => t.Id == "scheduled_pax");
        var airliner = Content.Aircraft.Single(a => a.Id == "a20n");
        Assert.All(Generate(scheduled, airliner, 100), o => Assert.Contains(o.Destination.Size, new[] { AirportSize.Large, AirportSize.Medium }));

        var bush = Content.MissionTypes.Single(t => t.Id == "bush_supply");
        var caravan = Content.Aircraft.Single(a => a.Id == "c208b");
        var bushOffers = Generate(bush, caravan, 100);
        Assert.NotEmpty(bushOffers);
        Assert.All(bushOffers, o => Assert.Equal(AirportSize.Small, o.Destination.Size));
    }

    [Fact]
    public void Require_large_only_picks_large_airports()
    {
        var charter = Content.MissionTypes.Single(t => t.Id == "charter");
        var offers = Generate(charter, Jet(), 50, (_, ctx) => ctx with { RequireLarge = true });
        Assert.NotEmpty(offers);
        Assert.All(offers, o => Assert.Equal(AirportSize.Large, o.Destination.Size));
    }

    [Fact]
    public void Recent_destinations_get_reused()
    {
        var charter = Content.MissionTypes.Single(t => t.Id == "charter");
        // The generator doesn't add to the set itself; the search loop does, simulated here.
        var recent = new HashSet<string>();
        var gen = new StandardMissionGenerator();
        var rng = new Random(3);
        var airports = RealisticMix();
        var picked = new List<string>();
        for (int i = 0; i < 60; i++)
        {
            var o = gen.TryGenerate(new GenerationContext(charter, Jet(), Origin, airports, DateTimeOffset.UnixEpoch, rng) { RecentDestinations = recent });
            if (o is null) continue;
            picked.Add(o.Destination.Ident);
            recent.Add(o.Destination.Ident);
        }

        int repeats = picked.Count - picked.Distinct().Count();
        Assert.True(picked.Count > 40);
        Assert.True(repeats >= picked.Count * 0.15, $"{repeats} repeats in {picked.Count} offers");
    }

    [Fact]
    public void Shipped_types_have_sensible_destination_rules()
    {
        var byId = Content.MissionTypes.ToDictionary(t => t.Id);
        Assert.Equal(0.5, byId["scheduled_pax"].Destinations.MinLargeShare);
        Assert.False(byId["scheduled_pax"].Destinations.Allows(AirportSize.Small));
        Assert.True(byId["charter"].Destinations.SizeWeights.For(AirportSize.Large) > 1);
        Assert.Equal(1, byId["air_ambulance"].Destinations.SizeWeights.For(AirportSize.Large));
    }

    [Fact]
    public void Big_cities_and_capitals_attract_more_jobs()
    {
        // Same airports, but a few of the medium/large ones serve a capital of millions.
        var airports = RealisticMix()
            .Select((a, i) => a.Size != AirportSize.Small && i % 20 == 0
                ? a with { City = "Metropolis", CityPopulation = 5_000_000, IsCapital = true }
                : a)
            .ToList();
        var charter = Content.MissionTypes.Single(t => t.Id == "charter");
        var neutral = charter with { Destinations = charter.Destinations with { CityWeight = 0 } };

        double Share(MissionType type)
        {
            var gen = new StandardMissionGenerator();
            var rng = new Random(5);
            int city = 0, n = 0;
            for (int i = 0; i < 400; i++)
                if (gen.TryGenerate(new GenerationContext(type, Jet(), Origin, airports, DateTimeOffset.UnixEpoch, rng)) is { } o)
                {
                    n++;
                    if (o.Destination.IsCapital) city++;
                }
            return city / (double)n;
        }

        double weighted = Share(charter), plain = Share(neutral);
        Assert.True(weighted > plain * 2, $"weighted {weighted:P0} vs plain {plain:P0}");
    }

    [Fact]
    public void City_importance_scales_with_population_and_ignores_plain_airfields()
    {
        var big = new Airport("BIG", "Big", Center, AirportSize.Large, 3000, [RunwaySurface.Paved]);
        Assert.Equal(0, DestinationRule.CityImportance(big with { CityPopulation = 50_000 }));
        Assert.Equal(1, DestinationRule.CityImportance(big with { CityPopulation = 1_000_000 }), 3);
        Assert.Equal(3, DestinationRule.CityImportance(big with { CityPopulation = 30_000_000, IsCapital = true }), 3);
        var field = big with { Size = AirportSize.Small, CityPopulation = 2_000_000, IsCapital = true };
        Assert.Equal(0, DestinationRule.CityImportance(field));
        Assert.True(DestinationRule.CityImportance(field with { ScheduledService = true }) > 1);
    }

    [Fact]
    public void Capitals_beat_unimportant_airports_at_the_ideal_distance()
    {
        // Jet range ~2500 nm → ideal trip ~1250 nm. Ten plain airports sit right there, one capital at 400 nm.
        static Airport At(string id, double nm, double bearingDeg) => new(id, id,
            new GeoPosition(Center.Latitude + nm / 60 * Math.Cos(bearingDeg * Math.PI / 180),
                Center.Longitude + nm / 60 * Math.Sin(bearingDeg * Math.PI / 180) / Math.Cos(Center.Latitude * Math.PI / 180)),
            AirportSize.Large, 3000, [RunwaySurface.Paved]) { LongestPavedRunwayM = 3000 };
        var airports = Enumerable.Range(0, 10).Select(i => At($"P{i}", 1250, i * 36)).ToList();
        airports.Add(At("CAP", 400, 10) with { City = "Capital", CityPopulation = 3_000_000, IsCapital = true });

        var charter = Content.MissionTypes.Single(t => t.Id == "charter");
        var gen = new StandardMissionGenerator();
        var rng = new Random(9);
        int capital = 0, n = 0;
        for (int i = 0; i < 300; i++)
            if (gen.TryGenerate(new GenerationContext(charter, Jet(), Origin, airports, DateTimeOffset.UnixEpoch, rng)) is { } o)
            {
                n++;
                if (o.Destination.Ident == "CAP") capital++;
            }
        // Expected ~28 %; without the damped distance influence it would be ~15 %.
        Assert.True(capital > n / 5, $"capital {capital} of {n}");
    }

    [Fact]
    public void Military_fields_are_only_offered_to_military_aircraft()
    {
        var airports = RealisticMix().Select((a, i) => i % 3 == 0 ? a with { IsMilitary = true } : a).ToList();
        var charter = Content.MissionTypes.Single(t => t.Id == "charter");

        List<MissionOffer> Run(AircraftProfile aircraft)
        {
            var gen = new StandardMissionGenerator();
            var rng = new Random(4);
            return Enumerable.Range(0, 200)
                .Select(_ => gen.TryGenerate(new GenerationContext(charter, aircraft, Origin, airports, DateTimeOffset.UnixEpoch, rng)))
                .OfType<MissionOffer>().ToList();
        }

        var civil = Run(Jet());
        Assert.NotEmpty(civil);
        Assert.DoesNotContain(civil, o => o.Destination.IsMilitary);

        var military = Run(Jet() with { Tags = ["pax", "jet", AircraftProfile.MilitaryTag] });
        Assert.Contains(military, o => o.Destination.IsMilitary);
    }

    [Fact]
    public void Require_capital_only_picks_airports_serving_a_capital()
    {
        var airports = RealisticMix()
            .Select((a, i) => i % 7 == 0 ? a with { City = "Cap", CityPopulation = 1_000_000, IsCapital = true } : a)
            .ToList();
        var charter = Content.MissionTypes.Single(t => t.Id == "charter");
        var gen = new StandardMissionGenerator();
        var rng = new Random(12);
        var offers = Enumerable.Range(0, 50)
            .Select(_ => gen.TryGenerate(new GenerationContext(charter, Jet(), Origin, airports, DateTimeOffset.UnixEpoch, rng) { RequireCapital = true }))
            .OfType<MissionOffer>().ToList();
        Assert.NotEmpty(offers);
        // Small fields near a capital don't count: they don't serve it.
        Assert.All(offers, o => Assert.True(DestinationRule.ServesCapital(o.Destination)));
        Assert.All(offers, o => Assert.NotEqual(AirportSize.Small, o.Destination.Size));
    }

    [Fact]
    public void Payout_follows_the_aircraft_class_and_covers_rented_costs()
    {
        var charter = Content.MissionTypes.Single(t => t.Id == "charter");
        var caravan = Content.Aircraft.Single(a => a.Id == "c208b");
        var kingAir = Content.Aircraft.Single(a => a.Id == "kingair350");
        var dest = new Airport("DEST", "Dest", new GeoPosition(Center.Latitude + 300 / 60.0, Center.Longitude), AirportSize.Large, 3000,
            [RunwaySurface.Paved]) { LongestPavedRunwayM = 3000 };

        decimal AveragePay(AircraftProfile a)
        {
            var rng = new Random(3);
            var offers = Enumerable.Range(0, 30)
                .Select(_ => new StandardMissionGenerator().TryGenerate(new GenerationContext(charter, a, Origin, [dest], DateTimeOffset.UnixEpoch, rng)))
                .OfType<MissionOffer>().ToList();
            Assert.NotEmpty(offers);
            return offers.Average(o => o.Payout.Amount);
        }

        decimal caravanPay = AveragePay(caravan), kingAirPay = AveragePay(kingAir);
        Assert.True(kingAirPay > caravanPay * 1.3m, $"King Air {kingAirPay:N0} vs Caravan {caravanPay:N0}");

        // A rented King Air on 300 nm (~1.5 block hours) must earn clearly more than its rent and fuel.
        double blockHours = 300 / kingAir.CruiseKtas + 0.5;
        decimal rentAndFuel = Domain.Finance.FleetEconomics.HourlyRate(kingAir) * (decimal)blockHours;
        Assert.True(kingAirPay > rentAndFuel * 1.5m, $"pay {kingAirPay:N0} vs rent+fuel {rentAndFuel:N0}");
    }
}
