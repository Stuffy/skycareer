using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;
using SkyCareer.Missions.Generation;

namespace SkyCareer.Missions.Tests;

public class MultiStopTests
{
    private static readonly GameContent Content = ContentLoader.Load(FindContentRoot());
    private static readonly Airport Origin = Strip("ORIG", 0, 0);

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "content");
    }

    /// <summary>A grass strip at a distance and bearing from 50° N / 8° E.</summary>
    private static Airport Strip(string ident, double distanceNm, double bearingDeg)
    {
        double b = bearingDeg * Math.PI / 180;
        return new Airport(ident, ident, new GeoPosition(50 + distanceNm / 60 * Math.Cos(b), 8 + distanceNm / 60 * Math.Sin(b) / Math.Cos(50 * Math.PI / 180)),
            AirportSize.Small, 900, [RunwaySurface.Grass, RunwaySurface.Gravel, RunwaySurface.Dirt]);
    }

    /// <summary>Strips in rings of 20–100 nm around the origin.</summary>
    private static List<Airport> Ring() =>
        [.. Enumerable.Range(0, 40).Select(i => Strip($"S{i:00}", 20 + i % 5 * 20, i * 37 % 360)), Origin];

    private static List<MissionOffer> Offers(MissionType type, int count) => Enumerable.Range(0, count)
        .Select(i => new StandardMissionGenerator().TryGenerate(
            new GenerationContext(type, Content.Aircraft.Single(a => a.Id == "c208b"), Origin, Ring(), DateTimeOffset.UnixEpoch, new Random(i))))
        .OfType<MissionOffer>().ToList();

    [Fact]
    public void A_mail_run_visits_new_stops_and_returns_home()
    {
        var runs = Offers(Content.MissionTypes.Single(t => t.Id == "milk_run"), 40);
        Assert.NotEmpty(runs);
        Assert.All(runs, run =>
        {
            Assert.Equal(PackageKind.MultiStop, run.PackageKind);
            Assert.InRange(run.FollowUps.Count + 1, 3, 4);
            Assert.Equal(0.25m, run.PackageBonus);
            Assert.Null(run.Return);
            var legs = run.Legs.ToList();
            // Each leg starts where the one before ended, the run ends at home and visits no stop twice.
            for (int i = 1; i < legs.Count; i++)
            {
                Assert.Equal(legs[i - 1].Destination.Ident, legs[i].Origin.Ident);
                Assert.True(legs[i].Deadline > legs[i - 1].Deadline);
                Assert.Equal(run.ExpiresAt, legs[i].ExpiresAt);
            }
            Assert.Equal(Origin.Ident, legs[^1].Destination.Ident);
            Assert.Equal(legs.Count - 1, legs.SkipLast(1).Select(l => l.Destination.Ident).Distinct().Count());
            Assert.DoesNotContain(legs.SkipLast(1), l => l.Destination.Ident == Origin.Ident);
            Assert.All(legs, l => Assert.InRange(l.DistanceNm, 15, 135));
            Assert.All(legs, l => Assert.True(l.PlannedLoad.IsWithinLimits));
        });
    }

    /// <summary>Large and medium airports 150–600 nm around the origin, as a VIP roadshow needs.</summary>
    private static List<Airport> Cities()
    {
        var hub = new Airport("HUB", "Hub", new GeoPosition(50, 8), AirportSize.Large, 3500, [RunwaySurface.Paved]) { City = "Hub", CityPopulation = 1_000_000 };
        return [hub, .. Enumerable.Range(0, 30).Select(i =>
        {
            double nm = 150 + i % 5 * 100, b = i * 47 % 360 * Math.PI / 180;
            return new Airport($"C{i:00}", $"City {i}", new GeoPosition(50 + nm / 60 * Math.Cos(b), 8 + nm / 60 * Math.Sin(b) / Math.Cos(50 * Math.PI / 180)),
                i % 2 == 0 ? AirportSize.Large : AirportSize.Medium, 3000, [RunwaySurface.Paved]) { City = $"City {i}", CityPopulation = 500_000, LongestPavedRunwayM = 3000 };
        })];
    }

    [Fact]
    public void A_vip_roadshow_takes_the_same_group_to_every_stop_and_back()
    {
        var vip = Content.MissionTypes.Single(t => t.Id == "vip_charter");
        Assert.True(vip.MultiStop!.SameLoad);
        var jet = Content.Aircraft.First(a => RequirementEvaluator.Evaluate(vip, a).Eligible);
        var airports = Cities();
        var roadshows = Enumerable.Range(0, 300)
            .Select(i => new StandardMissionGenerator().TryGenerate(new GenerationContext(vip, jet, airports[0], airports, DateTimeOffset.UnixEpoch, new Random(i))))
            .OfType<MissionOffer>().Where(o => o.PackageKind == PackageKind.MultiStop && o.FollowUps.Count > 0).ToList();

        Assert.NotEmpty(roadshows);
        Assert.All(roadshows, run =>
        {
            var legs = run.Legs.ToList();
            Assert.InRange(legs.Count, 3, 4);
            Assert.Equal(airports[0].Ident, legs[^1].Destination.Ident);
            Assert.All(legs, l =>
            {
                Assert.Equal(run.Passengers, l.Passengers);
                Assert.Equal(run.Cargo, l.Cargo);
                Assert.Equal(run.PlannedLoad.PassengerMass, l.PlannedLoad.PassengerMass);
                Assert.Equal(run.PlannedLoad.BaggageMass, l.PlannedLoad.BaggageMass);
                Assert.True(l.PlannedLoad.IsWithinLimits);
            });
            // Pay follows each leg's distance.
            Assert.Equal(legs.Count, legs.Select(l => l.Id).Distinct().Count());
        });
    }

    [Fact]
    public void A_mail_run_loads_each_leg_anew()
    {
        var runs = Offers(Content.MissionTypes.Single(t => t.Id == "milk_run"), 40);
        Assert.Contains(runs, run => run.FollowUps.Any(l => l.Cargo != run.Cargo));
    }

    [Fact]
    public void Multi_stop_runs_are_occasional_for_bush_supply()
    {
        var offers = Offers(Content.MissionTypes.Single(t => t.Id == "bush_supply"), 200);
        double share = offers.Count(o => o.PackageKind == PackageKind.MultiStop && o.FollowUps.Count > 0) / (double)offers.Count;
        Assert.InRange(share, 0.15, 0.45);
        Assert.All(offers.Where(o => o.FollowUps.Count == 0), o => Assert.Empty(o.FollowUps));
    }
}
