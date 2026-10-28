using SkyCareer.Domain;
using SkyCareer.Domain.Aircraft;
using SkyCareer.Domain.Career;
using SkyCareer.Domain.Loading;
using SkyCareer.Domain.World;
using SkyCareer.Missions.Departure;

namespace SkyCareer.Missions.Tests;

public class DepartureCheckTests
{
    private static readonly GameContent Content = ContentLoader.Load(FindContentRoot());
    private static readonly Airport Origin = new("EDDF", "Frankfurt", new GeoPosition(50.0333, 8.5706), AirportSize.Large, 4000, [RunwaySurface.Paved]);
    private static readonly DateTime Now = new(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc);

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SkyCareer.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "content");
    }

    private static readonly AircraftProfile Caravan = Content.Aircraft.Single(a => a.Id == "c208b");
    private static readonly FleetAircraft Plane = new() { ProfileId = "c208b", Registration = "D-FCAR", Location = "EDDF" };

    private static MissionContract Job(MissionType type) => new()
    {
        MissionTypeId = type.Id, Origin = "EDDF", Destination = "EDDM", DistanceNm = 165, Adults = 4,
        PassengerMassKg = 4 * 84, BaggageKg = 60, CargoKg = 200, Deadline = Now.AddHours(5), Status = ContractStatus.Accepted
    };

    private static LoadSheet Sheet(MissionContract c) => LoadSheetBuilder.FromMasses(Caravan, new PassengerCount(c.Adults), Mass.FromKg(c.PassengerMassKg),
        Mass.FromKg(c.BaggageKg), Mass.FromKg(c.CargoKg), FuelPlanner.Plan(Caravan, c.DistanceNm, FlightRules.Ifr, 50));

    /// <summary>The aircraft exactly as the load sheet wants it, parked at the origin.</summary>
    private static SimAircraftState Perfect(LoadSheet sheet) => new("Cessna 208B Grand Caravan EX", Origin.Position, true,
        sheet.SimPayload.Kg, sheet.Fuel.Block.Kg, Caravan.OperatingEmptyKg + sheet.SimPayload.Kg + sheet.Fuel.Block.Kg);

    private static DepartureReadiness Check(MissionType type, Func<SimAircraftState, SimAircraftState>? change = null, MissionContract? job = null)
    {
        var c = job ?? Job(type);
        var sheet = Sheet(c);
        var sim = Perfect(sheet);
        return DepartureCheck.Evaluate(c, type, Caravan, Plane, sheet, Origin, Now, change is null ? sim : change(sim));
    }

    private static MissionType Cargo => Content.MissionTypes.Single(t => t.Id == "cargo_feeder") with { SimBrief = SimBriefUsage.None };

    [Fact]
    public void Matching_aircraft_is_all_green()
    {
        var r = Check(Cargo);
        Assert.Equal(Light.Green, r.Overall);
        Assert.Empty(r.Blocking);
        Assert.Empty(r.Penalised);
    }

    [Fact]
    public void Wrong_type_and_position_block_the_departure()
    {
        var r = Check(Cargo, s => s with { Title = "Airbus A320neo", Position = new GeoPosition(50.2, 8.57) });
        Assert.Equal(Light.Red, r[DepartureCheck.Type]!.Light);
        Assert.Equal(Light.Red, r[DepartureCheck.Position]!.Light);
        Assert.Contains("10 nm", r[DepartureCheck.Position]!.Detail);
        Assert.Equal(2, r.Blocking.Count());
    }

    [Fact]
    public void Payload_deviation_is_yellow_then_red()
    {
        Assert.Equal(Light.Yellow, Check(Cargo, s => s with { PayloadKg = s.PayloadKg + 25, TotalWeightKg = s.TotalWeightKg + 25 })[DepartureCheck.Payload]!.Light);
        var red = Check(Cargo, s => s with { PayloadKg = s.PayloadKg - 300, TotalWeightKg = s.TotalWeightKg - 300 });
        Assert.Equal(Light.Red, red[DepartureCheck.Payload]!.Light);
        Assert.Contains(red.Penalised, i => i.Key == DepartureCheck.Payload);
    }

    [Fact]
    public void Fuel_more_than_planned_is_yellow_and_below_minimum_is_red()
    {
        Assert.Equal(Light.Yellow, Check(Cargo, s => s with { FuelKg = s.FuelKg + 200, TotalWeightKg = s.TotalWeightKg + 200 })[DepartureCheck.Fuel]!.Light);
        Assert.Equal(Light.Red, Check(Cargo, s => s with { FuelKg = 60, TotalWeightKg = s.TotalWeightKg - s.FuelKg + 60 })[DepartureCheck.Fuel]!.Light);
    }

    [Fact]
    public void Fuel_burnt_while_taxiing_is_still_on_plan()
    {
        var c = Job(Cargo);
        var taxi = Sheet(c).Fuel.Taxi.Kg;
        Assert.True(taxi > 0);
        var r = Check(Cargo, s => s with { FuelKg = s.FuelKg - taxi * 0.8, TotalWeightKg = s.TotalWeightKg - taxi * 0.8 });
        Assert.Equal(Light.Green, r[DepartureCheck.Fuel]!.Light);
    }

    [Fact]
    public void Overall_is_grey_without_sim_unless_something_is_red()
    {
        var c = Job(Cargo with { SimBrief = SimBriefUsage.Optional });
        var type = Cargo with { SimBrief = SimBriefUsage.Optional };
        Assert.Equal(Light.Unknown, DepartureCheck.Evaluate(c, type, Caravan, Plane, Sheet(c), Origin, Now, null).Overall);
        var required = Cargo with { SimBrief = SimBriefUsage.Required };
        Assert.Equal(Light.Red, DepartureCheck.Evaluate(c, required, Caravan, Plane, Sheet(c), Origin, Now, null).Overall);
    }

    [Fact]
    public void Exceeding_mzfw_is_red()
    {
        var r = Check(Cargo, s => s with { TotalWeightKg = Caravan.MaxZeroFuelKg + 100 + s.FuelKg });
        Assert.Equal(Light.Red, r[DepartureCheck.Limits]!.Light);
        Assert.Contains("MZFW", r[DepartureCheck.Limits]!.Detail);
    }

    [Fact]
    public void Missing_ofp_depends_on_the_mission_type()
    {
        Assert.Equal(Light.Red, Check(Cargo with { SimBrief = SimBriefUsage.Required })[DepartureCheck.Ofp]!.Light);
        Assert.Equal(Light.Yellow, Check(Cargo with { SimBrief = SimBriefUsage.Optional })[DepartureCheck.Ofp]!.Light);
        var withOfp = Job(Cargo);
        withOfp.OfpBlockFuelKg = 300;
        Assert.Equal(Light.Green, Check(Cargo with { SimBrief = SimBriefUsage.Required }, job: withOfp)[DepartureCheck.Ofp]!.Light);
        Assert.Null(Check(Cargo)[DepartureCheck.Ofp]);
    }

    [Fact]
    public void Without_the_sim_everything_sim_related_is_grey()
    {
        var c = Job(Cargo);
        var r = DepartureCheck.Evaluate(c, Cargo, Caravan, Plane, Sheet(c), Origin, Now, null);
        Assert.All(r.Items.Where(i => i.Key != DepartureCheck.Availability), i => Assert.Equal(Light.Unknown, i.Light));
        Assert.Empty(r.Blocking);
    }
}
