using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.World;
using SkyCareer.Missions.Events;

namespace SkyCareer.Missions.Tests;

public class InFlightEventTests
{
    private static readonly GameContent Content = ContentLoader.Load(FindContentRoot());

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "content");
    }

    private static MissionType Type(string id) => Content.MissionTypes.Single(t => t.Id == id);

    [Fact]
    public void About_every_tenth_flight_gets_an_event_that_fits_it()
    {
        var aircraft = new FleetAircraft { ProfileId = "kingair350", Registration = "D-TEST", Location = "EDDF" };
        var charters = Enumerable.Range(0, 2000).Select(_ => new MissionContract { MissionTypeId = "charter", Origin = "EDDF", Destination = "EDDM", Adults = 4 }).ToList();
        var plans = charters.Select(c => InFlightEvents.Plan(c, Type("charter"), aircraft)).ToList();

        Assert.InRange(plans.Count(p => p is not null), 140, 260);
        Assert.All(plans.OfType<PlannedEvent>(), p => Assert.InRange(p.TriggerFraction, 0.3, 0.61));
        Assert.All(plans.OfType<PlannedEvent>(), p => Assert.NotEqual(InFlightEventKind.TechnicalWarning, p.Kind));   // a new aircraft
        Assert.Equal(plans[0], InFlightEvents.Plan(charters[0], Type("charter"), aircraft));                              // same job, same plan

        // Cargo without passengers on a new aircraft: nothing can happen; worn: only a technical warning.
        var cargo = charters.Select(c => new MissionContract { Id = c.Id, MissionTypeId = "cargo_feeder", Origin = "EDDF", Destination = "EDDM", CargoKg = 500 }).ToList();
        Assert.All(cargo, c => Assert.Null(InFlightEvents.Plan(c, Type("cargo_feeder"), aircraft)));
        var worn = new FleetAircraft { ProfileId = "kingair350", Registration = "D-WORN", Location = "EDDF", ComponentWear = "37=0.5" };
        Assert.All(cargo.Select(c => InFlightEvents.Plan(c, Type("cargo_feeder"), worn)).OfType<PlannedEvent>(), p => Assert.Equal(InFlightEventKind.TechnicalWarning, p.Kind));
        // A decided event isn't planned again.
        Assert.Null(InFlightEvents.Plan(new MissionContract { Id = charters[0].Id, MissionTypeId = "charter", Origin = "EDDF", Destination = "EDDM", Adults = 4, EventKind = "MedicalEmergency" }, Type("charter"), aircraft));
    }

    [Fact]
    public void Pay_follows_the_new_destination_or_the_share_flown()
    {
        var origin = new Airport("EDDF", "Frankfurt", new GeoPosition(50.0333, 8.5706), AirportSize.Large, 4000, [RunwaySurface.Paved]);
        var halfway = new Airport("HALF", "Halfway", new GeoPosition(49.2, 10.2), AirportSize.Medium, 2000, [RunwaySurface.Paved]);
        var farther = new Airport("FAR", "Farther", new GeoPosition(48.0, 12.5), AirportSize.Medium, 2000, [RunwaySurface.Paved]);
        var job = new MissionContract { MissionTypeId = "charter", Origin = "EDDF", Destination = "X", DistanceNm = origin.Position.DistanceNmTo(halfway.Position) * 2, Payout = 10_000m };

        Assert.Equal(5000m, InFlightEvents.PrecautionaryPayout(job, origin, halfway), 0);
        decimal farPay = InFlightEvents.ChangedPayout(job, origin, farther);
        Assert.Equal(Math.Round(10_000m * (decimal)(origin.Position.DistanceNmTo(farther.Position) / job.DistanceNm) * 1.1m, 0), farPay);
        // Never below half the pay, even when the new destination is close by.
        Assert.Equal(5500m, InFlightEvents.ChangedPayout(job, origin, origin with { Ident = "NEAR" }));
    }
}
