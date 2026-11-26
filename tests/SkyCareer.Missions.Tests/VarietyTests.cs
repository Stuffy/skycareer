using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Finance;
using SkyCareer.Domain.World;
using SkyCareer.Missions.Generation;
using SkyCareer.Missions.Settlement;

namespace SkyCareer.Missions.Tests;

/// <summary>Passenger comfort, handling and timing rules, landing-condition bonuses and demand events.</summary>
public class VarietyTests
{
    private static readonly GameContent Content = ContentLoader.Load(FindContentRoot());
    private static readonly Airport Origin = new("EDDF", "Frankfurt", new GeoPosition(50.0333, 8.5706), AirportSize.Large, 4000, [RunwaySurface.Paved], "DE");
    private static readonly Airport Destination = new("EDDS", "Stuttgart", new GeoPosition(48.6899, 9.222), AirportSize.Large, 3300, [RunwaySurface.Paved], "DE");
    private static readonly DateTime Departure = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
    private static readonly ComfortStats Gentle = new() { AirborneSeconds = 2400, MaxBankDeg = 20, SustainedBankDeg = 18, MaxSinkFpm = 900, SustainedSinkFpm = 800, SustainedG = 1.1 };

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "content");
    }

    private static MissionType Type(string id) => Content.MissionTypes.Single(t => t.Id == id);
    private static AircraftProfile Caravan => Content.Aircraft.Single(a => a.Id == "c208b");

    private static SettlementResult Settle(string typeId, int passengers, ComfortStats? comfort, double touchdownFpm = -120,
        LandingConditions? conditions = null, double blockHours = 1, double deadlineHours = 3)
    {
        var contract = new MissionContract
        {
            MissionTypeId = typeId, Origin = Origin.Ident, Destination = Destination.Ident, DistanceNm = 85, Adults = passengers,
            Payout = 10_000m, Deadline = Departure.AddHours(deadlineHours)
        };
        var aircraft = new FleetAircraft { ProfileId = Caravan.Id, Registration = "D-TEST", Location = Origin.Ident, Ownership = Ownership.Owned };
        var result = new FlightResult
        {
            BlockTime = TimeSpan.FromHours(blockHours), AirTime = TimeSpan.FromHours(blockHours - 0.2), DistanceFlownNm = 85, FuelUsedKg = 150,
            TouchdownFpm = touchdownFpm, MaxGForce = comfort?.SustainedG ?? 1.1, FinalPosition = Destination.Position, Parked = true,
            Comfort = comfort, LandingConditions = conditions
        };
        return FlightSettlement.Settle(new SettlementInput(Type(typeId), contract, Caravan, aircraft, Origin, Destination, null, result,
            Departure, EconomyTables.For(Difficulty.Normal)));
    }

    [Fact]
    public void Passenger_comfort_replaces_the_smoothness_of_passenger_flights()
    {
        var rough = Gentle with { MaxBankDeg = 50, SustainedBankDeg = 45, SteepBankSeconds = 40 };

        var gentle = Settle("charter", 4, Gentle);
        var steep = Settle("charter", 4, rough);
        Assert.Equal(100, gentle.Comfort!.Bank);
        Assert.Equal(100, gentle.Score.Smoothness);
        Assert.True(steep.Comfort!.Bank < 30);
        Assert.Equal(steep.Comfort.InFlight, steep.Score.Smoothness);
        Assert.True(steep.Score.Total < gentle.Score.Total);
        Assert.NotEmpty(gentle.PassengerComments);
        Assert.Contains(steep.PassengerComments, c => c.Contains("turns") || c.Contains("window"));

        // Without flight data (old recordings) and without passengers the peak G alone counts, as before.
        Assert.Null(Settle("charter", 4, null).Comfort);
        var cargo = Settle("cargo_feeder", 0, rough);
        Assert.Null(cargo.Comfort);
        Assert.Empty(cargo.PassengerComments);
        Assert.Equal(100, cargo.Score.Smoothness);
    }

    [Fact]
    public void Very_comfortable_flights_earn_a_tip()
    {
        Assert.Equal(0m, FlightSettlement.TipShare(89));
        Assert.Equal(0.02m, FlightSettlement.TipShare(90));
        Assert.Equal(0.035m, FlightSettlement.TipShare(95));
        Assert.Equal(0.05m, FlightSettlement.TipShare(100));

        var s = Settle("charter", 4, Gentle);
        Assert.Equal(100, s.Comfort!.Score);
        Assert.Equal(500m, Assert.Single(s.Lines, l => l.Description.StartsWith("Passenger tip")).Amount);
        Assert.DoesNotContain(Settle("charter", 4, Gentle with { SustainedBankDeg = 45 }).Lines, l => l.Description.StartsWith("Passenger tip"));
    }

    [Fact]
    public void Comments_are_the_same_for_the_same_flight_and_start_with_the_complaints()
    {
        var rating = new ComfortRating(55, 95, 95, 95, 20);
        var id = Guid.NewGuid();
        var first = PassengerComfort.Comments(rating, TimeSpan.FromMinutes(30), [GroundService.Catering], id);
        Assert.Equal(first, PassengerComfort.Comments(rating, TimeSpan.FromMinutes(30), [GroundService.Catering], id));
        Assert.Equal(3, first.Count);
        Assert.True(first[0].Contains("landing") || first[0].Contains("land"));   // the hard landing first
        Assert.Contains("ate", first[1]);                                          // then being late
        Assert.Single(PassengerComfort.Comments(new ComfortRating(75, 75, 75, 75, 75), TimeSpan.Zero, [], id));
    }

    [Fact]
    public void Exceeded_handling_limits_cost_part_of_the_pay()
    {
        Assert.NotNull(Type("air_ambulance").Handling);
        var within = Settle("air_ambulance", 2, Gentle);
        Assert.DoesNotContain(within.Lines, l => l.Description.StartsWith("Patient on board"));

        // Bank 35° (max 25°) and sink 2,000 fpm (max 1,500): two limits, 10 % each.
        var s = Settle("air_ambulance", 2, Gentle with { SustainedBankDeg = 35, SustainedSinkFpm = 2000 });
        var penalties = s.Lines.Where(l => l.Description.StartsWith("Patient on board")).ToList();
        Assert.Equal(2, penalties.Count);
        Assert.All(penalties, l => Assert.Equal(-1000m, l.Amount));
        Assert.Contains(s.Notes, n => n.Contains("bank 35°"));

        // Three limits exceeded still cost only two penalties; no flight data, no penalty.
        var all = Settle("air_ambulance", 2, Gentle with { SustainedBankDeg = 35, SustainedSinkFpm = 2000, SustainedG = 1.6 });
        Assert.Equal(HandlingRule.MaxBreaches, all.Lines.Count(l => l.Description.StartsWith("Patient on board")));
        Assert.DoesNotContain(Settle("air_ambulance", 2, null).Lines, l => l.Description.StartsWith("Patient on board"));
        Assert.Equal("livestock", Type("livestock").Handling!.Reason);
        Assert.Equal(0.15m, Type("dangerous_goods").Handling!.Penalty);
    }

    [Fact]
    public void A_tight_deadline_pays_for_every_minute_early()
    {
        var timing = Type("organ_transport").Timing;
        Assert.Equal(0.12m, timing.EarlyBonus(TimeSpan.FromMinutes(30)));
        Assert.Equal(0.2m, timing.EarlyBonus(TimeSpan.FromMinutes(100)));
        Assert.Equal(0m, timing.EarlyBonus(TimeSpan.FromMinutes(-5)));
        Assert.Equal(0m, Type("charter").Timing.EarlyBonus(TimeSpan.FromHours(2)));

        // One block hour against a deadline 1.5 h after departure: 30 min early.
        var early = Settle("organ_transport", 1, Gentle, deadlineHours: 1.5);
        Assert.Equal(1200m, Assert.Single(early.Lines, l => l.Description.StartsWith("Early arrival bonus")).Amount);
        var late = Settle("organ_transport", 1, Gentle, deadlineHours: 0.5);
        Assert.Equal(FlightOutcome.Late, late.Outcome);
        Assert.DoesNotContain(late.Lines, l => l.Description.StartsWith("Early arrival bonus"));

        // The generator sets the deadline from the type's timing rule.
        var type = Type("organ_transport");
        var aircraft = Content.Aircraft.First(a => RequirementEvaluator.AvailableFor([type], a).Any());
        var offer = Enumerable.Range(0, 50)
            .Select(i => new StandardMissionGenerator().TryGenerate(new GenerationContext(type, aircraft, Origin, [Far()], DateTimeOffset.UnixEpoch, new Random(i))))
            .First(o => o is not null)!;
        double hours = StandardMissionGenerator.BlockHours(aircraft, offer.DistanceNm) * timing.DeadlineFactor + timing.DeadlineSlackHours;
        Assert.Equal(DateTimeOffset.UnixEpoch.AddHours(hours), offer.Deadline);
    }

    private static Airport Far() => new("EDDM", "Munich", new GeoPosition(48.3538, 11.7861), AirportSize.Large, 4000, [RunwaySurface.Paved], "DE")
    {
        LongestPavedRunwayM = 4000, HasLightedRunway = true, City = "Munich", CityPopulation = 1_200_000
    };

    [Fact]
    public void Demanding_landing_conditions_pay_a_bonus_each()
    {
        var hard = new LandingConditions(Night: true, VisibilityM: 3000, CrosswindKt: 14);
        var s = Settle("charter", 4, Gentle, conditions: hard);
        var bonuses = s.Lines.Where(l => l.Description.StartsWith("Night landing") || l.Description.StartsWith("Low visibility")
                                         || l.Description.StartsWith("Crosswind landing")).ToList();
        Assert.Equal(3, bonuses.Count);
        Assert.All(bonuses, l => Assert.Equal(500m, l.Amount));

        Assert.Single(FlightSettlement.ConditionBonuses(new LandingConditions(true, 9000, 4)));
        Assert.Empty(FlightSettlement.ConditionBonuses(new LandingConditions(false, null, null)));
        Assert.Empty(FlightSettlement.ConditionBonuses(null));
        // A landing beyond the type's touchdown limit earns nothing for the conditions.
        var slammed = Settle("charter", 4, Gentle, touchdownFpm: -900, conditions: hard);
        Assert.DoesNotContain(slammed.Lines, l => l.Description.StartsWith("Night landing"));
    }

    [Fact]
    public void Demand_events_are_reproducible_and_rare()
    {
        var week = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        // One airport per region: a grid of 50 × 40 cells around the equator.
        var airports = Enumerable.Range(0, 2000)
            .Select(i => new Airport($"L{i:0000}", "Large", new GeoPosition(-29 + i / 50 * 1.5, -40 + i % 50 * 1.6), AirportSize.Large, 3000, [RunwaySurface.Paved])).ToList();

        var events = airports.Select(a => DemandEvents.At(42, week, a)).ToList();
        int count = events.Count(e => e is not null);
        Assert.InRange(count, 60, 150);   // 5 % of 2000 regions
        Assert.Equal(events, airports.Select(a => DemandEvents.At(42, week.AddDays(2), a)).ToList());       // same ISO week
        Assert.NotEqual(events, airports.Select(a => DemandEvents.At(42, week.AddDays(7), a)).ToList());    // next week
        Assert.NotEqual(events, airports.Select(a => DemandEvents.At(43, week, a)).ToList());               // another career
        Assert.All(events.OfType<DemandEvent>(), e => Assert.Equal(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc), e.Until));

        // The event belongs to the region: every airfield in it has it, a military field never.
        var hub = airports[events.FindIndex(e => e is not null)];
        var region = DemandEvents.At(42, week, hub)!.Region!;
        Assert.True(region.Contains(hub.Position));
        Assert.InRange(region.North - region.South, 1.49, 1.51);
        var strip = new Airport("XX-0001", "Strip", new GeoPosition((region.South + region.North) / 2, (region.West + region.East) / 2),
            AirportSize.Small, 600, [RunwaySurface.Grass]);
        Assert.Equal(DemandEvents.At(42, week, hub), DemandEvents.At(42, week, strip));
        Assert.Null(DemandEvents.At(42, week, hub with { IsMilitary = true }));
        // Regions stay about 90 nm wide: at 60° N a column spans twice as many degrees of longitude as at the equator.
        var north = DemandEvents.RegionOf(new GeoPosition(60.5, 10));
        Assert.InRange(north.East - north.West, 2.9, 3.2);
        Assert.True(north.Contains(new GeoPosition(60.5, 10)));

        var fair = new DemandEvent(DemandKind.TradeFair, DemandEvents.PassengerPayFactor, week);
        Assert.True(fair.AppliesTo(Type("charter")));
        Assert.False(fair.AppliesTo(Type("cargo_feeder")));
        Assert.True(new DemandEvent(DemandKind.Harvest, DemandEvents.CargoPayFactor, week).AppliesTo(Type("bush_supply")));
        Assert.StartsWith("Trade fair +25", DemandEvents.Label("TradeFair"));
        Assert.Null(DemandEvents.Label(null));
    }

    [Fact]
    public void A_demand_event_at_the_destination_raises_the_pay()
    {
        var type = Type("charter") with { ReturnFlight = null };
        var aircraft = Content.Aircraft.First(a => RequirementEvaluator.AvailableFor([type], a).Any());
        var fair = new DemandEvent(DemandKind.TradeFair, DemandEvents.PassengerPayFactor, Departure);
        MissionOffer Offer(Func<Airport, DemandEvent?>? demand) => Enumerable.Range(0, 50)
            .Select(i => new StandardMissionGenerator().TryGenerate(
                new GenerationContext(type, aircraft, Origin, [Far()], DateTimeOffset.UnixEpoch, new Random(i)) { Demand = demand }))
            .First(o => o is not null)!;

        var plain = Offer(null);
        var boosted = Offer(_ => fair);
        Assert.Null(plain.Demand);
        Assert.Equal(DemandKind.TradeFair, boosted.Demand!.Kind);
        Assert.InRange(boosted.Payout.Amount, plain.Payout.Amount * 1.25m - 1, plain.Payout.Amount * 1.25m + 1);
        // A cargo event doesn't concern a charter flight.
        Assert.Null(Offer(_ => new DemandEvent(DemandKind.Harvest, DemandEvents.CargoPayFactor, Departure)).Demand);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(20, 0, 15)]
    [InlineData(10, 10, 15)]
    [InlineData(120, 0, FlightScorer.MaxSafetyPenalty)]
    public void Safety_deduction_counts_sustained_warnings_after_a_grace_time(int overspeed, int stall, int expected)
    {
        var r = new FlightResult { OverspeedSeconds = overspeed, StallWarningSeconds = stall, TouchdownFpm = -120 };
        Assert.Equal(expected, FlightScorer.Score(new ScoringRule(), r, TimeSpan.Zero).SafetyPenalty);
    }

    [Fact]
    public void Rule_violations_do_not_lower_the_score()
    {
        var clean = new FlightResult { TouchdownFpm = -120 };
        var violated = clean with { Violations = [new RuleViolation(ViolationType.PayloadChanged, "test")] };
        var a = FlightScorer.Score(new ScoringRule(), clean, TimeSpan.Zero);
        var b = FlightScorer.Score(new ScoringRule(), violated, TimeSpan.Zero);
        Assert.Equal(a.Total, b.Total);
        Assert.Equal(0, b.SafetyPenalty);
    }
}
