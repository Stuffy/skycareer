using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Finance;
using SkyCareer.Domain.World;
using SkyCareer.Missions.Generation;
using SkyCareer.Missions.Settlement;

namespace SkyCareer.Missions.Tests;

public class TourTests
{
    private static readonly GameContent Content = ContentLoader.Load(FindContentRoot());
    private static readonly Airport Home = Field("HOME", 0, 0, "Hometown", AirportSize.Medium);

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "content");
    }

    private static Airport Field(string ident, double distanceNm, double bearingDeg, string city, AirportSize size = AirportSize.Small)
    {
        double b = bearingDeg * Math.PI / 180;
        return new Airport(ident, ident + " Airfield", new GeoPosition(50 + distanceNm / 60 * Math.Cos(b), 8 + distanceNm / 60 * Math.Sin(b) / Math.Cos(50 * Math.PI / 180)),
            size, 1200, [RunwaySurface.Paved, RunwaySurface.Grass]) { City = city };
    }

    /// <summary>Towns 10–30 nm around, two airfields serving the same town.</summary>
    private static List<Airport> Towns() =>
        [Home, .. Enumerable.Range(0, 12).Select(i => Field($"T{i:00}", 10 + i % 3 * 10, i * 30, $"Town {i}")), Field("T00B", 11, 2, "Town 0")];

    private static MissionOffer? Tour(string typeId, int seed) => new SightseeingMissionGenerator().TryGenerate(
        new GenerationContext(Content.MissionTypes.Single(t => t.Id == typeId), Content.Aircraft.Single(a => a.Id == "c208b"), Home, Towns(),
            DateTimeOffset.UnixEpoch, new Random(seed)));

    [Fact]
    public void A_sightseeing_flight_is_a_loop_over_distinct_towns()
    {
        var offers = Enumerable.Range(0, 30).Select(i => Tour("sightseeing", i)).OfType<MissionOffer>().ToList();
        Assert.NotEmpty(offers);
        Assert.All(offers, o =>
        {
            Assert.Equal(Home.Ident, o.Origin.Ident);
            Assert.Equal(Home.Ident, o.Destination.Ident);
            Assert.InRange(o.Waypoints.Count, 2, 4);
            Assert.Equal(o.Waypoints.Count, o.Waypoints.Select(w => w.Name).Distinct().Count());
            Assert.All(o.Waypoints, w => Assert.StartsWith("Town ", w.Name));
            Assert.All(o.Waypoints, w => Assert.Equal(3000, w.MaxAglFt));
            Assert.Equal(TourWaypoint.TourLengthNm(Home.Position, o.Waypoints), o.DistanceNm, 6);
            // Flown in order of bearing, so the tour is a loop.
            var courses = o.Waypoints.Select(w => Home.Position.InitialCourseTo(w.Position)).ToList();
            Assert.Equal(courses.Order(), courses);
        });
        var survey = Enumerable.Range(0, 30).Select(i => Tour("aerial_survey", i)).OfType<MissionOffer>().First();
        Assert.All(survey.Waypoints, w => Assert.Equal((1200.0, 1800.0), (w.MinAglFt!.Value, w.MaxAglFt!.Value)));
    }

    [Fact]
    public void Waypoints_survive_the_contract_text()
    {
        var waypoints = new[] { new TourWaypoint("Bad Dürkheim", new GeoPosition(49.4618, 8.1679), 1200, 1800), new TourWaypoint("Speyer", new GeoPosition(49.3173, 8.4412), null, 3000) };
        Assert.Equal(waypoints, TourWaypoint.Parse(TourWaypoint.Serialize(waypoints)));
        Assert.Empty(TourWaypoint.Parse(null));
        Assert.Equal("BADDRK", waypoints[0].Ident);
        Assert.True(waypoints[1].PassedBy(new GeoPosition(49.33, 8.45), 2500));
        Assert.False(waypoints[1].PassedBy(new GeoPosition(49.33, 8.45), 3500));      // too high
        Assert.False(waypoints[0].PassedBy(new GeoPosition(49.4618, 8.1679), 900));   // below the survey band
        Assert.False(waypoints[1].PassedBy(new GeoPosition(49.40, 8.45), 2000));      // 5 nm away
    }

    [Fact]
    public void Missed_waypoints_cost_their_share_of_the_pay()
    {
        var waypoints = new[] { new TourWaypoint("A", Home.Position), new TourWaypoint("B", Home.Position), new TourWaypoint("C", Home.Position), new TourWaypoint("D", Home.Position) };
        var contract = new MissionContract
        {
            MissionTypeId = "sightseeing", Origin = Home.Ident, Destination = Home.Ident, DistanceNm = 40, Adults = 2, Payout = 1000m,
            Deadline = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc), Waypoints = TourWaypoint.Serialize(waypoints)
        };
        var caravan = Content.Aircraft.Single(a => a.Id == "c208b");
        SettlementResult Settle(IReadOnlyList<string>? missed) => FlightSettlement.Settle(new SettlementInput(
            Content.MissionTypes.Single(t => t.Id == "sightseeing"), contract, caravan,
            new FleetAircraft { ProfileId = caravan.Id, Registration = "D-TEST", Location = Home.Ident, Ownership = Ownership.Owned },
            Home, Home, null,
            new FlightResult { BlockTime = TimeSpan.FromMinutes(40), AirTime = TimeSpan.FromMinutes(30), TouchdownFpm = -120, FinalPosition = Home.Position, Parked = true, MissedWaypoints = missed },
            new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc), EconomyTables.For(Difficulty.Normal)));

        var some = Settle(["B"]);
        Assert.Equal(FlightOutcome.Completed, some.Outcome);
        Assert.Equal(-250m, Assert.Single(some.Lines, l => l.Description.StartsWith("Waypoints missed")).Amount);
        Assert.Contains(some.Notes, n => n.Contains("B"));
        Assert.DoesNotContain(Settle([]).Lines, l => l.Description.StartsWith("Waypoints missed"));
        Assert.Equal(-1000m, Assert.Single(Settle(["A", "B", "C", "D"]).Lines, l => l.Description.StartsWith("Waypoints missed")).Amount);
    }
}
